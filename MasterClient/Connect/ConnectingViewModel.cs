using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MasterIM.SDK;
using MasterClient.Chat;

namespace MasterClient.Connect;

/// <summary>
/// "正在连接"页的功能组件：房间连接的完整生命周期——
/// 连接 / 重试(最多3次) / 超时 / 取消 / 连上后开聊天窗口。
/// 自包含，不依赖 MainWindowViewModel 的其它状态。
/// </summary>
public partial class ConnectingViewModel : ObservableObject
{
    private const int MaxRetries = 3;

    private CancellationTokenSource? _cts;
    private IMClient? _connectingClient;

    // 记住上次连接参数，供错误界面"重试"复用
    private (string url, string room, string channel, string user)? _lastParams;

    /// <summary>是否正在连接（驱动"连接中"态的转圈）</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsActive))]
    private bool _isBusy;

    /// <summary>连接界面是否应显示：连接中 或 停在错误态。主窗口据此显隐连接界面。</summary>
    public bool IsActive => IsBusy || HasError;

    /// <summary>状态文本（正在连接/重试中…）</summary>
    [ObservableProperty] private string _statusText = "正在连接房间...";

    /// <summary>最终错误；非空时连接界面切到"错误态"（显示错误 + 重试/返回），不自动回门户</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    [NotifyPropertyChangedFor(nameof(IsActive))]
    private string _errorMessage = string.Empty;

    /// <summary>是否处于错误态（连接界面据此在"连接中"与"错误"两态间切换）</summary>
    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    /// <summary>连接成功 → 通知外层记忆房间；参数：server/room/channel/user</summary>
    public event Action<string, string, string, string>? Succeeded;
    /// <summary>离开连接界面回门户（仅成功后或用户手动返回时触发；失败时保持在界面上）</summary>
    public event Action? Finished;

    /// <summary>
    /// 发起一次房间连接。瘦状态机驱动：调用 IMClient.ConnectAsync 拿确定性 JoinOutcome，
    /// 按结果分派——成功开窗、永久失败报错、暂时性失败有限重试。
    /// </summary>
    public async Task ConnectAsync(string serverUrl, string roomId, string channelId, string userId)
    {
        _lastParams = (serverUrl, roomId, channelId, userId);

        // 取消上一次残留操作，并确定性拆除旧 client（等其 ReceiveLoop 退出由 IMClient 内部保证）
        _cts?.Cancel();
        try { _connectingClient?.Dispose(); } catch { }
        _connectingClient = null;

        var cts = new CancellationTokenSource();
        _cts = cts;
        var token = cts.Token;

        ErrorMessage = string.Empty;
        IsBusy = true;

        try
        {
            for (int attempt = 1; attempt <= MaxRetries; attempt++)
            {
                if (token.IsCancellationRequested) return;

                StatusText = attempt == 1
                    ? "正在连接房间..."
                    : $"正在重试 ({attempt}/{MaxRetries})...";

                var client = new IMClient();
                _connectingClient = client;

                // 一次确定性的连接 + 加入握手
                var outcome = await client.ConnectAsync(serverUrl, userId, roomId, channelId, token);

                if (token.IsCancellationRequested) { client.Dispose(); return; }

                switch (outcome.Result)
                {
                    case JoinResult.Success:
                        // 会话建立：开启断线重连，把 client 交给聊天窗口
                        client.EnableAutoReconnect();
                        _connectingClient = null;
                        OpenChatWindow(client, userId, outcome.Info);
                        Succeeded?.Invoke(serverUrl, roomId, channelId, userId);
                        IsBusy = false;
                        Finished?.Invoke();
                        return;

                    case JoinResult.Pending:
                        // 等待房主批准：保持连接与界面，不重试也不结束（后续可扩展批准回调）
                        StatusText = "已连接，等待房主批准...";
                        return;

                    case JoinResult.Rejected:
                    case JoinResult.RoomError:
                        // 永久失败：停在连接界面显示错误 + 手动返回，不自动回门户
                        client.Dispose();
                        _connectingClient = null;
                        FailWith(outcome.Message);
                        return;

                    case JoinResult.Cancelled:
                        client.Dispose();
                        _connectingClient = null;
                        return;

                    default:
                        // 暂时性失败（NetworkError / Timeout / DuplicateConnection）：有限重试
                        client.Dispose();
                        _connectingClient = null;

                        if (attempt < MaxRetries)
                        {
                            StatusText = outcome.Result == JoinResult.DuplicateConnection
                                ? "旧连接清理中，稍后重试..."
                                : $"连接失败，正在重试 ({attempt + 1}/{MaxRetries})...";
                            var delay = outcome.Result == JoinResult.DuplicateConnection ? 1500 : 2000;
                            try { await Task.Delay(delay, token); }
                            catch (OperationCanceledException) { return; }
                        }
                        else
                        {
                            // 重试用尽：停在连接界面显示错误 + 手动返回
                            FailWith(outcome.Result == JoinResult.DuplicateConnection
                                ? "该账号已在此房间登录（可能另一处仍在线）"
                                : $"连接失败（已重试 {MaxRetries} 次）：{outcome.Message}");
                            return;
                        }
                        break;
                }
            }
        }
        finally
        {
            if (_cts == cts) _cts = null;
            cts.Dispose();
        }
    }

    /// <summary>进入错误态：停在连接界面，展示错误信息，等用户重试或返回。</summary>
    private void FailWith(string message)
    {
        IsBusy = false;
        ErrorMessage = message;   // HasError 变 true → 界面切错误态
        StatusText = "连接失败";
    }

    /// <summary>取消并返回门户：中断进行中的连接（连接中显示为"取消并返回"）。</summary>
    [RelayCommand]
    private void Cancel()
    {
        _cts?.Cancel();
        try { _connectingClient?.Dispose(); } catch { }
        _connectingClient = null;
        IsBusy = false;
        ErrorMessage = string.Empty;
        Finished?.Invoke();
    }

    /// <summary>错误态下"返回门户"。</summary>
    [RelayCommand]
    private void Return()
    {
        ErrorMessage = string.Empty;
        Finished?.Invoke();
    }

    /// <summary>错误态下"重试"：用上次的参数重新连接（房主刚开房时很有用）。</summary>
    [RelayCommand]
    private async Task Retry()
    {
        if (_lastParams is { } p)
            await ConnectAsync(p.url, p.room, p.channel, p.user);
    }

    private void OpenChatWindow(IMClient client, string userId, MasterIM.Models.JoinInfo? joinInfo)
    {
        // 打开三栏高级聊天室（阶段1），把已连接的 client + 握手房间快照交给它
        var vm = new ChatRoomViewModel(client, userId, joinInfo);
        var win = new ChatRoomWindow { DataContext = vm };
        win.Show();
        _ = vm.InitializeAsync();
    }

    public static string NormalizeServerUrl(string address)
    {
        if (string.IsNullOrWhiteSpace(address)) return "ws://localhost:5000/im";
        if (address.StartsWith("ws://", StringComparison.OrdinalIgnoreCase) ||
            address.StartsWith("wss://", StringComparison.OrdinalIgnoreCase))
            return address;
        return $"ws://{address}/im";
    }
}
