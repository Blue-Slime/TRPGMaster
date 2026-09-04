using System;
using System.Collections.Concurrent;
using MapEngine.Core;
using MapEngine.Core.Commands;
using MapEngine.Core.Networking;
using MasterIM.Server.MapPersistence;

namespace MasterIM.Server;

/// <summary>
/// 管理每个房间的地图同步处理器（per-room MapSyncHandler）
/// 负责地图的加载、保存和同步
/// </summary>
public class RoomMapManager
{
    private readonly ConcurrentDictionary<string, RoomMapContext> _contexts = new();

    /// <summary>
    /// 获取或创建指定房间的地图同步处理器
    /// 首次创建时会自动从磁盘加载场景
    /// </summary>
    public MapSyncHandler GetOrCreateHandler(string roomId)
    {
        var context = _contexts.GetOrAdd(roomId, rid =>
        {
            var world = new World();
            var commandBus = new CommandBus(world);
            var handler = new MapSyncHandler(world, commandBus);

            // 从磁盘加载场景（如果存在）
            MapScenePersistence.LoadScene(rid, world);

            // 订阅命令执行成功事件，自动保存场景
            commandBus.CommandExecuted += (sender, eventArgs) =>
            {
                // 只在 Execute 操作时保存（Undo/Redo/RemoteApply 不保存，避免重复）
                if (eventArgs.Action == MapEngine.Core.Commands.CommandAction.Execute)
                {
                    MapScenePersistence.SaveScene(rid, world);
                }
            };

            return new RoomMapContext(world, commandBus, handler);
        });

        return context.Handler;
    }

    /// <summary>
    /// 移除房间的地图上下文（房间关闭时调用）
    /// 会在移除前保存最终状态
    /// </summary>
    public void RemoveRoom(string roomId)
    {
        if (_contexts.TryRemove(roomId, out var context))
        {
            // 保存最终状态
            MapScenePersistence.SaveScene(roomId, context.World);
            Console.WriteLine($"[RoomMapManager] 房间地图已保存并移除 RoomId={roomId}");
        }
    }

    /// <summary>
    /// 手动保存指定房间的地图（用于定期备份或强制保存）
    /// </summary>
    public void SaveRoom(string roomId)
    {
        if (_contexts.TryGetValue(roomId, out var context))
        {
            MapScenePersistence.SaveScene(roomId, context.World);
        }
    }

    /// <summary>
    /// 保存所有房间的地图（服务器关闭时调用）
    /// </summary>
    public void SaveAllRooms()
    {
        foreach (var kvp in _contexts)
        {
            MapScenePersistence.SaveScene(kvp.Key, kvp.Value.World);
        }
        Console.WriteLine($"[RoomMapManager] 所有房间地图已保存，共 {_contexts.Count} 个房间");
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
