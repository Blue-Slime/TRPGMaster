using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using MasterIM.Models;

namespace MasterIM.Server.Storage;

/// <summary>
/// 高级私聊存储 v2.0 - 使用 MsgId 架构
/// </summary>
public class DMAdvancedStore
{
    private readonly string _basePath;
    private readonly MsgIdGenerator _idGenerator = new();

    public DMAdvancedStore(string basePath)
    {
        _basePath = basePath;
    }

    public static string GetPairId(string userId1, string userId2)
    {
        var users = new[] { userId1, userId2 };
        Array.Sort(users);
        return $"{users[0]}_{users[1]}";
    }

    #region 配置管理

    public async Task<DMConfig> GetOrCreateConfigAsync(string userId1, string userId2, bool enableStorage, int retentionDays = -1)
    {
        var pairId = GetPairId(userId1, userId2);
        var configPath = GetConfigPath(pairId);

        if (File.Exists(configPath))
        {
            var json = await File.ReadAllTextAsync(configPath);
            return JsonSerializer.Deserialize<DMConfig>(json) ?? new DMConfig();
        }

        var config = new DMConfig
        {
            PairId = pairId,
            EnableStorage = enableStorage,
            RetentionDays = retentionDays
        };

        await SaveConfigAsync(config);
        return config;
    }

    public async Task SaveConfigAsync(DMConfig config)
    {
        var configPath = GetConfigPath(config.PairId);
        var dir = Path.GetDirectoryName(configPath);
        if (dir != null)
            Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(config);
        await File.WriteAllTextAsync(configPath, json);
    }

    #endregion

    #region 消息存储

    /// <summary>
    /// 保存私聊消息
    /// </summary>
    public async Task<long> SaveAsync(string pairId, GroupMessage msg)
    {
        // 生成 MsgId
        if (msg.MsgId == 0)
            msg.MsgId = _idGenerator.NextId();

        if (msg.LastModified == default)
            msg.LastModified = DateTime.UtcNow;

        // 从 MsgId 提取时间确定分区
        var sendTime = MsgIdGenerator.ExtractDateTime(msg.MsgId);
        var partition = GetPartition(sendTime);
        var dbPath = GetDbPath(pairId, partition);
        EnsureDatabase(dbPath);

        using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync();

        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT OR REPLACE INTO dm_messages (
                msg_id, last_modified, version,
                sender_id, content, reply_to_msg_id, quoted_content,
                role_id, message_type, is_deleted
            ) VALUES (
                @msgId, @lastModified, @version,
                @senderId, @content, @replyToMsgId, @quotedContent,
                @roleId, @messageType, @isDeleted
            )";

        cmd.Parameters.AddWithValue("@msgId", msg.MsgId);
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

        return msg.MsgId;
    }

    /// <summary>
    /// 按时间范围查询消息
    /// </summary>
    public async Task<List<GroupMessage>> GetMessagesByTimeRangeAsync(
        string pairId,
        DateTime startTime,
        DateTime endTime,
        int limit = 100)
    {
        var messages = new List<GroupMessage>();
        var partitions = GetPartitionsInRange(startTime, endTime);

        var startMsgId = GroupMessage.DateTimeToMsgIdBase(startTime);
        var endMsgId = GroupMessage.DateTimeToMsgIdBase(endTime) | ((1L << 22) - 1);

        foreach (var partition in partitions)
        {
            var dbPath = GetDbPath(pairId, partition);
            if (!File.Exists(dbPath))
                continue;

            using var conn = new SqliteConnection($"Data Source={dbPath}");
            await conn.OpenAsync();

            var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT msg_id, last_modified, version,
                       sender_id, content, reply_to_msg_id, quoted_content,
                       role_id, message_type, is_deleted
                FROM dm_messages
                WHERE msg_id >= @start AND msg_id <= @end
                  AND is_deleted = 0
                ORDER BY msg_id DESC
                LIMIT @limit";

            cmd.Parameters.AddWithValue("@start", startMsgId);
            cmd.Parameters.AddWithValue("@end", endMsgId);
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
    /// 编辑消息
    /// </summary>
    public async Task EditMessageAsync(string pairId, long msgId, string newContent)
    {
        var sendTime = MsgIdGenerator.ExtractDateTime(msgId);
        var partition = GetPartition(sendTime);
        var dbPath = GetDbPath(pairId, partition);

        if (!File.Exists(dbPath))
            throw new Exception($"Message {msgId} not found");

        using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync();

        var now = ToUnixMillis(DateTime.UtcNow);
        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE dm_messages
            SET content = @content,
                version = version + 1,
                last_modified = @now
            WHERE msg_id = @msgId";

        cmd.Parameters.AddWithValue("@content", newContent);
        cmd.Parameters.AddWithValue("@now", now);
        cmd.Parameters.AddWithValue("@msgId", msgId);

        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// 删除消息（软删除）
    /// </summary>
    public async Task DeleteMessageAsync(string pairId, long msgId)
    {
        var sendTime = MsgIdGenerator.ExtractDateTime(msgId);
        var partition = GetPartition(sendTime);
        var dbPath = GetDbPath(pairId, partition);

        if (!File.Exists(dbPath))
            throw new Exception($"Message {msgId} not found");

        using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync();

        var now = ToUnixMillis(DateTime.UtcNow);
        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE dm_messages
            SET is_deleted = 1,
                last_modified = @now
            WHERE msg_id = @msgId";

        cmd.Parameters.AddWithValue("@now", now);
        cmd.Parameters.AddWithValue("@msgId", msgId);

        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// 增量查询
    /// </summary>
    public async Task<Dictionary<string, List<GroupMessage>>> QueryIncrementalAsync(
        string pairId,
        Dictionary<string, long> partitionMaxModified)
    {
        var updates = new Dictionary<string, List<GroupMessage>>();

        foreach (var (partition, maxModified) in partitionMaxModified)
        {
            var dbPath = GetDbPath(pairId, partition);
            if (!File.Exists(dbPath))
            {
                updates[partition] = new List<GroupMessage>();
                continue;
            }

            using var conn = new SqliteConnection($"Data Source={dbPath}");
            await conn.OpenAsync();

            var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT msg_id, last_modified, version,
                       sender_id, content, reply_to_msg_id, quoted_content,
                       role_id, message_type, is_deleted
                FROM dm_messages
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

    #region 辅助方法

    // 时间统一用 Unix 毫秒存储（last_modified 增量同步游标），不使用 Ticks。
    // 具体消息按 msg_id 定位。
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
            LastModified = FromUnixMillis(reader.GetInt64(1)),
            Version = reader.GetInt32(2),
            SenderId = reader.GetString(3),
            Content = reader.GetString(4),
            ReplyToMsgId = reader.IsDBNull(5) ? null : reader.GetInt64(5),
            QuotedContent = reader.IsDBNull(6) ? null : reader.GetString(6),
            RoleId = reader.IsDBNull(7) ? null : reader.GetString(7),
            MessageType = reader.GetString(8),
            IsDeleted = reader.GetInt32(9) == 1
        };
    }

    private string GetPartition(DateTime time)
    {
        return time.ToString("yyyy-MM");
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

    private string GetDbPath(string pairId, string partition)
    {
        var dir = Path.Combine(_basePath, "dm_advanced", pairId);
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, $"{partition}.db");
    }

    private string GetConfigPath(string pairId)
    {
        return Path.Combine(_basePath, "dm_advanced", pairId, "config.json");
    }

    private void EnsureDatabase(string dbPath)
    {
        using var conn = new SqliteConnection($"Data Source={dbPath}");
        conn.Open();

        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS dm_messages (
                msg_id INTEGER PRIMARY KEY,
                last_modified INTEGER NOT NULL,
                version INTEGER NOT NULL DEFAULT 1,
                sender_id TEXT NOT NULL,
                content TEXT NOT NULL,
                reply_to_msg_id INTEGER,
                quoted_content TEXT,
                role_id TEXT,
                message_type TEXT DEFAULT 'text',
                is_deleted INTEGER DEFAULT 0
            );

            CREATE INDEX IF NOT EXISTS idx_last_modified ON dm_messages(last_modified);
        ";
        cmd.ExecuteNonQuery();
    }

    #endregion
}
