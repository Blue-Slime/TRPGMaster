using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using MasterIM.SDK;
using MasterIM.Models;

namespace MasterClient.Chat.Panels;

/// <summary>
/// 右栏：成员列表。接 IMClient.GetRoomMembersAsync + OnMemberJoined / OnMemberLeft。
/// </summary>
public partial class MemberPanelViewModel : ObservableObject, IDisposable
{
    private readonly IMClient _client;
    private readonly string _currentUserId;
    private bool _disposed;

    [ObservableProperty] private ObservableCollection<MemberItemViewModel> _members = new();
    [ObservableProperty] private int _memberCount;

    public MemberPanelViewModel(IMClient client, string currentUserId)
    {
        _client = client;
        _currentUserId = currentUserId;
        _client.OnMemberJoined += OnMemberJoined;
        _client.OnMemberLeft += OnMemberLeft;
        _client.OnPresenceUpdate += OnPresenceUpdate;
    }

    public async Task LoadMembersAsync()
    {
        try
        {
            var list = await _client.GetRoomMembersAsync();
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                Members.Clear();
                foreach (var m in list)
                    Members.Add(ToVm(m.UserId, m.Role));
                // 确保自己在列表里
                if (!Members.Any(x => x.UserId == _currentUserId))
                    Members.Add(ToVm(_currentUserId, "member"));
                MemberCount = Members.Count;
            });
        }
        catch (Exception ex) { Console.WriteLine($"加载成员失败: {ex.Message}"); }
    }

    private MemberItemViewModel ToVm(string userId, string role) => new()
    {
        UserId = userId,
        DisplayName = userId,
        Role = role,
        AvatarText = string.IsNullOrEmpty(userId) ? "?" : userId.Substring(0, 1).ToUpperInvariant(),
        AvatarColor = ColorFor(userId),
        IsCurrentUser = userId == _currentUserId
    };

    private void OnMemberJoined(string userId, string role)
        => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (Members.Any(m => m.UserId == userId)) return;
            Members.Add(ToVm(userId, role));
            MemberCount = Members.Count;
        });

    private void OnMemberLeft(string userId)
        => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            var m = Members.FirstOrDefault(x => x.UserId == userId);
            if (m != null) Members.Remove(m);
            MemberCount = Members.Count;
        });

    private static string ColorFor(string userId)
    {
        var palette = new[] { "#5865F2", "#3BA55D", "#FAA61A", "#ED4245", "#EB459E", "#00A8FC" };
        return palette[Math.Abs(userId.GetHashCode()) % palette.Length];
    }

    private void OnPresenceUpdate(string userId, string status, string channelId)
        => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            var m = Members.FirstOrDefault(x => x.UserId == userId);
            if (m != null) m.OnlineStatus = status; // online / away / offline
        });

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _client.OnMemberJoined -= OnMemberJoined;
        _client.OnMemberLeft -= OnMemberLeft;
        _client.OnPresenceUpdate -= OnPresenceUpdate;
    }
}

public partial class MemberItemViewModel : ObservableObject
{
    [ObservableProperty] private string _userId = string.Empty;
    [ObservableProperty] private string _displayName = string.Empty;
    [ObservableProperty] private string _role = "member";
    [ObservableProperty] private string _avatarText = "?";
    [ObservableProperty] private string _avatarColor = "#5865F2";
    [ObservableProperty] private bool _isCurrentUser;

    /// <summary>在线状态：online / away / offline（空=online，兼容旧推送）。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusDotColor))]
    [NotifyPropertyChangedFor(nameof(StatusDotVisible))]
    private string _onlineStatus = "online";

    /// <summary>在线状态点颜色（View 直接绑）。</summary>
    public string StatusDotColor => OnlineStatus switch
    {
        "away"    => "#FAA61A",   // 黄：暂离
        "busy"    => "#ED4245",   // 红：勿扰
        "offline" => "#747F8D",   // 灰：离线（隐身）
        _         => "#3BA55D"    // 绿：在线（默认）
    };

    /// <summary>成员离线时不显示状态点。</summary>
    public bool StatusDotVisible => OnlineStatus != "offline";
}
