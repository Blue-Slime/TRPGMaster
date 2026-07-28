using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.IO;
using Microsoft.Data.Sqlite;
using MasterIM.Models;

namespace MasterIM.Server.Storage;

public class RoomStore
{
    private readonly string _basePath;

    public RoomStore(string basePath)
    {
        _basePath = basePath;
    }

    private string GetDbPath()
    {
        var dir = Path.Combine(_basePath, "rooms");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "rooms.db");
    }

    private void EnsureDatabase()
    {
        var dbPath = GetDbPath();
        using var conn = new SqliteConnection($"Data Source={dbPath}");
        conn.Open();

        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS Rooms (
                RoomId TEXT PRIMARY KEY,
                RoomName TEXT NOT NULL,
                Description TEXT,
                Password TEXT,
                OwnerId TEXT NOT NULL,
                HostUserId TEXT NOT NULL DEFAULT '',
                CreatedAt TEXT NOT NULL,
                IsPublic INTEGER NOT NULL DEFAULT 1,
                RequireApproval INTEGER NOT NULL DEFAULT 0
            )";
        cmd.ExecuteNonQuery();
    }

    private static Room ReadRoom(SqliteDataReader reader)
    {
        return new Room
        {
            RoomId = reader.GetString(reader.GetOrdinal("RoomId")),
            RoomName = reader.GetString(reader.GetOrdinal("RoomName")),
            Description = reader.IsDBNull(reader.GetOrdinal("Description")) ? "" : reader.GetString(reader.GetOrdinal("Description")),
            Password = reader.IsDBNull(reader.GetOrdinal("Password")) ? "" : reader.GetString(reader.GetOrdinal("Password")),
            OwnerId = reader.GetString(reader.GetOrdinal("OwnerId")),
            HostUserId = reader.GetString(reader.GetOrdinal("HostUserId")),
            CreatedAt = DateTime.Parse(reader.GetString(reader.GetOrdinal("CreatedAt"))),
            IsPublic = reader.GetInt32(reader.GetOrdinal("IsPublic")) == 1,
            RequireApproval = reader.GetInt32(reader.GetOrdinal("RequireApproval")) == 1
        };
    }

    public async Task CreateRoomAsync(Room room)
    {
        EnsureDatabase();
        var dbPath = GetDbPath();

        // HostUserId 兜底：未设置时用 OwnerId
        if (string.IsNullOrEmpty(room.HostUserId))
            room.HostUserId = room.OwnerId;

        using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync();

        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT OR REPLACE INTO Rooms
                (RoomId, RoomName, Description, Password, OwnerId, HostUserId, CreatedAt, IsPublic, RequireApproval)
            VALUES
                (@id, @name, @desc, @pwd, @owner, @host, @created, @public, @approval)";

        cmd.Parameters.AddWithValue("@id", room.RoomId);
        cmd.Parameters.AddWithValue("@name", room.RoomName);
        cmd.Parameters.AddWithValue("@desc", room.Description ?? "");
        cmd.Parameters.AddWithValue("@pwd", room.Password ?? "");
        cmd.Parameters.AddWithValue("@owner", room.OwnerId);
        cmd.Parameters.AddWithValue("@host", room.HostUserId);
        cmd.Parameters.AddWithValue("@created", room.CreatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("@public", room.IsPublic ? 1 : 0);
        cmd.Parameters.AddWithValue("@approval", room.RequireApproval ? 1 : 0);

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<Room?> GetRoomAsync(string roomId)
    {
        EnsureDatabase();
        var dbPath = GetDbPath();

        using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync();

        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM Rooms WHERE RoomId=@id";
        cmd.Parameters.AddWithValue("@id", roomId);

        using var reader = (SqliteDataReader)await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
            return ReadRoom(reader);

        return null;
    }

    public async Task<List<Room>> GetAllRoomsAsync()
    {
        EnsureDatabase();
        var dbPath = GetDbPath();

        using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync();

        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM Rooms ORDER BY CreatedAt DESC";

        var rooms = new List<Room>();
        using var reader = (SqliteDataReader)await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            rooms.Add(ReadRoom(reader));

        return rooms;
    }

    public async Task UpdateRoomAsync(Room room)
    {
        EnsureDatabase();
        var dbPath = GetDbPath();

        if (string.IsNullOrEmpty(room.HostUserId))
            room.HostUserId = room.OwnerId;

        using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync();

        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE Rooms SET
                RoomName=@name, Description=@desc, Password=@pwd,
                HostUserId=@host, IsPublic=@public, RequireApproval=@approval
            WHERE RoomId=@id";

        cmd.Parameters.AddWithValue("@id", room.RoomId);
        cmd.Parameters.AddWithValue("@name", room.RoomName);
        cmd.Parameters.AddWithValue("@desc", room.Description ?? "");
        cmd.Parameters.AddWithValue("@pwd", room.Password ?? "");
        cmd.Parameters.AddWithValue("@host", room.HostUserId);
        cmd.Parameters.AddWithValue("@public", room.IsPublic ? 1 : 0);
        cmd.Parameters.AddWithValue("@approval", room.RequireApproval ? 1 : 0);

        await cmd.ExecuteNonQueryAsync();
    }

    public async Task DeleteRoomAsync(string roomId)
    {
        EnsureDatabase();
        var dbPath = GetDbPath();

        using var conn = new SqliteConnection($"Data Source={dbPath}");
        await conn.OpenAsync();

        var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM Rooms WHERE RoomId=@id";
        cmd.Parameters.AddWithValue("@id", roomId);

        await cmd.ExecuteNonQueryAsync();

        // 删除房间文件夹
        var roomDir = Path.Combine(_basePath, "rooms", roomId);
        if (Directory.Exists(roomDir))
        {
            Directory.Delete(roomDir, true);
        }
    }
}
