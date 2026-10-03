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
using MapEngine.Core.Networking;

namespace MasterIM.Server.WebSocket;

public class IMServer
{
    private readonly ConnectionManager _connMgr;
    private readonly MessageStore _store;
    private readonly ObjectStore _objStore;
    private readonly RoomMemberStore _memberStore;
    private readonly RoomStore _roomStore;
    private readonly ChannelStore _channelStore;
    private readonly ReactionStore _reactionStore;
    private readonly RoomRuntimeManager _runtime;
    private readonly RoomMapManager _mapManager;

    public IMServer(ConnectionManager connMgr, MessageStore store, ObjectStore objStore, RoomMemberStore memberStore, RoomStore roomStore, ChannelStore channelStore, ReactionStore reactionStore, RoomRuntimeManager runtime, RoomMapManager mapManager)
    {
        _connMgr = connMgr;
        _store = store;
        _objStore = objStore;
        _memberStore = memberStore;
        _roomStore = roomStore;
        _channelStore = channelStore;
        _reactionStore = reactionStore;
        _runtime = runtime;
        _mapManager = mapManager;
    }

    public async Task HandleConnectionAsync(System.Net.WebSockets.WebSocket ws, string userId, string roomId, string channelId)
    {
        try
        {
            // 检查房间是否存在
            var room = await _roomStore.GetRoomAsync(roomId);
            if (room == null)
            {
                await SendAsync(ws, new Packet { T = "error", P = "房间不存在" });
                await TryCloseAsync(ws, WebSocketCloseStatus.NormalClosure, "房间不存在");
                return;
            }

            // 检查房间是否已开启（单进程多房间：只有开启的房间才接受连接）
            if (!_runtime.IsOnline(roomId))
            {
                if (_runtime.AutoOpenOnConnect)
                {
                    _runtime.Open(roomId);
                }
                else
                {
                    await SendAsync(ws, new Packet { T = "error", P = "房间未开启" });
                    await TryCloseAsync(ws, WebSocketCloseStatus.NormalClosure, "房间未开启");
                    return;
                }
            }

            // 同一用户在同一房间只允许一条活动连接。
            // 规则：若旧连接仍然存活(WebSocket.Open) → 拒绝这次重复连接；
            //       若旧连接已死(强杀/断网残留) → 清理掉，放行本次（正常断线重连）。
            var existingAlive = false;
            foreach (var old in _connMgr.GetRoomUserConnections(roomId, userId))
            {
                if (old.WebSocket.State == WebSocketState.Open)
                {
                    existingAlive = true;
                }
                else
                {
                    _connMgr.Remove(old);  // 死连接，清理
                }
            }

            if (existingAlive)
            {
                // 该账号已在此房间在线 → 屏蔽重复连接（用 join_rejected，客户端不会自动重试）
                await SendAsync(ws, new Packet { T = "join_rejected", P = "该账号已在此房间登录，重复连接被拒绝" });
                await TryCloseAsync(ws, WebSocketCloseStatus.NormalClosure, "重复连接");
                return;
            }

        // 检查用户是否已在房间中
        var members = await _memberStore.GetAllMembersAsync(roomId);
        var isMember = members.Any(m => m.UserId == userId);

        Connection conn;

        if (isMember)
        {
            // 已在房间中，直接连接
            conn = new Connection(ws, userId, roomId, channelId);
            _connMgr.Add(conn);
            await SendAsync(ws, new Packet { T = "join_success", P = await BuildJoinInfoAsync(room) });

            // 向房间其他在场成员广播"该成员上线"（老成员重连也要通知，否则别人列表不刷新）
            await BroadcastMemberJoined(roomId, userId, "member");
        }
        else if (room.IsPublic && !room.RequireApproval)
        {
            // 公开房间且无需批准，自动加入
            await _memberStore.AddOrUpdateMemberAsync(roomId, new RoomMember
            {
                UserId = userId,
                Role = "member",
                JoinedAt = DateTime.UtcNow
            });
            conn = new Connection(ws, userId, roomId, channelId);
            _connMgr.Add(conn);
            await SendAsync(ws, new Packet { T = "join_success", P = await BuildJoinInfoAsync(room) });

            await BroadcastMemberJoined(roomId, userId, "member");
        }
        else
        {
            // 需要房主批准
            conn = new Connection(ws, userId, roomId, channelId);
            conn.Status = "pending";  // 设置为等待状态
            _connMgr.Add(conn);

            await SendAsync(ws, new Packet { T = "join_pending", P = "等待房主批准" });

            // 通知房主有新的加入请求
            var hostConn = _connMgr.GetConnection(room.HostUserId);
            if (hostConn != null)
            {
                await SendAsync(hostConn.WebSocket, new Packet
                {
                    T = "join_request",
                    P = new
                    {
                        UserId = userId,
                        RoomId = roomId,
                        RequestTime = DateTime.UtcNow
                    }
                });
            }
        }

            try
            {
                await ReceiveLoop(conn);
            }
            finally
            {
                // 无论正常关闭还是异常断开（强杀/网络中断），都必须移除连接，
                // 否则死连接会永久留在 ConnectionManager 里堆积成同名幽灵成员。
                _connMgr.Remove(conn);

                // 该用户在本房间已无任何连接 → 广播"成员离开"，让其他人的成员列表移除他
                if (_connMgr.GetRoomUserConnections(conn.RoomId, conn.UserId).Count == 0)
                {
                    foreach (var c in _connMgr.GetRoomConnections(conn.RoomId))
                    {
                        try
                        {
                            await SendAsync(c.WebSocket, new Packet
                            {
                                T = "member_left",
                                P = new { UserId = conn.UserId }
                            });
                        }
                        catch { }
                    }
                }
            }
        }
        catch (WebSocketException ex)
        {
            // WebSocket 连接异常（客户端提前断开等），记录日志但不抛出
            Console.WriteLine($"[IMServer] WebSocket 连接异常 (room={roomId}, user={userId}): {ex.Message}");
        }
        catch (Exception ex)
        {
            // 其他未预期的异常，记录详细信息
            Console.WriteLine($"[IMServer] HandleConnectionAsync 异常 (room={roomId}, user={userId}): {ex}");
        }
    }

    /// <summary>安全地关闭 WebSocket 连接，吞掉客户端已断开的异常</summary>
    private static async Task TryCloseAsync(System.Net.WebSockets.WebSocket ws, WebSocketCloseStatus status, string description)
    {
        try
        {
            if (ws.State == WebSocketState.Open || ws.State == WebSocketState.CloseReceived)
            {
                await ws.CloseAsync(status, description, CancellationToken.None);
            }
        }
        catch (WebSocketException)
        {
            // 客户端已断开，忽略
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[IMServer] TryCloseAsync 失败: {ex.Message}");
        }
    }

    private async Task ReceiveLoop(Connection conn)
    {
        var buffer = new byte[8192];
        try
        {
            while (conn.WebSocket.State == WebSocketState.Open)
            {
                var result = await conn.WebSocket.ReceiveAsync(buffer, CancellationToken.None);
                if (result.MessageType == WebSocketMessageType.Close) break;

                var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                try
                {
                    await HandlePacket(conn, json);
                }
                catch (Exception ex)
                {
                    // 单个消息处理出错不应断开整条连接（否则一个坏包会踢掉整个聊天室）
                    Console.WriteLine($"[IMServer] 处理消息出错({conn.RoomId}/{conn.UserId}): {ex.Message}");
                }
            }
        }
        catch
        {
            // 客户端异常断开会让 ReceiveAsync 抛异常，吞掉即可，
            // 连接移除由外层 finally 统一处理。
        }
    }

    private async Task HandlePacket(Connection conn, string json)
    {
        var packet = JsonSerializer.Deserialize<Packet>(json);
        if (packet == null) return;

        switch (packet.T)
        {
            case "msg":
                await HandleMessage(conn, packet);
                break;
            case "qry":
                await HandleQuery(conn, packet);
                break;
            case "chk":  // 检查分页修改时间
                await HandleCheckPage(conn, packet);
                break;
            case "crt":  // 创建空白分页
                await HandleCreatePage(conn, packet);
                break;
            case "del":  // 删除空白分页
                await HandleDeletePage(conn, packet);
                break;
            case "bmv":  // 批量平移消息
                await HandleBatchMove(conn, packet);
                break;
            case "bdl":  // 批量删除消息
                await HandleBatchDelete(conn, packet);
                break;
            case "obj_create":  // 创建对象
                await HandleObjectCreate(conn, packet);
                break;
            case "obj_update":  // 更新对象
                await HandleObjectUpdate(conn, packet);
                break;
            case "obj_delete":  // 删除对象
                await HandleObjectDelete(conn, packet);
                break;
            case "obj_query":  // 查询对象
                await HandleObjectQuery(conn, packet);
                break;
            case "stm":
                await HandleStream(conn, packet);
                break;
            case "grp_add_member":
                await HandleAddMember(conn, packet);
                break;
            case "grp_remove_member":
                await HandleRemoveMember(conn, packet);
                break;
            case "presence":
                await HandlePresence(conn, packet);
                break;
            case "typing":
                await HandleTyping(conn, packet);
                break;
            case "dice_roll":
                await HandleDiceRoll(conn, packet);
                break;
            case "room_invite":
                await HandleRoomInvite(conn, packet);
                break;
            case "join_request":
                await HandleJoinRequest(conn, packet);
                break;
            case "invite_accept":
                await HandleInviteAccept(conn, packet);
                break;
            case "invite_reject":
                await HandleInviteReject(conn, packet);
                break;
            case "read_receipt":
                await HandleReadReceipt(conn, packet);
                break;
            case "search_msg":
                await HandleSearchMessage(conn, packet);
                break;
            case "query_around":
                await HandleQueryAround(conn, packet);
                break;
            case "pin_message":
                await HandlePinMessage(conn, packet);
                break;
            case "get_pins":
                await HandleGetPins(conn, packet);
                break;
            case "react":
                await HandleReact(conn, packet);
                break;
            case "get_reactions":
                await HandleGetReactions(conn, packet);
                break;
            case "get_members":
                await HandleGetMembers(conn, packet);
                break;
            case "update_member":
                await HandleUpdateMember(conn, packet);
                break;
            case "ban_member":
                await HandleBanMember(conn, packet);
                break;
            case "create_room":
                await HandleCreateRoom(conn, packet);
                break;
            case "get_rooms":
                await HandleGetRooms(conn, packet);
                break;
            case "update_room":
                await HandleUpdateRoom(conn, packet);
                break;
            case "delete_room":
                await HandleDeleteRoom(conn, packet);
                break;
            case "create_channel":
                await HandleCreateChannel(conn, packet);
                break;
            case "get_channels":
                await HandleGetChannels(conn, packet);
                break;
            case "update_channel":
                await HandleUpdateChannel(conn, packet);
                break;
            case "delete_channel":
                await HandleDeleteChannel(conn, packet);
                break;
            case "reorder_channels":
                await HandleReorderChannels(conn, packet);
                break;
            case PacketTypes.Ping:
                await SendAsync(conn.WebSocket, new Packet { T = PacketTypes.Pong });
                break;
            case PacketTypes.ApproveJoin:
                await HandleApproveJoin(conn, packet);
                break;
            case PacketTypes.RejectJoin:
                await HandleRejectJoin(conn, packet);
                break;
            case PacketTypes.MapCommand:
                await HandleMapCommand(conn, packet);
                break;
            case PacketTypes.MapFullSyncRequest:
                await HandleMapFullSyncRequest(conn, packet);
                break;
            default:
                // 处理自定义消息类型
                await HandleCustomType(conn, packet);
                break;
        }
    }

    /// <summary>
    /// 处理消息发送（包括普通消息、插入历史消息、修改消息、撤回消息）
    /// </summary>
    private async Task HandleMessage(Connection conn, Packet packet)
    {
        var data = JsonSerializer.Deserialize<Dictionary<string, object>>(packet.P?.ToString() ?? "");
        if (data == null) return;

        if (data.ContainsKey("Type"))
        {
            var type = data["Type"].ToString();
            if (type == "insert")
            {
                await HandleInsertHistory(conn, data);
                return;
            }
            if (type == "modify")
            {
                await HandleModify(conn, data);
                return;
            }
            if (type == "revoke")
            {
                await HandleRevoke(conn, data);
                return;
            }
        }

        var msg = JsonSerializer.Deserialize<GroupMessage>(packet.P?.ToString() ?? "");
        if (msg == null) return;

        // Discord 对齐：一条连接服务整个房间的所有频道。
        // 消息归属以包内 ChannelId 为准（空则回退连接默认频道）。
        var channelId = string.IsNullOrEmpty(msg.ChannelId) ? conn.ChannelId : msg.ChannelId;
        msg.ChannelId = channelId;
        // 发送人以连接身份为准（服务端权威），不信任客户端自报，杜绝冒名。
        msg.SenderId = conn.UserId;

        await _store.SaveAsync(conn.RoomId, channelId, msg);
        // 广播给房间内所有活动连接，消息自带 ChannelId 供客户端分拣。
        await BroadcastToRoomActive(conn.RoomId, new Packet { T = "msg", P = msg });
    }

    /// <summary>
    /// 处理插入历史消息（超时空编辑）
    /// </summary>
    private async Task HandleInsertHistory(Connection conn, Dictionary<string, object> data)
    {
        var msg = JsonSerializer.Deserialize<GroupMessage>(data["GroupMessage"]?.ToString() ?? "");
        if (msg == null) return;

        await _store.SaveAsync(conn.RoomId, conn.ChannelId, msg);

        await BroadcastToChannel(conn.RoomId, conn.ChannelId, new Packet
        {
            T = "ntf",
            P = new { Type = "message_inserted", TimeMs = new DateTimeOffset(msg.SendTime).ToUnixTimeMilliseconds() }
        });
    }

    /// <summary>
    /// 处理修改消息内容
    /// </summary>
    private async Task HandleModify(Connection conn, Dictionary<string, object> data)
    {
        // v2.0: 使用 msgId 而不是 page/seq。
        // 注意：data 值是 JsonElement，不能用 Convert.ToInt64（JsonElement 未实现 IConvertible 会抛异常）。
        var msgId = ToLong(data.GetValueOrDefault("MsgId"));
        var content = ToStr(data.GetValueOrDefault("Content"));
        // Discord 对齐：消息归属以包内 ChannelId 为准（空则回退连接默认频道）
        var channelId = ToStr(data.GetValueOrDefault("ChannelId"));
        if (string.IsNullOrEmpty(channelId)) channelId = conn.ChannelId;

        await _store.EditMessageAsync(conn.RoomId, channelId, msgId, content);

        // 全房活动连接广播，载荷带 ChannelId 供客户端分拣到正确频道
        await BroadcastToRoomActive(conn.RoomId, new Packet
        {
            T = "msg_edited",
            P = new { MsgId = msgId, Content = content, ChannelId = channelId }
        });
    }

    /// <summary>
    /// 处理分页查询历史消息
    /// </summary>
    private async Task HandleQuery(Connection conn, Packet packet)
    {
        var req = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(packet.P?.ToString() ?? "");
        if (req == null) return;

        // v2.0: 按时间范围查询
        var startTime = DateTime.UtcNow.AddDays(-7); // 默认查询最近7天
        var endTime = DateTime.UtcNow;
        var limit = 100;

        if (req.TryGetValue("Limit", out var limitEl) && limitEl.ValueKind == JsonValueKind.Number)
            limit = limitEl.GetInt32();
        if (req.TryGetValue("StartTime", out var startEl) && startEl.ValueKind == JsonValueKind.String)
            startTime = startEl.GetDateTime();
        if (req.TryGetValue("EndTime", out var endEl) && endEl.ValueKind == JsonValueKind.String)
            endTime = endEl.GetDateTime();

        // Discord 对齐：查询指定频道历史（空则回退连接默认频道）
        var channelId = conn.ChannelId;
        if (req.TryGetValue("ChannelId", out var chEl) && chEl.ValueKind == JsonValueKind.String)
        {
            var ch = chEl.GetString();
            if (!string.IsNullOrEmpty(ch)) channelId = ch;
        }

        var messages = await _store.QueryByTimeRangeAsync(conn.RoomId, channelId, startTime, endTime, limit);

        await SendAsync(conn.WebSocket, new Packet
        {
            T = "qry",
            Id = packet.Id,
            P = messages
        });
    }

    /// <summary>
    /// 处理检查分页修改时间请求
    /// </summary>
    private async Task HandleCheckPage(Connection conn, Packet packet)
    {
        var req = JsonSerializer.Deserialize<Dictionary<string, object>>(packet.P?.ToString() ?? "");
        if (req == null) return;

        var pageNumber = Convert.ToInt32(req["PageNumber"]);

        // v2.0: 分页已废弃，返回空响应
        await SendAsync(conn.WebSocket, new Packet
        {
            T = "chk",
            P = new { Deprecated = true, Message = "Pagination is deprecated in v2.0" },
            Id = packet.Id
        });
    }

    /// <summary>
    /// 处理创建空白分页请求（v2.0 已废弃）
    /// </summary>
    private async Task HandleCreatePage(Connection conn, Packet packet)
    {
        // v2.0: 无分页架构，不需要创建空白页
        await SendAsync(conn.WebSocket, new Packet
        {
            T = "crt",
            Id = packet.Id,
            P = new { Deprecated = true, Message = "Pagination is deprecated in v2.0" }
        });
    }

    /// <summary>
    /// 处理删除空白分页请求（v2.0 已废弃）
    /// </summary>
    private async Task HandleDeletePage(Connection conn, Packet packet)
    {
        // v2.0: 无分页架构，不需要删除空白页
        await SendAsync(conn.WebSocket, new Packet
        {
            T = "del",
            Id = packet.Id,
            P = new { Success = true, Deprecated = true }
        });
    }

    /// <summary>
    /// 处理批量平移消息请求
    /// </summary>
    private async Task HandleBatchMove(Connection conn, Packet packet)
    {
        var req = JsonSerializer.Deserialize<Dictionary<string, object>>(packet.P?.ToString() ?? "");
        if (req == null) return;

        var msgIds = JsonSerializer.Deserialize<List<long>>(req["MsgIds"]?.ToString() ?? "");
        var targetTime = DateTime.Parse(req["TargetTime"]?.ToString() ?? "");
        var preserveOrder = req.ContainsKey("PreserveOrder") ? Convert.ToBoolean(req["PreserveOrder"]) : true;

        if (msgIds == null || msgIds.Count == 0) return;

        var affectedPartitions = await _store.BatchMoveMessagesAsync(conn.RoomId, conn.ChannelId, msgIds, targetTime, preserveOrder);

        await BroadcastToChannel(conn.RoomId, conn.ChannelId, new Packet
        {
            T = "batch_moved",
            P = new { MsgIds = msgIds, TargetTime = targetTime, AffectedPartitions = affectedPartitions }
        });
    }

    /// <summary>
    /// 处理批量删除消息请求
    /// </summary>
    private async Task HandleBatchDelete(Connection conn, Packet packet)
    {
        var req = JsonSerializer.Deserialize<Dictionary<string, object>>(packet.P?.ToString() ?? "");
        if (req == null) return;

        var msgIds = JsonSerializer.Deserialize<List<long>>(req["MsgIds"]?.ToString() ?? "");
        if (msgIds == null || msgIds.Count == 0) return;

        var affectedPartitions = await _store.BatchDeleteMessagesAsync(conn.RoomId, conn.ChannelId, msgIds);

        await BroadcastToChannel(conn.RoomId, conn.ChannelId, new Packet
        {
            T = "batch_deleted",
            P = new { MsgIds = msgIds, AffectedPartitions = affectedPartitions }
        });
    }

    /// <summary>
    /// 处理流式数据传输（实时同步）
    /// </summary>
    private async Task HandleStream(Connection conn, Packet packet)
    {
        await BroadcastToChannelExcept(conn.RoomId, conn.ChannelId, conn.UserId, new Packet { T = "stm", P = packet.P });
    }

    /// <summary>
    /// 处理创建游戏对象
    /// </summary>
    private async Task HandleObjectCreate(Connection conn, Packet packet)
    {
        var obj = JsonSerializer.Deserialize<Storage.GameObject>(packet.P?.ToString() ?? "");
        if (obj == null) return;

        obj.RoomId = conn.RoomId;
        obj.CreatorId = conn.UserId;
        var seqNumber = await _objStore.SaveAsync(conn.RoomId, obj);

        await BroadcastToRoom(conn.RoomId, new Packet
        {
            T = "obj_sync",
            P = new { Action = "create", Object = obj, SequenceNumber = seqNumber }
        });
    }

    /// <summary>
    /// 处理更新游戏对象
    /// </summary>
    private async Task HandleObjectUpdate(Connection conn, Packet packet)
    {
        var obj = JsonSerializer.Deserialize<Storage.GameObject>(packet.P?.ToString() ?? "");
        if (obj == null) return;

        var seqNumber = await _objStore.SaveAsync(conn.RoomId, obj);

        await BroadcastToRoom(conn.RoomId, new Packet
        {
            T = "obj_sync",
            P = new { Action = "update", Object = obj, SequenceNumber = seqNumber }
        });
    }

    /// <summary>
    /// 处理删除游戏对象
    /// </summary>
    private async Task HandleObjectDelete(Connection conn, Packet packet)
    {
        var req = JsonSerializer.Deserialize<Dictionary<string, object>>(packet.P?.ToString() ?? "");
        if (req == null) return;

        var objectId = req["ObjectId"].ToString() ?? "";
        await _objStore.DeleteAsync(conn.RoomId, objectId);

        await BroadcastToRoom(conn.RoomId, new Packet
        {
            T = "obj_sync",
            P = new { Action = "delete", ObjectId = objectId }
        });
    }

    /// <summary>
    /// 处理查询游戏对象（按类型或序列号范围）
    /// </summary>
    private async Task HandleObjectQuery(Connection conn, Packet packet)
    {
        var req = JsonSerializer.Deserialize<Dictionary<string, object>>(packet.P?.ToString() ?? "");
        if (req == null) return;

        List<Storage.GameObject> objects;

        if (req.ContainsKey("Type"))
        {
            var type = req["Type"].ToString() ?? "";
            objects = await _objStore.GetByTypeAsync(conn.RoomId, type);
        }
        else if (req.ContainsKey("StartSeq") && req.ContainsKey("EndSeq"))
        {
            var startSeq = Convert.ToInt64(req["StartSeq"]);
            var endSeq = Convert.ToInt64(req["EndSeq"]);
            objects = await _objStore.GetBySequenceRangeAsync(conn.RoomId, startSeq, endSeq);
        }
        else
        {
            objects = new();
        }

        await SendAsync(conn.WebSocket, new Packet { T = "obj_query", P = objects, Id = packet.Id });
    }

    private async Task BroadcastToRoom(string roomId, Packet packet)
    {
        var connections = _connMgr.GetRoomConnections(roomId);
        foreach (var conn in connections)
        {
            await SendAsync(conn.WebSocket, packet);
        }
    }

    /// <summary>
    /// 向房间内所有 active 连接广播（Discord 式：一条连接服务整个房间的所有频道，
    /// 消息自带 channelId，由客户端按频道分拣）。pending（等待批准）连接不推。
    /// </summary>
    private async Task BroadcastToRoomActive(string roomId, Packet packet)
    {
        foreach (var conn in _connMgr.GetRoomConnections(roomId))
        {
            if (conn.Status != "active") continue;
            try { await SendAsync(conn.WebSocket, packet); }
            catch { /* 忽略发送失败 */ }
        }
    }

    /// <summary>
    /// 向房间其他在场成员广播"某成员上线"。
    /// 必须同时带 UserId + Role（SDK 端要求两者都在才会触发 OnMemberJoined）。
    /// </summary>
    private async Task BroadcastMemberJoined(string roomId, string userId, string role)
    {
        foreach (var c in _connMgr.GetRoomConnections(roomId))
        {
            if (c.UserId == userId) continue;  // 不通知自己
            try
            {
                await SendAsync(c.WebSocket, new Packet
                {
                    T = "member_joined",
                    P = new { UserId = userId, Role = role }
                });
            }
            catch { /* 忽略发送失败 */ }
        }
    }

    private async Task BroadcastToChannel(string roomId, string channelId, Packet packet)
    {
        var connections = _connMgr.GetChannelConnections(roomId, channelId);
        foreach (var conn in connections)
        {
            await SendAsync(conn.WebSocket, packet);
        }
    }

    private async Task BroadcastToChannelExcept(string roomId, string channelId, string exceptUserId, Packet packet)
    {
        var connections = _connMgr.GetChannelConnections(roomId, channelId).Where(c => c.UserId != exceptUserId);
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

    // JsonSerializer.Deserialize<Dictionary<string, object>> 的值是装箱的 JsonElement，
    // 不能用 Convert.ToInt64/ToString（JsonElement 未实现 IConvertible → 抛 InvalidCastException）。
    // 这两个辅助方法从装箱值里安全取出 long / string。
    private static long ToLong(object? value)
    {
        if (value is JsonElement el)
        {
            if (el.ValueKind == JsonValueKind.Number && el.TryGetInt64(out var n)) return n;
            if (el.ValueKind == JsonValueKind.String && long.TryParse(el.GetString(), out var s)) return s;
            return 0;
        }
        return value != null && long.TryParse(value.ToString(), out var v) ? v : 0;
    }

    private static string ToStr(object? value)
    {
        if (value is JsonElement el)
        {
            return el.ValueKind == JsonValueKind.String ? (el.GetString() ?? "") : el.ToString();
        }
        return value?.ToString() ?? "";
    }

    /// <summary>
    /// 处理消息撤回
    /// </summary>
    private async Task HandleRevoke(Connection conn, Dictionary<string, object> data)
    {
        // v2.0: 使用 msgId（旧版 page/seq 已废弃）。
        // 注意：data 值是 JsonElement，不能用 Convert.ToInt64（会抛 InvalidCastException）。
        var msgId = ToLong(data.GetValueOrDefault("MsgId"));
        // Discord 对齐：消息归属以包内 ChannelId 为准（空则回退连接默认频道）
        var channelId = ToStr(data.GetValueOrDefault("ChannelId"));
        if (string.IsNullOrEmpty(channelId)) channelId = conn.ChannelId;

        // 软删除（is_deleted=1），历史查询已过滤，重载不再出现
        await _store.DeleteMessageAsync(conn.RoomId, channelId, msgId);

        // 全房活动连接广播，载荷带 ChannelId 供客户端分拣到正确频道
        await BroadcastToRoomActive(conn.RoomId, new Packet
        {
            T = "msg_revoked",
            P = new { MsgId = msgId, ChannelId = channelId }
        });
    }

    /// <summary>
    /// 处理添加群组成员
    /// </summary>
    private async Task HandleAddMember(Connection conn, Packet packet)
    {
        var data = JsonSerializer.Deserialize<Dictionary<string, string>>(packet.P?.ToString() ?? "");
        if (data == null) return;

        var userId = data["UserId"];
        var role = data.ContainsKey("Role") ? data["Role"] : "member";

        await BroadcastToChannel(conn.RoomId, conn.ChannelId, new Packet
        {
            T = "member_joined",
            P = new { UserId = userId, Role = role }
        });
    }

    /// <summary>
    /// 处理移除群组成员
    /// </summary>
    private async Task HandleRemoveMember(Connection conn, Packet packet)
    {
        var data = JsonSerializer.Deserialize<Dictionary<string, string>>(packet.P?.ToString() ?? "");
        if (data == null) return;

        var userId = data["UserId"];

        await BroadcastToChannel(conn.RoomId, conn.ChannelId, new Packet
        {
            T = "member_left",
            P = new { UserId = userId }
        });
    }

    /// <summary>
    /// 处理用户在线状态更新
    /// </summary>
    private async Task HandlePresence(Connection conn, Packet packet)
    {
        var data = JsonSerializer.Deserialize<Dictionary<string, string>>(packet.P?.ToString() ?? "");
        if (data == null) return;

        var status = data["Status"]; // online, away, busy, offline

        await BroadcastToChannel(conn.RoomId, conn.ChannelId, new Packet
        {
            T = "presence_update",
            P = new { UserId = conn.UserId, Status = status, RoomId = conn.RoomId, ChannelId = conn.ChannelId }
        });
    }

    /// <summary>
    /// 处理正在输入状态
    /// </summary>
    private async Task HandleTyping(Connection conn, Packet packet)
    {
        // 用 JsonElement 解析：Dictionary<string,object> 的值是 JsonElement，
        // Convert.ToBoolean(JsonElement) 会抛（JsonElement 未实现 IConvertible），故显式取值。
        var data = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(packet.P?.ToString() ?? "");
        if (data == null) return;

        // 默认按"正在输入"处理；仅当显式传 IsTyping=false 才视为停止。
        var isTyping = !(data.TryGetValue("IsTyping", out var t) && t.ValueKind == JsonValueKind.False);
        // Discord 对齐：typing 带 channelId，全房广播（除自己），客户端按当前频道过滤显示。
        var typingChannel = data.TryGetValue("ChannelId", out var chEl) && chEl.ValueKind == JsonValueKind.String
            ? chEl.GetString() ?? conn.ChannelId
            : conn.ChannelId;

        foreach (var c in _connMgr.GetRoomConnections(conn.RoomId))
        {
            if (c.UserId == conn.UserId || c.Status != "active") continue;
            try
            {
                await SendAsync(c.WebSocket, new Packet
                {
                    T = isTyping ? "typing_start" : "typing_stop",
                    P = new { UserId = conn.UserId, ChannelId = typingChannel }
                });
            }
            catch { /* 忽略 */ }
        }
    }

    /// <summary>
    /// 处理骰子投掷
    /// </summary>
    private async Task HandleDiceRoll(Connection conn, Packet packet)
    {
        var data = JsonSerializer.Deserialize<Dictionary<string, object>>(packet.P?.ToString() ?? "");
        if (data == null) return;

        var isSecret = data.ContainsKey("IsSecret") && Convert.ToBoolean(data["IsSecret"]);
        var formula = data["Formula"]?.ToString() ?? "";
        var result = data["Result"]?.ToString() ?? "";

        await BroadcastToChannel(conn.RoomId, conn.ChannelId, new Packet
        {
            T = "dice_result",
            P = new
            {
                UserId = conn.UserId,
                RoomId = conn.RoomId,
                ChannelId = conn.ChannelId,
                Formula = formula,
                Result = result,
                IsSecret = isSecret
            }
        });
    }

    /// <summary>
    /// 处理发送房间邀请
    /// </summary>
    private async Task HandleRoomInvite(Connection conn, Packet packet)
    {
        var data = JsonSerializer.Deserialize<Dictionary<string, string>>(packet.P?.ToString() ?? "");
        if (data == null) return;

        var targetUserId = data["TargetUserId"];

        // 发送给目标用户
        var targetConn = _connMgr.GetUserConnection(targetUserId);
        if (targetConn != null)
        {
            await SendAsync(targetConn.WebSocket, new Packet
            {
                T = "invite_received",
                P = new { InviterId = conn.UserId, RoomId = conn.RoomId, ChannelId = conn.ChannelId }
            });
        }
    }

    /// <summary>
    /// 处理请求加入房间
    /// </summary>
    private async Task HandleJoinRequest(Connection conn, Packet packet)
    {
        var data = JsonSerializer.Deserialize<Dictionary<string, string>>(packet.P?.ToString() ?? "");
        if (data == null) return;

        var targetUserId = data["TargetUserId"];

        // 发送给目标用户
        var targetConn = _connMgr.GetUserConnection(targetUserId);
        if (targetConn != null)
        {
            await SendAsync(targetConn.WebSocket, new Packet
            {
                T = "join_requested",
                P = new { RequesterId = conn.UserId, RoomId = conn.RoomId, ChannelId = conn.ChannelId }
            });
        }
    }

    /// <summary>
    /// 处理接受邀请
    /// </summary>
    private async Task HandleInviteAccept(Connection conn, Packet packet)
    {
        var data = JsonSerializer.Deserialize<Dictionary<string, string>>(packet.P?.ToString() ?? "");
        if (data == null) return;

        var inviterId = data["InviterId"];

        var inviterConn = _connMgr.GetUserConnection(inviterId);
        if (inviterConn != null)
        {
            await SendAsync(inviterConn.WebSocket, new Packet
            {
                T = "invite_accepted",
                P = new { UserId = conn.UserId }
            });
        }
    }

    /// <summary>
    /// 处理拒绝邀请
    /// </summary>
    private async Task HandleInviteReject(Connection conn, Packet packet)
    {
        var data = JsonSerializer.Deserialize<Dictionary<string, string>>(packet.P?.ToString() ?? "");
        if (data == null) return;

        var inviterId = data["InviterId"];

        var inviterConn = _connMgr.GetUserConnection(inviterId);
        if (inviterConn != null)
        {
            await SendAsync(inviterConn.WebSocket, new Packet
            {
                T = "invite_rejected",
                P = new { UserId = conn.UserId }
            });
        }
    }

    /// <summary>
    /// 处理消息已读回执
    /// </summary>
    private async Task HandleReadReceipt(Connection conn, Packet packet)
    {
        var data = JsonSerializer.Deserialize<Dictionary<string, int>>(packet.P?.ToString() ?? "");
        if (data == null) return;

        await BroadcastToChannelExcept(conn.RoomId, conn.ChannelId, conn.UserId, new Packet
        {
            T = "read_receipt",
            P = new
            {
                UserId = conn.UserId,
                PageNumber = data["PageNumber"],
                InPageSeq = data["InPageSeq"]
            }
        });
    }

    /// <summary>
    /// 处理消息搜索请求
    /// </summary>
    private async Task HandleSearchMessage(Connection conn, Packet packet)
    {
        var query = JsonSerializer.Deserialize<MessageSearchQuery>(packet.P?.ToString() ?? "");
        if (query == null) return;

        // 解析频道范围：空=当前连接频道；含哨兵 "*" =枚举全房频道；否则=指定集合。
        List<string> channelIds;
        if (query.ChannelIds == null || query.ChannelIds.Count == 0)
        {
            channelIds = new List<string> { conn.ChannelId };
        }
        else if (query.ChannelIds.Contains(MessageSearchQuery.AllChannels))
        {
            var all = await _channelStore.GetChannelsAsync(conn.RoomId);
            channelIds = all.Select(c => c.ChannelId).ToList();
        }
        else
        {
            channelIds = query.ChannelIds;
        }

        var results = await _store.SearchAdvancedAsync(conn.RoomId, channelIds, query);

        await SendAsync(conn.WebSocket, new Packet
        {
            T = "search_msg",
            Id = packet.Id,
            P = results
        });
    }

    /// <summary>
    /// 围绕某条消息取上下文（搜索结果跳转定位用）：返回目标 + 前后各 N 条。
    /// </summary>
    private async Task HandleQueryAround(Connection conn, Packet packet)
    {
        var data = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(packet.P?.ToString() ?? "");
        if (data == null) return;

        var channelId = data.TryGetValue("ChannelId", out var chEl) && chEl.ValueKind == JsonValueKind.String
            ? (chEl.GetString() ?? "") : "";
        if (string.IsNullOrEmpty(channelId)) channelId = conn.ChannelId;

        long msgId = 0;
        if (data.TryGetValue("MsgId", out var idEl))
        {
            if (idEl.ValueKind == JsonValueKind.Number) idEl.TryGetInt64(out msgId);
            else if (idEl.ValueKind == JsonValueKind.String) long.TryParse(idEl.GetString(), out msgId);
        }
        var before = data.TryGetValue("Before", out var bEl) && bEl.ValueKind == JsonValueKind.Number ? bEl.GetInt32() : 25;
        var after = data.TryGetValue("After", out var aEl) && aEl.ValueKind == JsonValueKind.Number ? aEl.GetInt32() : 25;

        var results = await _store.QueryAroundMessageAsync(conn.RoomId, channelId, msgId, before, after);

        await SendAsync(conn.WebSocket, new Packet
        {
            T = "query_around",
            Id = packet.Id,
            P = results
        });
    }

    /// <summary>置顶 / 取消置顶消息，广播 msg_pin_changed 给全房活动连接。</summary>
    private async Task HandlePinMessage(Connection conn, Packet packet)
    {
        var data = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(packet.P?.ToString() ?? "");
        if (data == null) return;

        long msgId = 0;
        if (data.TryGetValue("MsgId", out var idEl))
        {
            if (idEl.ValueKind == JsonValueKind.Number) idEl.TryGetInt64(out msgId);
            else if (idEl.ValueKind == JsonValueKind.String) long.TryParse(idEl.GetString(), out msgId);
        }
        var pin = !(data.TryGetValue("Pin", out var pinEl) && pinEl.ValueKind == JsonValueKind.False);
        var channelId = data.TryGetValue("ChannelId", out var chEl) && chEl.ValueKind == JsonValueKind.String
            ? (chEl.GetString() ?? "") : "";
        if (string.IsNullOrEmpty(channelId)) channelId = conn.ChannelId;

        await _store.PinMessageAsync(conn.RoomId, channelId, msgId, pin);

        await BroadcastToRoomActive(conn.RoomId, new Packet
        {
            T = "msg_pin_changed",
            P = new { MsgId = msgId, ChannelId = channelId, IsPinned = pin }
        });
    }

    /// <summary>获取频道置顶消息列表。</summary>
    private async Task HandleGetPins(Connection conn, Packet packet)
    {
        var data = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(packet.P?.ToString() ?? "");
        var channelId = data != null && data.TryGetValue("ChannelId", out var chEl) && chEl.ValueKind == JsonValueKind.String
            ? (chEl.GetString() ?? "") : "";
        if (string.IsNullOrEmpty(channelId)) channelId = conn.ChannelId;

        var pins = await _store.GetPinnedMessagesAsync(conn.RoomId, channelId);
        await SendAsync(conn.WebSocket, new Packet { T = "get_pins", Id = packet.Id, P = pins });
    }

    /// <summary>
    /// 消息表情反应 toggle（对标 Discord/QQ）：同一 (msgId,emoji,userId) 已存在则移除、否则添加。
    /// 服务端权威盖章 userId（不信客户端上报），toggle 结果全房广播供实时聚合。
    /// </summary>
    private async Task HandleReact(Connection conn, Packet packet)
    {
        var data = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(packet.P?.ToString() ?? "");
        if (data == null) return;

        long msgId = 0;
        if (data.TryGetValue("MsgId", out var idEl))
        {
            if (idEl.ValueKind == JsonValueKind.Number) idEl.TryGetInt64(out msgId);
            else if (idEl.ValueKind == JsonValueKind.String) long.TryParse(idEl.GetString(), out msgId);
        }
        var emoji = data.TryGetValue("Emoji", out var eEl) && eEl.ValueKind == JsonValueKind.String
            ? (eEl.GetString() ?? "") : "";
        var channelId = data.TryGetValue("ChannelId", out var chEl) && chEl.ValueKind == JsonValueKind.String
            ? (chEl.GetString() ?? "") : "";
        if (string.IsNullOrEmpty(channelId)) channelId = conn.ChannelId;
        if (msgId == 0 || string.IsNullOrEmpty(emoji)) return;

        // userId 服务端权威（不信客户端），防止代他人反应。
        // 返回切换后该 (msg,emoji) 的最新用户列表，直接广播聚合结果。
        var update = await _reactionStore.ToggleAsync(conn.RoomId, msgId, channelId, emoji, conn.UserId);

        await BroadcastToRoomActive(conn.RoomId, new Packet
        {
            T = "reaction_changed",
            P = update
        });
    }

    /// <summary>拉取某条消息的全部反应（进频道/跳转加载消息时按需批量补齐聚合）。</summary>
    private async Task HandleGetReactions(Connection conn, Packet packet)
    {
        var data = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(packet.P?.ToString() ?? "");
        if (data == null) return;

        var channelId = data.TryGetValue("ChannelId", out var chEl) && chEl.ValueKind == JsonValueKind.String
            ? (chEl.GetString() ?? "") : "";
        if (string.IsNullOrEmpty(channelId)) channelId = conn.ChannelId;

        // MsgIds 数组：一次批量拉一屏消息的反应
        var msgIds = new List<long>();
        if (data.TryGetValue("MsgIds", out var idsEl) && idsEl.ValueKind == JsonValueKind.Array)
            foreach (var el in idsEl.EnumerateArray())
            {
                if (el.ValueKind == JsonValueKind.Number && el.TryGetInt64(out var v)) msgIds.Add(v);
                else if (el.ValueKind == JsonValueKind.String && long.TryParse(el.GetString(), out var s)) msgIds.Add(s);
            }

        var rows = await _reactionStore.GetForMessagesAsync(conn.RoomId, msgIds);
        await SendAsync(conn.WebSocket, new Packet { T = "get_reactions", Id = packet.Id, P = rows });
    }

    /// <summary>
    /// 获取房间成员列表 —— 返回**当前在线成员**（基于活动连接），
    /// 而不是数据库里的历史花名册（否则离线的旧成员会残留在列表里）。
    /// </summary>
    private async Task HandleGetMembers(Connection conn, Packet packet)
    {
        // 按 userId 去重（同一用户可能有多条连接，虽然已限制为一条）
        var online = _connMgr.GetRoomConnections(conn.RoomId)
            .GroupBy(c => c.UserId)
            .Select(g => new RoomMember
            {
                RoomId = conn.RoomId,
                UserId = g.Key,
                Role = "member"
            })
            .ToList();

        await SendAsync(conn.WebSocket, new Packet
        {
            T = "get_members",
            Id = packet.Id,
            P = online
        });
    }

    /// <summary>
    /// 更新成员信息
    /// </summary>
    private async Task HandleUpdateMember(Connection conn, Packet packet)
    {
        var member = JsonSerializer.Deserialize<RoomMember>(packet.P?.ToString() ?? "");
        if (member == null) return;

        await _memberStore.AddOrUpdateMemberAsync(conn.RoomId, member);
    }

    /// <summary>
    /// 禁止成员进入房间
    /// </summary>
    private async Task HandleBanMember(Connection conn, Packet packet)
    {
        var data = JsonSerializer.Deserialize<Dictionary<string, string>>(packet.P?.ToString() ?? "");
        if (data == null) return;

        var userId = data["UserId"];
        await _memberStore.BanMemberAsync(conn.RoomId, userId);
    }

    /// <summary>
    /// 创建房间
    /// </summary>
    private async Task HandleCreateRoom(Connection conn, Packet packet)
    {
        var room = JsonSerializer.Deserialize<Room>(packet.P?.ToString() ?? "");
        if (room == null) return;

        await _roomStore.CreateRoomAsync(room);
    }

    /// <summary>
    /// 获取房间列表
    /// </summary>
    private async Task HandleGetRooms(Connection conn, Packet packet)
    {
        var rooms = await _roomStore.GetAllRoomsAsync();

        await SendAsync(conn.WebSocket, new Packet
        {
            T = "get_rooms",
            Id = packet.Id,
            P = rooms
        });
    }

    /// <summary>
    /// 更新房间设置
    /// </summary>
    private async Task HandleUpdateRoom(Connection conn, Packet packet)
    {
        var room = JsonSerializer.Deserialize<Room>(packet.P?.ToString() ?? "");
        if (room == null) return;

        await _roomStore.UpdateRoomAsync(room);
    }

    /// <summary>
    /// 删除房间
    /// </summary>
    private async Task HandleDeleteRoom(Connection conn, Packet packet)
    {
        var data = JsonSerializer.Deserialize<Dictionary<string, string>>(packet.P?.ToString() ?? "");
        if (data == null) return;

        var roomId = data["RoomId"];
        await _roomStore.DeleteRoomAsync(roomId);
    }

    // ========== 频道管理 ==========

    /// <summary>
    /// 创建频道
    /// </summary>
    private async Task HandleCreateChannel(Connection conn, Packet packet)
    {
        var channel = JsonSerializer.Deserialize<Channel>(packet.P?.ToString() ?? "");
        if (channel == null) return;

        // 校验边界：服务端是权威，客户端校验只是体验优化，这里必须再校验一次。
        var (ok, normalized, reason) = NormalizeChannelName(channel.ChannelName);
        if (!ok)
        {
            await SendAsync(conn.WebSocket, new Packet { T = "error", P = reason });
            return;
        }

        // ID 权威：忽略客户端传入的 ChannelId，由 store 生成（避免弱ID碰撞/覆盖）
        channel.ChannelId = string.Empty;
        channel.ChannelName = normalized;
        channel.RoomId = conn.RoomId;

        try
        {
            await _channelStore.CreateChannelAsync(channel);
        }
        catch (InvalidOperationException ex)
        {
            await SendAsync(conn.WebSocket, new Packet { T = "error", P = ex.Message });
            return;
        }

        await BroadcastChannelsChanged(conn.RoomId);
    }

    /// <summary>
    /// 获取频道列表
    /// </summary>
    private async Task HandleGetChannels(Connection conn, Packet packet)
    {
        var channels = await _channelStore.GetChannelsAsync(conn.RoomId);

        await SendAsync(conn.WebSocket, new Packet
        {
            T = "get_channels",
            Id = packet.Id,
            P = channels
        });
    }

    /// <summary>
    /// 更新频道
    /// </summary>
    private async Task HandleUpdateChannel(Connection conn, Packet packet)
    {
        var channel = JsonSerializer.Deserialize<Channel>(packet.P?.ToString() ?? "");
        if (channel == null || string.IsNullOrEmpty(channel.ChannelId)) return;

        var (ok, normalized, reason) = NormalizeChannelName(channel.ChannelName);
        if (!ok)
        {
            await SendAsync(conn.WebSocket, new Packet { T = "error", P = reason });
            return;
        }

        channel.ChannelName = normalized;
        channel.RoomId = conn.RoomId;

        try
        {
            await _channelStore.UpdateChannelAsync(channel);
        }
        catch (InvalidOperationException ex)
        {
            await SendAsync(conn.WebSocket, new Packet { T = "error", P = ex.Message });
            return;
        }

        await BroadcastChannelsChanged(conn.RoomId);
    }

    /// <summary>
    /// 删除频道
    /// </summary>
    private async Task HandleDeleteChannel(Connection conn, Packet packet)
    {
        var data = JsonSerializer.Deserialize<Dictionary<string, string>>(packet.P?.ToString() ?? "");
        if (data == null || !data.ContainsKey("ChannelId")) return;

        try
        {
            await _channelStore.DeleteChannelAsync(conn.RoomId, data["ChannelId"]);
        }
        catch (InvalidOperationException ex)
        {
            await SendAsync(conn.WebSocket, new Packet { T = "error", P = ex.Message });
            return;
        }

        await BroadcastChannelsChanged(conn.RoomId);
    }

    /// <summary>
    /// 频道名规范化 + 校验：Trim、非空、长度上限 32。
    /// 返回 (是否通过, 规范化后的名字, 失败原因)。
    /// </summary>
    private static (bool ok, string normalized, string reason) NormalizeChannelName(string? raw)
    {
        var name = (raw ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(name))
            return (false, string.Empty, "频道名不能为空");
        if (name.Length > 32)
            return (false, string.Empty, "频道名过长（最多 32 字）");
        return (true, name, string.Empty);
    }

    /// <summary>
    /// 频道拖拽重排：收到有序 ChannelId 列表 → 持久化 SortOrder → 广播频道列表变更
    /// </summary>
    private async Task HandleReorderChannels(Connection conn, Packet packet)
    {
        var data = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(packet.P?.ToString() ?? "");
        if (data == null || !data.TryGetValue("ChannelIds", out var idsEl) || idsEl.ValueKind != JsonValueKind.Array)
            return;

        var orderedIds = new List<string>();
        foreach (var el in idsEl.EnumerateArray())
        {
            var s = el.GetString();
            if (!string.IsNullOrEmpty(s)) orderedIds.Add(s);
        }
        if (orderedIds.Count == 0) return;

        await _channelStore.ReorderChannelsAsync(conn.RoomId, orderedIds);
        await BroadcastChannelsChanged(conn.RoomId);
    }

    /// <summary>
    /// 向房间广播频道列表变更
    /// </summary>
    private async Task BroadcastChannelsChanged(string roomId)
    {
        var channels = await _channelStore.GetChannelsAsync(roomId);
        await BroadcastToRoom(roomId, new Packet
        {
            T = "channels_changed",
            P = channels
        });
    }

    /// <summary>
    /// 构建 join_success 握手快照：房间名 + 首批频道列表（已按 SortOrder 排序）。
    /// 客户端据此直接渲染标题与频道列表，免额外 get_rooms / get_channels 往返。
    /// </summary>
    private async Task<JoinInfo> BuildJoinInfoAsync(Room room)
    {
        var channels = await _channelStore.GetChannelsAsync(room.RoomId);
        return new JoinInfo
        {
            RoomId = room.RoomId,
            RoomName = room.RoomName,
            OwnerId = room.HostUserId,
            Channels = channels
        };
    }

    /// <summary>
    /// 房主批准加入请求
    /// </summary>
    private async Task HandleApproveJoin(Connection conn, Packet packet)
    {
        var data = JsonSerializer.Deserialize<Dictionary<string, string>>(packet.P?.ToString() ?? "");
        if (data == null || !data.ContainsKey("UserId"))
            return;

        var targetUserId = data["UserId"];
        var roomId = conn.RoomId;

        // 验证权限（只有房主可以批准）
        var room = await _roomStore.GetRoomAsync(roomId);
        if (room == null || room.HostUserId != conn.UserId)
        {
            await SendAsync(conn.WebSocket, new Packet { T = "error", P = "无权限操作" });
            return;
        }

        // 添加成员
        await _memberStore.AddOrUpdateMemberAsync(roomId, new RoomMember
        {
            UserId = targetUserId,
            Role = "member",
            JoinedAt = DateTime.UtcNow
        });

        // 通知目标用户
        var targetConn = _connMgr.GetConnection(targetUserId);
        if (targetConn != null)
        {
            targetConn.Status = "active";  // 更新状态
            await SendAsync(targetConn.WebSocket, new Packet
            {
                T = "join_success",
                P = await BuildJoinInfoAsync(room)
            });

            // 广播成员加入
            var roomConnections = _connMgr.GetRoomConnections(roomId);
            foreach (var c in roomConnections)
            {
                if (c.UserId != targetUserId && c.Status == "active")
                {
                    try
                    {
                        await SendAsync(c.WebSocket, new Packet
                        {
                            T = "member_joined",
                            P = new { UserId = targetUserId, RoomId = roomId }
                        });
                    }
                    catch
                    {
                        // 忽略发送失败
                    }
                }
            }
        }
    }

    /// <summary>
    /// 房主拒绝加入请求
    /// </summary>
    private async Task HandleRejectJoin(Connection conn, Packet packet)
    {
        var data = JsonSerializer.Deserialize<Dictionary<string, object>>(packet.P?.ToString() ?? "");
        if (data == null || !data.ContainsKey("UserId"))
            return;

        var targetUserId = data["UserId"]?.ToString();
        var reason = data.ContainsKey("Reason") ? data["Reason"]?.ToString() : "房主拒绝了你的加入请求";
        var roomId = conn.RoomId;

        // 验证权限
        var room = await _roomStore.GetRoomAsync(roomId);
        if (room == null || room.HostUserId != conn.UserId)
        {
            await SendAsync(conn.WebSocket, new Packet { T = "error", P = "无权限操作" });
            return;
        }

        // 通知目标用户
        var targetConn = _connMgr.GetConnection(targetUserId);
        if (targetConn != null)
        {
            await SendAsync(targetConn.WebSocket, new Packet
            {
                T = "join_rejected",
                P = reason
            });

            // 断开连接
            await targetConn.WebSocket.CloseAsync(
                WebSocketCloseStatus.NormalClosure,
                "加入请求被拒绝",
                CancellationToken.None
            );
        }
    }

    /// <summary>
    /// 广播到房间所有成员（已有方法的扩展版本）
    /// </summary>
    private async Task BroadcastToRoom(string roomId, Packet packet, string excludeUserId)
    {
        var connections = _connMgr.GetRoomConnections(roomId);
        foreach (var conn in connections)
        {
            if (conn.UserId != excludeUserId && conn.Status == "active")
            {
                try
                {
                    await SendAsync(conn.WebSocket, packet);
                }
                catch
                {
                    // 忽略发送失败
                }
            }
        }
    }

    /// <summary>
    /// 处理自定义消息类型
    /// </summary>
    private async Task HandleCustomType(Connection conn, Packet packet)
    {
        // 验证自定义消息类型格式
        if (!PacketTypes.IsValidCustomType(packet.T))
        {
            // 不是有效的自定义类型，忽略
            return;
        }

        // 自定义消息直接转发给房间内其他客户端
        await BroadcastToChannelExcept(conn.RoomId, conn.ChannelId, conn.UserId, packet);
    }

    // ========== 地图引擎命令同步 ==========

    /// <summary>
    /// 处理地图命令请求 (C→S: map:command)
    /// 接收命令、执行、将 Delta 广播给房间内所有客户端（含发送者，由客户端回声过滤）
    /// </summary>
    private async Task HandleMapCommand(Connection conn, Packet packet)
    {
        MapCommandRequest? request;
        try
        {
            request = JsonSerializer.Deserialize<MapCommandRequest>(
                packet.P?.ToString() ?? "{}",
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
            );
        }
        catch
        {
            await SendAsync(conn.WebSocket, new Packet { T = "error", P = "map:command 格式错误" });
            return;
        }

        if (request == null)
        {
            await SendAsync(conn.WebSocket, new Packet { T = "error", P = "map:command 参数为空" });
            return;
        }

        // 确保 UserId 来自连接本身（防止客户端伪造）
        request = request with { UserId = conn.UserId };

        var handler = _mapManager.GetOrCreateHandler(conn.RoomId);

        // 挂载 Delta 广播事件（仅当 handler 刚创建时注册；每次调用只要 handler 存在就会触发正确的广播）
        // 注意：DeltaBroadcast 是 EventHandler，重复注册会导致多次广播，需按房间管理
        // 使用 MapBroadcastRelay 来解耦（见下文），这里简单做法：通过返回的 Delta 广播
        var result = handler.HandleCommand(request);

        if (!result.Success)
        {
            await SendAsync(conn.WebSocket, new Packet
            {
                T = "error",
                P = $"map:command 执行失败: {result.Error}"
            });
            return;
        }

        // 广播 Delta 给房间内所有连接（含发送者，客户端自己过滤回声）
        var delta = new MapStateDelta
        {
            CommandType = request.CommandType,
            Params = request.Params,
            Version = result.NewVersion,
            UserId = conn.UserId
        };

        var deltaPacket = new Packet
        {
            T = PacketTypes.MapDelta,
            P = delta
        };
        await BroadcastToRoom(conn.RoomId, deltaPacket);
    }

    /// <summary>
    /// 处理地图全量同步请求 (C→S: map:fullsync_req)
    /// 向请求的客户端发送完整地图状态
    /// </summary>
    private async Task HandleMapFullSyncRequest(Connection conn, Packet packet)
    {
        var handler = _mapManager.GetOrCreateHandler(conn.RoomId);

        var syncRequest = new MapEngine.Core.Networking.MapFullSyncRequest
        {
            CurrentVersion = 0  // 客户端当前版本，全量同步时不关心
        };

        var fullSync = handler.HandleFullSyncRequest(syncRequest);

        await SendAsync(conn.WebSocket, new Packet
        {
            T = PacketTypes.MapFullSync,
            P = fullSync
        });
    }
}
