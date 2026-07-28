namespace MasterIM.Models;

/// <summary>
/// 骰子投掷结果（服务端广播 dice_result 事件的载荷）。
/// </summary>
public class DiceResult
{
    /// <summary>投掷者用户ID</summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>所在房间</summary>
    public string RoomId { get; set; } = string.Empty;

    /// <summary>所在频道</summary>
    public string ChannelId { get; set; } = string.Empty;

    /// <summary>骰子表达式（如 1d20+3）</summary>
    public string Formula { get; set; } = string.Empty;

    /// <summary>投掷结果</summary>
    public string Result { get; set; } = string.Empty;

    /// <summary>是否暗骰</summary>
    public bool IsSecret { get; set; }
}
