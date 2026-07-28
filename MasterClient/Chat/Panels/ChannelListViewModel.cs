using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MasterIM.SDK;
using MasterIM.Models;

namespace MasterClient.Chat.Panels;

/// <summary>
/// 左栏：频道列表。接 IMClient.GetChannelsAsync / OnChannelsChanged。
/// 频道切换通过 ChannelSelected 事件通知外层（ChatRoomViewModel）。
/// </summary>
public partial class ChannelListViewModel : ObservableObject
{
    private readonly IMClient _client;

    [ObservableProperty] private string _roomTitle = string.Empty;
    [ObservableProperty] private ObservableCollection<ChannelItemViewModel> _channels = new();
    [ObservableProperty] private ChannelItemViewModel? _selectedChannel;

    /// <summary>频道被选中（channelId）</summary>
    public event Action<string>? ChannelSelected;

    public ChannelListViewModel(IMClient client, string roomTitle)
    {
        _client = client;
        _roomTitle = roomTitle;
        _client.OnChannelsChanged += OnChannelsChanged;
    }

    public async Task LoadChannelsAsync()
    {
        try
        {
            var list = await _client.GetChannelsAsync();
            ApplyChannels(list);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"加载频道失败: {ex.Message}");
        }
    }

    private void OnChannelsChanged(List<Channel> list)
        => Avalonia.Threading.Dispatcher.UIThread.Post(() => ApplyChannels(list));

    /// <summary>
    /// 增量合并服务端频道列表到本地集合（专业做法，替代 Clear+重建）：
    /// 存在的原地更新名字（保留 HasUnread/选中实例），新增的插入，缺失的移除，
    /// 最后按服务端顺序重排。这样别人建/改频道不会抹掉本地未读红点，也不闪烁、
    /// 不会因重建实例而重复触发 OnSelectedChannelChanged。
    /// </summary>
    public void ApplyChannels(List<Channel> list)
    {
        var byId = Channels.ToDictionary(c => c.ChannelId);
        var incomingIds = new HashSet<string>(list.Select(c => c.ChannelId));

        // 1) 移除本地多出的（已被删除的频道）
        for (int i = Channels.Count - 1; i >= 0; i--)
            if (!incomingIds.Contains(Channels[i].ChannelId))
                Channels.RemoveAt(i);

        // 2) 更新已有 / 插入新增
        foreach (var c in list)
        {
            if (byId.TryGetValue(c.ChannelId, out var existing))
            {
                if (existing.ChannelName != c.ChannelName)
                    existing.ChannelName = c.ChannelName;  // 原地改名，保留 HasUnread + 实例
            }
            else
            {
                Channels.Add(MakeItem(c));
            }
        }

        // 3) 按服务端顺序重排（拖拽排序/新增位置以服务端为准）
        var order = list.Select(c => c.ChannelId).ToList();
        for (int target = 0; target < order.Count; target++)
        {
            var cur = Channels.IndexOf(Channels.First(c => c.ChannelId == order[target]));
            if (cur != target) Channels.Move(cur, target);
        }

        // 4) 选中项：保持原选中实例；首次或原选中已被删则选第一个
        if (SelectedChannel == null || !incomingIds.Contains(SelectedChannel.ChannelId))
            SelectedChannel = Channels.FirstOrDefault();
    }

    partial void OnSelectedChannelChanged(ChannelItemViewModel? value)
    {
        if (value != null) ChannelSelected?.Invoke(value.ChannelId);
    }

    /// <summary>建频道行 VM 并注入右键回调（ContextMenu 是独立 Popup，上溯绑定拿不到 VM）。</summary>
    private ChannelItemViewModel MakeItem(Channel c)
    {
        var vm = new ChannelItemViewModel { ChannelId = c.ChannelId, ChannelName = c.ChannelName };
        vm.EditRequested = t => _ = EditChannel(t);
        vm.DeleteRequested = t => _ = DeleteChannel(t);
        return vm;
    }

    // ========== 新建 / 编辑 / 删除 / 排序 ==========

    [RelayCommand]
    private async Task AddChannel()
    {
        var owner = ActiveWindow();
        if (owner == null) return;

        var vm = new ChannelDialogViewModel();
        var dlg = new ChannelDialog { DataContext = vm };
        vm.Confirmed += async name =>
        {
            try
            {
                await _client.CreateChannelAsync(name);  // ID 由服务端生成
            }
            catch (Exception ex) { Console.WriteLine($"新建频道失败: {ex.Message}"); }
            dlg.CloseWith(true);
        };
        vm.Cancelled += () => dlg.CloseWith(false);
        await dlg.ShowDialog(owner);
    }

    // 注：不加 [RelayCommand]。右键菜单走回调注入（MakeItem → EditRequested），
    // 不需要 VM 上的 EditChannelCommand。加了反而生成无人绑定的多余命令。
    private async Task EditChannel(ChannelItemViewModel? channel)
    {
        if (channel == null) return;
        var owner = ActiveWindow();
        if (owner == null) return;

        var vm = new ChannelDialogViewModel(channel.ChannelId, channel.ChannelName);
        var dlg = new ChannelDialog { DataContext = vm };
        vm.Confirmed += async name =>
        {
            try
            {
                await _client.UpdateChannelAsync(new Channel
                {
                    ChannelId = channel.ChannelId,
                    ChannelName = name
                });
            }
            catch (Exception ex) { Console.WriteLine($"修改频道失败: {ex.Message}"); }
            dlg.CloseWith(true);
        };
        vm.Cancelled += () => dlg.CloseWith(false);
        await dlg.ShowDialog(owner);
    }

    private async Task DeleteChannel(ChannelItemViewModel? channel)
    {
        if (channel == null) return;
        try { await _client.DeleteChannelAsync(channel.ChannelId); }
        catch (Exception ex) { Console.WriteLine($"删除频道失败: {ex.Message}"); }
    }

    /// <summary>拖拽排序：把 from 位置的频道移动到 to 位置，并持久化到服务端。</summary>
    public async Task MoveChannelAsync(int fromIndex, int toIndex)
    {
        if (fromIndex < 0 || fromIndex >= Channels.Count) return;
        if (toIndex < 0 || toIndex >= Channels.Count) return;
        if (fromIndex == toIndex) return;

        var item = Channels[fromIndex];
        Channels.Move(fromIndex, toIndex);

        // 持久化新顺序（服务端会广播 channels_changed 回来，届时按持久化顺序刷新）
        var orderedIds = Channels.Select(c => c.ChannelId).ToList();
        try { await _client.ReorderChannelsAsync(orderedIds); }
        catch (Exception ex) { Console.WriteLine($"频道排序失败: {ex.Message}"); }
    }

    private static Avalonia.Controls.Window? ActiveWindow()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime
            is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
        {
            // 优先当前激活窗口（聊天室窗口），否则主窗口
            foreach (var w in desktop.Windows)
                if (w.IsActive) return w;
            return desktop.MainWindow;
        }
        return null;
    }

    public void Dispose() => _client.OnChannelsChanged -= OnChannelsChanged;
}

public partial class ChannelItemViewModel : ObservableObject
{
    [ObservableProperty] private string _channelId = string.Empty;
    [ObservableProperty] private string _channelName = string.Empty;
    [ObservableProperty] private bool _hasUnread;

    // 右键菜单命令：ContextMenu 是独立 Popup，不在 ListBox 可视树内，
    // $parent[ListBox] 上溯绑定会静默失败。改用回调注入到 item 自身（与消息悬停条同模式）。
    public Action<ChannelItemViewModel>? EditRequested;
    public Action<ChannelItemViewModel>? DeleteRequested;

    [RelayCommand] private void Edit() => EditRequested?.Invoke(this);
    [RelayCommand] private void Delete() => DeleteRequested?.Invoke(this);
}
