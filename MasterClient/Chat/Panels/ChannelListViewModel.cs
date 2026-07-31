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

    /// <summary>默认分类名称（无 CategoryName 的频道归入此类）。</summary>
    public const string DefaultCategory = "文字频道";

    [ObservableProperty] private string _roomTitle = string.Empty;

    /// <summary>分类列表（Discord 式分类折叠）。UI 绑定此集合。</summary>
    [ObservableProperty] private ObservableCollection<ChannelCategoryViewModel> _categories = new();

    /// <summary>所有分类下的频道展开列表（兼容旧调用方，不做 CollectionChanged 通知）。</summary>
    public IEnumerable<ChannelItemViewModel> AllChannels =>
        Categories.SelectMany(c => c.Channels);

    /// <summary>当前选中的频道（跨分类唯一选中）。</summary>
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
    /// 增量合并服务端频道列表到分类结构（保留未读红点/选中实例）。
    /// </summary>
    public void ApplyChannels(List<Channel> list)
    {
        // 按分类名分组（null/空 → DefaultCategory）
        var groups = list
            .GroupBy(c => string.IsNullOrEmpty(c.CategoryName) ? DefaultCategory : c.CategoryName)
            .ToList();

        var incomingChannelIds = new HashSet<string>(list.Select(c => c.ChannelId));

        // 建立现有频道 VM 索引（跨所有分类）
        var existingById = Categories
            .SelectMany(cat => cat.Channels)
            .ToDictionary(c => c.ChannelId);

        // 遍历服务端分类，增量更新
        var seenCategories = new HashSet<string>();
        foreach (var group in groups)
        {
            var catName = group.Key;
            seenCategories.Add(catName);

            var cat = Categories.FirstOrDefault(c => c.CategoryName == catName);
            if (cat == null)
            {
                cat = new ChannelCategoryViewModel(catName);
                // 注入回调：新建频道时指定分类
                cat.AddChannelRequested = cn => _ = AddChannelInCategory(cn, catName);
                cat.RenameCategoryRequested = (oldName, newName) => _ = RenameCategory(oldName, newName);
                Categories.Add(cat);
            }

            // 增量更新该分类内的频道
            var channelsInGroup = group.ToList();
            var incomingIds = new HashSet<string>(channelsInGroup.Select(c => c.ChannelId));

            // 移除不再属于此分类的频道
            for (int i = cat.Channels.Count - 1; i >= 0; i--)
                if (!incomingIds.Contains(cat.Channels[i].ChannelId))
                    cat.Channels.RemoveAt(i);

            // 更新/插入
            foreach (var c in channelsInGroup)
            {
                if (existingById.TryGetValue(c.ChannelId, out var existing))
                {
                    if (existing.ChannelName != c.ChannelName)
                        existing.ChannelName = c.ChannelName;
                    // 确保在正确分类里
                    if (!cat.Channels.Contains(existing))
                        cat.Channels.Add(existing);
                }
                else if (!cat.Channels.Any(ch => ch.ChannelId == c.ChannelId))
                {
                    cat.Channels.Add(MakeItem(c));
                }
            }

            // 按服务端顺序重排
            var order = channelsInGroup.Select(c => c.ChannelId).ToList();
            for (int target = 0; target < order.Count; target++)
            {
                var cur = cat.Channels.IndexOf(cat.Channels.First(ch => ch.ChannelId == order[target]));
                if (cur != target) cat.Channels.Move(cur, target);
            }
        }

        // 移除已消失的分类
        for (int i = Categories.Count - 1; i >= 0; i--)
            if (!seenCategories.Contains(Categories[i].CategoryName))
                Categories.RemoveAt(i);

        // 选中项：保持原选中；首次或原选中已被删则选第一个可见频道
        if (SelectedChannel == null || !incomingChannelIds.Contains(SelectedChannel.ChannelId))
        {
            SelectedChannel = Categories
                .Where(c => !c.IsCollapsed)
                .SelectMany(c => c.Channels)
                .FirstOrDefault()
                ?? Categories.SelectMany(c => c.Channels).FirstOrDefault();
        }
    }

    partial void OnSelectedChannelChanged(ChannelItemViewModel? value)
    {
        // 清除所有分类的选中状态，设置新选中
        foreach (var cat in Categories)
            foreach (var ch in cat.Channels)
                ch.IsSelected = ch == value;

        if (value != null) ChannelSelected?.Invoke(value.ChannelId);
    }

    /// <summary>选中指定频道（外部调用，如握手预载后自动选第一个）。</summary>
    public void SelectChannel(string channelId)
    {
        var item = Categories.SelectMany(c => c.Channels).FirstOrDefault(c => c.ChannelId == channelId);
        if (item != null) SelectedChannel = item;
    }

    /// <summary>标记某频道有未读消息（不改变选中状态）。</summary>
    public void MarkUnread(string channelId, bool hasUnread)
    {
        var item = Categories.SelectMany(c => c.Channels).FirstOrDefault(c => c.ChannelId == channelId);
        if (item != null) item.HasUnread = hasUnread;
    }

    /// <summary>建频道行 VM 并注入右键回调。</summary>
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
        => await AddChannelInCategory(null, DefaultCategory);

    private async Task AddChannelInCategory(string? prefilledName, string categoryName)
    {
        var owner = ActiveWindow();
        if (owner == null) return;

        var vm = new ChannelDialogViewModel(categoryName: categoryName);
        if (!string.IsNullOrEmpty(prefilledName)) vm.ChannelName = prefilledName;
        var dlg = new ChannelDialog { DataContext = vm };
        vm.Confirmed += async (name, cat) =>
        {
            try { await _client.CreateChannelAsync(name, cat); }
            catch (Exception ex) { Console.WriteLine($"新建频道失败: {ex.Message}"); }
            dlg.CloseWith(true);
        };
        vm.Cancelled += () => dlg.CloseWith(false);
        await dlg.ShowDialog(owner);
    }

    private async Task EditChannel(ChannelItemViewModel? channel)
    {
        if (channel == null) return;
        var owner = ActiveWindow();
        if (owner == null) return;

        // 找该频道所属分类
        var currentCategory = Categories
            .FirstOrDefault(c => c.Channels.Any(ch => ch.ChannelId == channel.ChannelId))
            ?.CategoryName ?? DefaultCategory;

        var vm = new ChannelDialogViewModel(channel.ChannelId, channel.ChannelName, categoryName: currentCategory);
        var dlg = new ChannelDialog { DataContext = vm };
        vm.Confirmed += async (name, cat) =>
        {
            try
            {
                await _client.UpdateChannelAsync(new Channel
                {
                    ChannelId = channel.ChannelId,
                    ChannelName = name,
                    CategoryName = cat
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

    /// <summary>重命名分类：把所有属于 oldName 的频道 CategoryName 改为 newName。</summary>
    private async Task RenameCategory(string oldName, string newName)
    {
        var cat = Categories.FirstOrDefault(c => c.CategoryName == oldName);
        if (cat == null || string.IsNullOrWhiteSpace(newName) || oldName == newName) return;
        foreach (var ch in cat.Channels.ToList())
        {
            try
            {
                await _client.UpdateChannelAsync(new Channel
                {
                    ChannelId = ch.ChannelId,
                    ChannelName = ch.ChannelName,
                    CategoryName = newName
                });
            }
            catch (Exception ex) { Console.WriteLine($"重命名分类失败: {ex.Message}"); }
        }
    }

    public async Task MoveChannelAsync(int fromIndex, int toIndex)
    {
        // 注意：拖拽现在操作的是平铺频道列表（跨分类）
        var allChannels = Categories.SelectMany(c => c.Channels).ToList();
        if (fromIndex < 0 || fromIndex >= allChannels.Count) return;
        if (toIndex < 0 || toIndex >= allChannels.Count) return;
        if (fromIndex == toIndex) return;

        var orderedIds = allChannels.Select(c => c.ChannelId).ToList();
        var id = orderedIds[fromIndex];
        orderedIds.RemoveAt(fromIndex);
        orderedIds.Insert(toIndex, id);

        try { await _client.ReorderChannelsAsync(orderedIds); }
        catch (Exception ex) { Console.WriteLine($"频道排序失败: {ex.Message}"); }
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

    public void Dispose() => _client.OnChannelsChanged -= OnChannelsChanged;
}

/// <summary>频道分类 VM：一个分类标题 + 可折叠的子频道列表（对标 Discord 分类折叠）。</summary>
public partial class ChannelCategoryViewModel : ObservableObject
{
    [ObservableProperty] private string _categoryName;
    [ObservableProperty] private bool _isCollapsed;
    public ObservableCollection<ChannelItemViewModel> Channels { get; } = new();

    /// <summary>在此分类下新建频道的回调（由 ChannelListViewModel 注入）。</summary>
    public Action<string?>? AddChannelRequested;
    /// <summary>重命名分类的回调（由 ChannelListViewModel 注入）。</summary>
    public Action<string, string>? RenameCategoryRequested;

    public ChannelCategoryViewModel(string categoryName)
    {
        _categoryName = categoryName;
    }

    [RelayCommand]
    private void ToggleCollapse() => IsCollapsed = !IsCollapsed;

    [RelayCommand]
    private void AddChannel() => AddChannelRequested?.Invoke(null);
}

public partial class ChannelItemViewModel : ObservableObject
{
    [ObservableProperty] private string _channelId = string.Empty;
    [ObservableProperty] private string _channelName = string.Empty;
    [ObservableProperty] private bool _hasUnread;
    [ObservableProperty] private bool _isSelected;

    public Action<ChannelItemViewModel>? EditRequested;
    public Action<ChannelItemViewModel>? DeleteRequested;

    [RelayCommand] private void Edit() => EditRequested?.Invoke(this);
    [RelayCommand] private void Delete() => DeleteRequested?.Invoke(this);
}
