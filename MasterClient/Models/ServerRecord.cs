namespace MasterClient.Models;

/// <summary>
/// 已知服务器记录（持久化）
/// </summary>
public class ServerRecord
{
    /// <summary>
    /// 服务器唯一标识（基于地址生成）
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 服务器地址 (host:port)
    /// </summary>
    public string Address { get; set; } = string.Empty;

    /// <summary>
    /// 服务器显示名称（用户自定义或自动获取）
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// 服务器类型
    /// </summary>
    public ServerType Type { get; set; } = ServerType.Remote;

    /// <summary>
    /// 存储来源（本地/云端）
    /// </summary>
    public StorageSource Source { get; set; } = StorageSource.Local;

    /// <summary>
    /// 最后连接时间
    /// </summary>
    public DateTime LastConnectedAt { get; set; }

    /// <summary>
    /// 最后检测时间
    /// </summary>
    public DateTime LastCheckedAt { get; set; }

    /// <summary>
    /// 添加时间
    /// </summary>
    public DateTime AddedAt { get; set; } = DateTime.Now;

    /// <summary>
    /// 是否收藏
    /// </summary>
    public bool IsFavorite { get; set; }

    /// <summary>
    /// 该服务器下的房间记录
    /// </summary>
    public List<RoomRecord> Rooms { get; set; } = new();

    /// <summary>
    /// 生成服务器ID
    /// </summary>
    public static string GenerateId(string address)
    {
        return address.ToLowerInvariant().Replace(":", "_");
    }

    /// <summary>
    /// 创建副本（用于复制到不同存储）
    /// </summary>
    public ServerRecord Clone()
    {
        return new ServerRecord
        {
            Id = Id,
            Address = Address,
            DisplayName = DisplayName,
            Type = Type,
            Source = Source,
            LastConnectedAt = LastConnectedAt,
            LastCheckedAt = LastCheckedAt,
            AddedAt = AddedAt,
            IsFavorite = IsFavorite,
            Rooms = Rooms.Select(r => r.Clone()).ToList()
        };
    }
}

/// <summary>
/// 存储来源
/// </summary>
public enum StorageSource
{
    /// <summary>
    /// 本地存储（settings.json）
    /// </summary>
    Local,

    /// <summary>
    /// 云端存储（账号同步）
    /// </summary>
    Cloud
}

/// <summary>
/// 服务器类型
/// </summary>
public enum ServerType
{
    /// <summary>
    /// 本地服务器 (localhost)
    /// </summary>
    Local,

    /// <summary>
    /// 局域网服务器
    /// </summary>
    LAN,

    /// <summary>
    /// 远程服务器（公网）
    /// </summary>
    Remote,

    /// <summary>
    /// 官方中心服务器
    /// </summary>
    Official
}

/// <summary>
/// 房间记录（持久化）
/// </summary>
public class RoomRecord
{
    /// <summary>
    /// 房间ID
    /// </summary>
    public Guid RoomId { get; set; }

    /// <summary>
    /// 房间名称
    /// </summary>
    public string RoomName { get; set; } = string.Empty;

    /// <summary>
    /// GM名称
    /// </summary>
    public string GMName { get; set; } = string.Empty;

    /// <summary>
    /// 最大玩家数
    /// </summary>
    public int MaxPlayers { get; set; }

    /// <summary>
    /// 游戏系统（COC、DND等）
    /// </summary>
    public string GameSystem { get; set; } = string.Empty;

    /// <summary>
    /// 最后加入时间
    /// </summary>
    public DateTime LastJoinedAt { get; set; }

    /// <summary>
    /// 是否收藏
    /// </summary>
    public bool IsFavorite { get; set; }

    /// <summary>
    /// 房间描述
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// 创建副本
    /// </summary>
    public RoomRecord Clone()
    {
        return new RoomRecord
        {
            RoomId = RoomId,
            RoomName = RoomName,
            GMName = GMName,
            MaxPlayers = MaxPlayers,
            GameSystem = GameSystem,
            LastJoinedAt = LastJoinedAt,
            IsFavorite = IsFavorite,
            Description = Description
        };
    }
}

/// <summary>
/// 服务器状态（运行时）
/// </summary>
public enum ServerStatus
{
    /// <summary>
    /// 未知（未检测）
    /// </summary>
    Unknown,

    /// <summary>
    /// 正在检测
    /// </summary>
    Checking,

    /// <summary>
    /// 在线
    /// </summary>
    Online,

    /// <summary>
    /// 离线/无法连接
    /// </summary>
    Offline,

    /// <summary>
    /// 连接错误
    /// </summary>
    Error
}

/// <summary>
/// 房间状态（运行时）
/// </summary>
public enum RoomStatus
{
    /// <summary>
    /// 未知（未检测）
    /// </summary>
    Unknown,

    /// <summary>
    /// 存在且可加入
    /// </summary>
    Available,

    /// <summary>
    /// 存在但已满
    /// </summary>
    Full,

    /// <summary>
    /// 存在但需要密码
    /// </summary>
    PasswordRequired,

    /// <summary>
    /// 不存在/已删除
    /// </summary>
    NotFound,

    /// <summary>
    /// 游戏进行中（可能限制加入）
    /// </summary>
    InProgress
}
