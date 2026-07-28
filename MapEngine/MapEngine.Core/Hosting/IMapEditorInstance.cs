namespace MapEngine.Core.Hosting;

/// <summary>
/// 地图编辑器实例的对外接口。host 通过此接口控制地图、观测状态。
/// 所有 host → 地图的操作严格通过此接口。
/// </summary>
public interface IMapEditorInstance : IDisposable
{
    /// <summary>
    /// 单向写入：执行 JSON-RPC 命令（与 AgentService 同一入口，
    /// 不论命令来源是 AI、网络、还是 host 自己）。
    /// </summary>
    /// <param name="jsonRpc">JSON-RPC 2.0 格式的请求</param>
    /// <returns>JSON-RPC 2.0 格式的响应</returns>
    Task<string> ProcessMessageAsync(string jsonRpc, CancellationToken ct = default);

    /// <summary>
    /// 单向读取：导出当前存档为 JSON 快照（供 AI 观测、外部分析、调试）。
    /// </summary>
    Task<string> ExportSceneSnapshotAsync(SnapshotScope scope = SnapshotScope.Full, CancellationToken ct = default);

    /// <summary>加载场景文件</summary>
    Task LoadSceneAsync(string scenePath, CancellationToken ct = default);

    /// <summary>保存当前场景</summary>
    Task SaveSceneAsync(CancellationToken ct = default);

    /// <summary>
    /// 命令执行事件冒泡。host 可监听此事件以决定何时把变更同步给 AI / 远程客户端。
    /// 地图模块本身不知道这些事件会被如何使用。
    /// </summary>
    event EventHandler<MapEditorCommandEvent>? CommandExecuted;
}

/// <summary>导出快照的范围</summary>
public enum SnapshotScope
{
    /// <summary>完整存档（全部对象，含组件细节）</summary>
    Full,
    /// <summary>仅当前视口可见对象（适合视觉相关的 AI 任务）</summary>
    Visible,
    /// <summary>仅选中对象（适合细节修改任务）</summary>
    Selected,
}

/// <summary>命令冒泡事件参数</summary>
public sealed class MapEditorCommandEvent : EventArgs
{
    /// <summary>命令的简要描述（已存在于 ICommand.Description）</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>动作类型：执行 / 撤销 / 重做</summary>
    public string Action { get; init; } = string.Empty;

    /// <summary>触发命令的用户 ID（用于多人协作过滤回环）</summary>
    public string UserId { get; init; } = string.Empty;

    /// <summary>事件发生的时间戳（UTC）</summary>
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}
