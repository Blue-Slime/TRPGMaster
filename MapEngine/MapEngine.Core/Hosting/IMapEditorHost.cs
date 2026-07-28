namespace MapEngine.Core.Hosting;

/// <summary>
/// 地图编辑器宿主接口。由外部主程序（TRPGMaster）实现并注入给地图模块。
/// 地图模块通过此接口获取上下文（用户身份、项目路径、日志等），但绝不直接调用外部服务。
/// 所有外部能力（AI、网络、脚本）都是单向：host → 地图（写入命令），地图 → host（事件冒泡）。
/// </summary>
public interface IMapEditorHost
{
    /// <summary>当前用户 ID（用于权限、记录、协作标识）</summary>
    string CurrentUserId { get; }

    /// <summary>项目根目录（场景文件、素材库、配置都在此目录下）</summary>
    string ProjectRootPath { get; }

    /// <summary>用户角色：决定 UI 权限与可见性</summary>
    UserRole Role { get; }

    /// <summary>日志输出目标（地图模块只生产日志，不消费）</summary>
    IMapEditorLogger Logger { get; }

    /// <summary>
    /// 发送实时流式数据（拖拽预览/光标位置/画笔轨迹等）。
    /// 不持久化，不进入命令历史，仅用于视觉即时反馈。
    /// 地图模块不依赖网络实现；宿主可以选择忽略（如单机模式）。
    /// </summary>
    /// <param name="type">数据类型标识，如 "map_drag" / "map_cursor"</param>
    /// <param name="data">可序列化的数据对象</param>
    void SendStream(string type, object data);
}

public enum UserRole
{
    /// <summary>地下城主：完整权限</summary>
    GM,
    /// <summary>玩家：受限编辑（仅自己的 Token 等）</summary>
    Player,
    /// <summary>观战者：完全只读</summary>
    Spectator,
}

/// <summary>地图模块向外冒泡日志的轻量接口</summary>
public interface IMapEditorLogger
{
    void Info(string message);
    void Warn(string message);
    void Error(string message, Exception? ex = null);
}

/// <summary>用于离线/独立运行的默认宿主实现（流式数据不做任何操作）</summary>
public sealed class StandaloneMapEditorHost : IMapEditorHost
{
    public string CurrentUserId { get; init; } = "local";
    public string ProjectRootPath { get; init; } = AppContext.BaseDirectory;
    public UserRole Role { get; init; } = UserRole.GM;
    public IMapEditorLogger Logger { get; init; } = ConsoleMapEditorLogger.Instance;

    /// <inheritdoc/>
    public void SendStream(string type, object data) { /* 单机模式：不发送 */ }
}

/// <summary>默认控制台日志实现</summary>
public sealed class ConsoleMapEditorLogger : IMapEditorLogger
{
    public static readonly ConsoleMapEditorLogger Instance = new();
    public void Info(string message) => Console.WriteLine($"[MapEditor] INFO: {message}");
    public void Warn(string message) => Console.WriteLine($"[MapEditor] WARN: {message}");
    public void Error(string message, Exception? ex = null)
        => Console.WriteLine($"[MapEditor] ERROR: {message}" + (ex != null ? $"\n{ex}" : ""));
}

/// <summary>静默日志实现</summary>
public sealed class NullMapEditorLogger : IMapEditorLogger
{
    public static readonly NullMapEditorLogger Instance = new();
    public void Info(string message) { }
    public void Warn(string message) { }
    public void Error(string message, Exception? ex = null) { }
}
