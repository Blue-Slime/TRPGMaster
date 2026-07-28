namespace MasterClient.Models;

/// <summary>
/// 全局偏好设置（持久化到 %APPDATA%/TRPGMaster/settings.json）
/// </summary>
public class AppSettings
{
    // ===== 存储设置 =====

    /// <summary>
    /// 默认房间存储根目录
    /// 默认值: {应用根目录}/Rooms
    /// </summary>
    public string DefaultRoomsPath { get; set; } = GetDefaultRoomsPath();

    // ===== 登录设置 =====
    public bool RememberPassword { get; set; } = true;
    public bool AutoLogin { get; set; } = false;
    public string? SavedAccount { get; set; }
    public string? EncryptedPassword { get; set; }

    /// <summary>全局用户ID：造访新世界 / 记忆房间进房时统一使用（在设置页手填）</summary>
    public string UserId { get; set; } = "player";

    // ===== 窗口设置 =====
    public double? WindowX { get; set; }
    public double? WindowY { get; set; }
    public bool MinimizeToTray { get; set; } = true;
    public bool StartWithWindows { get; set; } = false;
    public bool CheckUpdateOnStart { get; set; } = true;

    // ===== 显示设置 =====
    public DisplayPreferences Display { get; set; } = new();

    // ===== 输入状态指示器 =====
    public TypingIndicatorPreferences TypingIndicator { get; set; } = new();

    // ===== 文件传输 =====
    public FileTransferPreferences FileTransfer { get; set; } = new();

    // ===== 通知设置 =====
    public NotificationPreferences Notification { get; set; } = new();

    // ===== 已知服务器列表 =====
    public List<ServerRecord> KnownServers { get; set; } = new();

    // ===== 最近房间（旧格式，保留兼容性） =====
    public List<RecentRoom> RecentRooms { get; set; } = new();

    private static string GetDefaultRoomsPath()
    {
        var appDir = AppContext.BaseDirectory;
        return Path.Combine(appDir, "Rooms");
    }
}

/// <summary>
/// 显示设置
/// </summary>
public class DisplayPreferences
{
    /// <summary>
    /// 主题: Dark, Light, System
    /// </summary>
    public string Theme { get; set; } = "Dark";

    /// <summary>
    /// 字体大小 (px)
    /// </summary>
    public int FontSize { get; set; } = 14;

    /// <summary>
    /// 显示消息时间戳
    /// </summary>
    public bool ShowTimestamp { get; set; } = true;

    /// <summary>
    /// 紧凑消息模式
    /// </summary>
    public bool CompactMode { get; set; } = false;
}

/// <summary>
/// 输入状态指示器设置
/// </summary>
public class TypingIndicatorPreferences
{
    /// <summary>
    /// 是否启用
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 发送冷却时间（秒）
    /// </summary>
    public int SendCooldownSeconds { get; set; } = 4;

    /// <summary>
    /// 显示持续时间（秒）
    /// </summary>
    public int DisplayDurationSeconds { get; set; } = 5;

    /// <summary>
    /// 发送消息后清除方式: Timeout(自动超时), Immediate(立即清除)
    /// </summary>
    public string ClearModeOnSend { get; set; } = "Immediate";
}

/// <summary>
/// 文件传输设置
/// </summary>
public class FileTransferPreferences
{
    /// <summary>
    /// 分块大小 (KB)
    /// </summary>
    public int ChunkSizeKB { get; set; } = 64;

    /// <summary>
    /// 单块超时时间（秒）
    /// </summary>
    public int ChunkTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// 全局超时时间（秒）
    /// </summary>
    public int GlobalTimeoutSeconds { get; set; } = 600;

    /// <summary>
    /// 最大重试次数
    /// </summary>
    public int MaxRetryCount { get; set; } = 3;
}

/// <summary>
/// 通知设置
/// </summary>
public class NotificationPreferences
{
    /// <summary>
    /// 启用消息提示音
    /// </summary>
    public bool EnableSound { get; set; } = true;

    /// <summary>
    /// 启用桌面通知
    /// </summary>
    public bool EnableDesktopNotification { get; set; } = true;

    /// <summary>
    /// @提及时高亮显示
    /// </summary>
    public bool HighlightMention { get; set; } = true;
}

/// <summary>
/// 记忆的房间（进过的房间，点击可再次直接进入）。
/// 存储进房所需的完整信息：服务器地址 + 字符串房间ID + 频道ID + 用户ID。
/// </summary>
public class RecentRoom
{
    /// <summary>字符串房间ID（如 room_001）</summary>
    public string RoomId { get; set; } = string.Empty;
    public string RoomName { get; set; } = string.Empty;
    public string ServerAddress { get; set; } = string.Empty;
    public string ChannelId { get; set; } = "channel_lobby";
    public string UserId { get; set; } = string.Empty;
    public DateTime LastJoinedAt { get; set; }

    /// <summary>列表显示用：房间名为空时回退显示房间ID</summary>
    public string DisplayName => string.IsNullOrWhiteSpace(RoomName) ? RoomId : RoomName;
    /// <summary>头像占位字：取房间名/ID 首字符</summary>
    public string AvatarText => string.IsNullOrWhiteSpace(DisplayName) ? "?" : DisplayName.Substring(0, 1).ToUpperInvariant();
    public string LastJoinedDisplay => LastJoinedAt.ToString("yyyy-MM-dd HH:mm");
}
