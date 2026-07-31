using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MasterIM.SDK;
using MasterIM.Models;
using MasterClient.Chat.Panels;

namespace MasterClient.Chat;

/// <summary>
/// 三栏聊天室协调器。持有一个<b>已连接</b>的 IMClient（由 ConnectingVm 连上后传入），
/// 分发给三个子面板；自身只做协调，不塞业务逻辑。
/// <para>
/// Discord 对齐（step4）：单连接服务全部频道。收到消息按 ChannelId 分拣到对应频道缓存；
/// 非当前频道有新消息时给频道条目亮未读红点。切频道零重连，仅换绑 MessageArea 的消息集合。
/// </para>
/// </summary>
public partial class ChatRoomViewModel : ObservableObject, IDisposable
{
    private readonly IMClient _client;
    private readonly string _userId;
    private bool _disposed;

    // 内部访问器：供 ChatRoomWindow 初始化地图同步使用
    internal IMClient Client => _client;
    internal string UserId => _userId;

    [ObservableProperty] private string _roomTitle = string.Empty;

    public ChannelListViewModel ChannelList { get; }
    public MessageAreaViewModel MessageArea { get; }
    public MemberPanelViewModel MemberPanel { get; }

    // 顶部工具栏占位提示
    [ObservableProperty] private string _featureHint = string.Empty;

    // ===== 地图覆盖层（整窗覆盖，从底部推拉）=====
    /// <summary>地图层是否展开（true=铺满窗口，false=收起到底部）</summary>
    [ObservableProperty] private bool _isMapOpen;

    /// <summary>地图层是否已首次加载（懒加载标记：首次展开才 Mount MapEditorView）</summary>
    [ObservableProperty] private bool _isMapLoaded;

    /// <summary>切换地图层展开/收起。地图通常已在进房后预载，这里只切展开态。</summary>
    [RelayCommand]
    private void ToggleMap()
    {
        if (!IsMapLoaded) IsMapLoaded = true; // 兜底：预载未完成时点击也能加载
        IsMapOpen = !IsMapOpen;
    }

    private readonly JoinInfo? _joinInfo;

    public ChatRoomViewModel(IMClient connectedClient, string userId, JoinInfo? joinInfo)
    {
        _client = connectedClient;
        _userId = userId;
        _joinInfo = joinInfo;

        // 房间名优先取握手快照；缺失时回退 roomId（不至于空白）
        _roomTitle = !string.IsNullOrWhiteSpace(joinInfo?.RoomName)
            ? joinInfo!.RoomName
            : (connectedClient.CurrentRoomId ?? string.Empty);

        ChannelList = new ChannelListViewModel(_client, _roomTitle);
        MessageArea = new MessageAreaViewModel(_client, userId);
        MemberPanel = new MemberPanelViewModel(_client, userId);

        // 频道切换 → 协调器处理换绑 + 懒加载 + 清未读
        ChannelList.ChannelSelected += OnChannelSelected;
        // 成员列表变化时同步 @提及候选
        MemberPanel.Members.CollectionChanged += (_, _) => SyncMentionCandidates();
        // 消息分拣：收到任意频道的消息都在这里路由
        _client.OnMessageReceived += OnMessageReceived;
    }

    /// <summary>进房后初始化：频道 / 历史 / 成员（互相独立，一个失败不影响其它）</summary>
    public async Task InitializeAsync()
    {
        // 频道列表优先用握手快照（零往返、无闪烁）；缺失才回退 get_channels。
        if (_joinInfo is { Channels.Count: > 0 })
            ChannelList.ApplyChannels(_joinInfo.Channels);
        else
        {
            try { await ChannelList.LoadChannelsAsync(); }
            catch (Exception ex) { Console.WriteLine($"频道加载异常: {ex.Message}"); }
        }

        // 自动选中第一个频道并加载其历史
        var first = ChannelList.AllChannels.FirstOrDefault();
        if (first != null)
        {
            ChannelList.SelectedChannel = first;
            // OnChannelSelected 会被触发，完成 SwitchToChannelAsync + 历史加载
        }
        else
        {
            // 无频道时仍尝试加载一次历史（兜底）
            try { await MessageArea.LoadHistoryAsync(string.Empty); } catch { /* 无频道，忽略 */ }
        }

        try { await MemberPanel.LoadMembersAsync(); } catch (Exception ex) { Console.WriteLine($"成员加载异常: {ex.Message}"); }

        // 聊天全部加载完后，后台预载地图（Mount MapEditorView + GL 冷启动），
        // 但不展开（IsMapOpen 仍为 false）。这样用户点圆按钮时地图已就绪，零等待。
        IsMapLoaded = true;
    }

    // ===== 频道切换（由 ChannelListViewModel 的选中事件触发）=====

    private void OnChannelSelected(string channelId)
    {
        var ch = ChannelList.AllChannels.FirstOrDefault(c => c.ChannelId == channelId);
        if (ch == null) return;

        // 清当前频道未读
        ch.HasUnread = false;

        // 通知 SDK 切换当前频道（发送/查询等均以此为准，零重连）
        _client.SetCurrentChannel(channelId);

        // MessageArea 换绑到该频道的缓存集合，并懒加载历史
        _ = MessageArea.SwitchToChannelAsync(channelId, ch.ChannelName);
    }

    // ===== 消息分拣（Discord 对齐：全房间广播，客户端按 channelId 路由）=====

    private void OnMessageReceived(GroupMessage msg)
    {
        var channelId = string.IsNullOrEmpty(msg.ChannelId)
            ? MessageArea.CurrentChannelId
            : msg.ChannelId;

        // 投递到对应频道的缓存（MessageArea 内部按 channelId 管理）
        MessageArea.AppendMessage(msg);

        // 若不是当前频道，给频道条目亮未读红点
        if (channelId != MessageArea.CurrentChannelId)
        {
            var channelItem = ChannelList.AllChannels.FirstOrDefault(c => c.ChannelId == channelId);
            if (channelItem != null)
                Avalonia.Threading.Dispatcher.UIThread.Post(() => channelItem.HasUnread = true);
        }
    }

    // ===== 顶部工具栏命令 =====
    [RelayCommand] private void QuickDice() => FeatureHint = "骰子功能待接入";
    [RelayCommand] private void InsertLink() => FeatureHint = "插入链接待接入";
    [RelayCommand] private void InvokeRule() => FeatureHint = "规则调用待接入";
    [RelayCommand] private void OpenCharacterCard() => FeatureHint = "角色卡待接入";
    [RelayCommand] private void OpenMap() => ToggleMap();
    [RelayCommand] private void OpenNotes() => FeatureHint = "笔记待接入";

    /// <summary>@提及：在输入框当前位置插入 @ 符，触发 MessageArea 的候选浮层逻辑。</summary>
    [RelayCommand]
    private void Mention()
    {
        MessageArea.MessageInput += "@";
        MessageArea.HandleInputChanged(MessageArea.MessageInput);
    }

    /// <summary>同步成员列表到 @提及候选（成员列表变化时调用）。</summary>
    private void SyncMentionCandidates()
    {
        MessageArea.MentionCandidates.Clear();
        foreach (var m in MemberPanel.Members)
            MessageArea.MentionCandidates.Add(m);
    }

    [RelayCommand]
    private async Task AttachFile()
    {
        var win = ActiveWindow(); if (win == null) return;
        var files = await win.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择文件",
            AllowMultiple = false
        });
        if (files.Count == 0) return;
        await UploadAndSend(files[0].Path.LocalPath);
    }

    [RelayCommand]
    private async Task AttachImage()
    {
        var win = ActiveWindow(); if (win == null) return;
        var files = await win.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择图片",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("图片") { Patterns = new[] { "*.jpg","*.jpeg","*.png","*.gif","*.webp" } }
            }
        });
        if (files.Count == 0) return;
        await UploadAndSend(files[0].Path.LocalPath);
    }

    private async Task UploadAndSend(string localPath)
    {
        FeatureHint = "上传中...";
        try
        {
            var result = await _client.UploadFileAsync(localPath);
            await _client.SendFileMessageAsync(result);
            FeatureHint = string.Empty;
        }
        catch (Exception ex)
        {
            FeatureHint = $"上传失败：{ex.Message}";
        }
    }

    /// <summary>
    /// 打开独立聊天记录搜索窗口（对标 QQ）：把当前频道列表 + 成员 + 当前频道喂给搜索 VM；
    /// 双击结果 → NavigateRequested → 主聊天窗切频道 + 定位高亮。
    /// </summary>
    [RelayCommand]
    private async Task OpenPinned()
    {
        var name = ChannelList.AllChannels.FirstOrDefault(c => c.ChannelId == MessageArea.CurrentChannelId)?.ChannelName
                   ?? MessageArea.CurrentChannelName;
        var vm = new Pins.PinnedPanelViewModel(_client, MessageArea.CurrentChannelId, name);
        vm.NavigateRequested += (channelId, channelName, msgId) =>
            Avalonia.Threading.Dispatcher.UIThread.Post(async () =>
            {
                var item = ChannelList.AllChannels.FirstOrDefault(c => c.ChannelId == channelId);
                if (item != null) ChannelList.SelectedChannel = item;
                await MessageArea.NavigateToMessageAsync(channelId, channelName, msgId);
            });

        var win = new Pins.PinnedPanelWindow { DataContext = vm };
        win.Show(ActiveWindow());
        await vm.LoadAsync();
    }

    [RelayCommand]
    private void OpenSearch()
    {
        var channels = ChannelList.AllChannels.Select(c => (c.ChannelId, c.ChannelName)).ToList();
        var members = MemberPanel.Members.Select(m => (m.UserId, m.DisplayName)).ToList();

        var vm = new Search.MessageSearchViewModel(
            _client, _userId, channels, MessageArea.CurrentChannelId, members);

        vm.NavigateRequested += (channelId, channelName, msgId) =>
            Avalonia.Threading.Dispatcher.UIThread.Post(async () =>
            {
                // 结果列表若只有 channelId（全频道搜到别的频道），用本地频道名兜底
                var name = ChannelList.AllChannels.FirstOrDefault(c => c.ChannelId == channelId)?.ChannelName
                           ?? channelName;
                // 同步左栏选中态到目标频道
                var item = ChannelList.AllChannels.FirstOrDefault(c => c.ChannelId == channelId);
                if (item != null) ChannelList.SelectedChannel = item;
                await MessageArea.NavigateToMessageAsync(channelId, name, msgId);
            });

        var win = new Search.MessageSearchWindow { DataContext = vm };
        win.Show(ActiveWindow());
    }

    private static Avalonia.Controls.Window? ActiveWindow()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime
            is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
        {
            foreach (var w in desktop.Windows)
                if (w.IsActive) return w;
            return desktop.MainWindow;
        }
        return null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _client.OnMessageReceived -= OnMessageReceived;
        ChannelList.ChannelSelected -= OnChannelSelected;
        ChannelList.Dispose();
        MessageArea.Dispose();
        MemberPanel.Dispose();
        _client.Dispose(); // 离开房间 = 断连
    }
}
