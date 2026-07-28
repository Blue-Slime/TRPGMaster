using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MasterIM.SDK;
using MasterIM.Models;

namespace MasterClient.Chat.Search;

/// <summary>
/// 独立聊天记录搜索窗口的 VM（对标 QQ 聊天记录搜索）：
/// 多维筛选（关键字 / 范围：本频道·全部频道 / 发送人 / 日期 / 类型）→ 结果列表 →
/// 双击某条 → 通过 NavigateRequested 回调让主聊天窗切频道 + 定位高亮。
/// 自身不持有消息控件，只发结果行 VM；导航靠事件解耦，不反向依赖 ChatRoom。
/// </summary>
public partial class MessageSearchViewModel : ObservableObject
{
    private readonly IMClient _client;
    private readonly string _currentUserId;

    /// <summary>频道下拉候选（含"全部频道"哨兵，ChannelId 为空串）。</summary>
    public ObservableCollection<ChannelOption> ChannelOptions { get; } = new();
    /// <summary>发送人下拉候选（含"所有人"哨兵，UserId 为空串）。</summary>
    public ObservableCollection<SenderOption> SenderOptions { get; } = new();
    /// <summary>消息类型下拉候选（含"全部类型"哨兵，Value 为空串）。</summary>
    public ObservableCollection<TypeOption> TypeOptions { get; } = new();

    /// <summary>搜索结果行。</summary>
    public ObservableCollection<SearchResultItem> Results { get; } = new();

    [ObservableProperty] private string _keyword = string.Empty;
    [ObservableProperty] private ChannelOption? _selectedChannel;
    [ObservableProperty] private SenderOption? _selectedSender;
    [ObservableProperty] private TypeOption? _selectedType;
    [ObservableProperty] private DateTimeOffset? _startDate;
    [ObservableProperty] private DateTimeOffset? _endDate;
    [ObservableProperty] private bool _isSearching;
    [ObservableProperty] private string _statusText = "输入条件后点搜索";

    /// <summary>双击结果 → 请求主聊天窗导航定位 (channelId, channelName, msgId)。</summary>
    public event Action<string, string, long>? NavigateRequested;

    // 频道 id→name 映射（结果行显示频道名 + 导航时回传名字）
    private readonly Dictionary<string, string> _channelNames;

    public MessageSearchViewModel(
        IMClient client, string currentUserId,
        IEnumerable<(string id, string name)> channels,
        string currentChannelId,
        IEnumerable<(string id, string name)> members)
    {
        _client = client;
        _currentUserId = currentUserId;
        _channelNames = channels.ToDictionary(c => c.id, c => c.name);

        // 频道候选：全部频道 + 各频道；默认选当前频道
        ChannelOptions.Add(new ChannelOption(string.Empty, "全部频道"));
        foreach (var (id, name) in channels)
            ChannelOptions.Add(new ChannelOption(id, name));
        SelectedChannel = ChannelOptions.FirstOrDefault(c => c.ChannelId == currentChannelId)
                          ?? ChannelOptions.First();

        // 发送人候选：所有人 + 各成员
        SenderOptions.Add(new SenderOption(string.Empty, "所有人"));
        foreach (var (id, name) in members)
            SenderOptions.Add(new SenderOption(id, name));
        SelectedSender = SenderOptions.First();

        // 类型候选
        TypeOptions.Add(new TypeOption(string.Empty, "全部类型"));
        TypeOptions.Add(new TypeOption("text", "文字"));
        TypeOptions.Add(new TypeOption("image", "图片"));
        TypeOptions.Add(new TypeOption("file", "文件"));
        TypeOptions.Add(new TypeOption("dice", "骰子"));
        SelectedType = TypeOptions.First();
    }

    /// <summary>执行搜索：组装 MessageSearchQuery → SDK → 结果行。</summary>
    [RelayCommand]
    private async Task Search()
    {
        // 频道范围：选"全部频道"(ChannelId 空) → 哨兵 "*"；否则 → 指定单频道。
        var scope = string.IsNullOrEmpty(SelectedChannel?.ChannelId)
            ? new List<string> { MessageSearchQuery.AllChannels }
            : new List<string> { SelectedChannel!.ChannelId };

        var query = new MessageSearchQuery
        {
            Keyword = Keyword.Trim(),
            ChannelIds = scope,
            SenderId = string.IsNullOrEmpty(SelectedSender?.UserId) ? null : SelectedSender!.UserId,
            MessageType = string.IsNullOrEmpty(SelectedType?.Value) ? null : SelectedType!.Value,
            // 日期取本地日的起止，转 UTC 交给服务端
            StartTime = StartDate?.Date.ToUniversalTime(),
            EndTime = EndDate?.Date.AddDays(1).AddSeconds(-1).ToUniversalTime(),
            Limit = 200
        };

        IsSearching = true;
        StatusText = "搜索中...";
        try
        {
            var hits = await _client.SearchMessagesAdvancedAsync(query);
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                Results.Clear();
                foreach (var m in hits.OrderByDescending(m => m.MsgId))
                {
                    _channelNames.TryGetValue(m.ChannelId, out var chName);
                    Results.Add(new SearchResultItem
                    {
                        MsgId = m.MsgId,
                        ChannelId = m.ChannelId,
                        ChannelName = chName ?? m.ChannelId,
                        SenderName = ShortName(m.SenderId),
                        TimeText = m.SendTime.ToLocalTime().ToString("yyyy-MM-dd HH:mm"),
                        Preview = BuildPreview(m)
                    });
                }
                StatusText = Results.Count == 0 ? "无匹配结果" : $"共 {Results.Count} 条结果";
            });
        }
        catch (Exception ex)
        {
            StatusText = $"搜索失败：{ex.Message}";
        }
        finally { IsSearching = false; }
    }

    /// <summary>双击某结果行 → 请求主窗导航定位。</summary>
    [RelayCommand]
    private void Open(SearchResultItem? item)
    {
        if (item == null) return;
        NavigateRequested?.Invoke(item.ChannelId, item.ChannelName, item.MsgId);
    }

    /// <summary>清空日期快捷（"不限"）。</summary>
    [RelayCommand]
    private void ClearDates()
    {
        StartDate = null;
        EndDate = null;
    }

    private static string BuildPreview(GroupMessage m)
    {
        if (m.IsDeleted) return "（已撤回）";
        return m.MessageType switch
        {
            "image" => "[图片]",
            "file" => "[文件]",
            "dice" => $"🎲 {m.Content}",
            _ => m.Content
        };
    }

    private static string ShortName(string userId)
        => string.IsNullOrEmpty(userId) ? "?" : (userId.Length > 12 ? userId.Substring(0, 12) : userId);
}

/// <summary>搜索结果行（纯展示数据，不复用消息控件——搜索列表样式与聊天流不同）。</summary>
public partial class SearchResultItem : ObservableObject
{
    [ObservableProperty] private long _msgId;
    [ObservableProperty] private string _channelId = string.Empty;
    [ObservableProperty] private string _channelName = string.Empty;
    [ObservableProperty] private string _senderName = string.Empty;
    [ObservableProperty] private string _timeText = string.Empty;
    [ObservableProperty] private string _preview = string.Empty;
}

public record ChannelOption(string ChannelId, string Display);
public record SenderOption(string UserId, string Display);
public record TypeOption(string Value, string Display);
