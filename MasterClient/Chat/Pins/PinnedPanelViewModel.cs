using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MasterIM.SDK;
using MasterIM.Models;

namespace MasterClient.Chat.Pins;

/// <summary>
/// 置顶消息面板 VM（对标 Discord 置顶列表）：拉当前频道的置顶消息，
/// 双击某条 → NavigateRequested → 主聊天窗定位高亮；也可就地取消置顶。
/// </summary>
public partial class PinnedPanelViewModel : ObservableObject
{
    private readonly IMClient _client;
    private readonly string _channelId;
    private readonly string _channelName;

    public ObservableCollection<PinnedItem> Items { get; } = new();

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _statusText = "加载中...";

    /// <summary>双击结果 → 请求主聊天窗导航定位 (channelId, channelName, msgId)。</summary>
    public event Action<string, string, long>? NavigateRequested;

    public PinnedPanelViewModel(IMClient client, string channelId, string channelName)
    {
        _client = client;
        _channelId = channelId;
        _channelName = channelName;
    }

    public async Task LoadAsync()
    {
        IsLoading = true;
        StatusText = "加载中...";
        try
        {
            var pins = await _client.GetPinnedMessagesAsync(_channelId);
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                Items.Clear();
                foreach (var m in pins.OrderByDescending(m => m.MsgId))
                    Items.Add(new PinnedItem
                    {
                        MsgId = m.MsgId,
                        SenderName = string.IsNullOrEmpty(m.SenderId) ? "?" : m.SenderId,
                        TimeText = m.SendTime.ToLocalTime().ToString("yyyy-MM-dd HH:mm"),
                        Preview = BuildPreview(m)
                    });
                StatusText = Items.Count == 0 ? "本频道暂无置顶消息" : $"共 {Items.Count} 条置顶";
            });
        }
        catch (Exception ex) { StatusText = $"加载失败：{ex.Message}"; }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    private void Open(PinnedItem? item)
    {
        if (item == null) return;
        NavigateRequested?.Invoke(_channelId, _channelName, item.MsgId);
    }

    [RelayCommand]
    private async Task Unpin(PinnedItem? item)
    {
        if (item == null) return;
        try
        {
            await _client.PinMessageAsync(item.MsgId, _channelId, false);
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                Items.Remove(item);
                StatusText = Items.Count == 0 ? "本频道暂无置顶消息" : $"共 {Items.Count} 条置顶";
            });
        }
        catch (Exception ex) { StatusText = $"取消置顶失败：{ex.Message}"; }
    }

    private static string BuildPreview(GroupMessage m) => m.MessageType switch
    {
        "image" => "[图片]",
        "file" => "[文件]",
        "dice" => $"🎲 {m.Content}",
        _ => m.Content
    };
}

/// <summary>置顶列表行（纯展示数据）。</summary>
public partial class PinnedItem : ObservableObject
{
    [ObservableProperty] private long _msgId;
    [ObservableProperty] private string _senderName = string.Empty;
    [ObservableProperty] private string _timeText = string.Empty;
    [ObservableProperty] private string _preview = string.Empty;
}
