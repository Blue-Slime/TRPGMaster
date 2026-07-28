using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using System.Threading;

using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using MasterIM.Models;
using MasterIM.Server.Storage;

namespace MasterIM.Server.WebSocket;

public class DMAdvancedServer
{
    private readonly ConnectionManager _connMgr;
    private readonly DMAdvancedStore _store;

    public DMAdvancedServer(ConnectionManager connMgr, DMAdvancedStore store)
    {
        _connMgr = connMgr;
        _store = store;
    }

    public async Task HandleConnectionAsync(System.Net.WebSockets.WebSocket ws, string userId, string targetUserId, bool enableStorage, int retentionDays = -1)
    {
        var pairId = DMAdvancedStore.GetPairId(userId, targetUserId);
        var config = await _store.GetOrCreateConfigAsync(userId, targetUserId, enableStorage, retentionDays);

        var conn = new Connection(ws, userId, pairId, "dm");
        _connMgr.Add(conn);

        await ReceiveLoop(conn, config);
        _connMgr.Remove(conn);
    }

    private async Task ReceiveLoop(Connection conn, DMConfig config)
    {
        var buffer = new byte[8192];
        while (conn.WebSocket.State == WebSocketState.Open)
        {
            var result = await conn.WebSocket.ReceiveAsync(buffer, CancellationToken.None);
            if (result.MessageType == WebSocketMessageType.Close) break;

            var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
            await HandlePacket(conn, json, config);
        }
    }

    private async Task HandlePacket(Connection conn, Packet packet, DMConfig config)
    {
        switch (packet.T)
        {
            case "msg":
                await HandleMessage(conn, packet, config);
                break;
            case "qry":
                await HandleQuery(conn, packet, config);
                break;
            case "chk":
                await HandleCheckPage(conn, packet, config);
                break;
        }
    }

    private async Task HandlePacket(Connection conn, string json, DMConfig config)
    {
        var packet = JsonSerializer.Deserialize<Packet>(json);
        if (packet == null) return;
        await HandlePacket(conn, packet, config);
    }

    private async Task HandleMessage(Connection conn, Packet packet, DMConfig config)
    {
        var data = JsonSerializer.Deserialize<Dictionary<string, object>>(packet.P?.ToString() ?? "");
        if (data == null) return;

        if (data.ContainsKey("Type"))
        {
            var type = data["Type"].ToString();
            if (type == "modify" && config.EnableEdit)
            {
                var msgId = Convert.ToInt64(data["MsgId"]);
                var content = data["Content"].ToString() ?? "";
                await _store.EditMessageAsync(config.PairId, msgId, content);
            }
            return;
        }

        var msg = JsonSerializer.Deserialize<GroupMessage>(packet.P?.ToString() ?? "");
        if (msg == null) return;

        if (config.EnableStorage)
        {
            await _store.SaveAsync(config.PairId, msg);
        }

        await BroadcastToPair(config.PairId, new Packet { T = "msg", P = msg });
    }

    private async Task HandleQuery(Connection conn, Packet packet, DMConfig config)
    {
        if (!config.EnableRoaming)
        {
            await SendAsync(conn.WebSocket, new Packet { T = "qry", P = new List<GroupMessage>(), Id = packet.Id });
            return;
        }

        var req = JsonSerializer.Deserialize<Dictionary<string, object>>(packet.P?.ToString() ?? "");
        if (req == null) return;

        // v2.0: 按时间范围查询
        var startTime = DateTime.UtcNow.AddDays(-7);
        var endTime = DateTime.UtcNow;
        var limit = Convert.ToInt32(req.GetValueOrDefault("Limit", 100));

        if (req.ContainsKey("StartTime"))
            startTime = DateTime.Parse(req["StartTime"].ToString()!);
        if (req.ContainsKey("EndTime"))
            endTime = DateTime.Parse(req["EndTime"].ToString()!);

        var messages = await _store.GetMessagesByTimeRangeAsync(config.PairId, startTime, endTime, limit);
        await SendAsync(conn.WebSocket, new Packet { T = "qry", P = messages, Id = packet.Id });
    }

    private async Task HandleCheckPage(Connection conn, Packet packet, DMConfig config)
    {
        if (!config.EnableRoaming)
        {
            await SendAsync(conn.WebSocket, new Packet { T = "chk", P = new { LastModified = 0 }, Id = packet.Id });
            return;
        }

        var req = JsonSerializer.Deserialize<Dictionary<string, object>>(packet.P?.ToString() ?? "");
        if (req == null) return;

        var pageNumber = Convert.ToInt32(req["PageNumber"]);

        // v2.0: DMAdvancedStore 待升级
        await SendAsync(conn.WebSocket, new Packet
        {
            T = "chk",
            P = new { Deprecated = true, Message = "DMAdvancedStore is being upgraded to v2.0" },
            Id = packet.Id
        });
    }

    private async Task BroadcastToPair(string pairId, Packet packet)
    {
        var connections = _connMgr.GetRoomConnections(pairId);
        foreach (var conn in connections)
        {
            await SendAsync(conn.WebSocket, packet);
        }
    }

    private async Task SendAsync(System.Net.WebSockets.WebSocket ws, Packet packet)
    {
        if (ws.State != WebSocketState.Open) return;

        var json = JsonSerializer.Serialize(packet);
        var bytes = Encoding.UTF8.GetBytes(json);
        await ws.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
    }
}



