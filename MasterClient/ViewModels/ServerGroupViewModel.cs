using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using MasterClient.Models;

namespace MasterClient.ViewModels;

/// <summary>
/// 服务器分组视图模型（用于"已知的世界"页面）
/// </summary>
public partial class ServerGroupViewModel : ObservableObject
{
    /// <summary>
    /// 服务器记录（持久化数据）
    /// </summary>
    public ServerRecord Record { get; }

    [ObservableProperty]
    private string _displayName = string.Empty;

    [ObservableProperty]
    private string _address = string.Empty;

    [ObservableProperty]
    private ServerStatus _status = ServerStatus.Unknown;

    [ObservableProperty]
    private string _statusText = "未检测";

    [ObservableProperty]
    private bool _isExpanded = true;

    [ObservableProperty]
    private bool _isChecking = false;

    [ObservableProperty]
    private bool _isFavorite = false;

    [ObservableProperty]
    private int _onlineRoomCount = 0;

    [ObservableProperty]
    private int _totalRoomCount = 0;

    [ObservableProperty]
    private string _lastCheckedDisplay = "从未检测";

    [ObservableProperty]
    private StorageSource _source = StorageSource.Local;

    /// <summary>
    /// 该服务器下的房间列表
    /// </summary>
    public ObservableCollection<RoomItemViewModel> Rooms { get; } = new();

    public ServerGroupViewModel(ServerRecord record)
    {
        Record = record;
        _displayName = string.IsNullOrEmpty(record.DisplayName) ? record.Address : record.DisplayName;
        _address = record.Address;
        _isFavorite = record.IsFavorite;
        _totalRoomCount = record.Rooms.Count;
        _source = record.Source;

        // 初始化房间列表
        foreach (var room in record.Rooms)
        {
            Rooms.Add(new RoomItemViewModel(room, record.Address));
        }

        UpdateLastCheckedDisplay();
    }

    /// <summary>
    /// 状态颜色
    /// </summary>
    public string StatusColor => Status switch
    {
        ServerStatus.Online => "#3BA55D",
        ServerStatus.Offline => "#888888",
        ServerStatus.Checking => "#FFA500",
        ServerStatus.Error => "#ED4245",
        _ => "#6b7280"
    };

    /// <summary>
    /// 状态图标
    /// </summary>
    public string StatusIcon => Status switch
    {
        ServerStatus.Online => "🟢",
        ServerStatus.Offline => "⚫",
        ServerStatus.Checking => "🔄",
        ServerStatus.Error => "🔴",
        _ => "⚪"
    };

    /// <summary>
    /// 服务器类型图标
    /// </summary>
    public string TypeIcon => Record.Type switch
    {
        ServerType.Local => "🏠",
        ServerType.LAN => "🌐",
        ServerType.Remote => "🖥️",
        ServerType.Official => "⭐",
        _ => "🖥️"
    };

    /// <summary>
    /// 存储来源图标
    /// </summary>
    public string SourceIcon => Source switch
    {
        StorageSource.Cloud => "☁️",
        StorageSource.Local => "💾",
        _ => "💾"
    };

    /// <summary>
    /// 存储来源描述
    /// </summary>
    public string SourceText => Source switch
    {
        StorageSource.Cloud => "云端同步",
        StorageSource.Local => "本地存储",
        _ => "本地存储"
    };

    /// <summary>
    /// 是否为云端存储
    /// </summary>
    public bool IsCloudSource => Source == StorageSource.Cloud;

    /// <summary>
    /// 是否为本地存储
    /// </summary>
    public bool IsLocalSource => Source == StorageSource.Local;

    /// <summary>
    /// 更新最后检测时间显示
    /// </summary>
    public void UpdateLastCheckedDisplay()
    {
        if (Record.LastCheckedAt == default)
        {
            LastCheckedDisplay = "从未检测";
        }
        else
        {
            var diff = DateTime.Now - Record.LastCheckedAt;
            if (diff.TotalMinutes < 1)
                LastCheckedDisplay = "刚刚";
            else if (diff.TotalHours < 1)
                LastCheckedDisplay = $"{(int)diff.TotalMinutes}分钟前";
            else if (diff.TotalDays < 1)
                LastCheckedDisplay = $"{(int)diff.TotalHours}小时前";
            else
                LastCheckedDisplay = Record.LastCheckedAt.ToString("MM-dd HH:mm");
        }
    }

    /// <summary>
    /// 更新服务器状态
    /// </summary>
    public void UpdateStatus(ServerStatus status, string? statusText = null)
    {
        Status = status;
        StatusText = statusText ?? status switch
        {
            ServerStatus.Online => "在线",
            ServerStatus.Offline => "离线",
            ServerStatus.Checking => "检测中...",
            ServerStatus.Error => "连接错误",
            _ => "未检测"
        };
        OnPropertyChanged(nameof(StatusColor));
        OnPropertyChanged(nameof(StatusIcon));
    }

    /// <summary>
    /// 更新房间列表（从服务器获取最新数据后）
    /// </summary>
    public void UpdateRooms(IEnumerable<ServerRoomInfo> serverRooms)
    {
        var serverRoomIds = serverRooms.Select(r => r.Id).ToHashSet();
        var existingRoomIds = Rooms.Select(r => r.RoomId).ToHashSet();

        // 标记不存在的房间
        foreach (var roomVm in Rooms)
        {
            if (!serverRoomIds.Contains(roomVm.RoomId))
            {
                roomVm.UpdateStatus(Models.RoomStatus.NotFound);
            }
        }

        // 更新/添加存在的房间
        foreach (var serverRoom in serverRooms)
        {
            var existingVm = Rooms.FirstOrDefault(r => r.RoomId == serverRoom.Id);
            if (existingVm != null)
            {
                // 更新现有房间
                existingVm.UpdateFromServerRoom(serverRoom);
            }
            else
            {
                // 检查是否在记录中
                var recordRoom = Record.Rooms.FirstOrDefault(r => r.RoomId == serverRoom.Id);
                if (recordRoom != null)
                {
                    // 更新记录并创建 ViewModel
                    recordRoom.RoomName = serverRoom.Name;
                    recordRoom.GMName = serverRoom.OwnerName ?? "";
                    recordRoom.MaxPlayers = serverRoom.MaxPlayers;
                    var newVm = new RoomItemViewModel(recordRoom, Address);
                    newVm.UpdateFromServerRoom(serverRoom);
                    Rooms.Add(newVm);
                }
                // 如果不在记录中，不添加（只显示曾经加入过的房间）
            }
        }

        // 更新统计
        OnlineRoomCount = Rooms.Count(r => r.Status == Models.RoomStatus.Available || r.Status == Models.RoomStatus.Full);
        TotalRoomCount = Rooms.Count;
    }
}

/// <summary>
/// 房间项视图模型
/// </summary>
public partial class RoomItemViewModel : ObservableObject
{
    /// <summary>
    /// 房间记录（持久化数据）
    /// </summary>
    public RoomRecord Record { get; }

    /// <summary>
    /// 所属服务器地址
    /// </summary>
    public string ServerAddress { get; }

    [ObservableProperty]
    private Guid _roomId;

    [ObservableProperty]
    private string _roomName = string.Empty;

    [ObservableProperty]
    private string _gmName = string.Empty;

    [ObservableProperty]
    private int _currentPlayers = 0;

    [ObservableProperty]
    private int _maxPlayers = 0;

    [ObservableProperty]
    private string _gameSystem = string.Empty;

    [ObservableProperty]
    private Models.RoomStatus _status = Models.RoomStatus.Unknown;

    [ObservableProperty]
    private string _statusText = "未知";

    [ObservableProperty]
    private bool _isFavorite = false;

    [ObservableProperty]
    private string _lastJoinedDisplay = string.Empty;

    public RoomItemViewModel(RoomRecord record, string serverAddress)
    {
        Record = record;
        ServerAddress = serverAddress;
        _roomId = record.RoomId;
        _roomName = record.RoomName;
        _gmName = record.GMName;
        _maxPlayers = record.MaxPlayers;
        _gameSystem = record.GameSystem;
        _isFavorite = record.IsFavorite;

        UpdateLastJoinedDisplay();
    }

    /// <summary>
    /// 玩家数显示
    /// </summary>
    public string PlayerCountDisplay => Status == Models.RoomStatus.NotFound ? "—" : $"{CurrentPlayers}/{MaxPlayers}";

    /// <summary>
    /// 状态颜色
    /// </summary>
    public string StatusColor => Status switch
    {
        Models.RoomStatus.Available => "#3BA55D",
        Models.RoomStatus.Full => "#FFA500",
        Models.RoomStatus.PasswordRequired => "#5865F2",
        Models.RoomStatus.NotFound => "#888888",
        Models.RoomStatus.InProgress => "#9B59B6",
        _ => "#6b7280"
    };

    /// <summary>
    /// 房间名称颜色（不存在的房间显示灰色）
    /// </summary>
    public string NameColor => Status == Models.RoomStatus.NotFound ? "#666666" : "#FFFFFF";

    /// <summary>
    /// 是否可以加入
    /// </summary>
    public bool CanJoin => Status == Models.RoomStatus.Available || Status == Models.RoomStatus.PasswordRequired;

    /// <summary>
    /// 状态图标
    /// </summary>
    public string StatusIcon => Status switch
    {
        Models.RoomStatus.Available => "🟢",
        Models.RoomStatus.Full => "🟡",
        Models.RoomStatus.PasswordRequired => "🔐",
        Models.RoomStatus.NotFound => "❌",
        Models.RoomStatus.InProgress => "🎮",
        _ => "⚪"
    };

    /// <summary>
    /// 更新最后加入时间显示
    /// </summary>
    public void UpdateLastJoinedDisplay()
    {
        if (Record.LastJoinedAt == default)
        {
            LastJoinedDisplay = "";
        }
        else
        {
            var diff = DateTime.Now - Record.LastJoinedAt;
            if (diff.TotalDays < 1)
                LastJoinedDisplay = "今天";
            else if (diff.TotalDays < 2)
                LastJoinedDisplay = "昨天";
            else if (diff.TotalDays < 7)
                LastJoinedDisplay = $"{(int)diff.TotalDays}天前";
            else
                LastJoinedDisplay = Record.LastJoinedAt.ToString("MM-dd");
        }
    }

    /// <summary>
    /// 更新房间状态
    /// </summary>
    public void UpdateStatus(Models.RoomStatus status, string? statusText = null)
    {
        Status = status;
        StatusText = statusText ?? status switch
        {
            Models.RoomStatus.Available => "可加入",
            Models.RoomStatus.Full => "已满",
            Models.RoomStatus.PasswordRequired => "需要密码",
            Models.RoomStatus.NotFound => "已不存在",
            Models.RoomStatus.InProgress => "游戏中",
            _ => "未知"
        };
        OnPropertyChanged(nameof(StatusColor));
        OnPropertyChanged(nameof(NameColor));
        OnPropertyChanged(nameof(StatusIcon));
        OnPropertyChanged(nameof(CanJoin));
        OnPropertyChanged(nameof(PlayerCountDisplay));
    }

    /// <summary>
    /// 从服务器房间信息更新
    /// </summary>
    public void UpdateFromServerRoom(ServerRoomInfo serverRoom)
    {
        RoomName = serverRoom.Name;
        GmName = serverRoom.OwnerName ?? "";
        CurrentPlayers = serverRoom.CurrentPlayers;
        MaxPlayers = serverRoom.MaxPlayers;

        // 确定状态
        if (serverRoom.CurrentPlayers >= serverRoom.MaxPlayers)
        {
            UpdateStatus(Models.RoomStatus.Full);
        }
        else if (serverRoom.HasPassword)
        {
            UpdateStatus(Models.RoomStatus.PasswordRequired);
        }
        else
        {
            UpdateStatus(Models.RoomStatus.Available);
        }

        // 更新记录
        Record.RoomName = serverRoom.Name;
        Record.GMName = serverRoom.OwnerName ?? "";
        Record.MaxPlayers = serverRoom.MaxPlayers;
    }
}
