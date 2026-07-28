using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using System.Threading;

using System.Net.WebSockets;

namespace MasterIM.Server.WebSocket;

public class Connection
{
    /// <summary>连接唯一标识（同一 userId 在多房间可并存，用它区分/移除）</summary>
    public string Id { get; } = Guid.NewGuid().ToString("N");
    public DateTime CreatedAt { get; } = DateTime.UtcNow;

    public System.Net.WebSockets.WebSocket WebSocket { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string RoomId { get; set; } = string.Empty;
    public string ChannelId { get; set; } = string.Empty;
    public string Status { get; set; } = "active";  // active, pending

    public Connection(System.Net.WebSockets.WebSocket ws, string userId, string roomId, string channelId)
    {
        WebSocket = ws;
        UserId = userId;
        RoomId = roomId;
        ChannelId = channelId;
    }
}
