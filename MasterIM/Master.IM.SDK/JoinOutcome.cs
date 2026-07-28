using MasterIM.Models;

namespace MasterIM.SDK;

/// <summary>
/// 加入房间的终态结果（每次 ConnectAsync 调用必然得到其中之一，不会永久挂起）。
/// </summary>
public enum JoinResult
{
    /// <summary>加入成功，会话已建立。</summary>
    Success,

    /// <summary>等待房主批准（连接已建立，可继续等待）。</summary>
    Pending,

    /// <summary>
    /// 加入被永久拒绝（房主手动拒绝、黑名单等）。
    /// 不应重试，直接向用户展示 Message。
    /// </summary>
    Rejected,

    /// <summary>
    /// 重复连接竞态（同账号短时间内旧连接未完全清理）。
    /// 属暂时性拒绝，等待后可重试。
    /// </summary>
    DuplicateConnection,

    /// <summary>
    /// 房间层面的永久错误（房间不存在 / 未开启）。
    /// 不应重试。
    /// </summary>
    RoomError,

    /// <summary>网络层面的暂时性错误（握手失败、连接意外中断）。可重试。</summary>
    NetworkError,

    /// <summary>等待服务端响应超时。可重试。</summary>
    Timeout,

    /// <summary>调用方主动取消（CancellationToken 触发）。</summary>
    Cancelled,
}

/// <summary>连接 + 加入握手的确定性结果。</summary>
public sealed class JoinOutcome
{
    public JoinResult Result { get; }
    public string Message { get; }

    /// <summary>加入成功时由 join_success 握手带回的房间名 + 首批频道列表（其余结果为 null）。</summary>
    public JoinInfo? Info { get; }

    private JoinOutcome(JoinResult result, string message, JoinInfo? info = null)
    {
        Result = result;
        Message = message;
        Info = info;
    }

    /// <summary>是否属于可重试的暂时性失败。</summary>
    public bool IsTransient =>
        Result == JoinResult.NetworkError ||
        Result == JoinResult.Timeout ||
        Result == JoinResult.DuplicateConnection;

    /// <summary>是否应向用户展示明确错误（永久失败）。</summary>
    public bool IsPermanentFailure =>
        Result == JoinResult.Rejected ||
        Result == JoinResult.RoomError;

    public static JoinOutcome Success(JoinInfo? info = null)
        => new(JoinResult.Success, string.Empty, info);
    public static JoinOutcome Pending()
        => new(JoinResult.Pending, "等待房主批准...");
    public static JoinOutcome Rejected(string reason)
        => new(JoinResult.Rejected, reason);
    public static JoinOutcome Duplicate()
        => new(JoinResult.DuplicateConnection, "旧连接正在清理，稍后重试...");
    public static JoinOutcome RoomError(string reason)
        => new(JoinResult.RoomError, reason);
    public static JoinOutcome NetworkError(string reason)
        => new(JoinResult.NetworkError, reason);
    public static JoinOutcome TimedOut()
        => new(JoinResult.Timeout, "服务器未响应，请检查网络或服务端是否启动");
    public static JoinOutcome Cancelled()
        => new(JoinResult.Cancelled, string.Empty);
}
