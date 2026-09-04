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

    /// <summary>
    /// 房间共享素材库：%AppData%/TRPGMaster/rooms/{roomId}/assets。
    /// 未连接房间（单机运行）时返回 null，素材库面板据此隐藏"房间库"切换。
    /// </summary>
    public string? RoomAssetLibraryPath
    {
        get
        {
            var roomId = _imClient?.CurrentRoomId;
            if (string.IsNullOrWhiteSpace(roomId)) return null;

            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var dir = Path.Combine(appData, "TRPGMaster", "rooms", roomId, "assets");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    /// <summary>
    /// 房间场景缓存目录：%AppData%/TRPGMaster/rooms/{roomId}/scenes。
    /// 用于缓存从服务器同步的场景文件（.scene），与本地 Maps\ 分离。
    /// 未连接房间时返回 null。
    /// </summary>
    public string? RoomScenesPath
    {
        get
        {
            var roomId = _imClient?.CurrentRoomId;
            if (string.IsNullOrWhiteSpace(roomId)) return null;

            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var dir = Path.Combine(appData, "TRPGMaster", "rooms", roomId, "scenes");
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

    /// <inheritdoc/>
    /// <remarks>
    /// 走 HTTP POST /asset/import：服务端按 assetType 落到 assets/{tokens|maps|audio|files}/，
    /// 保留原文件名（语义命名），同内容按哈希去重复用。
    /// </remarks>
    public async Task<string?> UploadRoomAssetAsync(string localFilePath, string assetType)
    {
        if (_imClient is null) return null;
        if (string.IsNullOrWhiteSpace(_imClient.CurrentRoomId)) return null;
        if (!File.Exists(localFilePath)) return null;

        var result = await _imClient.UploadAssetAsync(localFilePath, assetType);
        return result.Success ? result.Hash : null;
    }

    /// <summary>
    /// 把服务端房间共享库增量同步到本地房间缓存（结构与服务端同构）。
    /// 进房间后调用一次即可，只拉本地缺失的素材。
    /// </summary>
    /// <returns>本次实际下载的素材数量</returns>
    public async Task<int> SyncRoomAssetsAsync()
    {
        if (_imClient is null) return 0;
        var cacheRoot = RoomAssetLibraryPath;
        if (cacheRoot is null) return 0;

        return await _imClient.SyncRoomAssetsAsync(cacheRoot);
    }
}

file sealed class DebugMapEditorLogger : IMapEditorLogger
{
    public void Info(string message)  => System.Diagnostics.Debug.WriteLine($"[MapEngine] {message}");
    public void Warn(string message)  => System.Diagnostics.Debug.WriteLine($"[MapEngine] WARN {message}");
    public void Error(string message, Exception? ex = null)
        => System.Diagnostics.Debug.WriteLine($"[MapEngine] ERROR {message}{(ex != null ? $"\n{ex}" : "")}");
}
