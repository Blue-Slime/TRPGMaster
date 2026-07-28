using MapEngine.Core.Hosting;
using MasterIM.Models;
using MasterIM.SDK;

namespace MasterClient.Services;

/// <summary>
/// 把 MasterClient 的上下文适配成 MapEngine 的 IMapEditorHost。
/// imClient 可选；单机运行时传 null，流式数据自动忽略。
/// </summary>
public sealed class MapEngineHostAdapter : IMapEditorHost
{
    private readonly string _userId;
    private readonly IMClient? _imClient;

    public MapEngineHostAdapter(string userId = "local", IMClient? imClient = null)
    {
        _userId = userId;
        _imClient = imClient;
    }

    public string CurrentUserId => _userId;

    public string ProjectRootPath
    {
        get
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var dir = Path.Combine(appData, "TRPGMaster", "Maps");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public UserRole Role => UserRole.GM; // 房间角色未做前默认 GM

    public IMapEditorLogger Logger { get; } = new DebugMapEditorLogger();

    /// <inheritdoc/>
    /// <remarks>
    /// 流式数据（拖拽预览、光标位置等）通过 stm 通道发送，不进入命令历史。
    /// imClient 为 null 时（单机模式）静默忽略。
    /// </remarks>
    public void SendStream(string type, object data)
    {
        if (_imClient is null) return;

        var streamData = new StreamData
        {
            Type = type,
            Data = data,
            Timestamp = DateTime.UtcNow,
        };
        // fire-and-forget：流式数据丢失不影响正确性
        _ = _imClient.SendStreamAsync(streamData);
    }
}

file sealed class DebugMapEditorLogger : IMapEditorLogger
{
    public void Info(string message)  => System.Diagnostics.Debug.WriteLine($"[MapEngine] {message}");
    public void Warn(string message)  => System.Diagnostics.Debug.WriteLine($"[MapEngine] WARN {message}");
    public void Error(string message, Exception? ex = null)
        => System.Diagnostics.Debug.WriteLine($"[MapEngine] ERROR {message}{(ex != null ? $"\n{ex}" : "")}");
}
