using System;
using System.Collections.Concurrent;
using MapEngine.Core;
using MapEngine.Core.Commands;
using MapEngine.Core.Networking;

namespace MasterIM.Server;

/// <summary>
/// 管理每个房间的地图同步处理器（per-room MapSyncHandler）
/// </summary>
public class RoomMapManager
{
    private readonly ConcurrentDictionary<string, RoomMapContext> _contexts = new();

    /// <summary>
    /// 获取或创建指定房间的地图同步处理器
    /// </summary>
    public MapSyncHandler GetOrCreateHandler(string roomId)
    {
        var context = _contexts.GetOrAdd(roomId, rid =>
        {
            var world = new World();
            var commandBus = new CommandBus(world);
            var handler = new MapSyncHandler(world, commandBus);
            return new RoomMapContext(world, commandBus, handler);
        });

        return context.Handler;
    }

    /// <summary>
    /// 移除房间的地图上下文（房间关闭时调用）
    /// </summary>
    public void RemoveRoom(string roomId)
    {
        _contexts.TryRemove(roomId, out _);
    }

    /// <summary>
    /// 获取房间数量
    /// </summary>
    public int Count => _contexts.Count;

    private sealed record RoomMapContext(
        World World,
        CommandBus CommandBus,
        MapSyncHandler Handler
    );
}
