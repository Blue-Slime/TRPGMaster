using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using MapEngine.Core;

namespace MasterIM.Server;

/// <summary>
/// 房间运行时状态（单进程多房间）。
/// 记录哪些房间当前处于"开启(online)"状态——只有开启的房间才接受连接。
/// 房间数据始终在共享存储里，"开启/关闭"只影响运行时是否可进入，不动数据。
///
/// S1 扩展: 每个在线房间持有一个 MapEngine.Core.World 实例作为地图权威状态。
/// </summary>
public class RoomRuntimeManager
{
    // roomId -> 开启时间
    private readonly ConcurrentDictionary<string, DateTime> _onlineRooms = new();

    // roomId -> World (地图权威状态)
    private readonly ConcurrentDictionary<string, World> _mapWorlds = new();

    /// <summary>自动开启：房间只要在 RoomStore 存在，有人连即自动开启（默认关，走手动）</summary>
    public bool AutoOpenOnConnect { get; set; } = false;

    public bool IsOnline(string roomId) => _onlineRooms.ContainsKey(roomId);

    /// <summary>开启房间（允许连接进入）。如果已有存档，从存档加载 World；否则初始化空 World。</summary>
    public void Open(string roomId)
    {
        if (string.IsNullOrEmpty(roomId)) return;
        _onlineRooms[roomId] = DateTime.UtcNow;

        // 初始化地图 World（暂时创建空 World，存档加载在后续 Step 实现）
        if (!_mapWorlds.ContainsKey(roomId))
        {
            _mapWorlds[roomId] = new World();
        }
    }

    /// <summary>关闭房间（拒绝新连接；断开在场连接由调用方负责）。保留 World 在内存中（可选：后续改为持久化后释放）。</summary>
    public void Close(string roomId)
    {
        _onlineRooms.TryRemove(roomId, out _);
        // 暂不删除 _mapWorlds[roomId]，保留在内存中（后续可改为持久化后释放）
    }

    public DateTime? GetOpenedAt(string roomId)
        => _onlineRooms.TryGetValue(roomId, out var t) ? t : null;

    /// <summary>当前所有开启的房间ID</summary>
    public IReadOnlyCollection<string> OnlineRoomIds => _onlineRooms.Keys.ToList();

    public int OnlineRoomCount => _onlineRooms.Count;

    /// <summary>
    /// 获取指定房间的地图 World（权威状态）。
    /// 房间未开启时返回 null。
    /// </summary>
    public World? GetMapWorld(string roomId)
        => _mapWorlds.TryGetValue(roomId, out var world) ? world : null;
}
