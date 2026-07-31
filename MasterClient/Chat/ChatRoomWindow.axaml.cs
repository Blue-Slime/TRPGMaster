using System.ComponentModel;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using MapEngine.Avalonia.Views;
using MapEngine.Avalonia.ViewModels;
using MapEngine.Core.Networking;
using MasterClient.Services;
using MasterIM.Models;

namespace MasterClient.Chat;

public partial class ChatRoomWindow : Window
{
    private Border? _mapOverlay;
    private TranslateTransform? _mapTransform;
    private ContentControl? _mapHost;
    private bool _mapMounted;
    private MapSyncClient? _mapSyncClient;

    public ChatRoomWindow()
    {
        InitializeComponent();
        _mapOverlay = this.FindControl<Border>("MapOverlay");
        _mapTransform = _mapOverlay?.RenderTransform as TranslateTransform;
        _mapHost = this.FindControl<ContentControl>("MapHost");

        // 给 TranslateTransform.Y 加平滑过渡（苹果式滑入/滑出）
        if (_mapTransform is not null)
        {
            _mapTransform.Transitions = new Avalonia.Animation.Transitions
            {
                new Avalonia.Animation.DoubleTransition
                {
                    Property = TranslateTransform.YProperty,
                    Duration = System.TimeSpan.FromMilliseconds(280),
                    Easing = new Avalonia.Animation.Easings.CubicEaseOut()
                }
            };
        }

        // 初始收起：地图层下移一整屏（只露出底部圆按钮）
        UpdateOverlayPosition(open: false);

        // 窗口尺寸变化时，若地图处于收起态则重新贴合底部（避免预载时 Bounds 未测量导致偏移）
        SizeChanged += (_, _) =>
        {
            if (DataContext is ChatRoomViewModel vm && !vm.IsMapOpen)
                UpdateOverlayPosition(false);
        };

        // Opened：窗口首次显示后订阅 PropertyChanged（此时 DataContext 已赋值且窗口已进入视觉树）。
        // 先用 DataContextChanged 订阅在构造器赋值时可能不可靠，改为在 Opened 里集中处理。
        Opened += (_, _) =>
        {
            if (DataContext is not ChatRoomViewModel vm) return;

            // 订阅 VM 属性变化（在此之后 IsMapLoaded = true 才会触发 EnsureMapMounted）
            vm.PropertyChanged -= OnVmPropertyChanged; // 防止重复订阅
            vm.PropertyChanged += OnVmPropertyChanged;

            // 兜底：若 InitializeAsync 已在 Show 之前完成（极罕见），此处补触发
            if (vm.IsMapLoaded)
                EnsureMapMounted();
        };
    }

    private void OnDataContextChanged(object? sender, System.EventArgs e)
    {
        if (DataContext is ChatRoomViewModel vm)
            vm.PropertyChanged += OnVmPropertyChanged;
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (DataContext is not ChatRoomViewModel vm) return;

        if (e.PropertyName == nameof(ChatRoomViewModel.IsMapLoaded) && vm.IsMapLoaded)
            EnsureMapMounted();

        if (e.PropertyName == nameof(ChatRoomViewModel.IsMapOpen))
            UpdateOverlayPosition(vm.IsMapOpen);
    }

    /// <summary>懒加载：首次展开时才构造 MapEditorView + GL 上下文，并接入 WebSocket 地图同步。</summary>
    private void EnsureMapMounted()
    {
        if (_mapMounted || _mapHost is null) return;

        if (DataContext is not ChatRoomViewModel chatVm) return;

        MainWindowViewModel mapVm;
        try
        {
            mapVm = new MainWindowViewModel();
        }
        catch (Exception ex)
        {
            var logPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "TRPGMaster", "map_mount_error.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
            File.WriteAllText(logPath,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] MainWindowViewModel 构造失败:\n{ex}\n");
            System.Diagnostics.Debug.WriteLine($"[MapMount] 构造失败，详情见: {logPath}");
            // 不设 _mapMounted，允许下次重试
            return;
        }

        MapEditorView mapView;
        try
        {
            mapView = new MapEditorView { DataContext = mapVm };
        }
        catch (Exception ex)
        {
            var logPath2 = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "TRPGMaster", "map_mount_error.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(logPath2)!);
            File.WriteAllText(logPath2,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] MapEditorView 构造失败:\n{ex}\n");
            System.Diagnostics.Debug.WriteLine($"[MapMount] MapEditorView 构造失败，详情见: {logPath2}");
            return;
        }

        _mapMounted = true;
        _mapHost.Content = mapView;

        // ── 地图 WebSocket 同步初始化 ──────────────────────────────────────
        // Step 1: 注入 Host（用于流式数据发送 + 日志）
        var hostAdapter = new MapEngineHostAdapter(chatVm.UserId, chatVm.Client);
        mapVm.Host = hostAdapter;

        // Step 2: 创建发送器，接入 CommandBus（Execute/Undo/Redo 自动同步到服务端）
        var sender = new WebSocketCommandSender(chatVm.Client, chatVm.UserId);
        mapVm.CommandBus.SetNetworkSender(sender);

        // Step 3: 创建本地同步器（应用来自其他客户端的 Delta）
        _mapSyncClient = new MapSyncClient(mapVm.World, mapVm.CommandBus, chatVm.UserId);

        // Step 3b: 注入全量同步回调 —— 版本跳号时自动触发
        _mapSyncClient.RequestFullSyncAsync = chatVm.Client.RequestMapFullSyncAsync;

        // Step 3c: 订阅连接错误（版本跳号日志）
        _mapSyncClient.ConnectionError += (_, msg) =>
            System.Diagnostics.Debug.WriteLine($"[MapSync] {msg}");

        // Step 4: 订阅 Delta 事件（先订阅再请求全量，防止全量响应到达前 Delta 漏掉）
        chatVm.Client.OnMapDeltaJson += OnMapDelta;

        // Step 5: 订阅全量同步事件
        chatVm.Client.OnMapFullSyncJson += OnMapFullSync;

        // Step 5b: 断线重连后重新请求全量同步
        chatVm.Client.OnReconnected += OnReconnected;

        // Step 6: 订阅流式实时数据（拖拽预览、光标等）
        chatVm.Client.OnStreamReceived += OnStreamReceived;

        // Step 7: 请求全量同步，拉取服务端当前权威状态
        _ = chatVm.Client.RequestMapFullSyncAsync();
        // ──────────────────────────────────────────────────────────────────
    }

    private void OnMapDelta(string json)
    {
        try
        {
            var delta = JsonSerializer.Deserialize<MapStateDelta>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (delta != null)
                _mapSyncClient?.ApplyDelta(delta);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MapSync] Delta 处理失败: {ex.Message}");
        }
    }

    private void OnMapFullSync(string json)
    {
        try
        {
            var fullSync = JsonSerializer.Deserialize<MapFullSync>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (fullSync is null) return;

            // 统一走 MapSyncClient.ApplyFullSync（负责清场、重建 World、更新版本号、清 Undo 栈）
            _mapSyncClient?.ApplyFullSync(fullSync);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MapSync] FullSync 处理失败: {ex.Message}");
        }
    }

    /// <summary>断线重连成功后重新拉取全量状态。</summary>
    private void OnReconnected()
    {
        if (DataContext is ChatRoomViewModel chatVm)
            _ = chatVm.Client.RequestMapFullSyncAsync();
    }

    /// <summary>展开=归零铺满，收起=下移一整屏高度（露出底部圆按钮）。</summary>
    private void UpdateOverlayPosition(bool open)
    {
        if (_mapTransform is null) return;
        _mapTransform.Y = open ? 0 : Bounds.Height <= 0 ? 900 : Bounds.Height;
    }

    /// <summary>
    /// 处理来自其他玩家的流式实时数据（拖拽预览、光标等）。
    /// 目前支持 map_drag：用幽灵位置更新对应 Token，不触发命令历史。
    /// </summary>
    private void OnStreamReceived(StreamData data)
    {
        if (data.Type != "map_drag") return;
        if (_mapHost?.Content is not MapEditorView { DataContext: MainWindowViewModel mapVm }) return;

        try
        {
            // data.Data 是 object，在此反序列化为具体类型
            var json = JsonSerializer.Serialize(data.Data);
            var drag = JsonSerializer.Deserialize<MapDragPayload>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (drag is null || string.IsNullOrEmpty(drag.Id)) return;

            // 找到对应 ViewModel 并更新预览位置（不经过 CommandBus，不产生历史记录）
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                var vm = mapVm.FindHierarchyById(drag.Id);
                if (vm is not null)
                {
                    vm.X = drag.X;
                    vm.Y = drag.Y;
                }
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MapStream] map_drag 处理失败: {ex.Message}");
        }
    }

    protected override void OnClosed(System.EventArgs e)
    {
        base.OnClosed(e);
        if (DataContext is ChatRoomViewModel vm)
        {
            vm.PropertyChanged -= OnVmPropertyChanged;
            // 取消所有地图相关事件订阅，防止内存泄漏
            vm.Client.OnMapDeltaJson -= OnMapDelta;
            vm.Client.OnMapFullSyncJson -= OnMapFullSync;
            vm.Client.OnStreamReceived -= OnStreamReceived;
            vm.Client.OnReconnected -= OnReconnected;
        }
        (DataContext as ChatRoomViewModel)?.Dispose();
    }

    /// <summary>map_drag 流式数据负载结构</summary>
    private sealed record MapDragPayload(string Id, double X, double Y);
}
