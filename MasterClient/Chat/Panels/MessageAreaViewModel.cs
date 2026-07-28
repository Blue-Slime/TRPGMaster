using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Timers;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MasterIM.SDK;
using MasterIM.Models;
using MasterClient.Chat.Messages;

namespace MasterClient.Chat.Panels;

/// <summary>
/// 中栏：消息区。消息流 / 输入 / 发送 / 历史 / 正在输入。
/// 接 IMClient.SendMessageAsync / OnMessageReceived / QueryRecentMessagesAsync / SendTypingAsync。
/// </summary>
public partial class MessageAreaViewModel : ObservableObject, IDisposable
{
    private readonly IMClient _client;
    private readonly string _currentUserId;
    private readonly System.Timers.Timer _typingTimer;
    private DateTime _lastTypingSent;
    private readonly Dictionary<string, DateTime> _typingUsers = new();
    private bool _disposed;

    // Discord 对齐：每频道一份消息缓存 + 已加载历史标记。切频道零重连，仅换绑集合。
    private readonly Dictionary<string, ObservableCollection<MessageItemViewModel>> _channelMessages = new();
    private readonly HashSet<string> _historyLoaded = new();

    /// <summary>当前正在显示的频道ID（协调器切换时设置）。</summary>
    public string CurrentChannelId { get; private set; } = string.Empty;

    [ObservableProperty] private string _currentChannelName = "大厅";
    [ObservableProperty] private ObservableCollection<MessageItemViewModel> _messages = new();
    [ObservableProperty] private string _messageInput = string.Empty;
    [ObservableProperty] private bool _isLoadingHistory;
    [ObservableProperty] private bool _showTypingIndicator;
    [ObservableProperty] private string _typingIndicatorText = string.Empty;

    // ===== 回复 / 编辑态（对标 Discord：输入框上方显示回复条 / 消息就地编辑）=====

    /// <summary>正在回复的目标消息（非空时输入框上方显示回复条）。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsReplying))]
    private MessageItemViewModel? _replyingTo;

    /// <summary>正在编辑的目标消息（非空时该消息就地变输入框）。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditing))]
    private MessageItemViewModel? _editingMessage;

    /// <summary>编辑框内容（编辑态下绑定）。</summary>
    [ObservableProperty] private string _editInput = string.Empty;

    public bool IsReplying => ReplyingTo != null;
    public bool IsEditing => EditingMessage != null;

    // ===== @提及浮层 =====

    /// <summary>
    /// 成员候选（由协调器 ChatRoomViewModel 在构造/成员变化时注入）。
    /// 当用户输入 @ 后过滤此列表供 View 浮层展示。
    /// </summary>
    public ObservableCollection<Panels.MemberItemViewModel> MentionCandidates { get; } = new();

    /// <summary>@浮层是否显示。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMentionSuggestions))]
    private ObservableCollection<Panels.MemberItemViewModel> _mentionSuggestions = new();

    public bool HasMentionSuggestions => MentionSuggestions.Count > 0;

    /// <summary>输入框改变时调用（已在 code-behind 钩住）：检测 @ 触发候选列表。</summary>
    public void HandleInputChanged(string text)
    {
        // 找最后一个未闭合的 @ 片段
        var at = text.LastIndexOf('@');
        if (at < 0) { MentionSuggestions.Clear(); return; }
        var frag = text.Substring(at + 1).ToLower();
        if (frag.Contains(' ')) { MentionSuggestions.Clear(); return; }

        var filtered = MentionCandidates
            .Where(m => m.UserId.ToLower().Contains(frag) || m.DisplayName.ToLower().Contains(frag))
            .Take(8).ToList();
        MentionSuggestions.Clear();
        foreach (var m in filtered) MentionSuggestions.Add(m);
    }

    /// <summary>用户从浮层选中某成员：把 @ 触发的片段替换成 @DisplayName 并追加空格。</summary>
    public void InsertMention(Panels.MemberItemViewModel member)
    {
        var text = MessageInput;
        var at = text.LastIndexOf('@');
        if (at < 0) return;
        MessageInput = text.Substring(0, at) + $"@{member.DisplayName} ";
        _pendingMentions.Add(member.UserId);
        MentionSuggestions.Clear();
    }

    private readonly List<string> _pendingMentions = new();

    public MessageAreaViewModel(IMClient client, string currentUserId)
    {
        _client = client;
        _currentUserId = currentUserId;
        _client.OnUserTyping += OnUserTyping;
        _client.OnUserStopTyping += OnUserStopTyping;
        _client.OnMessageModified += OnMessageModified;
        _client.OnMessageRevoked += OnMessageRevoked;
        _client.OnPinChanged += OnPinChanged;
        _client.OnReactionChanged += OnReactionChanged;

        _typingTimer = new System.Timers.Timer(1000);
        _typingTimer.Elapsed += (_, _) => PruneTyping();
        _typingTimer.Start();
    }

    /// <summary>获取（或惰性创建）某频道的消息缓存集合。</summary>
    private ObservableCollection<MessageItemViewModel> CacheFor(string channelId)
    {
        if (!_channelMessages.TryGetValue(channelId, out var col))
        {
            col = new ObservableCollection<MessageItemViewModel>();
            _channelMessages[channelId] = col;
        }
        return col;
    }

    /// <summary>
    /// 切换到指定频道（零重连）：换绑 Messages 到该频道缓存；首次进入时懒加载历史。
    /// </summary>
    public async Task SwitchToChannelAsync(string channelId, string channelName)
    {
        CurrentChannelId = channelId;
        CurrentChannelName = channelName;

        var col = CacheFor(channelId);
        Avalonia.Threading.Dispatcher.UIThread.Post(() => Messages = col);

        if (!_historyLoaded.Contains(channelId))
            await LoadHistoryAsync(channelId);
    }

    /// <summary>加载指定频道历史到其缓存（仅首次；重复调用会重刷该频道）。</summary>
    public async Task LoadHistoryAsync(string channelId, int days = 7, int limit = 100)
    {
        IsLoadingHistory = true;
        try
        {
            var history = await _client.QueryChannelMessagesAsync(channelId, days, limit);
            var col = CacheFor(channelId);
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                col.Clear();
                MessageItemViewModel? prev = null;
                foreach (var m in history.OrderBy(m => m.DisplayTime))
                {
                    var vm = ToVm(m);
                    vm.IsGrouped = ShouldGroup(prev, vm);
                    col.Add(vm);
                    prev = vm;
                }
            });
            _historyLoaded.Add(channelId);
            // 历史加载后批量补齐这批消息的反应聚合
            _ = LoadReactionsAsync(channelId, history.Select(m => m.MsgId).ToList());
        }
        catch (Exception ex) { Console.WriteLine($"加载历史失败: {ex.Message}"); }
        finally { IsLoadingHistory = false; }
    }

    /// <summary>
    /// 协调器分拣后投递一条消息到其所属频道缓存。
    /// 仅当该频道历史已加载过才追加（未加载的频道打开时会整体拉取，避免重复/错序）。
    /// </summary>
    public void AppendMessage(GroupMessage m)
    {
        var channelId = string.IsNullOrEmpty(m.ChannelId) ? CurrentChannelId : m.ChannelId;
        if (!_historyLoaded.Contains(channelId)) return; // 未打开过的频道：留待懒加载
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            var col = CacheFor(channelId);
            var vm = ToVm(m);
            vm.IsGrouped = ShouldGroup(col.LastOrDefault(), vm);
            col.Add(vm);
        });
    }

    /// <summary>
    /// 连续消息折叠判定（对标 Discord）：同一发送者、7 分钟内、前后都是普通消息 → 折叠头。
    /// </summary>
    private static bool ShouldGroup(MessageItemViewModel? prev, MessageItemViewModel cur)
    {
        if (prev == null) return false;
        if (prev.SenderId != cur.SenderId) return false;
        if (prev.IsSystem || cur.IsSystem) return false;
        if (prev.IsRecalled) return false;
        return (cur.SendTime - prev.SendTime).TotalMinutes <= 7;
    }

    [RelayCommand]
    private async Task SendMessage()
    {
        if (string.IsNullOrWhiteSpace(MessageInput)) return;
        var msg = new GroupMessage
        {
            Content = MessageInput,
            SenderId = _currentUserId,
            MessageType = "text",
            ChannelId = CurrentChannelId
        };
        if (ReplyingTo != null)
        {
            msg.ReplyToMsgId = ReplyingTo.MsgId;
            msg.QuotedContent = $"{ReplyingTo.SenderName}: {ReplyingTo.Content}";
        }
        // @提及：把本次输入积累的 userId 集合写入消息
        if (_pendingMentions.Count > 0)
        {
            msg.MentionedUserIds = new List<string>(_pendingMentions);
            _pendingMentions.Clear();
        }
        try
        {
            await _client.SendMessageAsync(msg);
            MessageInput = string.Empty;
            ReplyingTo = null;
            MentionSuggestions.Clear();
        }
        catch (Exception ex) { Console.WriteLine($"发送失败: {ex.Message}"); }
    }

    public void NotifyTyping()
    {
        var now = DateTime.Now;
        if ((now - _lastTypingSent).TotalSeconds < 2) return;
        _lastTypingSent = now;
        _client.SendTypingAsync().ConfigureAwait(false);
    }

    // ===== 回复 / 编辑 / 撤回（对标 Discord）=====

    /// <summary>开始回复某条消息（在输入区上方显示"正在回复 X"）。</summary>
    [RelayCommand]
    private void BeginReply(MessageItemViewModel? m)
    {
        if (m == null || m.IsRecalled) return;
        EndEdit();               // 回复与编辑互斥
        ReplyingTo = m;
    }

    /// <summary>取消当前回复。</summary>
    [RelayCommand]
    private void CancelReply() => ReplyingTo = null;

    /// <summary>开始编辑自己的消息：把正文填进编辑框（仅本人可编辑）。</summary>
    [RelayCommand]
    private void BeginEdit(MessageItemViewModel? m)
    {
        if (m == null || !m.IsCurrentUser || m.IsRecalled) return;
        ReplyingTo = null;       // 编辑与回复互斥
        EditingMessage = m;
        EditInput = m.Content;
    }

    /// <summary>提交编辑（回车触发）。</summary>
    [RelayCommand]
    private async Task CommitEdit()
    {
        var target = EditingMessage;
        if (target == null || string.IsNullOrWhiteSpace(EditInput)) { EndEdit(); return; }
        var newContent = EditInput.Trim();
        try { await _client.ModifyMessageAsync(target.MsgId, newContent); }
        catch (Exception ex) { Console.WriteLine($"编辑失败: {ex.Message}"); }
        EndEdit();
    }

    /// <summary>取消编辑。</summary>
    [RelayCommand]
    private void CancelEdit() => EndEdit();

    private void EndEdit()
    {
        EditingMessage = null;
        EditInput = string.Empty;
    }

    /// <summary>撤回自己的消息（仅本人）。</summary>
    [RelayCommand]
    private async Task RevokeMessage(MessageItemViewModel? m)
    {
        if (m == null || !m.IsCurrentUser || m.IsRecalled) return;
        try { await _client.RevokeMessageAsync(m.MsgId); }
        catch (Exception ex) { Console.WriteLine($"撤回失败: {ex.Message}"); }
    }

    private MessageItemViewModel ToVm(GroupMessage m)
    {
        var vm = new MessageItemViewModel
        {
            MsgId = m.MsgId,
            SenderId = m.SenderId,
            SenderName = ShortName(m.SenderId),
            SendTime = m.SendTime,
            IsCurrentUser = m.SenderId == _currentUserId,
            MessageType = string.IsNullOrEmpty(m.MessageType) ? "text" : m.MessageType,
            Content = m.Content,
            QuotedContent = m.QuotedContent,
            IsRecalled = m.IsDeleted,
            AvatarColor = ColorFor(m.SenderId)
        };

        // 图片/文件消息：Content 是 JSON {FileId,FileName,FileSize,Url}，解析后填专用字段
        if ((m.MessageType == "image" || m.MessageType == "file") && !string.IsNullOrEmpty(m.Content))
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(m.Content);
                var root = doc.RootElement;
                var relUrl = root.TryGetProperty("Url", out var uProp) ? uProp.GetString() ?? "" : "";
                var absUrl = relUrl.StartsWith("http") ? relUrl : $"{_client.BaseHttpUrl}{relUrl}";
                var fileName = root.TryGetProperty("FileName", out var fProp) ? fProp.GetString() ?? "" : "";
                var fileSize = root.TryGetProperty("FileSize", out var sProp) ? sProp.GetInt64() : 0L;

                vm.FileName = fileName;
                vm.FileSize = FormatFileSize(fileSize);
                if (m.MessageType == "image") vm.ImageSource = absUrl;
            }
            catch { /* 格式异常退化为文本显示 */ }
        }

        // 注入悬停操作条回调（回复/编辑/撤回/置顶）
        vm.ReplyRequested = t => BeginReply(t);
        vm.EditRequested = t => BeginEdit(t);
        vm.RevokeRequested = t => _ = RevokeMessage(t);
        vm.PinRequested = t => _ = TogglePin(t);
        vm.IsPinned = m.IsPinned;
        // Reaction 回调：点已有气泡 toggle / 点“+”弹选择器
        vm.ReactRequested = (t, emoji) => _ = ToggleReaction(t, emoji);
        vm.PickReactionRequested = t => PickReactionRequested?.Invoke(t);
        return vm;
    }

    private async Task TogglePin(MessageItemViewModel vm)
    {
        try { await _client.PinMessageAsync(vm.MsgId, CurrentChannelId, !vm.IsPinned); }
        catch (Exception ex) { Console.WriteLine($"置顶失败: {ex.Message}"); }
    }

    /// <summary>View 层订阅：某条消息请求打开 emoji 选择器（弹 Popup）。</summary>
    public event Action<MessageItemViewModel>? PickReactionRequested;

    private async Task ToggleReaction(MessageItemViewModel vm, string emoji)
    {
        try { await _client.ReactAsync(vm.MsgId, emoji, CurrentChannelId); }
        catch (Exception ex) { Console.WriteLine($"反应失败: {ex.Message}"); }
    }

    /// <summary>服务端反应变更推送：定位到对应频道缓存的消息，更新其气泡聚合。</summary>
    private void OnReactionChanged(ReactionUpdate u)
    {
        var cid = string.IsNullOrEmpty(u.ChannelId) ? CurrentChannelId : u.ChannelId;
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            var vm = CacheFor(cid).FirstOrDefault(m => m.MsgId == u.MsgId);
            vm?.ApplyReaction(u.Emoji, u.UserIds, _currentUserId);
        });
    }

    /// <summary>历史加载后批量补齐一屏消息的反应聚合。</summary>
    private async Task LoadReactionsAsync(string channelId, System.Collections.Generic.IEnumerable<long> msgIds)
    {
        try
        {
            var all = await _client.GetReactionsAsync(msgIds, channelId);
            if (all.Count == 0) return;
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                var col = CacheFor(channelId);
                foreach (var u in all)
                {
                    var vm = col.FirstOrDefault(m => m.MsgId == u.MsgId);
                    vm?.ApplyReaction(u.Emoji, u.UserIds, _currentUserId);
                }
            });
        }
        catch (Exception ex) { Console.WriteLine($"加载反应失败: {ex.Message}"); }
    }

    private static string FormatFileSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:0.#} KB";
        if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024):0.#} MB";
        return $"{bytes / (1024.0 * 1024 * 1024):0.#} GB";
    }

    // ===== 搜索跳转定位（由独立搜索窗口双击结果触发）=====

    /// <summary>请求视图把某条消息滚动到可视区（View 层订阅，用容器 BringIntoView）。</summary>
    public event Action<long>? ScrollToMessageRequested;

    private System.Threading.CancellationTokenSource? _highlightCts;

    /// <summary>
    /// 跳转到指定频道的指定消息（对标 QQ 搜索双击定位）：
    /// 切频道 → 若目标不在缓存则「围绕加载」把它所在时段的历史拉进来 → 滚动到位 → 高亮闪一下。
    /// </summary>
    public async Task NavigateToMessageAsync(string channelId, string channelName, long msgId)
    {
        // 1) 切到目标频道（零重连；首次会懒加载近 7 天）
        if (channelId != CurrentChannelId)
            await SwitchToChannelAsync(channelId, channelName);

        // 2) 目标不在当前缓存 → 围绕该消息加载一屏历史（老消息可能超出近 7 天窗口）
        var col = CacheFor(channelId);
        if (col.All(m => m.MsgId != msgId))
            await LoadAroundMessageAsync(channelId, msgId);

        // 3) 滚动到位 + 高亮
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            ScrollToMessageRequested?.Invoke(msgId);
            HighlightMessage(msgId);
        });
    }

    /// <summary>围绕某条消息加载上下各若干条，重建该频道缓存并居中目标（供跳转到缓存外老消息）。</summary>
    private async Task LoadAroundMessageAsync(string channelId, long msgId, int radius = 25)
    {
        try
        {
            var around = await _client.QueryAroundMessageAsync(channelId, msgId, radius);
            if (around.Count == 0) return;
            var col = CacheFor(channelId);
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                col.Clear();
                MessageItemViewModel? prev = null;
                foreach (var m in around.OrderBy(m => m.MsgId))
                {
                    var vm = ToVm(m);
                    vm.IsGrouped = ShouldGroup(prev, vm);
                    col.Add(vm);
                    prev = vm;
                }
            });
            // 围绕加载后不再连续追加实时消息（避免与懒加载窗口错位）：标记为已加载即可，
            // 实时消息仍会 AppendMessage 到尾部，顺序正确。
            _historyLoaded.Add(channelId);
        }
        catch (Exception ex) { Console.WriteLine($"围绕加载失败: {ex.Message}"); }
    }

    /// <summary>高亮某条消息 2.5 秒后自动熄灭（多次跳转时取消上一次的熄灭计时）。</summary>
    private void HighlightMessage(long msgId)
    {
        _highlightCts?.Cancel();
        var cts = new System.Threading.CancellationTokenSource();
        _highlightCts = cts;

        foreach (var m in Messages) m.IsHighlighted = false;
        var target = Messages.FirstOrDefault(m => m.MsgId == msgId);
        if (target == null) return;
        target.IsHighlighted = true;

        _ = Task.Delay(2500, cts.Token).ContinueWith(t =>
        {
            if (t.IsCanceled) return;
            Avalonia.Threading.Dispatcher.UIThread.Post(() => target.IsHighlighted = false);
        }, TaskScheduler.Default);
    }

    private void OnUserTyping(string userId, string userName, string channelId)
    {
        // 只显示当前频道的 typing 指示
        if (userId == _currentUserId) return;
        if (!string.IsNullOrEmpty(channelId) && channelId != CurrentChannelId) return;
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            _typingUsers[userName] = DateTime.Now;
            RefreshTyping();
        });
    }

    private void OnUserStopTyping(string userId, string channelId)
    {
        if (userId == _currentUserId) return;
        if (!string.IsNullOrEmpty(channelId) && channelId != CurrentChannelId) return;
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            _typingUsers.Remove(userId);
            RefreshTyping();
        });
    }

    // ===== 编辑 / 撤回：按 channelId 分派到对应频道缓存（对标 Discord，跨频道也正确）=====

    private void OnMessageModified(string channelId, long msgId, string newContent)
    {
        var cid = string.IsNullOrEmpty(channelId) ? CurrentChannelId : channelId;
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            var vm = CacheFor(cid).FirstOrDefault(m => m.MsgId == msgId);
            if (vm == null) return;
            vm.Content = newContent;
            vm.IsEdited = true;
        });
    }

    private void OnMessageRevoked(string channelId, long msgId)
    {
        var cid = string.IsNullOrEmpty(channelId) ? CurrentChannelId : channelId;
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            var vm = CacheFor(cid).FirstOrDefault(m => m.MsgId == msgId);
            if (vm != null) vm.IsRecalled = true;
        });
    }

    private void OnPinChanged(long msgId, string channelId, bool isPinned)
    {
        var cid = string.IsNullOrEmpty(channelId) ? CurrentChannelId : channelId;
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            var vm = CacheFor(cid).FirstOrDefault(m => m.MsgId == msgId);
            if (vm != null) vm.IsPinned = isPinned;
        });
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _typingTimer.Stop();
        _typingTimer.Dispose();
        _client.OnUserTyping -= OnUserTyping;
        _client.OnUserStopTyping -= OnUserStopTyping;
        _client.OnMessageModified -= OnMessageModified;
        _client.OnMessageRevoked -= OnMessageRevoked;
        _client.OnPinChanged -= OnPinChanged;
        _client.OnReactionChanged -= OnReactionChanged;
    }

    private void PruneTyping()
    {
        var now = DateTime.Now;
        var expired = _typingUsers.Where(kv => (now - kv.Value).TotalSeconds > 3).Select(kv => kv.Key).ToList();
        if (expired.Count == 0) return;
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            foreach (var k in expired) _typingUsers.Remove(k);
            RefreshTyping();
        });
    }

    private void RefreshTyping()
    {
        if (_typingUsers.Count == 0) { ShowTypingIndicator = false; TypingIndicatorText = string.Empty; }
        else { ShowTypingIndicator = true; TypingIndicatorText = string.Join("、", _typingUsers.Keys); }
    }

    private static string ShortName(string userId)
        => string.IsNullOrEmpty(userId) ? "?" : userId;

    private static string ColorFor(string userId)
    {
        var palette = new[] { "#5865F2", "#3BA55D", "#FAA61A", "#ED4245", "#EB459E", "#00A8FC" };
        return palette[Math.Abs(userId.GetHashCode()) % palette.Length];
    }
}
