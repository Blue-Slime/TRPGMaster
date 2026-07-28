using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using System.Threading;

using System.Collections.Concurrent;

namespace MasterIM.Server.WebSocket;

/// <summary>
/// 连接管理器（单进程多房间）。
/// 内部按 Connection.Id 存储，使同一 userId 可在多个房间并存，
/// 断开时只移除对应的那条连接，互不干扰。
/// </summary>
public class ConnectionManager
{
    // key = Connection.Id
    private readonly ConcurrentDictionary<string, Connection> _connections = new();

    /// <summary>加入一条连接（按连接身份存储）</summary>
    public void Add(Connection conn)
    {
        _connections[conn.Id] = conn;
    }

    /// <summary>移除一条连接（按连接身份）</summary>
    public void Remove(Connection conn)
    {
        _connections.TryRemove(conn.Id, out _);
    }

    /// <summary>按连接ID移除</summary>
    public void RemoveById(string connectionId)
    {
        _connections.TryRemove(connectionId, out _);
    }

    /// <summary>
    /// 按 userId 查连接（跨房间，返回最新的一条）。
    /// 用于房主通知/邀请等场景——这些当前不区分房间。
    /// </summary>
    public Connection? GetConnection(string userId) => GetUserConnection(userId);

    public Connection? GetUserConnection(string userId)
    {
        return _connections.Values
            .Where(c => c.UserId == userId)
            .OrderByDescending(c => c.CreatedAt)
            .FirstOrDefault();
    }

    /// <summary>按房间+用户查连接（作用域精确到房间）</summary>
    public Connection? GetRoomUserConnection(string roomId, string userId)
    {
        return _connections.Values
            .Where(c => c.RoomId == roomId && c.UserId == userId)
            .OrderByDescending(c => c.CreatedAt)
            .FirstOrDefault();
    }

    /// <summary>
    /// 返回某用户在某房间的所有现存连接（用于新连接进来时踢掉同一用户的旧连接，
    /// 保证"一个用户在一个房间同时只有一条连接"，避免重连/误连堆积同名成员）。
    /// </summary>
    public List<Connection> GetRoomUserConnections(string roomId, string userId)
    {
        return _connections.Values
            .Where(c => c.RoomId == roomId && c.UserId == userId)
            .ToList();
    }

    public List<Connection> GetChannelConnections(string roomId, string channelId)
    {
        return _connections.Values.Where(c => c.RoomId == roomId && c.ChannelId == channelId).ToList();
    }

    public List<Connection> GetRoomConnections(string roomId)
    {
        return _connections.Values.Where(c => c.RoomId == roomId).ToList();
    }

    /// <summary>所有活动连接（用于全局统计）</summary>
    public IReadOnlyCollection<Connection> AllConnections => _connections.Values.ToList();

    /// <summary>在线连接总数</summary>
    public int Count => _connections.Count;

    /// <summary>去重后的在线用户数（同一用户多连接算一个）</summary>
    public int DistinctUserCount => _connections.Values.Select(c => c.UserId).Distinct().Count();
}
