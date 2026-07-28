using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.IO;
using Microsoft.Data.Sqlite;
using MasterIM.Models;

namespace MasterIM.Server.Storage;

/// <summary>
/// 频道存储。每个房间一个 channels.db（位于房间目录下）。
/// </summary>
public class ChannelStore
{
    private readonly string _basePath;

    public ChannelStore(string basePath)
    {
        _basePath = basePath;
    }

    private string GetDbPath(string roomId)
    {
        var dir = Path.Combine(_basePath, "rooms", roomId);
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "channels.db");
    }

    private void EnsureDatabase(string dbPath)
    {
        using var conn = new SqliteConnection($"Data Source={dbPath}");
        conn.Open();

        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS Channels (
                ChannelId TEXT PRIMARY KEY,
                RoomId TEXT NOT NULL,
                ChannelName TEXT NOT NULL,
                CreateTime TEXT NOT NULL,
                SortOrder INTEGER NOT NULL DEFAULT 0
            )";
        cmd.ExecuteNonQuery();
    }

    private static Channel ReadChannel(SqliteDataReader reader)
    {
        return new Channel
        {
            ChannelId = reader.GetString(reader.GetOrdinal("ChannelId")),
            RoomId = reader.GetString(reader.GetOrdinal("RoomId")),
            ChannelName = reader.GetString(reader.GetOrdinal("ChannelName")),
            CreateTime = DateTime.Parse(reader.GetString(reader.GetOrdinal("CreateTime"))),
            SortOrder = reader.GetInt32(reader.GetOrdinal("SortOrder"))
        };
    }

    /// <summary>
    /// 创建频道。ID 由服务端权威生成（除非调用方显式指定，如系统种子频道 channel_lobby）。
    /// 纯 INSERT：撞 ID 抛异常（不覆盖已有频道）；同房重名抛 InvalidOperationException。
    /// 返回落库后的完整 Channel（含生成的 ChannelId 与 SortOrder）。
    /// </summary>
    public async Task<Channel> CreateChannelAsync(Channel channel)
    {
        var dbPath = GetDbPath(channel.RoomId);
        EnsureDatabase(dbPath);

        // ID 权威：调用方未指定时由服务端生成（满 Guid，不截断，避免碰撞）
        if (string.IsNullOrEmpty(channel.ChannelId))
            channel.ChannelId = $"ch_{Guid.NewGuid():N}";
        if (channel.CreateTime == default)
            channel.CreateTime = DateTime.UtcNow;

        using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync();

        // 同房重名查重（大小写不敏感，去空白后比较）
        var dupCmd = conn.CreateCommand();
        dupCmd.CommandText = "SELECT COUNT(*) FROM Channels WHERE ChannelName=@name COLLATE NOCASE";
        dupCmd.Parameters.AddWithValue("@name", channel.ChannelName);
        if (Convert.ToInt32(await dupCmd.ExecuteScalarAsync()) > 0)
            throw new InvalidOperationException($"频道名已存在：{channel.ChannelName}");

        // 新频道排到末尾：SortOrder = 当前最大 + 1
        if (channel.SortOrder == 0)
        {
            var maxCmd = conn.CreateCommand();
            maxCmd.CommandText = "SELECT COALESCE(MAX(SortOrder), 0) FROM Channels";
            channel.SortOrder = Convert.ToInt32(await maxCmd.ExecuteScalarAsync()) + 1;
        }

        // 纯 INSERT：ChannelId 是主键，撞车会抛 SqliteException（不静默覆盖）
        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO Channels (ChannelId, RoomId, ChannelName, CreateTime, SortOrder)
            VALUES (@id, @roomId, @name, @created, @sort)";

        cmd.Parameters.AddWithValue("@id", channel.ChannelId);
        cmd.Parameters.AddWithValue("@roomId", channel.RoomId);
        cmd.Parameters.AddWithValue("@name", channel.ChannelName);
        cmd.Parameters.AddWithValue("@created", channel.CreateTime.ToString("O"));
        cmd.Parameters.AddWithValue("@sort", channel.SortOrder);

        await cmd.ExecuteNonQueryAsync();
        return channel;
    }

    public async Task<Channel?> GetChannelAsync(string roomId, string channelId)
    {
        var dbPath = GetDbPath(roomId);
        EnsureDatabase(dbPath);

        using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync();

        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM Channels WHERE ChannelId=@id";
        cmd.Parameters.AddWithValue("@id", channelId);

        using var reader = (SqliteDataReader)await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
            return ReadChannel(reader);

        return null;
    }

    public async Task<List<Channel>> GetChannelsAsync(string roomId)
    {
        var dbPath = GetDbPath(roomId);
        EnsureDatabase(dbPath);

        using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync();

        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM Channels ORDER BY SortOrder ASC, CreateTime ASC";

        var channels = new List<Channel>();
        using var reader = (SqliteDataReader)await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            channels.Add(ReadChannel(reader));

        return channels;
    }

    /// <summary>按传入的有序频道ID列表批量重写 SortOrder（拖拽排序持久化）。</summary>
    public async Task ReorderChannelsAsync(string roomId, List<string> orderedChannelIds)
    {
        var dbPath = GetDbPath(roomId);
        EnsureDatabase(dbPath);

        using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync();
        using var tx = conn.BeginTransaction();

        for (int i = 0; i < orderedChannelIds.Count; i++)
        {
            var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "UPDATE Channels SET SortOrder=@sort WHERE ChannelId=@id";
            cmd.Parameters.AddWithValue("@sort", i + 1);
            cmd.Parameters.AddWithValue("@id", orderedChannelIds[i]);
            await cmd.ExecuteNonQueryAsync();
        }

        tx.Commit();
    }

    public async Task UpdateChannelAsync(Channel channel)
    {
        var dbPath = GetDbPath(channel.RoomId);
        EnsureDatabase(dbPath);

        using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync();

        // 重名查重（排除自身；大小写不敏感）
        var dupCmd = conn.CreateCommand();
        dupCmd.CommandText = "SELECT COUNT(*) FROM Channels WHERE ChannelName=@name COLLATE NOCASE AND ChannelId<>@id";
        dupCmd.Parameters.AddWithValue("@name", channel.ChannelName);
        dupCmd.Parameters.AddWithValue("@id", channel.ChannelId);
        if (Convert.ToInt32(await dupCmd.ExecuteScalarAsync()) > 0)
            throw new InvalidOperationException($"频道名已存在：{channel.ChannelName}");

        var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE Channels SET ChannelName=@name WHERE ChannelId=@id";
        cmd.Parameters.AddWithValue("@id", channel.ChannelId);
        cmd.Parameters.AddWithValue("@name", channel.ChannelName);

        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>系统频道 ID（大厅）：不可删除，作为房间的保底频道。</summary>
    public const string SystemChannelId = "channel_lobby";

    /// <summary>
    /// 删除频道。保护规则：系统频道(channel_lobby)不可删；删除后房间不得为零频道。
    /// 违反规则抛 InvalidOperationException（由 IMServer 转成 error 包回客户端）。
    /// </summary>
    public async Task DeleteChannelAsync(string roomId, string channelId)
    {
        if (channelId == SystemChannelId)
            throw new InvalidOperationException("系统频道（大厅）不可删除");

        var dbPath = GetDbPath(roomId);
        EnsureDatabase(dbPath);

        using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync();

        // 不得删到零频道
        var countCmd = conn.CreateCommand();
        countCmd.CommandText = "SELECT COUNT(*) FROM Channels";
        if (Convert.ToInt32(await countCmd.ExecuteScalarAsync()) <= 1)
            throw new InvalidOperationException("房间至少保留一个频道");

        var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM Channels WHERE ChannelId=@id";
        cmd.Parameters.AddWithValue("@id", channelId);

        await cmd.ExecuteNonQueryAsync();
    }
}
