using System.Text.Json;
using MapEngine.Core.Components;

namespace MapEngine.Core.Networking;

/// <summary>
/// C→S: 客户端发送命令请求
/// </summary>
public sealed record MapCommandRequest
{
    /// <summary>命令类型（如 "AddObject", "MoveObject", "SetProperty"）</summary>
    public required string CommandType { get; init; }

    /// <summary>命令参数（JSON 对象，每种命令的参数不同）</summary>
    public required JsonElement Params { get; init; }

    /// <summary>发起操作的用户 ID（服务端原样转发至 Delta，用于回声过滤）</summary>
    public required string UserId { get; init; }

    /// <summary>客户端当前版本号（用于冲突检测，0 表示不检测）</summary>
    public int ExpectedVersion { get; init; }
}

/// <summary>
/// S→C: 命令执行结果
/// </summary>
public sealed record MapCommandResult
{
    /// <summary>是否成功执行</summary>
    public required bool Success { get; init; }

    /// <summary>失败原因（Success=false 时提供）</summary>
    public string? Error { get; init; }

    /// <summary>执行后的新版本号</summary>
    public int NewVersion { get; init; }

    /// <summary>命令生成的新对象 ID（如 CreateObject 命令）</summary>
    public string? CreatedObjectId { get; init; }
}

/// <summary>
/// S→broadcast: 状态增量（某个命令导致的变化）
/// </summary>
public sealed record MapStateDelta
{
    /// <summary>命令类型</summary>
    public required string CommandType { get; init; }

    /// <summary>命令参数（与 MapCommandRequest.Params 格式相同）</summary>
    public required JsonElement Params { get; init; }

    /// <summary>新版本号</summary>
    public required int Version { get; init; }

    /// <summary>执行命令的用户 ID（用于 UI 显示"谁改了"）</summary>
    public string? UserId { get; init; }
}

/// <summary>
/// S→C: 全量同步（客户端加入房间时、或版本落后太多时）
/// </summary>
public sealed record MapFullSync
{
    /// <summary>完整场景文档</summary>
    public required SceneDocument Document { get; init; }

    /// <summary>当前版本号</summary>
    public required int Version { get; init; }
}

/// <summary>
/// C→S: 请求全量同步（客户端检测到版本跳号时）
/// </summary>
public sealed record MapFullSyncRequest
{
    /// <summary>客户端当前版本号</summary>
    public int CurrentVersion { get; init; }
}
