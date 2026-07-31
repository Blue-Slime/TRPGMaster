using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Threading;
using System.Linq;
using System.IO;
using System.Net.Http;

using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using MasterIM.Models;

namespace MasterIM.SDK;

public class IMClient : IDisposable
{
    private ClientWebSocket? _ws;
    private readonly Dictionary<string, TaskCompletionSource<object>> _pending = new();
    private CancellationTokenSource? _cts;
    private string? _url;
    private string? _userId;
    private string? _roomId;
    private string? _channelId;

    // 连接状态机字段
    private TaskCompletionSource<JoinOutcome>? _joinTcs;   // 加入握手的确定性结果，由 HandlePacket 完成
    private TaskCompletionSource<bool>? _receiveLoopDone;  // Dispose 等待 ReceiveLoop 真正退出
    private bool _autoReconnectEnabled;                    // 仅 join 成功后由调用方显式开启

    /// <summary>
    /// 当前选中频道（Discord 对齐：一条连接服务整个房间的所有频道，
    /// 发送/查询/正在输入都以此为准；切频道零重连）。默认为连接时的频道。
    /// </summary>
    public string? CurrentChannelId { get; private set; }

    /// <summary>当前连接所在房间 ID（连接时确定，供 UI 查询房间信息用）。</summary>
    public string? CurrentRoomId => _roomId;

    /// <summary>当前房间名（由 join_success 握手带回，供 UI 直接显示，无需二次拉取）。</summary>
    public string CurrentRoomName { get; private set; } = string.Empty;

    /// <summary>
    /// HTTP 基地址（ws → http）。用于拼接 /download 文件 URL。
    /// ws://host:5000/im → http://host:5000
    /// </summary>
    public string BaseHttpUrl => (_url ?? "")
        .Replace("wss://", "https://")
        .Replace("ws://", "http://")
        .Split("/im")[0]
        .TrimEnd('/');

    /// <summary>切换当前频道（纯客户端状态，不重连）。</summary>
    public void SetCurrentChannel(string channelId) => CurrentChannelId = channelId;

    // 消息相关事件
    public event Action<GroupMessage>? OnMessageReceived;
    public event Action<string, long, string>? OnMessageModified;  // 消息修改 (channelId, msgId, newContent)
    public event Action<string, long>? OnMessageRevoked;  // 消息撤回 (channelId, msgId)
    public event Action<List<long>, DateTime>? OnBatchMoved;  // 批量移动 (msgIds, targetTime)
    public event Action<List<long>>? OnBatchDeleted;  // 批量删除 (msgIds)

    // 通知相关事件
    public event Action<UpdateNotification>? OnUpdateNotification;
    public event Action<StreamData>? OnStreamReceived;
    public event Action<ObjectSyncData>? OnObjectSync;

    // 连接相关事件
    public event Action? OnConnected;
    public event Action? OnDisconnected;
    public event Action<int>? OnReconnecting;  // 重连中 (尝试次数)
    public event Action? OnReconnected;  // 重连成功
    public event Action<string>? OnConnectionError;  // 连接错误

    // 房间加入事件（新增）
    public event Action? OnJoinSuccess;  // 加入房间成功
    public event Action? OnJoinPending;  // 等待房主批准
    public event Action<string>? OnJoinRejected;  // 加入被拒绝 (reason)

    // 用户行为事件
    public event Action<string, string, string>? OnUserTyping;  // 用户正在输入 (userId, userName, channelId)
    public event Action<string, string>? OnUserStopTyping;  // 用户停止输入 (userId, channelId)
    public event Action<string, string, string>? OnPresenceUpdate;  // 在线状态变更 (userId, status, channelId)
    public event Action<DiceResult>? OnDiceResult;  // 骰子结果 (含投掷者/表达式/结果/是否暗骰)

    // 超时空编辑事件
    public event Action<long>? OnMessageInserted;  // 历史消息插入 (msgId)
    public event Action<int>? OnPageCreated;  // 分页创建（已废弃）
    public event Action<int>? OnPageDeleted;  // 分页删除（已废弃）

    // 群组成员事件
    public event Action<string, string>? OnMemberJoined;  // 成员加入 (userId, role)
    public event Action<string>? OnMemberLeft;  // 成员离开

    // 文件传输事件
    public event Action<string, long, long>? OnFileUploadProgress;  // 上传进度 (fileId, uploaded, total)
    public event Action<string>? OnFileUploadComplete;  // 上传完成 (fileId)
    public event Action<string, string>? OnFileUploadFailed;  // 上传失败 (fileId, error)

    // 房间邀请事件
    public event Action<string, string, string>? OnRoomInviteReceived;  // 收到邀请 (inviterId, roomId, channelId)
    public event Action<string, string, string>? OnJoinRequestReceived;  // 收到加入请求 (requesterId, roomId, channelId)
    public event Action<string>? OnInviteAccepted;  // 邀请被接受 (userId)
    public event Action<string>? OnInviteRejected;  // 邀请被拒绝 (userId)

    // @提及事件
    public event Action<GroupMessage>? OnMentioned;  // 被@提及

    // 已读回执事件
    public event Action<string, long>? OnMessageRead;  // 消息已读 (userId, msgId)

    // 自定义消息事件
    public event Action<string, object?>? OnCustomMessage;  // 自定义消息 (messageType, data)

    // 频道事件
    public event Action<List<Channel>>? OnChannelsChanged;  // 频道列表变更

    // 地图同步事件（原始 JSON，应用层自行反序列化为 MapStateDelta / MapFullSync）
    public event Action<string>? OnMapDeltaJson;     // map:delta 原始 JSON
    public event Action<string>? OnMapFullSyncJson;  // map:fullsync 原始 JSON

    // =========================================================
    // 连接 + 加入握手（确定性结果，不再有永久挂起）
    // =========================================================

    /// <summary>
    /// 建立 WS 连接并完成加入握手，必然返回确定结果，不会永久挂起。
    /// 每次调用前会先等待上次 ReceiveLoop 完全退出（杜绝跨房间状态残留）。
    /// </summary>
    public async Task<JoinOutcome> ConnectAsync(
        string url, string userId, string roomId, string channelId,
        CancellationToken cancellationToken = default)
    {
        _url = url;
        _userId = userId;
        _roomId = roomId;
        _channelId = channelId;
        CurrentChannelId = channelId;
        _autoReconnectEnabled = false;

        // 取消旧连接并等待 ReceiveLoop 真正退出，避免旧连接残留服务端
        _cts?.Cancel();
        if (_receiveLoopDone != null)
        {
            await _receiveLoopDone.Task
                .WaitAsync(TimeSpan.FromSeconds(3))
                .ContinueWith(_ => { }, CancellationToken.None);
        }

        _ws?.Dispose();
        _ws = new ClientWebSocket();
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _joinTcs = new TaskCompletionSource<JoinOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
        _receiveLoopDone = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            var uri = new Uri($"{url}?userId={userId}&roomId={roomId}&channelId={channelId}");
            await _ws.ConnectAsync(uri, _cts.Token);
        }
        catch (OperationCanceledException)
        {
            _receiveLoopDone.TrySetResult(true);
            return JoinOutcome.Cancelled();
        }
        catch (Exception ex)
        {
            _receiveLoopDone.TrySetResult(true);
            return JoinOutcome.NetworkError($"无法连接到服务器：{ex.Message}");
        }

        _ = ReceiveLoop();
        _ = Heartbeat();

        // 15s 兜底超时
        _ = Task.Delay(15000, _cts.Token)
              .ContinueWith(t =>
              {
                  if (!t.IsCanceled)
                      _joinTcs?.TrySetResult(JoinOutcome.TimedOut());
              }, CancellationToken.None);

        cancellationToken.Register(() => _joinTcs?.TrySetResult(JoinOutcome.Cancelled()));

        return await _joinTcs.Task;
    }

    /// <summary>
    /// 开启会话级自动断线重连（仅在 join 成功后由聊天窗口显式调用）。
    /// 只处理网络断线，加入阶段绝不自动重连。
    /// </summary>
    public void EnableAutoReconnect()
    {
        if (_autoReconnectEnabled) return;
        _autoReconnectEnabled = true;
        _ = AutoReconnectLoop();
    }

    private async Task AutoReconnectLoop()
    {
        int retryCount = 0;
        while (_autoReconnectEnabled && _cts?.IsCancellationRequested == false)
        {
            await Task.Delay(5000, _cts.Token)
                .ContinueWith(_ => { }, CancellationToken.None);

            if (_cts.IsCancellationRequested || !_autoReconnectEnabled) break;
            if (_ws?.State == WebSocketState.Open) { retryCount = 0; continue; }

            OnDisconnected?.Invoke();
            retryCount++;
            OnReconnecting?.Invoke(retryCount);

            try
            {
                _ws?.Dispose();
                _ws = new ClientWebSocket();
                var uri = new Uri($"{_url}?userId={_userId}&roomId={_roomId}&channelId={_channelId}");
                await _ws.ConnectAsync(uri, _cts.Token);
                _ = ReceiveLoop();
                _ = Heartbeat();
                OnReconnected?.Invoke();
                retryCount = 0;
            }
            catch { /* 继续重试 */ }
        }
    }

    /// <summary>
    /// 发送群聊消息
    /// </summary>
    public async Task SendMessageAsync(GroupMessage msg)
    {
        // Discord 对齐：消息归属以当前频道为准（未显式指定时填当前频道）
        if (string.IsNullOrEmpty(msg.ChannelId))
            msg.ChannelId = CurrentChannelId ?? _channelId ?? "";
        await SendAsync(new Packet { T = "msg", P = msg });
    }

    /// <summary>
    /// 按时间范围查询历史消息（v2.0）
    /// </summary>
    public async Task<List<GroupMessage>> QueryMessagesByTimeRangeAsync(
        DateTime startTime,
        DateTime endTime,
        int limit = 100)
    {
        var result = await RequestAsync<List<GroupMessage>>(new Packet
        {
            T = "qry",
            P = new { StartTime = startTime, EndTime = endTime, Limit = limit, ChannelId = CurrentChannelId ?? _channelId ?? "" }
        });
        return result ?? new();
    }

    /// <summary>
    /// 查询最近消息（便捷方法）
    /// </summary>
    public async Task<List<GroupMessage>> QueryRecentMessagesAsync(int days = 7, int limit = 100)
    {
        var endTime = DateTime.UtcNow;
        var startTime = endTime.AddDays(-days);
        return await QueryMessagesByTimeRangeAsync(startTime, endTime, limit);
    }

    /// <summary>
    /// 查询指定频道的最近消息（Discord 对齐：一条连接可查任意频道历史）
    /// </summary>
    public async Task<List<GroupMessage>> QueryChannelMessagesAsync(string channelId, int days = 7, int limit = 100)
    {
        var endTime = DateTime.UtcNow;
        var startTime = endTime.AddDays(-days);
        var result = await RequestAsync<List<GroupMessage>>(new Packet
        {
            T = "qry",
            P = new { StartTime = startTime, EndTime = endTime, Limit = limit, ChannelId = channelId }
        });
        return result ?? new();
    }

    /// <summary>
    /// 检查分页修改时间
    /// </summary>
    public async Task<long> CheckPageModifiedAsync(int pageNumber)
    {
        var result = await RequestAsync<Dictionary<string, object>>(new Packet
        {
            T = "chk",
            P = new { PageNumber = pageNumber }
        });

        if (result != null && result.TryGetValue("LastModified", out var time))
        {
            return Convert.ToInt64(time);
        }
        return 0;
    }

    /// <summary>
    /// 创建空白分页
    /// </summary>
    public async Task CreateEmptyPageAsync(int pageNumber)
    {
        await SendAsync(new Packet { T = "crt", P = new { PageNumber = pageNumber } });
    }

    /// <summary>
    /// 删除空白分页
    /// </summary>
    public async Task DeleteEmptyPageAsync(int pageNumber)
    {
        await SendAsync(new Packet { T = "del", P = new { PageNumber = pageNumber } });
    }

    /// <summary>
    /// 批量平移消息
    /// </summary>
    public async Task BatchMoveMessagesAsync(List<(int page, int seq)> messages, int targetPage, int targetSeq)
    {
        var msgList = messages.Select(m => new { Page = m.page, Seq = m.seq }).ToList();
        await SendAsync(new Packet
        {
            T = "bmv",
            P = new { Messages = msgList, TargetPage = targetPage, TargetSeq = targetSeq }
        });
    }

    /// <summary>
    /// 批量删除消息
    /// </summary>
    public async Task BatchDeleteMessagesAsync(List<(int page, int seq)> messages)
    {
        var msgList = messages.Select(m => new { Page = m.page, Seq = m.seq }).ToList();
        await SendAsync(new Packet { T = "bdl", P = new { Messages = msgList } });
    }

    /// <summary>
    /// 插入历史消息（v2.0 使用 DisplayTime）
    /// </summary>
    public async Task InsertHistoryMessageAsync(GroupMessage msg, DateTime displayTime)
    {
        msg.DisplayTime = displayTime;
        await SendAsync(new Packet { T = "msg", P = new { Type = "insert", GroupMessage = msg } });
    }

    /// <summary>
    /// 修改已发送的消息内容（v2.0 使用 MsgId）
    /// </summary>
    public async Task ModifyMessageAsync(long msgId, string newContent)
    {
        // Discord 对齐：带上频道，服务端按它路由（否则会误存/播到当前连接的默认频道）
        await SendAsync(new Packet { T = "msg", P = new { Type = "modify", MsgId = msgId, Content = newContent, ChannelId = CurrentChannelId ?? _channelId ?? "" } });
    }

    /// <summary>
    /// 撤回消息（v2.0 使用 MsgId）
    /// </summary>
    public async Task RevokeMessageAsync(long msgId)
    {
        await SendAsync(new Packet { T = "msg", P = new { Type = "revoke", MsgId = msgId, ChannelId = CurrentChannelId ?? _channelId ?? "" } });
    }

    /// <summary>
    /// 批量移动消息（v2.0 超时空编辑）
    /// </summary>
    public async Task BatchMoveMessagesAsync(List<long> msgIds, DateTime targetTime, bool preserveOrder = true)
    {
        await SendAsync(new Packet
        {
            T = "bmv",
            P = new { MsgIds = msgIds, TargetTime = targetTime, PreserveOrder = preserveOrder }
        });
    }

    /// <summary>
    /// 批量删除消息（v2.0 使用 MsgId）
    /// </summary>
    public async Task BatchDeleteMessagesAsync(List<long> msgIds)
    {
        await SendAsync(new Packet { T = "bdl", P = new { MsgIds = msgIds } });
    }

    /// <summary>
    /// 发送流式数据（用于实时同步）
    /// </summary>
    public async Task SendStreamAsync(StreamData data)
    {
        await SendAsync(new Packet { T = "stm", P = data });
    }

    /// <summary>
    /// 创建游戏对象
    /// </summary>
    public async Task<string> CreateObjectAsync(GameObject obj)
    {
        await SendAsync(new Packet { T = "obj_create", P = obj });
        return obj.Id;
    }

    /// <summary>
    /// 更新游戏对象
    /// </summary>
    public async Task UpdateObjectAsync(GameObject obj)
    {
        await SendAsync(new Packet { T = "obj_update", P = obj });
    }

    /// <summary>
    /// 删除游戏对象
    /// </summary>
    public async Task DeleteObjectAsync(string objectId)
    {
        await SendAsync(new Packet { T = "obj_delete", P = new { ObjectId = objectId } });
    }

    /// <summary>
    /// 按类型查询游戏对象
    /// </summary>
    public async Task<List<GameObject>> QueryObjectsByTypeAsync(string type)
    {
        var result = await RequestAsync<List<GameObject>>(new Packet
        {
            T = "obj_query",
            P = new { Type = type }
        });
        return result ?? new();
    }

    /// <summary>
    /// 按序列号范围查询游戏对象
    /// </summary>
    public async Task<List<GameObject>> QueryObjectsBySequenceAsync(long startSeq, long endSeq)
    {
        var result = await RequestAsync<List<GameObject>>(new Packet
        {
            T = "obj_query",
            P = new { StartSeq = startSeq, EndSeq = endSeq }
        });
        return result ?? new();
    }

    private async Task SendAsync(Packet packet)
    {
        if (_ws?.State != WebSocketState.Open) return;

        var json = JsonSerializer.Serialize(packet);
        var bytes = Encoding.UTF8.GetBytes(json);
        await _ws.SendAsync(bytes, WebSocketMessageType.Text, true, _cts?.Token ?? default);
    }

    private async Task<T?> RequestAsync<T>(Packet packet, int timeoutMs = 10000)
    {
        packet.Id = Guid.NewGuid().ToString();
        var tcs = new TaskCompletionSource<object>();
        _pending[packet.Id] = tcs;

        try
        {
            await SendAsync(packet);

            // 超时保护：服务端异常不应让客户端永久挂起
            var completed = await Task.WhenAny(tcs.Task, Task.Delay(timeoutMs));
            if (completed != tcs.Task)
                throw new TimeoutException($"请求超时（{packet.T}），未在 {timeoutMs}ms 内收到响应");

            var result = await tcs.Task;
            return JsonSerializer.Deserialize<T>(result.ToString() ?? "");
        }
        finally
        {
            _pending.Remove(packet.Id);
        }
    }

    private async Task ReceiveLoop()
    {
        var buffer = new byte[8192];
        try
        {
            while (_ws?.State == WebSocketState.Open && _cts?.IsCancellationRequested == false)
            {
                WebSocketReceiveResult result;
                try { result = await _ws.ReceiveAsync(buffer, _cts?.Token ?? default); }
                catch { break; }

                if (result.MessageType == WebSocketMessageType.Close) break;

                var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                try { HandlePacket(json); } catch { /* 单包异常不断连接 */ }
            }
        }
        finally
        {
            // ReceiveLoop 退出意味着连接实际断开；若握手未完成，视为网络错误
            _joinTcs?.TrySetResult(JoinOutcome.NetworkError("连接意外断开"));
            _receiveLoopDone?.TrySetResult(true);
        }
    }

    /// <summary>把包体解析成 string 字典（推送事件通用小工具，容忍非字符串值）。</summary>
    private static Dictionary<string, string> ParseDict(object? payload)
    {
        if (payload == null) return new();
        try
        {
            var raw = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(payload.ToString() ?? "");
            if (raw == null) return new();
            var result = new Dictionary<string, string>();
            foreach (var kv in raw)
                result[kv.Key] = kv.Value.ValueKind == JsonValueKind.String
                    ? kv.Value.GetString() ?? ""
                    : kv.Value.ToString();
            return result;
        }
        catch { return new(); }
    }

    private void HandlePacket(string json)
    {
        var packet = JsonSerializer.Deserialize<Packet>(json);
        if (packet == null) return;

        if (!string.IsNullOrEmpty(packet.Id) && _pending.TryGetValue(packet.Id, out var tcs))
        {
            tcs.SetResult(packet.P ?? new());
            _pending.Remove(packet.Id);
            return;
        }

        switch (packet.T)
        {
            case "msg":
                var msg = JsonSerializer.Deserialize<GroupMessage>(packet.P?.ToString() ?? "");
                if (msg != null)
                {
                    OnMessageReceived?.Invoke(msg);
                    // 检测@提及
                    if (msg.MentionAll || msg.MentionedUserIds.Contains(_userId ?? ""))
                    {
                        OnMentioned?.Invoke(msg);
                    }
                }
                break;
            case "join_success":
            {
                // 握手带回房间快照（房名 + 首批频道），UI 免二次 get_rooms/get_channels
                var info = JsonSerializer.Deserialize<JoinInfo>(packet.P?.ToString() ?? "");
                if (info != null) CurrentRoomName = info.RoomName;
                _joinTcs?.TrySetResult(JoinOutcome.Success(info));
                OnJoinSuccess?.Invoke();  // 兼容旧订阅者
                break;
            }
            case "join_pending":
                _joinTcs?.TrySetResult(JoinOutcome.Pending());
                OnJoinPending?.Invoke();
                break;
            case "join_rejected":
            {
                var rejectReason = packet.P?.ToString() ?? "";
                // 区分"重复连接竞态"（暂时性）和"真正被拒"（永久性）
                var outcome = rejectReason.Contains("重复连接")
                    ? JoinOutcome.Duplicate()
                    : JoinOutcome.Rejected(rejectReason);
                _joinTcs?.TrySetResult(outcome);
                OnJoinRejected?.Invoke(rejectReason);
                break;
            }
            case "error":
            {
                var errMsg = packet.P?.ToString() ?? "服务器错误";
                _joinTcs?.TrySetResult(JoinOutcome.RoomError(errMsg));
                break;
            }
            case "ntf":
                var ntf = JsonSerializer.Deserialize<UpdateNotification>(packet.P?.ToString() ?? "");
                if (ntf != null)
                {
                    OnUpdateNotification?.Invoke(ntf);
                    HandleNotification(ntf);
                }
                break;
            case "stm":
                var stm = JsonSerializer.Deserialize<StreamData>(packet.P?.ToString() ?? "");
                if (stm != null) OnStreamReceived?.Invoke(stm);
                break;
            case "obj_sync":
                var objSync = JsonSerializer.Deserialize<ObjectSyncData>(packet.P?.ToString() ?? "");
                if (objSync != null) OnObjectSync?.Invoke(objSync);
                break;
            case "channels_changed":
                var channels = JsonSerializer.Deserialize<List<Channel>>(packet.P?.ToString() ?? "[]");
                if (channels != null) OnChannelsChanged?.Invoke(channels);
                break;
            // ===== 服务端推送事件（Discord 对齐：一事件一类型；发送侧仍用命令式 typing/presence/dice_roll）=====
            case "typing_start":
            {
                var d = ParseDict(packet.P);
                if (d.TryGetValue("UserId", out var uid))
                    OnUserTyping?.Invoke(uid, uid, d.GetValueOrDefault("ChannelId", ""));
                break;
            }
            case "typing_stop":
            {
                var d = ParseDict(packet.P);
                if (d.TryGetValue("UserId", out var uid))
                    OnUserStopTyping?.Invoke(uid, d.GetValueOrDefault("ChannelId", ""));
                break;
            }
            case "presence_update":
            {
                var d = ParseDict(packet.P);
                if (d.TryGetValue("UserId", out var uid))
                    OnPresenceUpdate?.Invoke(uid, d.GetValueOrDefault("Status", ""), d.GetValueOrDefault("ChannelId", ""));
                break;
            }
            case "dice_result":
            {
                var dice = JsonSerializer.Deserialize<DiceResult>(packet.P?.ToString() ?? "");
                if (dice != null) OnDiceResult?.Invoke(dice);
                break;
            }
            case "invite_received":
            {
                var d = ParseDict(packet.P);
                OnRoomInviteReceived?.Invoke(
                    d.GetValueOrDefault("InviterId", ""),
                    d.GetValueOrDefault("RoomId", ""),
                    d.GetValueOrDefault("ChannelId", ""));
                break;
            }
            case "join_requested":
            {
                var d = ParseDict(packet.P);
                OnJoinRequestReceived?.Invoke(
                    d.GetValueOrDefault("RequesterId", ""),
                    d.GetValueOrDefault("RoomId", ""),
                    d.GetValueOrDefault("ChannelId", ""));
                break;
            }
            case "invite_accepted":
            {
                var d = ParseDict(packet.P);
                OnInviteAccepted?.Invoke(d.GetValueOrDefault("UserId", ""));
                break;
            }
            case "invite_rejected":
            {
                var d = ParseDict(packet.P);
                OnInviteRejected?.Invoke(d.GetValueOrDefault("UserId", ""));
                break;
            }
            case "msg_edited":
            {
                // 服务端广播的消息编辑（Discord 对齐：载荷带 ChannelId 供分拣到正确频道）
                var d = ParseDict(packet.P);
                if (d.TryGetValue("MsgId", out var midStr) && long.TryParse(midStr, out var mid))
                    OnMessageModified?.Invoke(
                        d.GetValueOrDefault("ChannelId", _channelId ?? ""),
                        mid,
                        d.GetValueOrDefault("Content", ""));
                break;
            }
            case "msg_revoked":
            {
                var d = ParseDict(packet.P);
                if (d.TryGetValue("MsgId", out var midStr) && long.TryParse(midStr, out var mid))
                    OnMessageRevoked?.Invoke(d.GetValueOrDefault("ChannelId", _channelId ?? ""), mid);
                break;
            }
            case "msg_pin_changed":
            {
                var d = ParseDict(packet.P);
                if (d.TryGetValue("MsgId", out var midStr) && long.TryParse(midStr, out var mid))
                {
                    var cid = d.GetValueOrDefault("ChannelId", _channelId ?? "");
                    var isPinned = !d.TryGetValue("IsPinned", out var pStr) || pStr == "True" || pStr == "true" || pStr == "1";
                    OnPinChanged?.Invoke(mid, cid, isPinned);
                }
                break;
            }
            case "reaction_changed":
            {
                // 服务端广播的是切换后该 (msg,emoji) 的完整聚合（含最新 UserIds），客户端直接替换本地聚合。
                var ru = JsonSerializer.Deserialize<ReactionUpdate>(packet.P?.ToString() ?? "");
                if (ru != null) OnReactionChanged?.Invoke(ru);
                break;
            }
            case "member_joined":
                var joinData = JsonSerializer.Deserialize<Dictionary<string, string>>(packet.P?.ToString() ?? "");
                if (joinData != null && joinData.ContainsKey("UserId") && joinData.ContainsKey("Role"))
                    OnMemberJoined?.Invoke(joinData["UserId"], joinData["Role"]);
                break;
            case "member_left":
                var leftData = JsonSerializer.Deserialize<Dictionary<string, string>>(packet.P?.ToString() ?? "");
                if (leftData != null && leftData.ContainsKey("UserId"))
                    OnMemberLeft?.Invoke(leftData["UserId"]);
                break;
            case "batch_moved":
                var batchMoveData = JsonSerializer.Deserialize<Dictionary<string, object>>(packet.P?.ToString() ?? "");
                if (batchMoveData != null && batchMoveData.ContainsKey("MsgIds") && batchMoveData.ContainsKey("TargetTime"))
                {
                    var msgIds = JsonSerializer.Deserialize<List<long>>(batchMoveData["MsgIds"]?.ToString() ?? "[]");
                    var targetTime = DateTime.Parse(batchMoveData["TargetTime"]?.ToString() ?? "");
                    if (msgIds != null) OnBatchMoved?.Invoke(msgIds, targetTime);
                }
                break;
            case "batch_deleted":
                var batchDeleteData = JsonSerializer.Deserialize<Dictionary<string, object>>(packet.P?.ToString() ?? "");
                if (batchDeleteData != null && batchDeleteData.ContainsKey("MsgIds"))
                {
                    var msgIds = JsonSerializer.Deserialize<List<long>>(batchDeleteData["MsgIds"]?.ToString() ?? "[]");
                    if (msgIds != null) OnBatchDeleted?.Invoke(msgIds);
                }
                break;
            case "read_receipt":
                var receiptData = JsonSerializer.Deserialize<Dictionary<string, object>>(packet.P?.ToString() ?? "");
                if (receiptData != null && receiptData.ContainsKey("UserId") && receiptData.ContainsKey("MsgId"))
                {
                    var userId = receiptData["UserId"]?.ToString() ?? "";
                    var msgId = Convert.ToInt64(receiptData["MsgId"]);
                    OnMessageRead?.Invoke(userId, msgId);
                }
                break;
            case "file_progress":
                var progressData = JsonSerializer.Deserialize<Dictionary<string, object>>(packet.P?.ToString() ?? "");
                if (progressData != null && progressData.ContainsKey("FileId"))
                    OnFileUploadProgress?.Invoke(
                        progressData["FileId"].ToString() ?? "",
                        Convert.ToInt64(progressData["Uploaded"]),
                        Convert.ToInt64(progressData["Total"]));
                break;
            case "file_complete":
                var completeData = JsonSerializer.Deserialize<Dictionary<string, string>>(packet.P?.ToString() ?? "");
                if (completeData != null && completeData.ContainsKey("FileId"))
                    OnFileUploadComplete?.Invoke(completeData["FileId"]);
                break;
            case "file_failed":
                var failData = JsonSerializer.Deserialize<Dictionary<string, string>>(packet.P?.ToString() ?? "");
                if (failData != null && failData.ContainsKey("FileId"))
                    OnFileUploadFailed?.Invoke(failData["FileId"], failData.GetValueOrDefault("Error", "Unknown error"));
                break;
            // ========== 地图引擎命令同步 ==========
            case MasterIM.Models.PacketTypes.MapDelta:
                OnMapDeltaJson?.Invoke(packet.P?.ToString() ?? "{}");
                break;
            case MasterIM.Models.PacketTypes.MapFullSync:
                OnMapFullSyncJson?.Invoke(packet.P?.ToString() ?? "{}");
                break;
        }
    }

    private async Task Heartbeat()
    {
        while (_ws?.State == WebSocketState.Open)
        {
            await Task.Delay(30000);
            await SendAsync(new Packet { T = "ping" });
        }
    }

    /// <summary>
    /// 添加群组成员
    /// </summary>
    public async Task AddGroupMemberAsync(string userId, string role = "member")
    {
        await SendAsync(new Packet { T = "grp_add_member", P = new { UserId = userId, Role = role } });
    }

    /// <summary>
    /// 移除群组成员
    /// </summary>
    public async Task RemoveGroupMemberAsync(string userId)
    {
        await SendAsync(new Packet { T = "grp_remove_member", P = new { UserId = userId } });
    }

    /// <summary>
    /// 上传文件到服务器
    /// </summary>
    public async Task<FileUploadResult> UploadFileAsync(string filePath)
    {
        using var client = new HttpClient();
        using var form = new MultipartFormDataContent();
        using var fileStream = File.OpenRead(filePath);

        var fileName = Path.GetFileName(filePath);
        var fileContent = new StreamContent(fileStream);
        form.Add(fileContent, "file", fileName);

        var uploadUrl = $"{_url?.Replace("ws://", "http://").Replace("wss://", "https://")}/upload?userId={_userId}&roomId={_roomId}";
        var response = await client.PostAsync(uploadUrl, form);

        var result = await response.Content.ReadAsStringAsync();
        var json = JsonSerializer.Deserialize<Dictionary<string, object>>(result);

        return new FileUploadResult
        {
            FileId = json?["FileId"]?.ToString() ?? "",
            FileName = json?["FileName"]?.ToString() ?? "",
            FileSize = Convert.ToInt64(json?["FileSize"]),
            FileType = json?["FileType"]?.ToString() ?? "",
            Url = json?["Url"]?.ToString() ?? ""
        };
    }

    /// <summary>
    /// 发送文件消息（上传后发送消息）
    /// </summary>
    public async Task SendFileMessageAsync(FileUploadResult fileResult, string? roleId = null)
    {
        var msg = new GroupMessage
        {
            MessageType = fileResult.FileType,
            Content = JsonSerializer.Serialize(new
            {
                FileId = fileResult.FileId,
                FileName = fileResult.FileName,
                FileSize = fileResult.FileSize,
                Url = fileResult.Url
            }),
            RoleId = roleId
        };

        await SendMessageAsync(msg);
    }

    /// <summary>
    /// 发送在线状态
    /// </summary>
    public async Task SendPresenceAsync(string status)
    {
        await SendAsync(new Packet { T = "presence", P = new { Status = status } });
    }

    /// <summary>
    /// 发送正在输入状态
    /// </summary>
    /// <summary>
    /// 发送正在输入通知
    /// </summary>
    public async Task SendTypingAsync()
    {
        // Discord 对齐：带上当前频道；服务端全房广播，客户端按频道过滤显示。
        await SendAsync(new Packet
        {
            T = "typing",
            P = new { UserId = _userId, IsTyping = true, ChannelId = CurrentChannelId ?? _channelId ?? "" }
        });
    }

    /// <summary>
    /// 发送骰子投掷结果
    /// </summary>
    public async Task SendDiceRollAsync(string formula, string result, bool isSecret = false)
    {
        await SendAsync(new Packet { T = "dice_roll", P = new { Formula = formula, Result = result, IsSecret = isSecret } });
    }

    /// <summary>
    /// 发送房间邀请给指定用户
    /// </summary>
    public async Task SendRoomInviteAsync(string targetUserId)
    {
        await SendAsync(new Packet { T = "room_invite", P = new { TargetUserId = targetUserId } });
    }

    /// <summary>
    /// 请求加入指定用户的房间
    /// </summary>
    public async Task RequestJoinRoomAsync(string targetUserId)
    {
        await SendAsync(new Packet { T = "join_request", P = new { TargetUserId = targetUserId } });
    }

    /// <summary>
    /// 接受房间邀请
    /// </summary>
    public async Task AcceptInviteAsync(string inviterId)
    {
        await SendAsync(new Packet { T = "invite_accept", P = new { InviterId = inviterId } });
    }

    /// <summary>
    /// 拒绝房间邀请
    /// </summary>
    public async Task RejectInviteAsync(string inviterId)
    {
        await SendAsync(new Packet { T = "invite_reject", P = new { InviterId = inviterId } });
    }

    /// <summary>
    /// 发送自定义消息
    /// </summary>
    /// <param name="customType">自定义消息类型（必须使用 custom:、plugin: 或 mod: 前缀）</param>
    /// <param name="data">消息数据</param>
    public async Task SendCustomMessageAsync(string customType, object data)
    {
        // 验证是否是有效的自定义消息类型
        if (!PacketTypes.IsValidCustomType(customType))
        {
            throw new ArgumentException(
                "自定义消息类型必须使用 custom:、plugin: 或 mod: 前缀，" +
                "且名称只能包含小写字母、数字和下划线。" +
                $"示例: custom:my_feature, plugin:voice_chat, mod:dnd_spell",
                nameof(customType));
        }

        await SendAsync(new Packet
        {
            T = customType,
            P = data
        });
    }

    /// <summary>
    /// 获取房间成员列表
    /// </summary>
    public async Task<List<RoomMember>> GetRoomMembersAsync()
    {
        var result = await RequestAsync<List<RoomMember>>(new Packet { T = "get_members" });
        return result ?? new();
    }

    /// <summary>
    /// 更新房间成员信息
    /// </summary>
    public async Task UpdateMemberAsync(RoomMember member)
    {
        await SendAsync(new Packet { T = "update_member", P = member });
    }

    /// <summary>
    /// 禁止成员进入房间
    /// </summary>
    public async Task BanMemberAsync(string userId)
    {
        await SendAsync(new Packet { T = "ban_member", P = new { UserId = userId } });
    }

    /// <summary>
    /// 发送消息已读回执
    /// </summary>
    public async Task SendReadReceiptAsync(int pageNumber, int inPageSeq)
    {
        await SendAsync(new Packet { T = "read_receipt", P = new { PageNumber = pageNumber, InPageSeq = inPageSeq } });
    }

    /// <summary>
    /// 高级搜索（多维筛选：关键字/发送人/时间范围/消息类型 + 频道范围）。
    /// query.ChannelIds 空=当前频道；含 "*"=全部频道；否则=指定集合。
    /// </summary>
    public async Task<List<GroupMessage>> SearchMessagesAdvancedAsync(MessageSearchQuery query)
    {
        // 频道范围未指定时默认当前频道（Discord 对齐：一条连接服务整房）
        if (query.ChannelIds == null || query.ChannelIds.Count == 0)
            query.ChannelIds = new List<string> { CurrentChannelId ?? "" };

        var result = await RequestAsync<List<GroupMessage>>(new Packet
        {
            T = "search_msg",
            P = query
        });
        return result ?? new();
    }

    /// <summary>
    /// 搜索消息内容（薄封装，仅关键字）。默认搜当前频道，也可显式指定 channelId。
    /// </summary>
    public async Task<List<GroupMessage>> SearchMessagesAsync(string keyword, int limit = 50, string? channelId = null)
    {
        return await SearchMessagesAdvancedAsync(new MessageSearchQuery
        {
            Keyword = keyword,
            Limit = limit,
            ChannelIds = new List<string> { channelId ?? CurrentChannelId ?? "" }
        });
    }

    /// <summary>
    /// 围绕某条消息取上下文（搜索跳转定位用）：返回目标 + 前后各 N 条，升序。
    /// </summary>
    public async Task<List<GroupMessage>> QueryAroundMessageAsync(string channelId, long msgId, int before = 25, int after = 25)
    {
        var result = await RequestAsync<List<GroupMessage>>(new Packet
        {
            T = "query_around",
            P = new { ChannelId = channelId, MsgId = msgId, Before = before, After = after }
        });
        return result ?? new();
    }

    /// <summary>置顶或取消置顶某条消息。服务端广播 msg_pin_changed 给全房。</summary>
    public async Task PinMessageAsync(long msgId, string channelId, bool pin = true)
        => await SendAsync(new Packet { T = "pin_message", P = new { MsgId = msgId, ChannelId = channelId, Pin = pin } });

    /// <summary>获取指定频道的置顶消息列表。</summary>
    public async Task<List<GroupMessage>> GetPinnedMessagesAsync(string? channelId = null)
    {
        var cid = channelId ?? CurrentChannelId ?? "";
        var result = await RequestAsync<List<GroupMessage>>(new Packet { T = "get_pins", P = new { ChannelId = cid } });
        return result ?? new();
    }

    /// <summary>收到 msg_pin_changed 事件（某条消息的置顶状态变了）。</summary>
    public event Action<long, string, bool>? OnPinChanged;  // (msgId, channelId, isPinned)

    /// <summary>切换某条消息的某个 emoji 反应（加了则取消、没加则加）。服务端广播 reaction_changed 给全房。</summary>
    public async Task ReactAsync(long msgId, string emoji, string? channelId = null)
        => await SendAsync(new Packet { T = "react", P = new { MsgId = msgId, Emoji = emoji, ChannelId = channelId ?? CurrentChannelId ?? "" } });

    /// <summary>批量拉取一组消息的全部反应（进频道/跳转加载消息后补齐聚合）。</summary>
    public async Task<List<ReactionUpdate>> GetReactionsAsync(IEnumerable<long> msgIds, string? channelId = null)
    {
        var result = await RequestAsync<List<ReactionUpdate>>(new Packet
        {
            T = "get_reactions",
            P = new { ChannelId = channelId ?? CurrentChannelId ?? "", MsgIds = msgIds }
        });
        return result ?? new();
    }

    /// <summary>收到 reaction_changed 事件：某条消息某 emoji 的用户列表变了（全量聚合）。</summary>
    public event Action<ReactionUpdate>? OnReactionChanged;

    /// <summary>
    /// 创建房间
    /// </summary>
    public async Task CreateRoomAsync(Room room)
    {
        await SendAsync(new Packet { T = "create_room", P = room });
    }

    /// <summary>
    /// 获取房间列表
    /// </summary>
    public async Task<List<Room>> GetRoomsAsync()
    {
        var result = await RequestAsync<List<Room>>(new Packet { T = "get_rooms" });
        return result ?? new();
    }

    /// <summary>
    /// 更新房间设置
    /// </summary>
    public async Task UpdateRoomAsync(Room room)
    {
        await SendAsync(new Packet { T = "update_room", P = room });
    }

    /// <summary>
    /// 删除房间
    /// </summary>
    public async Task DeleteRoomAsync(string roomId)
    {
        await SendAsync(new Packet { T = "delete_room", P = new { RoomId = roomId } });
    }

    /// <summary>
    /// 创建频道（归属当前连接所在房间）。只需传频道名——
    /// ChannelId 由服务端权威生成，客户端不再自造弱 ID。
    /// </summary>
    public async Task CreateChannelAsync(string channelName, string? categoryName = null)
    {
        await SendAsync(new Packet
        {
            T = "create_channel",
            P = new Channel { ChannelName = channelName, CategoryName = categoryName }
        });
    }

    /// <summary>
    /// 获取当前房间的频道列表
    /// </summary>
    public async Task<List<Channel>> GetChannelsAsync()
    {
        var result = await RequestAsync<List<Channel>>(new Packet { T = "get_channels" });
        return result ?? new();
    }

    /// <summary>
    /// 更新频道
    /// </summary>
    public async Task UpdateChannelAsync(Channel channel)
    {
        await SendAsync(new Packet { T = "update_channel", P = channel });
    }

    /// <summary>
    /// 删除频道
    /// </summary>
    public async Task DeleteChannelAsync(string channelId)
    {
        await SendAsync(new Packet { T = "delete_channel", P = new { ChannelId = channelId } });
    }

    /// <summary>
    /// 频道拖拽重排：传入当前完整的有序频道ID列表，服务端持久化并广播。
    /// </summary>
    public async Task ReorderChannelsAsync(System.Collections.Generic.List<string> channelIds)
    {
        await SendAsync(new Packet { T = "reorder_channels", P = new { ChannelIds = channelIds } });
    }

    // ========== 地图引擎命令同步 ==========

    /// <summary>
    /// 向服务端发送地图命令 JSON (C→S: map:command)
    /// commandJson 格式：{"command_type":"MoveObject","params":{...},"user_id":"...","expected_version":0}
    /// </summary>
    public async Task SendMapCommandAsync(string commandJson)
    {
        // P 接受任意对象；这里用 JsonSerializer 把 JSON 字符串解析为 JsonElement
        // 再传给 Packet 让它原样序列化出去，避免双重转义
        using var doc = System.Text.Json.JsonDocument.Parse(commandJson);
        var element = doc.RootElement.Clone();
        await SendAsync(new Packet { T = MasterIM.Models.PacketTypes.MapCommand, P = element });
    }

    /// <summary>
    /// 请求地图全量同步 (C→S: map:fullsync_req)
    /// </summary>
    public async Task RequestMapFullSyncAsync()
    {
        await SendAsync(new Packet
        {
            T = MasterIM.Models.PacketTypes.MapFullSyncRequest,
            P = new { CurrentVersion = 0 }
        });
    }


    private void HandleNotification(UpdateNotification ntf)
    {
        var json = JsonSerializer.Serialize(ntf);
        var data = JsonSerializer.Deserialize<Dictionary<string, object>>(json);
        if (data == null) return;

        switch (ntf.Type)
        {
            case "message_inserted":
                if (data.ContainsKey("TimeMs"))
                    OnMessageInserted?.Invoke(Convert.ToInt64(data["TimeMs"]));
                break;
            // 注：msg_edited / msg_revoked 由服务端以顶层 T 广播（见 HandlePacket），不走 ntf。
        }
    }

    public void Dispose()
    {
        _autoReconnectEnabled = false;
        _joinTcs?.TrySetResult(JoinOutcome.Cancelled());  // 若正在握手，立即解除等待

        // 优雅关闭：先发 Close 帧，让服务端 ReceiveAsync 立刻收到关闭并移除本连接。
        // 否则服务端要等 TCP 超时才知道我们走了，期间"退出后马上重连"会命中重复连接判定被拒。
        try
        {
            if (_ws?.State == WebSocketState.Open)
            {
                _ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "client leaving", CancellationToken.None)
                   .Wait(TimeSpan.FromMilliseconds(600));
            }
        }
        catch { /* 尽力而为，关不掉也继续释放 */ }

        _cts?.Cancel();
        _ws?.Dispose();
    }
}
