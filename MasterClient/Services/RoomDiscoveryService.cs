using System.Net;
using System.Net.Sockets;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using MasterClient.Models;

namespace MasterClient.Services;

public partial class RoomDiscoveryService : ObservableObject
{
    private const int DiscoveryPort = 7776;
    private const string DiscoveryMessage = "TRPG_DISCOVER";
    private const string QueryMessage = "TRPG_QUERY";
    private const int ConnectionTimeout = 5000; // 5秒超时

    private UdpClient? _udpClient;
    private CancellationTokenSource? _scanCts;

    [ObservableProperty]
    private bool _isScanning;

    [ObservableProperty]
    private bool _isConnecting;

    [ObservableProperty]
    private string _connectionError = string.Empty;

    public event Action<RoomInfo>? RoomDiscovered;
    public event Action<string>? ConnectionFailed;

    public async Task StartScanAsync()
    {
        if (IsScanning) return;

        IsScanning = true;
        _scanCts = new CancellationTokenSource();

        try
        {
            _udpClient = new UdpClient();
            _udpClient.EnableBroadcast = true;

            // 发送广播发现消息
            var discoveryData = Encoding.UTF8.GetBytes(DiscoveryMessage);
            var broadcastEndpoint = new IPEndPoint(IPAddress.Broadcast, DiscoveryPort);

            await _udpClient.SendAsync(discoveryData, broadcastEndpoint, _scanCts.Token);

            // 监听响应
            var receiveTask = ReceiveResponsesAsync(_scanCts.Token);

            // 每2秒发送一次广播，共发送3次
            for (int i = 0; i < 3 && !_scanCts.Token.IsCancellationRequested; i++)
            {
                await Task.Delay(2000, _scanCts.Token);
                await _udpClient.SendAsync(discoveryData, broadcastEndpoint, _scanCts.Token);
            }

            // 等待最后一次广播的响应
            await Task.Delay(2000, _scanCts.Token);
        }
        catch (OperationCanceledException)
        {
            // 正常取消
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[RoomDiscovery] 扫描错误: {ex.Message}");
        }
        finally
        {
            StopScan();
        }
    }

    private async Task ReceiveResponsesAsync(CancellationToken ct)
    {
        if (_udpClient == null) return;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var result = await _udpClient.ReceiveAsync(ct);
                var response = Encoding.UTF8.GetString(result.Buffer);

                // 解析响应，格式: TRPG_SERVER|RoomName|GMName|PlayerCount|MaxPlayers|Port
                if (response.StartsWith("TRPG_SERVER|"))
                {
                    var parts = response.Split('|');
                    if (parts.Length >= 6)
                    {
                        var room = new RoomInfo
                        {
                            Id = Guid.NewGuid(),
                            Name = parts[1],
                            GMName = parts[2],
                            PlayerCount = int.TryParse(parts[3], out var pc) ? pc : 0,
                            MaxPlayers = int.TryParse(parts[4], out var mp) ? mp : 6,
                            ServerAddress = $"{result.RemoteEndPoint.Address}:{parts[5]}",
                            Type = RoomType.LAN,
                            IsOnline = true,
                            LastActivity = DateTime.Now
                        };

                        RoomDiscovered?.Invoke(room);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RoomDiscovery] 接收错误: {ex.Message}");
            }
        }
    }

    public void StopScan()
    {
        _scanCts?.Cancel();
        _udpClient?.Close();
        _udpClient?.Dispose();
        _udpClient = null;
        IsScanning = false;
    }

    /// <summary>
    /// 直接连接到指定服务器并验证
    /// </summary>
    public async Task<RoomInfo?> ConnectDirectAsync(string address)
    {
        if (IsConnecting) return null;

        ConnectionError = string.Empty;
        IsConnecting = true;

        try
        {
            // 解析地址
            var (ip, port) = ParseAddress(address);
            if (ip == null || port <= 0)
            {
                ConnectionError = "地址格式无效，请使用 IP:端口 格式（如 192.168.1.100:7777）";
                ConnectionFailed?.Invoke(ConnectionError);
                return null;
            }

            System.Diagnostics.Debug.WriteLine($"[RoomDiscovery] 正在连接: {ip}:{port}");

            // 尝试TCP连接验证
            using var cts = new CancellationTokenSource(ConnectionTimeout);

            try
            {
                using var tcpClient = new TcpClient();
                await tcpClient.ConnectAsync(ip, port, cts.Token);

                // 连接成功，发送查询消息获取房间信息
                var stream = tcpClient.GetStream();
                var queryData = Encoding.UTF8.GetBytes(QueryMessage);
                await stream.WriteAsync(queryData, cts.Token);

                // 读取响应
                var buffer = new byte[1024];
                var bytesRead = await stream.ReadAsync(buffer, cts.Token);

                if (bytesRead > 0)
                {
                    var response = Encoding.UTF8.GetString(buffer, 0, bytesRead);

                    // 解析响应，格式: TRPG_INFO|RoomName|GMName|PlayerCount|MaxPlayers|RuleSystem
                    if (response.StartsWith("TRPG_INFO|"))
                    {
                        var parts = response.Split('|');
                        if (parts.Length >= 5)
                        {
                            var room = new RoomInfo
                            {
                                Id = Guid.NewGuid(),
                                Name = parts[1],
                                GMName = parts[2],
                                PlayerCount = int.TryParse(parts[3], out var pc) ? pc : 0,
                                MaxPlayers = int.TryParse(parts[4], out var mp) ? mp : 6,
                                ServerAddress = address,
                                Type = RoomType.Remote,
                                IsOnline = true,
                                LastActivity = DateTime.Now
                            };

                            System.Diagnostics.Debug.WriteLine($"[RoomDiscovery] 连接成功: {room.Name}");
                            return room;
                        }
                    }
                }

                // 服务器响应但格式不对，可能是旧版本服务器
                // 返回基本信息
                System.Diagnostics.Debug.WriteLine("[RoomDiscovery] 服务器响应格式未知，使用默认信息");
                return new RoomInfo
                {
                    Id = Guid.NewGuid(),
                    Name = "远程世界",
                    GMName = "未知",
                    ServerAddress = address,
                    PlayerCount = 0,
                    MaxPlayers = 6,
                    Type = RoomType.Remote,
                    IsOnline = true,
                    LastActivity = DateTime.Now
                };
            }
            catch (OperationCanceledException)
            {
                ConnectionError = "连接超时，请检查服务器地址和端口是否正确";
                ConnectionFailed?.Invoke(ConnectionError);
                System.Diagnostics.Debug.WriteLine($"[RoomDiscovery] 连接超时: {address}");
                return null;
            }
            catch (SocketException ex)
            {
                ConnectionError = GetSocketErrorMessage(ex.SocketErrorCode);
                ConnectionFailed?.Invoke(ConnectionError);
                System.Diagnostics.Debug.WriteLine($"[RoomDiscovery] Socket错误: {ex.SocketErrorCode} - {ex.Message}");
                return null;
            }
        }
        catch (Exception ex)
        {
            ConnectionError = $"连接失败: {ex.Message}";
            ConnectionFailed?.Invoke(ConnectionError);
            System.Diagnostics.Debug.WriteLine($"[RoomDiscovery] 直接连接错误: {ex.Message}");
            return null;
        }
        finally
        {
            IsConnecting = false;
        }
    }

    /// <summary>
    /// 解析地址字符串
    /// </summary>
    private (string? ip, int port) ParseAddress(string address)
    {
        if (string.IsNullOrWhiteSpace(address))
            return (null, 0);

        var parts = address.Trim().Split(':');
        if (parts.Length != 2)
            return (null, 0);

        var ip = parts[0].Trim();
        if (!int.TryParse(parts[1].Trim(), out var port) || port <= 0 || port > 65535)
            return (null, 0);

        // 验证IP地址格式
        if (!IPAddress.TryParse(ip, out _))
        {
            // 尝试解析为域名
            try
            {
                var hostEntry = Dns.GetHostEntry(ip);
                if (hostEntry.AddressList.Length > 0)
                {
                    ip = hostEntry.AddressList[0].ToString();
                }
                else
                {
                    return (null, 0);
                }
            }
            catch
            {
                return (null, 0);
            }
        }

        return (ip, port);
    }

    /// <summary>
    /// 获取Socket错误的友好提示信息
    /// </summary>
    private string GetSocketErrorMessage(SocketError errorCode)
    {
        return errorCode switch
        {
            SocketError.ConnectionRefused => "连接被拒绝，服务器可能未启动或端口错误",
            SocketError.HostUnreachable => "无法到达目标主机，请检查网络连接",
            SocketError.NetworkUnreachable => "网络不可达，请检查网络设置",
            SocketError.TimedOut => "连接超时，请检查服务器地址和防火墙设置",
            SocketError.HostNotFound => "找不到主机，请检查服务器地址",
            SocketError.AddressNotAvailable => "地址不可用",
            _ => $"网络错误 ({errorCode})"
        };
    }

    /// <summary>
    /// 检测服务器是否在线（快速ping检测）
    /// </summary>
    public async Task<bool> CheckServerOnlineAsync(string address, int timeoutMs = 2000)
    {
        try
        {
            var (ip, port) = ParseAddress(address);
            if (ip == null || port <= 0)
                return false;

            using var cts = new CancellationTokenSource(timeoutMs);
            using var tcpClient = new TcpClient();

            await tcpClient.ConnectAsync(ip, port, cts.Token);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
