using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.IO;
using Microsoft.Data.Sqlite;
using MasterIM.Models;

namespace MasterIM.Server.Storage;

/// <summary>
/// 消息反应存储（对标 Discord reaction）。每房一个 reactions.db。
/// 行级模型：(msg_id, channel_id, emoji, user_id) 四元组，PK(msg_id, emoji, user_id)。
/// 加反应=INSERT OR IGNORE，取消=DELETE。聚合按 (msg_id, emoji) 分组。
/// </summary>
public class ReactionStore
{
    private readonly string _basePath;

    public ReactionStore(string basePath) => _basePath = basePath;

    private string GetDbPath(string roomId)
    {
        var dir = Path.Combine(_basePath, "rooms", roomId);
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "reactions.db");
    }

    private void EnsureDatabase(string dbPath)
    {
        using var conn = new SqliteConnection($"Data Source={dbPath}");
        conn.Open();
        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS Reactions (
                MsgId INTEGER NOT NULL,
                ChannelId TEXT NOT NULL,
                Emoji TEXT NOT NULL,
                UserId TEXT NOT NULL,
                PRIMARY KEY (MsgId, Emoji, UserId)
            );
            CREATE INDEX IF NOT EXISTS idx_reactions_msg ON Reactions(MsgId);";
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// 切换反应：加了就取消、没加就加。返回该 (msg, emoji) 切换后最新的用户列表（供广播聚合）。
    /// </summary>
    public async Task<ReactionUpdate> ToggleAsync(string roomId, long msgId, string channelId, string emoji, string userId)
    {
        var dbPath = GetDbPath(roomId);
        EnsureDatabase(dbPath);

        using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync();

        // 是否已存在
        var check = conn.CreateCommand();
        check.CommandText = "SELECT COUNT(*) FROM Reactions WHERE MsgId=@m AND Emoji=@e AND UserId=@u";
        check.Parameters.AddWithValue("@m", msgId);
        check.Parameters.AddWithValue("@e", emoji);
        check.Parameters.AddWithValue("@u", userId);
        var exists = Convert.ToInt32(await check.ExecuteScalarAsync()) > 0;

        var cmd = conn.CreateCommand();
        if (exists)
        {
            cmd.CommandText = "DELETE FROM Reactions WHERE MsgId=@m AND Emoji=@e AND UserId=@u";
        }
        else
        {
            cmd.CommandText = @"INSERT OR IGNORE INTO Reactions (MsgId, ChannelId, Emoji, UserId)
                                VALUES (@m, @c, @e, @u)";
            cmd.Parameters.AddWithValue("@c", channelId);
        }
        cmd.Parameters.AddWithValue("@m", msgId);
        cmd.Parameters.AddWithValue("@e", emoji);
        cmd.Parameters.AddWithValue("@u", userId);
        await cmd.ExecuteNonQueryAsync();

        return await GetReactionAsync(conn, msgId, channelId, emoji);
    }

    private static async Task<ReactionUpdate> GetReactionAsync(SqliteConnection conn, long msgId, string channelId, string emoji)
    {
        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT UserId FROM Reactions WHERE MsgId=@m AND Emoji=@e";
        cmd.Parameters.AddWithValue("@m", msgId);
        cmd.Parameters.AddWithValue("@e", emoji);

        var users = new List<string>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            users.Add(reader.GetString(0));

        return new ReactionUpdate { MsgId = msgId, ChannelId = channelId, Emoji = emoji, UserIds = users };
    }

    /// <summary>
    /// 批量取一组消息的全部反应（历史加载用）。返回每条 (msg, emoji) 的用户列表。
    /// </summary>
    public async Task<List<ReactionUpdate>> GetForMessagesAsync(string roomId, IEnumerable<long> msgIds)
    {
        var ids = new List<long>(msgIds);
        if (ids.Count == 0) return new();

        var dbPath = GetDbPath(roomId);
        EnsureDatabase(dbPath);

        using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync();

        // IN 子句参数化
        var inParams = string.Join(",", ids.Select((_, i) => $"@id{i}"));
        var cmd = conn.CreateCommand();
        cmd.CommandText = $@"SELECT MsgId, ChannelId, Emoji, UserId FROM Reactions
                             WHERE MsgId IN ({inParams}) ORDER BY MsgId, Emoji";
        for (int i = 0; i < ids.Count; i++)
            cmd.Parameters.AddWithValue($"@id{i}", ids[i]);

        // 聚合 (msg, emoji) → 用户列表
        var map = new Dictionary<(long, string), ReactionUpdate>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var msgId = reader.GetInt64(0);
            var channelId = reader.GetString(1);
            var emoji = reader.GetString(2);
            var userId = reader.GetString(3);
            var key = (msgId, emoji);
            if (!map.TryGetValue(key, out var ru))
            {
                ru = new ReactionUpdate { MsgId = msgId, ChannelId = channelId, Emoji = emoji };
                map[key] = ru;
            }
            ru.UserIds.Add(userId);
        }
        return new List<ReactionUpdate>(map.Values);
    }
}
