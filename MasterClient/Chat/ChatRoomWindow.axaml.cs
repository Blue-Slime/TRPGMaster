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
        DataContextChanged += OnDataContextChanged;

        // 窗口尺寸变化时，若地图处于收起态则重新贴合底部（避免预载时 Bounds 未测量导致偏移）
        SizeChanged += (_, _) =>
        {
            if (DataContext is ChatRoomViewModel vm && !vm.IsMapOpen)
                UpdateOverlayPosition(false);
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
        _mapMounted = true;

        if (DataContext is not ChatRoomViewModel chatVm) return;

        var mapVm = new MainWindowViewModel();
        _mapHost.Content = new MapEditorView { DataContext = mapVm };

        // ── 地图 WebSocket 同步初始化 ──────────────────────────────────────
        // Step 1: 注入 Host（用于流式数据发送 + 日志）
        var hostAdapter = new MapEngineHostAdapter(chatVm.UserId, chatVm.Client);
        mapVm.Host = hostAdapter;

        // Step 2: 创建发送器，接入 CommandBus（Execute/Undo/Redo 自动同步到服务端）
        var sender = new WebSocketCommandSender(chatVm.Client, chatVm.UserId);
        mapVm.CommandBus.SetNetworkSender(sender);

        // Step 3: 创建本地同步器（应用来自其他客户端的 Delta）
        _mapSyncClient = new MapSyncClient(mapVm.World, mapVm.CommandBus, chatVm.UserId);

        // Step 4: 订阅 Delta 事件（先订阅再请求全量，防止全量响应到达前 Delta 漏掉）
        chatVm.Client.OnMapDeltaJson += OnMapDelta;

        // Step 5: 订阅全量同步事件
        chatVm.Client.OnMapFullSyncJson += OnMapFullSync;

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

            // 用全量状态重置本地 World
            var docJson = JsonSerializer.Serialize(fullSync.Document);
            if (_mapHost?.Content is MapEditorView { DataContext: MainWindowViewModel mapVm })
            {
                mapVm.World.LoadFromJson(docJson);
            }

            // 同步客户端版本号
            _mapSyncClient?.SetInitialVersion(fullSync.Version);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MapSync] FullSync 处理失败: {ex.Message}");
        }
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
        }
        (DataContext as ChatRoomViewModel)?.Dispose();
    }

    /// <summary>map_drag 流式数据负载结构</summary>
    private sealed record MapDragPayload(string Id, double X, double Y);
}
