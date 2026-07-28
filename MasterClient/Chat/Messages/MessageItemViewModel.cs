using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace MasterClient.Chat.Messages;

/// <summary>
/// 消息气泡模型。阶段1以文本为主，骰子/图片/文件/撤回/编辑字段先就位，
/// 后续阶段接后端富渲染时直接填充，不改结构。
/// </summary>
public partial class MessageItemViewModel : ObservableObject
{
    // 基本信息
    [ObservableProperty] private long _msgId;
    [ObservableProperty] private string _senderId = string.Empty;
    [ObservableProperty] private string _senderName = string.Empty;
    [ObservableProperty] private string _characterName = string.Empty;
    [ObservableProperty] private DateTime _sendTime;
    [ObservableProperty] private bool _isCurrentUser;
    [ObservableProperty] private string _avatarColor = "#5865F2";

    // 消息类型（text / dice / image / file / system）
    [ObservableProperty] private string _messageType = "text";

    // 文本内容
    [ObservableProperty] private string _content = string.Empty;

    // 引用（回复某条消息时的被引内容）
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasQuote))]
    private string? _quotedContent;

    // 状态
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EditedSuffix))]
    private bool _isEdited;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTextMessage))]
    [NotifyPropertyChangedFor(nameof(IsDiceRoll))]
    [NotifyPropertyChangedFor(nameof(IsImageMessage))]
    [NotifyPropertyChangedFor(nameof(IsFileMessage))]
    private bool _isRecalled;

    // 连续消息折叠（Discord 核心观感）：同一发送者短时间内连发，后续消息隐藏头像/名字头。
    // 由 MessageAreaViewModel 在追加/加载时按前一条计算。
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowHeader))]
    [NotifyPropertyChangedFor(nameof(TopMargin))]
    private bool _isGrouped;

    // 骰子（占位，阶段3启用）
    [ObservableProperty] private string _diceExpression = string.Empty;
    [ObservableProperty] private string _diceResultDisplay = string.Empty;

    // 图片/文件（占位，阶段3启用）
    [ObservableProperty] private string? _fileName;
    [ObservableProperty] private string? _fileSize;

    /// <summary>图片 URL（setter 触发异步加载 Bitmap，供 View 绑定）。</summary>
    [ObservableProperty] private string? _imageSource;
    partial void OnImageSourceChanged(string? value)
    {
        if (string.IsNullOrEmpty(value)) { ImageBitmap = null; return; }
        _ = LoadBitmapAsync(value);
    }

    /// <summary>已解码的 Bitmap（View 直接绑此属性）。</summary>
    [ObservableProperty] private Bitmap? _imageBitmap;

    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };

    private async Task LoadBitmapAsync(string url)
    {
        try
        {
            var bytes = await _http.GetByteArrayAsync(url);
            using var ms = new MemoryStream(bytes);
            var bmp = new Bitmap(ms);
            Avalonia.Threading.Dispatcher.UIThread.Post(() => ImageBitmap = bmp);
        }
        catch { /* 加载失败不崩溃，图片控件无 Source 显示空 */ }
    }

    // ===== 视图便捷判断 =====
    public bool HasCharacterName => !string.IsNullOrEmpty(CharacterName);
    public bool HasQuote => !string.IsNullOrEmpty(QuotedContent);
    public bool IsTextMessage => MessageType == "text" && !IsRecalled;
    public bool IsDiceRoll => MessageType == "dice" && !IsRecalled;
    public bool IsImageMessage => MessageType == "image" && !IsRecalled;
    public bool IsFileMessage => MessageType == "file" && !IsRecalled;
    public bool IsSystem => MessageType == "system";
    public string TimeString => SendTime.ToLocalTime().ToString("HH:mm");

    /// <summary>折叠悬停时显示的紧凑时间（对标 Discord 左侧灰色时间戳）。</summary>
    public string ShortTimeString => SendTime.ToLocalTime().ToString("HH:mm");

    /// <summary>编辑后缀（Run 不能 IsVisible，用后缀字符串条件显示"(已编辑)"）。</summary>
    public string EditedSuffix => IsEdited ? "  (已编辑)" : string.Empty;

    /// <summary>发送者头像上的首字（无头像图时的占位）。</summary>
    public string AvatarInitial => string.IsNullOrEmpty(SenderName) ? "?" : SenderName.Substring(0, 1).ToUpper();

    /// <summary>是否显示消息头（头像+名字+时间）。连续消息折叠时隐藏。</summary>
    public bool ShowHeader => !IsGrouped;

    /// <summary>折叠消息上边距更小，成组消息更紧凑（对标 Discord）。</summary>
    public Avalonia.Thickness TopMargin => IsGrouped ? new Avalonia.Thickness(0) : new Avalonia.Thickness(0, 17, 0, 0);

    /// <summary>搜索跳转命中时短暂高亮（黄褐底），由 MessageAreaViewModel 置位后定时复位。</summary>
    [ObservableProperty] private bool _isHighlighted;

    /// <summary>是否已置顶（服务端推送 msg_pin_changed 后由 VM 更新）。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PinTooltip))]
    [NotifyPropertyChangedFor(nameof(PinOpacity))]
    private bool _isPinned;

    /// <summary>置顶按钮提示文字。</summary>
    public string PinTooltip => IsPinned ? "取消置顶" : "置顶";
    /// <summary>置顶图标透明度（置顶中=不透明，未置顶=半透明）。</summary>
    public double PinOpacity => IsPinned ? 1.0 : 0.4;

    // ===== Reaction 聚合（对标 Discord：正文下方一排 emoji 计数气泡）=====

    /// <summary>本条消息的反应气泡（每个 emoji 一枚，含计数与"我是否参与"）。</summary>
    public System.Collections.ObjectModel.ObservableCollection<ReactionChipViewModel> Reactions { get; } = new();

    public bool HasReactions => Reactions.Count > 0;

    /// <summary>点某枚已有气泡切换我的反应（回调注入，参数 emoji）。</summary>
    public Action<MessageItemViewModel, string>? ReactRequested;
    /// <summary>点"+"打开 emoji 选择器（回调注入）。</summary>
    public Action<MessageItemViewModel>? PickReactionRequested;

    [RelayCommand] private void ToggleReaction(string emoji) => ReactRequested?.Invoke(this, emoji);
    [RelayCommand] private void PickReaction() => PickReactionRequested?.Invoke(this);

    // ===== 悬停操作条命令（由 MessageAreaViewModel 创建时注入回调）=====
    public Action<MessageItemViewModel>? ReplyRequested;
    public Action<MessageItemViewModel>? EditRequested;
    public Action<MessageItemViewModel>? RevokeRequested;
    public Action<MessageItemViewModel>? PinRequested;

    [RelayCommand] private void Reply() => ReplyRequested?.Invoke(this);
    [RelayCommand] private void Edit() => EditRequested?.Invoke(this);
    [RelayCommand] private void Revoke() => RevokeRequested?.Invoke(this);
    [RelayCommand] private void Pin() => PinRequested?.Invoke(this);

    /// <summary>用服务端聚合（某 emoji 的完整用户列表）更新/新增/移除对应气泡。</summary>
    public void ApplyReaction(string emoji, System.Collections.Generic.List<string> userIds, string myUserId)
    {
        var chip = Reactions.FirstOrDefault(r => r.Emoji == emoji);
        if (userIds.Count == 0)
        {
            if (chip != null) Reactions.Remove(chip);
        }
        else if (chip == null)
        {
            Reactions.Add(new ReactionChipViewModel
            {
                Emoji = emoji, Count = userIds.Count, Mine = userIds.Contains(myUserId)
            });
        }
        else
        {
            chip.Count = userIds.Count;
            chip.Mine = userIds.Contains(myUserId);
        }
        OnPropertyChanged(nameof(HasReactions));
    }
}

/// <summary>单枚反应气泡：一个 emoji + 计数 + 我是否参与（高亮描边）。</summary>
public partial class ReactionChipViewModel : ObservableObject
{
    [ObservableProperty] private string _emoji = string.Empty;
    [ObservableProperty] private int _count;
    /// <summary>当前用户是否已参与该反应（决定气泡高亮）。</summary>
    [ObservableProperty] private bool _mine;
}
