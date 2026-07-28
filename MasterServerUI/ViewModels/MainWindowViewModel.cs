using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MasterIM.Server;
using MasterIM.Models;
using MasterServerUI.Models;

namespace MasterServerUI.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly MasterServerInstance _server;
    private readonly AppSettings _settings;
    private Timer? _refreshTimer;
    private bool _autoRefresh = true;

    [ObservableProperty] private string _title = "TRPGMaster Server - 服务器管理控制台";
    [ObservableProperty] private string _serverStatus = "已停止";
    [ObservableProperty] private string _serverStatusColor = "#ED4245";
    [ObservableProperty] private int _port;
    [ObservableProperty] private int _onlineCount;
    [ObservableProperty] private int _roomCount;
    [ObservableProperty] private string _cpuUsage = "0%";
    [ObservableProperty] private string _memoryUsage = "0MB";
    [ObservableProperty] private RoomItemViewModel? _selectedRoom;
    [ObservableProperty] private bool _isServerRunning;

    public ObservableCollection<RoomItemViewModel> Rooms { get; } = new();
    public ObservableCollection<LogEntryViewModel> LogEntries { get; } = new();
    public ObservableCollection<MemberItemViewModel> RoomMembers { get; } = new();

    public MainWindowViewModel(MasterServerInstance server, AppSettings settings)
    {
        _server = server;
        _settings = settings;
        _port = settings.Network.Port;

        _server.StateChanged += OnServerStateChanged;

        _autoRefresh = settings.UI.AutoRefresh;
        _refreshTimer = new Timer(RefreshTimerCallback, null,
            TimeSpan.FromSeconds(settings.UI.RefreshIntervalSeconds),
            TimeSpan.FromSeconds(settings.UI.RefreshIntervalSeconds));

        AddLogEntry("UI", "ServerUI 已启动", LogLevel.Info);
    }

    private void RefreshTimerCallback(object? state)
    {
        if (_autoRefresh && _server.State == ServerState.Running)
        {
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                UpdateStatistics();
                RefreshRoomListInternal();
            });
        }
    }

    partial void OnSelectedRoomChanged(RoomItemViewModel? value)
    {
        RoomMembers.Clear();
        if (value == null) return;
        RefreshRoomMembers(value.RoomId);
    }

    private void RefreshRoomMembers(string roomId)
    {
        RoomMembers.Clear();
        if (_server.State != ServerState.Running) return;

        foreach (var m in _server.GetRoomMembers(roomId))
        {
            RoomMembers.Add(new MemberItemViewModel
            {
                SessionId = m.ConnectionId,
                Username = m.UserId,
                UserId = m.UserId,
                JoinedAt = m.JoinedAt,
                IsOnline = true
            });
        }
    }

    private void OnServerStateChanged(object? sender, ServerStateChangedEventArgs e)
    {
        IsServerRunning = e.NewState == ServerState.Running;
        ServerStatus = e.NewState switch
        {
            ServerState.Stopped => "已停止",
            ServerState.Starting => "启动中...",
            ServerState.Running => "运行中",
            ServerState.Stopping => "停止中...",
            ServerState.Error => "错误",
            _ => "未知"
        };
        ServerStatusColor = e.NewState switch
        {
            ServerState.Running => "#3BA55D",
            ServerState.Error => "#ED4245",
            _ => "#FFA500"
        };
        AddLogEntry("Server", $"服务器状态: {e.OldState} -> {e.NewState}",
            e.NewState == ServerState.Error ? LogLevel.Error : LogLevel.Info);
    }

    // ========== 服务器启停 ==========

    [RelayCommand]
    private async Task StartServer()
    {
        try
        {
            AddLogEntry("Server", "正在启动服务器...", LogLevel.Info);
            await _server.StartAsync(_settings.Network.Port);
            UpdateStatistics();
        }
        catch (Exception ex)
        {
            AddLogEntry("Server", $"启动失败: {ex.Message}", LogLevel.Error);
        }
    }

    [RelayCommand]
    private async Task StopServer()
    {
        try
        {
            AddLogEntry("Server", "正在停止服务器...", LogLevel.Info);
            await _server.StopAsync();
        }
        catch (Exception ex)
        {
            AddLogEntry("Server", $"停止失败: {ex.Message}", LogLevel.Error);
        }
    }

    [RelayCommand]
    private async Task RestartServer()
    {
        await StopServer();
        await Task.Delay(500);
        await StartServer();
    }

    // ========== 房间列表 ==========

    [RelayCommand]
    private void RefreshRoomList()
    {
        AddLogEntry("UI", "刷新房间列表", LogLevel.Info);
        RefreshRoomListInternal();
        UpdateStatistics();
    }

    private async void RefreshRoomListInternal()
    {
        if (_server.State != ServerState.Running) return;
        try
        {
            var rooms = await _server.GetAllRoomsAsync();
            var existingIds = Rooms.Select(r => r.RoomId).ToHashSet();
            var serverIds = rooms.Select(r => r.RoomId).ToHashSet();

            foreach (var room in Rooms.ToList())
                if (!serverIds.Contains(room.RoomId))
                    Rooms.Remove(room);

            foreach (var room in rooms)
            {
                var existing = Rooms.FirstOrDefault(r => r.RoomId == room.RoomId);
                var online = _server.IsRoomOnline(room.RoomId);
                var playerCount = _server.GetRoomMembers(room.RoomId).Count;

                if (existing != null)
                {
                    existing.RoomName = room.RoomName;
                    existing.PlayerCount = playerCount;
                    existing.CreatorName = room.OwnerId;
                    existing.StatusColor = online ? "#3BA55D" : "#888888";
                    existing.IsOnline = online;
                }
                else
                {
                    Rooms.Add(new RoomItemViewModel
                    {
                        RoomId = room.RoomId,
                        RoomName = room.RoomName,
                        CreatorName = room.OwnerId,
                        PlayerCount = playerCount,
                        StartTime = room.CreatedAt.ToString("HH:mm"),
                        StatusColor = online ? "#3BA55D" : "#888888",
                        IsOnline = online
                    });
                }
            }
            RoomCount = rooms.Count;
        }
        catch (Exception ex)
        {
            AddLogEntry("Error", $"刷新房间列表失败: {ex.Message}", LogLevel.Error);
        }
    }

    // ========== 房间操作 ==========

    [RelayCommand]
    private async Task CreateRoom()
    {
        if (_server.State != ServerState.Running)
        {
            AddLogEntry("Error", "服务器未运行，无法创建房间", LogLevel.Error);
            return;
        }
        await ShowRoomDialog(null);
    }

    [RelayCommand]
    private async Task EditRoom()
    {
        if (SelectedRoom == null)
        {
            AddLogEntry("Warning", "请先选择要编辑的房间", LogLevel.Warning);
            return;
        }
        // 查出完整 Room 对象预填弹窗
        var rooms = await _server.GetAllRoomsAsync();
        var room = rooms.FirstOrDefault(r => r.RoomId == SelectedRoom.RoomId);
        if (room == null) return;
        await ShowRoomDialog(room);
    }

    private async Task ShowRoomDialog(MasterIM.Models.Room? existing)
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is not
            Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            || desktop.MainWindow == null) return;

        var dialogVm = existing == null
            ? new CreateRoomDialogViewModel()
            : new CreateRoomDialogViewModel(existing);

        var dialog = new Views.CreateRoomDialog { DataContext = dialogVm };
        bool done = false;

        dialogVm.Confirmed += async (name, maxPlayers, description, gameSystem, isPublic, password, roomId) =>
        {
            try
            {
                if (existing == null)
                {
                    // 创建：用弹窗填的 RoomId
                    var room = new MasterIM.Models.Room
                    {
                        RoomId = roomId!,
                        RoomName = name,
                        Description = description ?? "",
                        Password = password ?? "",
                        OwnerId = "admin",
                        HostUserId = "admin",
                        IsPublic = isPublic,
                        RequireApproval = false,
                        CreatedAt = DateTime.UtcNow
                    };
                    await _server.GetAllRoomsAsync(); // ensure store ready
                    // 直接通过 RoomStore 路径创建（有自定义 ID）
                    var created = await _server.CreateRoomAsync(
                        name, "admin", maxPlayers, description, isPublic, password, roomId!);
                    AddLogEntry("Room", $"已创建房间 '{created.RoomName}' (ID: {created.RoomId})", LogLevel.Info);
                }
                else
                {
                    // 编辑：更新
                    existing.RoomName = name;
                    existing.Description = description ?? "";
                    existing.IsPublic = isPublic;
                    existing.Password = password ?? "";
                    await _server.UpdateRoomAsync(existing);
                    AddLogEntry("Room", $"已更新房间 '{name}'", LogLevel.Info);
                }
                done = true;
                dialog.SetResultAndClose(true);
            }
            catch (Exception ex)
            {
                dialogVm.SetError($"操作失败: {ex.Message}");
            }
        };
        dialogVm.Cancelled += () => dialog.SetResultAndClose(false);

        await dialog.ShowDialog<bool?>(desktop.MainWindow);
        if (done) RefreshRoomListInternal();
    }

    [RelayCommand]
    private async Task DeleteRoom()
    {
        if (SelectedRoom == null)
        {
            AddLogEntry("Warning", "请先选择房间", LogLevel.Warning);
            return;
        }
        if (_server.State != ServerState.Running)
        {
            AddLogEntry("Error", "服务器未运行", LogLevel.Error);
            return;
        }
        try
        {
            var name = SelectedRoom.RoomName;
            await _server.DeleteRoomAsync(SelectedRoom.RoomId);
            AddLogEntry("Room", $"已删除房间: {name}", LogLevel.Info);
            SelectedRoom = null;
            RefreshRoomListInternal();
        }
        catch (Exception ex)
        {
            AddLogEntry("Error", $"删除房间失败: {ex.Message}", LogLevel.Error);
        }
    }

    [RelayCommand]
    private void ToggleRoom()
    {
        if (SelectedRoom == null) return;
        if (SelectedRoom.IsOnline)
        {
            _server.CloseRoom(SelectedRoom.RoomId);
            SelectedRoom.IsOnline = false;
            SelectedRoom.StatusColor = "#888888";
            RoomMembers.Clear();
            AddLogEntry("Room", $"已关闭房间: {SelectedRoom.RoomName}", LogLevel.Info);
        }
        else
        {
            _server.OpenRoom(SelectedRoom.RoomId);
            SelectedRoom.IsOnline = true;
            SelectedRoom.StatusColor = "#3BA55D";
            AddLogEntry("Room", $"已开启房间: {SelectedRoom.RoomName}", LogLevel.Info);
        }
        UpdateStatistics();
    }

    [RelayCommand]
    private void OpenRoom()
    {
        if (SelectedRoom == null) return;
        _server.OpenRoom(SelectedRoom.RoomId);
        SelectedRoom.IsOnline = true;
        SelectedRoom.StatusColor = "#3BA55D";
        AddLogEntry("Room", $"已开启房间: {SelectedRoom.RoomName}", LogLevel.Info);
        UpdateStatistics();
    }

    [RelayCommand]
    private void CloseRoom()
    {
        if (SelectedRoom == null) return;
        _server.CloseRoom(SelectedRoom.RoomId);
        SelectedRoom.IsOnline = false;
        SelectedRoom.StatusColor = "#888888";
        RoomMembers.Clear();
        AddLogEntry("Room", $"已关闭房间: {SelectedRoom.RoomName}", LogLevel.Info);
        UpdateStatistics();
    }

    // ========== 成员操作 ==========

    [RelayCommand]
    private async Task KickMember(MemberItemViewModel? member)
    {
        if (member == null || _server.State != ServerState.Running) return;
        try
        {
            var ok = await _server.KickPlayerAsync(member.SessionId, "被管理员踢出");
            if (ok)
            {
                AddLogEntry("Room", $"已踢出成员: {member.Username}", LogLevel.Info);
                if (SelectedRoom != null) RefreshRoomMembers(SelectedRoom.RoomId);
            }
        }
        catch (Exception ex)
        {
            AddLogEntry("Error", $"踢出失败: {ex.Message}", LogLevel.Error);
        }
    }

    // ========== 广播 ==========

    [RelayCommand]
    private async Task BroadcastToRoom()
    {
        if (SelectedRoom == null || _server.State != ServerState.Running) return;
        try
        {
            await _server.BroadcastSystemMessageAsync(SelectedRoom.RoomId,
                "[系统] 来自管理员的广播消息");
            AddLogEntry("Room", $"已向 {SelectedRoom.RoomName} 发送广播", LogLevel.Info);
        }
        catch (Exception ex)
        {
            AddLogEntry("Error", $"广播失败: {ex.Message}", LogLevel.Error);
        }
    }

    // ========== 日志 ==========

    [RelayCommand]
    private void ClearLogs()
    {
        LogEntries.Clear();
        AddLogEntry("UI", "日志已清空", LogLevel.Info);
    }

    private void AddLogEntry(string category, string message, LogLevel level)
    {
        if (LogEntries.Count > 1000) LogEntries.RemoveAt(0);
        LogEntries.Add(new LogEntryViewModel
        {
            Timestamp = DateTime.Now,
            Category = category,
            Message = message,
            Level = level
        });
    }

    // ========== 统计 ==========

    private void UpdateStatistics()
    {
        if (_server.State != ServerState.Running) return;
        var stats = _server.GetStatistics();
        OnlineCount = stats.ActiveConnections;
        RoomCount = stats.TotalRooms;
        CpuUsage = $"{stats.CpuUsagePercent:F1}%";
        MemoryUsage = $"{stats.MemoryUsageBytes / 1024 / 1024}MB";
    }

    // ========== 设置 ==========

    [RelayCommand]
    private async Task OpenSettings()
    {
        AddLogEntry("UI", "打开服务器设置", LogLevel.Info);
        var vm = new SettingsViewModel(_settings);
        var win = new Views.SettingsWindow(vm);
        if (Avalonia.Application.Current?.ApplicationLifetime is
            Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            && desktop.MainWindow != null)
        {
            var ok = await win.ShowDialog<bool?>(desktop.MainWindow);
            if (ok == true)
            {
                _autoRefresh = _settings.UI.AutoRefresh;
                _refreshTimer?.Change(
                    TimeSpan.FromSeconds(_settings.UI.RefreshIntervalSeconds),
                    TimeSpan.FromSeconds(_settings.UI.RefreshIntervalSeconds));
                AddLogEntry("UI", "设置已保存", LogLevel.Info);
            }
        }
    }

    // 表情库管理（待接入后端）
    [RelayCommand]
    private void OpenEmojiManager()
    {
        AddLogEntry("UI", "表情库管理功能待接入", LogLevel.Warning);
    }
}

// ========== 子 ViewModel ==========

public partial class RoomItemViewModel : ObservableObject
{
    [ObservableProperty] private string _roomId = string.Empty;
    [ObservableProperty] private string _roomName = string.Empty;
    [ObservableProperty] private string _creatorName = string.Empty;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(PlayerCountDisplay))]
    private int _playerCount;
    [ObservableProperty] private int _maxPlayers;
    [ObservableProperty] private string _startTime = string.Empty;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(OnlineLabel), nameof(ToggleLabel), nameof(ToggleIcon))]
    private string _statusColor = "#888888";
    [ObservableProperty][NotifyPropertyChangedFor(nameof(OnlineLabel), nameof(ToggleLabel), nameof(ToggleIcon))]
    private bool _isOnline;

    public string PlayerCountDisplay => $"{PlayerCount}人";
    public string OnlineLabel => IsOnline ? "运行中" : "已关闭";
    public string ToggleLabel => IsOnline ? "关闭房间" : "开启房间";
    public string ToggleIcon => IsOnline ? "⏸" : "▶";
}

public partial class MemberItemViewModel : ObservableObject
{
    [ObservableProperty] private string _sessionId = string.Empty;
    [ObservableProperty] private string _username = string.Empty;
    [ObservableProperty] private string _userId = string.Empty;
    [ObservableProperty] private DateTime _joinedAt;
    [ObservableProperty] private bool _isOnline;

    public string OnlineStatus => IsOnline ? "在线" : "离线";
    public string OnlineStatusColor => IsOnline ? "#3BA55D" : "#888888";
}

public partial class LogEntryViewModel : ObservableObject
{
    [ObservableProperty] private DateTime _timestamp;
    [ObservableProperty] private string _category = string.Empty;
    [ObservableProperty] private string _message = string.Empty;
    [ObservableProperty] private LogLevel _level;

    public string TimeString => Timestamp.ToString("HH:mm:ss");
    public string LevelString => Level switch
    {
        LogLevel.Debug => "DEBUG", LogLevel.Info => "INFO",
        LogLevel.Warning => "WARN", LogLevel.Error => "ERROR", _ => "INFO"
    };
    public string LevelColor => Level switch
    {
        LogLevel.Debug => "#888888", LogLevel.Info => "#3BA55D",
        LogLevel.Warning => "#FFA500", LogLevel.Error => "#ED4245", _ => "#3BA55D"
    };
}

public enum LogLevel { Debug, Info, Warning, Error }
