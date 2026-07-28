using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;
using MasterIM.Models;

namespace MasterIM.Server.Storage;

/// <summary>
/// 消息存储 v2.0 - MsgId 包含时间戳
/// </summary>
public class MessageStore
{
    private readonly string _basePath;
    private readonly MsgIdGenerator _idGenerator = new();

    public MessageStore(string basePath)
    {
        _basePath = basePath;
        EnsureIndexDatabase();
    }

    #region 核心存储

    /// <summary>
    /// 保存消息
    /// </summary>
    public async Task<long> SaveAsync(string roomId, string channelId, GroupMessage msg)
    {
        // 生成 MsgId（包含时间戳）
        if (msg.MsgId == 0)
            msg.MsgId = _idGenerator.NextId();

        // 设置修改时间
        if (msg.LastModified == default)
            msg.LastModified = DateTime.UtcNow;

        // 设置显示时间（初始等于发送时间）
        if (msg.DisplayTime == default)
            msg.DisplayTime = msg.SendTime;

        // 从 MsgId 提取时间，确定存储分区
        var sendTime = MsgIdGenerator.ExtractDateTime(msg.MsgId);
        var partition = GetPartition(sendTime);
        var dbPath = GetDbPath(roomId, partition);
        EnsureDatabase(dbPath, channelId);

        // 写入消息表
        using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync();

        var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            INSERT OR REPLACE INTO channel_{channelId} (
                msg_id, display_time, last_modified, version,
                sender_id, content, reply_to_msg_id, quoted_content,
                role_id, message_type, is_deleted
            ) VALUES (
                @msgId, @displayTime, @lastModified, @version,
                @senderId, @content, @replyToMsgId, @quotedContent,
                @roleId, @messageType, @isDeleted
            )";

        cmd.Parameters.AddWithValue("@msgId", msg.MsgId);
        cmd.Parameters.AddWithValue("@displayTime", ToUnixMillis(msg.DisplayTime));
        cmd.Parameters.AddWithValue("@lastModified", ToUnixMillis(msg.LastModified));
        cmd.Parameters.AddWithValue("@version", msg.Version);
        cmd.Parameters.AddWithValue("@senderId", msg.SenderId);
        cmd.Parameters.AddWithValue("@content", msg.Content);
        cmd.Parameters.AddWithValue("@replyToMsgId", (object?)msg.ReplyToMsgId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@quotedContent", (object?)msg.QuotedContent ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@roleId", (object?)msg.RoleId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@messageType", msg.MessageType);
        cmd.Parameters.AddWithValue("@isDeleted", msg.IsDeleted ? 1 : 0);

        await cmd.ExecuteNonQueryAsync();

        // 更新全局索引
        await UpdateIndexAsync(msg.MsgId, channelId, partition, ToUnixMillis(msg.LastModified));

        return msg.MsgId;
    }

    /// <summary>
    /// 编辑消息
    /// </summary>
    public async Task EditMessageAsync(string roomId, string channelId, long msgId, string newContent)
    {
        var index = await GetMessageIndexAsync(msgId);
        if (index == null)
            throw new Exception($"Message {msgId} not found");

        var dbPath = GetDbPath(roomId, index.Partition);

        using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync();

        var now = ToUnixMillis(DateTime.UtcNow);
        var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            UPDATE channel_{channelId}
            SET content = @content,
                version = version + 1,
                last_modified = @now
            WHERE msg_id = @msgId";

        cmd.Parameters.AddWithValue("@content", newContent);
        cmd.Parameters.AddWithValue("@now", now);
        cmd.Parameters.AddWithValue("@msgId", msgId);

        await cmd.ExecuteNonQueryAsync();
        await UpdateIndexModifiedAsync(msgId, now);
    }

    /// <summary>
    /// 删除消息（软删除）
    /// </summary>
    public async Task DeleteMessageAsync(string roomId, string channelId, long msgId)
    {
        var index = await GetMessageIndexAsync(msgId);
        if (index == null)
            throw new Exception($"Message {msgId} not found");

        var dbPath = GetDbPath(roomId, index.Partition);

        using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync();

        var now = ToUnixMillis(DateTime.UtcNow);
        var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            UPDATE channel_{channelId}
            SET is_deleted = 1,
                last_modified = @now
            WHERE msg_id = @msgId";

        cmd.Parameters.AddWithValue("@now", now);
        cmd.Parameters.AddWithValue("@msgId", msgId);

        await cmd.ExecuteNonQueryAsync();
        await UpdateIndexModifiedAsync(msgId, now);
    }

    #endregion

    #region 查询接口

    /// <summary>
    /// 按时间范围查询消息
    /// </summary>
    public async Task<List<GroupMessage>> QueryByTimeRangeAsync(
        string roomId,
        string channelId,
        DateTime startTime,
        DateTime endTime,
        int limit = 100)
    {
        var messages = new List<GroupMessage>();
        var partitions = GetPartitionsInRange(startTime, endTime);

        // display_time 列以 Unix 毫秒存储（见 SaveAsync）。
        var startMs = ToUnixMillis(startTime);
        var endMs = ToUnixMillis(endTime);

        foreach (var partition in partitions)
        {
            var dbPath = GetDbPath(roomId, partition);
            if (!File.Exists(dbPath))
                continue;

            using var conn = new SqliteConnection($"Data Source={dbPath}");
            await conn.OpenAsync();

            var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
                SELECT msg_id, display_time, last_modified, version,
                       sender_id, content, reply_to_msg_id, quoted_content,
                       role_id, message_type, is_deleted
                FROM channel_{channelId}
                WHERE display_time >= @start AND display_time <= @end
                  AND is_deleted = 0
                ORDER BY display_time DESC, msg_id DESC
                LIMIT @limit";

            cmd.Parameters.AddWithValue("@start", startMs);
            cmd.Parameters.AddWithValue("@end", endMs);
            cmd.Parameters.AddWithValue("@limit", limit);

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                messages.Add(ReadMessage(reader));
            }

            if (messages.Count >= limit)
                break;
        }

        return messages.OrderByDescending(m => m.MsgId).Take(limit).ToList();
    }

    /// <summary>
    /// 增量查询
    /// </summary>
    public async Task<Dictionary<string, List<GroupMessage>>> QueryIncrementalAsync(
        string roomId,
        string channelId,
        Dictionary<string, long> partitionMaxModified)
    {
        var updates = new Dictionary<string, List<GroupMessage>>();

        foreach (var (partition, maxModified) in partitionMaxModified)
        {
            var dbPath = GetDbPath(roomId, partition);
            if (!File.Exists(dbPath))
            {
                updates[partition] = new List<GroupMessage>();
                continue;
            }

            using var conn = new SqliteConnection($"Data Source={dbPath}");
            await conn.OpenAsync();

            var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
                SELECT msg_id, display_time, last_modified, version,
                       sender_id, content, reply_to_msg_id, quoted_content,
                       role_id, message_type, is_deleted
                FROM channel_{channelId}
                WHERE last_modified > @maxModified
                ORDER BY last_modified ASC
                LIMIT 1000";

            cmd.Parameters.AddWithValue("@maxModified", maxModified);

            var partitionUpdates = new List<GroupMessage>();
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                partitionUpdates.Add(ReadMessage(reader));
            }

            updates[partition] = partitionUpdates;
        }

        return updates;
    }

    #endregion

    #region 批量操作

    /// <summary>
    /// 批量移动消息到指定时间点
    /// </summary>
    /// <param name="msgIds">要移动的消息ID列表</param>
    /// <param name="targetTime">目标时间（插入到这个时间点）</param>
    /// <param name="preserveOrder">是否保持原有顺序</param>
    public async Task<List<string>> BatchMoveMessagesAsync(
        string roomId,
        string channelId,
        List<long> msgIds,
        DateTime targetTime,
        bool preserveOrder = true)
    {
        var affectedPartitions = new HashSet<string>();
        var now = ToUnixMillis(DateTime.UtcNow);

        // display_time 全部移动到目标毫秒。
        // 同一毫秒内的先后顺序由 msg_id 决定（msg_id 自带亚毫秒序列），
        // 因此这里不再伪造亚毫秒级 display_time；顺序依赖读取时
        // ORDER BY display_time, msg_id 生效。
        var targetMs = ToUnixMillis(targetTime);

        foreach (var msgId in msgIds)
        {
            var sendTime = MsgIdGenerator.ExtractDateTime(msgId);
            var partition = GetPartition(sendTime);
            var dbPath = GetDbPath(roomId, partition);

            if (!File.Exists(dbPath))
                continue;

            using var conn = new SqliteConnection($"Data Source={dbPath}");
            await conn.OpenAsync();

            var newDisplayTime = targetMs;

            var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
                UPDATE channel_{channelId}
                SET display_time = @displayTime,
                    last_modified = @now,
                    version = version + 1
                WHERE msg_id = @msgId";

            cmd.Parameters.AddWithValue("@displayTime", newDisplayTime);
            cmd.Parameters.AddWithValue("@now", now);
            cmd.Parameters.AddWithValue("@msgId", msgId);

            await cmd.ExecuteNonQueryAsync();

            affectedPartitions.Add(partition);
        }

        return affectedPartitions.ToList();
    }

    /// <summary>
    /// 批量删除消息
    /// </summary>
    public async Task<List<string>> BatchDeleteMessagesAsync(string roomId, string channelId, List<long> msgIds)
    {
        var affectedPartitions = new HashSet<string>();
        var now = ToUnixMillis(DateTime.UtcNow);

        foreach (var msgId in msgIds)
        {
            var sendTime = MsgIdGenerator.ExtractDateTime(msgId);
            var partition = GetPartition(sendTime);
            var dbPath = GetDbPath(roomId, partition);

            if (!File.Exists(dbPath))
                continue;

            using var conn = new SqliteConnection($"Data Source={dbPath}");
            await conn.OpenAsync();

            var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
                UPDATE channel_{channelId}
                SET is_deleted = 1,
                    last_modified = @now
                WHERE msg_id = @msgId";

            cmd.Parameters.AddWithValue("@now", now);
            cmd.Parameters.AddWithValue("@msgId", msgId);

            await cmd.ExecuteNonQueryAsync();

            affectedPartitions.Add(partition);
        }

        return affectedPartitions.ToList();
    }

    /// <summary>
    /// 消息搜索
    /// </summary>
    public async Task<List<GroupMessage>> SearchMessagesAsync(
        string roomId,
        string channelId,
        string keyword,
        int limit = 50)
    {
        var messages = new List<GroupMessage>();

        // 搜索最近3个月的消息
        var endTime = DateTime.UtcNow;
        var startTime = endTime.AddMonths(-3);
        var partitions = GetPartitionsInRange(startTime, endTime);

        foreach (var partition in partitions.OrderByDescending(p => p))
        {
            var dbPath = GetDbPath(roomId, partition);
            if (!File.Exists(dbPath))
                continue;

            using var conn = new SqliteConnection($"Data Source={dbPath}");
            await conn.OpenAsync();

            var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
                SELECT msg_id, display_time, last_modified, version,
                       sender_id, content, reply_to_msg_id, quoted_content,
                       role_id, message_type, is_deleted
                FROM channel_{channelId}
                WHERE content LIKE @keyword
                  AND is_deleted = 0
                ORDER BY display_time DESC, msg_id DESC
                LIMIT @limit";

            cmd.Parameters.AddWithValue("@keyword", $"%{keyword}%");
            cmd.Parameters.AddWithValue("@limit", limit);

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                messages.Add(ReadMessage(reader));
            }

            if (messages.Count >= limit)
                break;
        }

        return messages.Take(limit).ToList();
    }

    /// <summary>
    /// 高级搜索：跨指定频道 + 多维过滤（关键字/发送人/时间范围/消息类型）。
    /// 每条结果回填其所属 ChannelId（供客户端分组与跳转）。
    /// </summary>
    public async Task<List<GroupMessage>> SearchAdvancedAsync(
        string roomId, IEnumerable<string> channelIds, MessageSearchQuery query)
    {
        var results = new List<GroupMessage>();

        // 时间窗：未指定则默认最近3个月（避免全表扫描）
        var endTime = query.EndTime ?? DateTime.UtcNow;
        var startTime = query.StartTime ?? endTime.AddMonths(-3);
        var startMs = ToUnixMillis(startTime);
        var endMs = ToUnixMillis(endTime);
        var partitions = GetPartitionsInRange(startTime, endTime);

        foreach (var channelId in channelIds.Distinct())
        {
            foreach (var partition in partitions.OrderByDescending(p => p))
            {
                var dbPath = GetDbPath(roomId, partition);
                if (!File.Exists(dbPath)) continue;

                using var conn = new SqliteConnection($"Data Source={dbPath}");
                await conn.OpenAsync();

                // 该分区库可能没有这个频道的表（频道是后建的）→ 跳过
                var check = conn.CreateCommand();
                check.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name=@t";
                check.Parameters.AddWithValue("@t", $"channel_{channelId}");
                if (await check.ExecuteScalarAsync() == null) continue;

                var cmd = conn.CreateCommand();
                var where = new List<string>
                {
                    "is_deleted = 0",
                    "display_time >= @start",
                    "display_time <= @end"
                };
                cmd.Parameters.AddWithValue("@start", startMs);
                cmd.Parameters.AddWithValue("@end", endMs);
                if (!string.IsNullOrEmpty(query.Keyword))
                {
                    where.Add("content LIKE @kw");
                    cmd.Parameters.AddWithValue("@kw", $"%{query.Keyword}%");
                }
                if (!string.IsNullOrEmpty(query.SenderId))
                {
                    where.Add("sender_id = @sender");
                    cmd.Parameters.AddWithValue("@sender", query.SenderId);
                }
                if (!string.IsNullOrEmpty(query.MessageType))
                {
                    where.Add("message_type = @type");
                    cmd.Parameters.AddWithValue("@type", query.MessageType);
                }
                cmd.Parameters.AddWithValue("@limit", query.Limit);

                cmd.CommandText = $@"
                    SELECT msg_id, display_time, last_modified, version,
                           sender_id, content, reply_to_msg_id, quoted_content,
                           role_id, message_type, is_deleted
                    FROM channel_{channelId}
                    WHERE {string.Join(" AND ", where)}
                    ORDER BY display_time DESC, msg_id DESC
                    LIMIT @limit";

                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var m = ReadMessage(reader);
                    m.ChannelId = channelId;  // 表名派生，回填供客户端使用
                    results.Add(m);
                }
            }
        }

        return results
            .OrderByDescending(m => m.DisplayTime).ThenByDescending(m => m.MsgId)
            .Take(query.Limit).ToList();
    }

    /// <summary>
    /// 围绕某条消息取上下文：目标消息 + 之前 before 条 + 之后 after 条（升序返回）。
    /// 用于搜索结果跳转时把不在缓存的老消息居中加载。
    /// </summary>
    public async Task<List<GroupMessage>> QueryAroundMessageAsync(
        string roomId, string channelId, long msgId, int before = 25, int after = 25)
    {
        // 先定位目标消息所在分区与 display_time
        var index = await GetMessageIndexAsync(msgId);
        if (index == null) return new();

        var dbPath = GetDbPath(roomId, index.Partition);
        if (!File.Exists(dbPath)) return new();

        using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync();

        // 取目标 display_time
        var pivotCmd = conn.CreateCommand();
        pivotCmd.CommandText = $"SELECT display_time FROM channel_{channelId} WHERE msg_id=@id";
        pivotCmd.Parameters.AddWithValue("@id", msgId);
        var pivotObj = await pivotCmd.ExecuteScalarAsync();
        if (pivotObj == null) return new();
        var pivot = Convert.ToInt64(pivotObj);

        const string cols = @"msg_id, display_time, last_modified, version,
                              sender_id, content, reply_to_msg_id, quoted_content,
                              role_id, message_type, is_deleted";

        var result = new List<GroupMessage>();

        // 之前 before 条（含目标）：display_time <= pivot 倒序取，再翻正
        var beforeCmd = conn.CreateCommand();
        beforeCmd.CommandText = $@"
            SELECT {cols} FROM channel_{channelId}
            WHERE is_deleted=0 AND (display_time < @pivot OR (display_time=@pivot AND msg_id<=@id))
            ORDER BY display_time DESC, msg_id DESC LIMIT @n";
        beforeCmd.Parameters.AddWithValue("@pivot", pivot);
        beforeCmd.Parameters.AddWithValue("@id", msgId);
        beforeCmd.Parameters.AddWithValue("@n", before + 1);
        using (var r = await beforeCmd.ExecuteReaderAsync())
            while (await r.ReadAsync()) { var m = ReadMessage(r); m.ChannelId = channelId; result.Add(m); }

        // 之后 after 条：display_time > pivot 升序取
        var afterCmd = conn.CreateCommand();
        afterCmd.CommandText = $@"
            SELECT {cols} FROM channel_{channelId}
            WHERE is_deleted=0 AND (display_time > @pivot OR (display_time=@pivot AND msg_id>@id))
            ORDER BY display_time ASC, msg_id ASC LIMIT @n";
        afterCmd.Parameters.AddWithValue("@pivot", pivot);
        afterCmd.Parameters.AddWithValue("@id", msgId);
        afterCmd.Parameters.AddWithValue("@n", after);
        using (var r = await afterCmd.ExecuteReaderAsync())
            while (await r.ReadAsync()) { var m = ReadMessage(r); m.ChannelId = channelId; result.Add(m); }

        return result
            .OrderBy(m => m.DisplayTime).ThenBy(m => m.MsgId)
            .ToList();
    }

    #endregion

    #region 全局索引

    private async Task UpdateIndexAsync(long msgId, string channelId, string partition, long lastModified)
    {
        var indexDbPath = GetIndexDbPath();
        using var conn = new SqliteConnection($"Data Source={indexDbPath}");
        await conn.OpenAsync();

        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT OR REPLACE INTO message_index (
                msg_id, channel_id, db_partition, last_modified
            ) VALUES (
                @msgId, @channelId, @partition, @lastModified
            )";

        cmd.Parameters.AddWithValue("@msgId", msgId);
        cmd.Parameters.AddWithValue("@channelId", channelId);
        cmd.Parameters.AddWithValue("@partition", partition);
        cmd.Parameters.AddWithValue("@lastModified", lastModified);

        await cmd.ExecuteNonQueryAsync();
    }

    private async Task UpdateIndexModifiedAsync(long msgId, long lastModified)
    {
        var indexDbPath = GetIndexDbPath();
        using var conn = new SqliteConnection($"Data Source={indexDbPath}");
        await conn.OpenAsync();

        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE message_index
            SET last_modified = @lastModified
            WHERE msg_id = @msgId";

        cmd.Parameters.AddWithValue("@lastModified", lastModified);
        cmd.Parameters.AddWithValue("@msgId", msgId);

        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<MessageIndex?> GetMessageIndexAsync(long msgId)
    {
        var indexDbPath = GetIndexDbPath();
        using var conn = new SqliteConnection($"Data Source={indexDbPath}");
        await conn.OpenAsync();

        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT msg_id, channel_id, db_partition, last_modified
            FROM message_index
            WHERE msg_id = @msgId";

        cmd.Parameters.AddWithValue("@msgId", msgId);

        using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return new MessageIndex
            {
                MsgId = reader.GetInt64(0),
                ChannelId = reader.GetString(1),
                Partition = reader.GetString(2),
                LastModified = reader.GetInt64(3)
            };
        }

        return null;
    }

    #endregion

    #region 辅助方法

    // ===== 时间单位约定（不使用 Ticks）=====
    // display_time / last_modified：统一 Unix 毫秒。
    // 具体消息的编辑/删除/移动/定位一律按 msg_id，不依赖时间列。

    private static long ToUnixMillis(DateTime dt)
    {
        var utc = dt.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(dt, DateTimeKind.Utc)
            : dt.ToUniversalTime();
        return new DateTimeOffset(utc).ToUnixTimeMilliseconds();
    }

    private static DateTime FromUnixMillis(long millis)
        => DateTimeOffset.FromUnixTimeMilliseconds(millis).UtcDateTime;

    private GroupMessage ReadMessage(SqliteDataReader reader)
    {
        return new GroupMessage
        {
            MsgId = reader.GetInt64(0),
            DisplayTime = FromUnixMillis(reader.GetInt64(1)),
            LastModified = FromUnixMillis(reader.GetInt64(2)),
            Version = reader.GetInt32(3),
            SenderId = reader.GetString(4),
            Content = reader.GetString(5),
            ReplyToMsgId = reader.IsDBNull(6) ? null : reader.GetInt64(6),
            QuotedContent = reader.IsDBNull(7) ? null : reader.GetString(7),
            RoleId = reader.IsDBNull(8) ? null : reader.GetString(8),
            MessageType = reader.GetString(9),
            IsDeleted = reader.GetInt32(10) == 1,
            IsPinned = reader.FieldCount > 11 && !reader.IsDBNull(11) && reader.GetInt32(11) == 1
        };
    }

    private string GetPartition(DateTime time) => time.ToString("yyyy-MM");

    /// <summary>置顶/取消置顶某条消息。</summary>
    public async Task PinMessageAsync(string roomId, string channelId, long msgId, bool pin)
    {
        var index = await GetMessageIndexAsync(msgId);
        if (index == null) return;
        var dbPath = GetDbPath(roomId, index.Partition);
        using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync();
        var cmd = conn.CreateCommand();
        cmd.CommandText = $"UPDATE channel_{channelId} SET is_pinned=@pin WHERE msg_id=@id";
        cmd.Parameters.AddWithValue("@pin", pin ? 1 : 0);
        cmd.Parameters.AddWithValue("@id", msgId);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>获取频道所有置顶消息（跨分区扫描，数量通常极少）。</summary>
    public async Task<List<GroupMessage>> GetPinnedMessagesAsync(string roomId, string channelId)
    {
        var result = new List<GroupMessage>();
        var partitions = GetPartitionsInRange(DateTime.UtcNow.AddYears(-2), DateTime.UtcNow);
        foreach (var partition in partitions.OrderByDescending(p => p))
        {
            var dbPath = GetDbPath(roomId, partition);
            if (!File.Exists(dbPath)) continue;
            using var conn = new SqliteConnection($"Data Source={dbPath}");
            await conn.OpenAsync();
            var check = conn.CreateCommand();
            check.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name=@t";
            check.Parameters.AddWithValue("@t", $"channel_{channelId}");
            if (await check.ExecuteScalarAsync() == null) continue;
            var cmd = conn.CreateCommand();
            cmd.CommandText = $@"
                SELECT msg_id, display_time, last_modified, version,
                       sender_id, content, reply_to_msg_id, quoted_content,
                       role_id, message_type, is_deleted, is_pinned
                FROM channel_{channelId}
                WHERE is_pinned=1 AND is_deleted=0
                ORDER BY display_time DESC";
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync()) { var m = ReadMessage(reader); m.ChannelId = channelId; result.Add(m); }
        }
        return result;
    }

    private List<string> GetPartitionsInRange(DateTime start, DateTime end)
    {
        var partitions = new List<string>();
        var current = new DateTime(start.Year, start.Month, 1);
        var endMonth = new DateTime(end.Year, end.Month, 1);

        while (current <= endMonth)
        {
            partitions.Add(current.ToString("yyyy-MM"));
            current = current.AddMonths(1);
        }

        return partitions;
    }

    private string GetDbPath(string roomId, string partition)
    {
        var dir = Path.Combine(_basePath, "rooms", roomId, "messages");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, $"{partition}.db");
    }

    private string GetIndexDbPath()
    {
        var dir = Path.Combine(_basePath, "index");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "message_index.db");
    }

    private void EnsureDatabase(string dbPath, string channelId)
    {
        using var conn = new SqliteConnection($"Data Source={dbPath}");
        conn.Open();

        var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            CREATE TABLE IF NOT EXISTS channel_{channelId} (
                msg_id INTEGER PRIMARY KEY,
                display_time INTEGER NOT NULL,
                last_modified INTEGER NOT NULL,
                version INTEGER NOT NULL DEFAULT 1,
                sender_id TEXT NOT NULL,
                content TEXT NOT NULL,
                reply_to_msg_id INTEGER,
                quoted_content TEXT,
                role_id TEXT,
                message_type TEXT DEFAULT 'text',
                is_deleted INTEGER DEFAULT 0,
                is_pinned INTEGER DEFAULT 0
            );

            CREATE INDEX IF NOT EXISTS idx_display_time_{channelId} ON channel_{channelId}(display_time);
            CREATE INDEX IF NOT EXISTS idx_last_modified_{channelId} ON channel_{channelId}(last_modified);
        ";
        cmd.ExecuteNonQuery();

        // 旧表无 is_pinned 列时平滑迁移（忽略"已存在"错误）
        try
        {
            var alter = conn.CreateCommand();
            alter.CommandText = $"ALTER TABLE channel_{channelId} ADD COLUMN is_pinned INTEGER DEFAULT 0";
            alter.ExecuteNonQuery();
        }
        catch { /* 列已存在，忽略 */ }
    }

    private void EnsureIndexDatabase()
    {
        var indexDbPath = GetIndexDbPath();
        using var conn = new SqliteConnection($"Data Source={indexDbPath}");
        conn.Open();

        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS message_index (
                msg_id INTEGER PRIMARY KEY,
                channel_id TEXT NOT NULL,
                db_partition TEXT NOT NULL,
                last_modified INTEGER NOT NULL
            );

            CREATE INDEX IF NOT EXISTS idx_channel_modified ON message_index(channel_id, last_modified);
        ";
        cmd.ExecuteNonQuery();
    }

    #endregion
}

internal class MessageIndex
{
    public long MsgId { get; set; }
    public string ChannelId { get; set; } = string.Empty;
    public string Partition { get; set; } = string.Empty;
    public long LastModified { get; set; }
}
