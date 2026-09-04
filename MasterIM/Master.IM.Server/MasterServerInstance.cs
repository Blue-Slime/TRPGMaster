using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MasterIM.Models;
using MasterIM.Server.Storage;
using MasterIM.Server.WebSocket;
using MasterIM.Server.Services;

namespace MasterIM.Server;

public enum ServerState { Stopped, Starting, Running, Stopping, Error }

public class ServerStateChangedEventArgs : EventArgs
{
    public ServerState OldState { get; init; }
    public ServerState NewState { get; init; }
}

/// <summary>服务器统计信息</summary>
public class ServerStatistics
{
    public int ActiveConnections { get; set; }
    public int OnlineUsers { get; set; }
    public int TotalRooms { get; set; }
    public int OnlineRooms { get; set; }
    public double CpuUsagePercent { get; set; }
    public long MemoryUsageBytes { get; set; }
}

/// <summary>房间内一名在场成员（基于活动连接）</summary>
public class RoomOnlineMember
{
    public string ConnectionId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string ChannelId { get; set; } = string.Empty;
    public string Status { get; set; } = "active";
    public DateTime JoinedAt { get; set; }
}

/// <summary>
/// 服务端实例（单进程多房间）。封装 WebSocket host + 存储 + 连接/房间运行时，
/// 供管理台(MasterServerUI)在同进程内直接持有、启停与管理，也供无头 MasterServer 复用。
/// </summary>
public class MasterServerInstance
{
    private readonly string _dataPath;
    private WebApplication? _app;
    private ServerState _state = ServerState.Stopped;

    // 内部服务（Start 后可用）
    private ConnectionManager? _connMgr;
    private RoomStore? _roomStore;
    private ChannelStore? _channelStore;
    private RoomMemberStore? _memberStore;
    private MessageStore? _messageStore;
    private RoomRuntimeManager? _runtime;
    private RoomMapManager? _mapManager;

    private Process? _proc;
    private DateTime _lastCpuSample = DateTime.UtcNow;
    private TimeSpan _lastCpuTotal = TimeSpan.Zero;

    public event EventHandler<ServerStateChangedEventArgs>? StateChanged;

    public ServerState State => _state;
    public int Port { get; private set; }
    public string DataPath => _dataPath;

    public MasterServerInstance(string? dataPath = null)
    {
        _dataPath = dataPath ?? SharedPaths.ResolveDataPath();
        System.IO.Directory.CreateDirectory(_dataPath);
    }

    private void SetState(ServerState next)
    {
        if (_state == next) return;
        var old = _state;
        _state = next;
        StateChanged?.Invoke(this, new ServerStateChangedEventArgs { OldState = old, NewState = next });
    }

    // MASTERSERVERINSTANCE_PART1_END
    public async Task StartAsync(int port)
    {
        if (_state == ServerState.Running || _state == ServerState.Starting) return;
        SetState(ServerState.Starting);
        try
        {
            Port = port;
            var builder = WebApplication.CreateBuilder();
            builder.Logging.ClearProviders();

            builder.Services.AddSingleton<ConnectionManager>();
            builder.Services.AddSingleton<DMConnectionManager>();
            builder.Services.AddSingleton(new MessageStore(_dataPath));
            builder.Services.AddSingleton(new ObjectStore(_dataPath));
            builder.Services.AddSingleton(new DMAdvancedStore(_dataPath));
            builder.Services.AddSingleton(new DMCleanupService(_dataPath));
            builder.Services.AddSingleton(new FileService(_dataPath));
            builder.Services.AddSingleton(new RoomMemberStore(_dataPath));
            builder.Services.AddSingleton(new RoomStore(_dataPath));
            builder.Services.AddSingleton(new ChannelStore(_dataPath));
            builder.Services.AddSingleton(new ReactionStore(_dataPath));
            builder.Services.AddSingleton(new UserAccountStore(_dataPath));
            builder.Services.AddSingleton<RoomRuntimeManager>();
            builder.Services.AddSingleton<RoomMapManager>();
            builder.Services.AddSingleton<IMServer>();
            builder.Services.AddSingleton<DMServer>();
            builder.Services.AddSingleton<DMAdvancedServer>();

            builder.WebHost.UseUrls($"http://localhost:{port}");

            var app = builder.Build();
            app.UseWebSockets();

            app.Map("/im", async context =>
            {
                if (!context.WebSockets.IsWebSocketRequest) { context.Response.StatusCode = 400; return; }
                var userId = context.Request.Query["userId"].ToString();
                var roomId = context.Request.Query["roomId"].ToString();
                var channelId = context.Request.Query["channelId"].ToString();
                var ws = await context.WebSockets.AcceptWebSocketAsync();
                var server = context.RequestServices.GetRequiredService<IMServer>();
                await server.HandleConnectionAsync(ws, userId, roomId, channelId);
            });

            app.Map("/dm_advanced", async context =>
            {
                if (!context.WebSockets.IsWebSocketRequest) { context.Response.StatusCode = 400; return; }
                var userId = context.Request.Query["userId"].ToString();
                var targetUserId = context.Request.Query["targetUserId"].ToString();
                var enableStorage = bool.Parse(context.Request.Query["enableStorage"].ToString() is { Length: > 0 } s ? s : "true");
                var retentionDays = int.Parse(context.Request.Query["retentionDays"].ToString() is { Length: > 0 } r ? r : "-1");
                var ws = await context.WebSockets.AcceptWebSocketAsync();
                var server = context.RequestServices.GetRequiredService<DMAdvancedServer>();
                await server.HandleConnectionAsync(ws, userId, targetUserId, enableStorage, retentionDays);
            });

            app.MapPost("/upload", async (Microsoft.AspNetCore.Http.HttpContext context) =>
            {
                var userId = context.Request.Query["userId"].ToString();
                var roomId = context.Request.Query["roomId"].ToString();
                if (!context.Request.HasFormContentType || context.Request.Form.Files.Count == 0)
                { context.Response.StatusCode = 400; return; }
                var file = context.Request.Form.Files[0];
                var fileService = context.RequestServices.GetRequiredService<FileService>();
                var ft = await fileService.SaveFileAsync(roomId, file.OpenReadStream(), file.FileName, userId);
                await context.Response.WriteAsJsonAsync(new { ft.FileId, ft.FileName, ft.FileSize, ft.FileType,
                    Url = $"/download?fileId={ft.FileId}&roomId={roomId}&fileName={ft.FileName}" });
            });

            app.MapGet("/download", async (Microsoft.AspNetCore.Http.HttpContext context) =>
            {
                var fileId = context.Request.Query["fileId"].ToString();
                var roomId = context.Request.Query["roomId"].ToString();
                var fileName = context.Request.Query["fileName"].ToString();
                var fileService = context.RequestServices.GetRequiredService<FileService>();
                var path = fileService.GetFilePath(roomId, fileId, fileName);
                if (!System.IO.File.Exists(path)) { context.Response.StatusCode = 404; return; }
                await context.Response.SendFileAsync(path);
            });

            app.MapPost("/asset/import", async (Microsoft.AspNetCore.Http.HttpContext context) =>
            {
                var roomId = context.Request.Query["roomId"].ToString();
                var assetType = context.Request.Query["type"].ToString(); // "token", "map", "audio", "file"
                var uploaderId = context.Request.Query["userId"].ToString();

                if (string.IsNullOrEmpty(roomId) || string.IsNullOrEmpty(assetType))
                { context.Response.StatusCode = 400; return; }

                if (!context.Request.HasFormContentType || context.Request.Form.Files.Count == 0)
                { context.Response.StatusCode = 400; return; }

                var file = context.Request.Form.Files[0];

                // 确保房间资产目录存在
                MapPersistence.RoomAssetPaths.EnsureDirectories(roomId);

                // 获取分类存储目录
                var targetDir = MapPersistence.RoomAssetPaths.GetAssetDirectory(roomId, assetType);

                // 语义命名：使用原始文件名（去掉路径分量，防目录穿越）
                var fileName = System.IO.Path.GetFileName(file.FileName);
                if (string.IsNullOrWhiteSpace(fileName))
                { context.Response.StatusCode = 400; return; }
                var assetsRoot = MapPersistence.RoomAssetPaths.GetAssetsDirectory(roomId);
                var index = new MapPersistence.AssetIndexManager(roomId);

                // 先落临时文件算哈希：哈希已在库里就直接复用，不产生重复副本
                var tempPath = System.IO.Path.Combine(targetDir, $".upload-{Guid.NewGuid():N}.tmp");
                string hash;
                try
                {
                    using (var stream = System.IO.File.Create(tempPath))
                        await file.CopyToAsync(stream);

                    hash = MapEngine.Core.Utilities.ContentHasher.ComputeHash(tempPath);

                    var existing = index.GetAsset(hash);
                    if (existing is not null &&
                        System.IO.File.Exists(System.IO.Path.Combine(assetsRoot, existing.RelativePath)))
                    {
                        System.IO.File.Delete(tempPath);
                        await context.Response.WriteAsJsonAsync(new
                        {
                            Success = true,
                            Hash = hash,
                            FileName = existing.FileName,
                            RelativePath = existing.RelativePath,
                            AssetType = existing.Type,
                            Deduplicated = true
                        });
                        return;
                    }

                    // 同名不同内容：加序号避免覆盖
                    var nameWithoutExt = System.IO.Path.GetFileNameWithoutExtension(fileName);
                    var ext = System.IO.Path.GetExtension(fileName);
                    var targetPath = System.IO.Path.Combine(targetDir, fileName);
                    var counter = 1;
                    while (System.IO.File.Exists(targetPath))
                    {
                        fileName = $"{nameWithoutExt}_{counter}{ext}";
                        targetPath = System.IO.Path.Combine(targetDir, fileName);
                        counter++;
                    }

                    System.IO.File.Move(tempPath, targetPath);

                    var relativePath = System.IO.Path.GetRelativePath(assetsRoot, targetPath)
                        .Replace('\\', '/');

                    index.AddAsset(new MapPersistence.AssetMetadata
                    {
                        Hash         = hash,
                        Type         = assetType.ToLowerInvariant(),
                        FileName     = fileName,
                        RelativePath = relativePath,
                        DisplayName  = nameWithoutExt,
                        MimeType     = file.ContentType ?? string.Empty,
                        Size         = new System.IO.FileInfo(targetPath).Length,
                        UploadedBy   = uploaderId,
                        UploadedAt   = DateTime.UtcNow
                    });

                    await context.Response.WriteAsJsonAsync(new
                    {
                        Success = true,
                        Hash = hash,
                        FileName = fileName,
                        RelativePath = relativePath,
                        AssetType = assetType.ToLowerInvariant(),
                        Deduplicated = false
                    });
                }
                catch
                {
                    if (System.IO.File.Exists(tempPath)) System.IO.File.Delete(tempPath);
                    throw;
                }
            });

            // 房间素材清单：客户端据此做增量同步（对比本地缺哪些 hash）
            app.MapGet("/asset/index", async (Microsoft.AspNetCore.Http.HttpContext context) =>
            {
                var roomId = context.Request.Query["roomId"].ToString();
                if (string.IsNullOrEmpty(roomId))
                { context.Response.StatusCode = 400; return; }

                var index = new MapPersistence.AssetIndexManager(roomId);
                await context.Response.WriteAsJsonAsync(new
                {
                    RoomId = roomId,
                    Assets = index.GetAllAssets().Select(a => new
                    {
                        a.Hash, a.Type, a.FileName, a.RelativePath,
                        a.DisplayName, a.MimeType, a.Size
                    }).ToList()
                });
            });

            // 按哈希下载房间素材（语义命名：实际路径查 index.json）
            app.MapGet("/asset/download", async (Microsoft.AspNetCore.Http.HttpContext context) =>
            {
                var roomId = context.Request.Query["roomId"].ToString();
                var hash = context.Request.Query["hash"].ToString();
                if (string.IsNullOrEmpty(roomId) || string.IsNullOrEmpty(hash))
                { context.Response.StatusCode = 400; return; }

                var index = new MapPersistence.AssetIndexManager(roomId);
                var meta = index.GetAsset(hash);
                if (meta is null || string.IsNullOrEmpty(meta.RelativePath))
                { context.Response.StatusCode = 404; return; }

                var assetsRoot = MapPersistence.RoomAssetPaths.GetAssetsDirectory(roomId);
                var fullPath = System.IO.Path.GetFullPath(
                    System.IO.Path.Combine(assetsRoot, meta.RelativePath));

                // 防目录穿越：解析后必须仍在 assets/ 下
                if (!fullPath.StartsWith(System.IO.Path.GetFullPath(assetsRoot), StringComparison.OrdinalIgnoreCase)
                    || !System.IO.File.Exists(fullPath))
                { context.Response.StatusCode = 404; return; }

                // 客户端要用 relativePath 落盘到同构缓存目录，走响应头带出去。
                // HTTP 头只允许 ASCII，中文文件名必须 URL 编码（客户端解码后使用）。
                context.Response.Headers["X-Asset-RelativePath"] = Uri.EscapeDataString(meta.RelativePath);
                context.Response.Headers["X-Asset-FileName"] = Uri.EscapeDataString(meta.FileName);
                await context.Response.SendFileAsync(fullPath);
            });

            // 缓存内部服务引用供管理 API 使用
            _connMgr = app.Services.GetRequiredService<ConnectionManager>();
            _roomStore = app.Services.GetRequiredService<RoomStore>();
            _channelStore = app.Services.GetRequiredService<ChannelStore>();
            _memberStore = app.Services.GetRequiredService<RoomMemberStore>();
            _messageStore = app.Services.GetRequiredService<MessageStore>();
            _runtime = app.Services.GetRequiredService<RoomRuntimeManager>();
            _mapManager = app.Services.GetRequiredService<RoomMapManager>();

            _proc = Process.GetCurrentProcess();
            _lastCpuTotal = _proc.TotalProcessorTime;
            _lastCpuSample = DateTime.UtcNow;

            await app.StartAsync();
            _app = app;
            SetState(ServerState.Running);
        }
        catch
        {
            SetState(ServerState.Error);
            throw;
        }
    }

    public async Task StopAsync()
    {
        if (_app == null) { SetState(ServerState.Stopped); return; }
        SetState(ServerState.Stopping);
        try
        {
            // 保存所有房间的地图场景
            _mapManager?.SaveAllRooms();

            await _app.StopAsync();
            await _app.DisposeAsync();
        }
        finally
        {
            _app = null;
            _connMgr = null; _roomStore = null; _channelStore = null;
            _memberStore = null; _messageStore = null; _runtime = null;
            _mapManager = null;
            SetState(ServerState.Stopped);
        }
    }

    // ========== 房间管理 ==========

    public async Task<List<Room>> GetAllRoomsAsync()
        => _roomStore == null ? new() : await _roomStore.GetAllRoomsAsync();

    public async Task<Room> CreateRoomAsync(string name, string ownerId, int maxPlayers,
        string? description = null, bool isPublic = true, string? password = null,
        string? customRoomId = null)
    {
        if (_roomStore == null) throw new InvalidOperationException("服务器未运行");
        var room = new Room
        {
            RoomId = string.IsNullOrWhiteSpace(customRoomId)
                ? $"room_{Guid.NewGuid():N}".Substring(0, 16)
                : customRoomId.Trim(),
            RoomName = name,
            Description = description ?? "",
            Password = password ?? "",
            OwnerId = string.IsNullOrWhiteSpace(ownerId) ? "admin" : ownerId,
            HostUserId = string.IsNullOrWhiteSpace(ownerId) ? "admin" : ownerId,
            IsPublic = isPublic,
            RequireApproval = false,
            CreatedAt = DateTime.UtcNow
        };
        await _roomStore.CreateRoomAsync(room);
        // 默认建一个大厅频道
        await _channelStore!.CreateChannelAsync(new Channel
        {
            ChannelId = "channel_lobby", RoomId = room.RoomId, ChannelName = "大厅"
        });
        // 建好即开启
        _runtime?.Open(room.RoomId);
        return room;
    }

    public async Task DeleteRoomAsync(string roomId)
    {
        if (_roomStore == null) return;
        CloseRoom(roomId);
        await _roomStore.DeleteRoomAsync(roomId);
    }

    public async Task UpdateRoomAsync(Room room)
    {
        if (_roomStore == null) return;
        await _roomStore.UpdateRoomAsync(room);
    }

    // ========== 房间上下线 ==========

    public bool IsRoomOnline(string roomId) => _runtime?.IsOnline(roomId) ?? false;

    public void OpenRoom(string roomId) => _runtime?.Open(roomId);

    /// <summary>关闭房间：拒绝新连接 + 断开在场连接</summary>
    public void CloseRoom(string roomId)
    {
        _runtime?.Close(roomId);
        if (_connMgr == null) return;
        foreach (var c in _connMgr.GetRoomConnections(roomId))
        {
            try { _ = c.WebSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "房间已关闭", CancellationToken.None); }
            catch { }
        }
    }

    public IReadOnlyCollection<string> OnlineRoomIds => _runtime?.OnlineRoomIds ?? Array.Empty<string>();

    // ========== 成员 / 踢人 / 广播 ==========

    /// <summary>房间在场成员（基于活动连接）</summary>
    public List<RoomOnlineMember> GetRoomMembers(string roomId)
    {
        if (_connMgr == null) return new();
        return _connMgr.GetRoomConnections(roomId).Select(c => new RoomOnlineMember
        {
            ConnectionId = c.Id,
            UserId = c.UserId,
            ChannelId = c.ChannelId,
            Status = c.Status,
            JoinedAt = c.CreatedAt
        }).ToList();
    }

    /// <summary>踢出某连接（按 connectionId）</summary>
    public async Task<bool> KickPlayerAsync(string connectionId, string reason = "被管理员踢出")
    {
        if (_connMgr == null) return false;
        var conn = _connMgr.AllConnections.FirstOrDefault(c => c.Id == connectionId);
        if (conn == null) return false;
        try
        {
            await conn.WebSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, reason, CancellationToken.None);
        }
        catch { }
        _connMgr.Remove(conn);
        return true;
    }

    /// <summary>向房间广播一条系统消息（落库 + 推送）</summary>
    public async Task BroadcastSystemMessageAsync(string roomId, string message, string channelId = "channel_lobby")
    {
        if (_connMgr == null || _messageStore == null) return;
        var msg = new GroupMessage
        {
            SenderId = "system",
            Content = message,
            MessageType = "system"
        };
        await _messageStore.SaveAsync(roomId, channelId, msg);

        var json = JsonSerializer.Serialize(new Packet { T = "msg", P = msg });
        var bytes = Encoding.UTF8.GetBytes(json);
        foreach (var c in _connMgr.GetChannelConnections(roomId, channelId))
        {
            if (c.WebSocket.State == WebSocketState.Open)
            {
                try { await c.WebSocket.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None); }
                catch { }
            }
        }
    }

    // ========== 统计 ==========

    public ServerStatistics GetStatistics()
    {
        var stats = new ServerStatistics();
        if (_state != ServerState.Running) return stats;

        stats.ActiveConnections = _connMgr?.Count ?? 0;
        stats.OnlineUsers = _connMgr?.DistinctUserCount ?? 0;
        stats.OnlineRooms = _runtime?.OnlineRoomCount ?? 0;
        try { stats.TotalRooms = _roomStore?.GetAllRoomsAsync().GetAwaiter().GetResult().Count ?? 0; }
        catch { stats.TotalRooms = 0; }

        try
        {
            _proc?.Refresh();
            stats.MemoryUsageBytes = _proc?.WorkingSet64 ?? 0;

            if (_proc != null)
            {
                var nowTotal = _proc.TotalProcessorTime;
                var now = DateTime.UtcNow;
                var cpuDelta = (nowTotal - _lastCpuTotal).TotalMilliseconds;
                var wallDelta = (now - _lastCpuSample).TotalMilliseconds;
                if (wallDelta > 0)
                    stats.CpuUsagePercent = Math.Clamp(cpuDelta / (wallDelta * Environment.ProcessorCount) * 100.0, 0, 100);
                _lastCpuTotal = nowTotal;
                _lastCpuSample = now;
            }
        }
        catch { }

        return stats;
    }
}
