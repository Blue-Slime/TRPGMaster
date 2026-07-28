using MasterClient.Models;

namespace MasterClient.Services;

/// <summary>
/// MasterServer 连接服务（桩实现，待接入后端）。
///
/// 旧版依赖 TRPGMaster.Network / TRPGMaster.Core 协议层，新项目暂不引入这些依赖。
/// 本实现保留 MainWindowViewModel 需要的公共方法与事件签名，但内部返回
/// 空 / false / 空列表，表示功能尚未接通。后续接入 MasterIM.SDK 时在此填充逻辑。
/// </summary>
public class MasterServerConnectionService : IDisposable
{
    private readonly SettingsService _settingsService;
    private string _currentServerAddress = string.Empty;
    private bool _disposed;

    /// <summary>
    /// 是否已连接（桩：始终 false）
    /// </summary>
    public bool IsConnected => false;

    /// <summary>
    /// 当前会话ID（桩：始终 null）
    /// </summary>
    public string? SessionId => null;

    /// <summary>
    /// 当前服务器地址
    /// </summary>
    public string CurrentServerAddress => _currentServerAddress;

    /// <summary>
    /// 连接状态变化事件
    /// </summary>
    public event EventHandler<ConnectionStateChangedEventArgs>? ConnectionStateChanged;

    /// <summary>
    /// 连接错误事件
    /// </summary>
    public event EventHandler<ConnectionErrorEventArgs>? ConnectionError;

    public MasterServerConnectionService(SettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    /// <summary>
    /// 连接到 MasterServer（桩：不做任何实际连接，返回 false）
    /// </summary>
    public Task<bool> ConnectAsync(string serverAddress, CancellationToken cancellationToken = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(MasterServerConnectionService));

        _currentServerAddress = serverAddress;
        System.Diagnostics.Debug.WriteLine($"[MasterServerConnection] (桩) 连接请求: {serverAddress} — 功能待接入");

        // 待接入后端：此处应发起真实连接。当前返回 false 表示未连接。
        return Task.FromResult(false);
    }

    /// <summary>
    /// 连接到 MasterServer（无取消令牌重载）
    /// </summary>
    public Task<bool> ConnectAsync(string serverAddress)
        => ConnectAsync(serverAddress, CancellationToken.None);

    /// <summary>
    /// 断开连接（桩：无操作）
    /// </summary>
    public Task DisconnectAsync()
    {
        _currentServerAddress = string.Empty;
        return Task.CompletedTask;
    }

    /// <summary>
    /// 请求房间列表（桩：返回空列表）
    /// </summary>
    public Task<List<ServerRoomInfo>> RequestRoomListAsync()
    {
        return Task.FromResult(new List<ServerRoomInfo>());
    }

    // 事件保留供后续接入使用，避免"未使用"警告导致的困惑
    protected virtual void OnConnectionStateChanged(ConnectionStateChangedEventArgs e)
        => ConnectionStateChanged?.Invoke(this, e);

    protected virtual void OnConnectionError(ConnectionErrorEventArgs e)
        => ConnectionError?.Invoke(this, e);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
    }
}

#region Event Args

public class ConnectionStateChangedEventArgs : EventArgs
{
    public bool IsConnected { get; set; }
    public string? SessionId { get; set; }
    public string ServerAddress { get; set; } = string.Empty;
    public string? DisconnectReason { get; set; }
}

public class ConnectionErrorEventArgs : EventArgs
{
    public string ErrorMessage { get; set; } = string.Empty;
    public Exception? Exception { get; set; }
}

#endregion
