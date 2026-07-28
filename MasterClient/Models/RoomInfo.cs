namespace MasterClient.Models;

public class RoomInfo
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string GMName { get; set; } = string.Empty;
    public string ServerAddress { get; set; } = string.Empty;
    public int PlayerCount { get; set; }
    public int MaxPlayers { get; set; }
    public RoomType Type { get; set; }
    public bool IsOnline { get; set; }
    public DateTime LastActivity { get; set; }
}

public enum RoomType
{
    LAN,        // 局域网房间
    Remote,     // 远程房间（P2P）
    Cloud,      // 云房间
    Offline     // 脱机房间（单机调试）
}

/// <summary>
/// 本地服务器房间信息（用于首页快捷入口）
/// </summary>
public class LocalRoomInfo
{
    public Guid RoomId { get; set; }
    public string RoomName { get; set; } = string.Empty;
    public string GmName { get; set; } = string.Empty;
    public int PlayerCount { get; set; }
    public int MaxPlayers { get; set; }
    public string ServerAddress { get; set; } = string.Empty;

    public string PlayerCountDisplay => $"{PlayerCount}/{MaxPlayers}";
}

/// <summary>
/// 服务端返回的房间信息（MasterClient 内部轻量类型，
/// 替代旧的 TRPGMaster.Core.Models.Rooms.RoomInfo，待接入后端后填充）。
/// </summary>
public class ServerRoomInfo
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? OwnerName { get; set; }
    public int CurrentPlayers { get; set; }
    public int MaxPlayers { get; set; }
    public bool HasPassword { get; set; }
}
