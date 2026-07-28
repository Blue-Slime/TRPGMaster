using System;

namespace MasterIM.Models;

/// <summary>
/// 消息类型常量定义
/// </summary>
public static class PacketTypes
{
    // ========== 系统保留类型（核心消息）==========

    /// <summary>消息发送/接收</summary>
    public const string Message = "msg";

    /// <summary>消息查询</summary>
    public const string Query = "qry";

    /// <summary>消息修改</summary>
    public const string Modify = "mod";

    /// <summary>消息删除</summary>
    public const string Delete = "del";

    // ========== 批量操作 ==========

    /// <summary>批量移动消息</summary>
    public const string BatchMove = "bmv";

    /// <summary>批量删除消息</summary>
    public const string BatchDelete = "bdl";

    // ========== 对象同步 ==========

    /// <summary>创建游戏对象</summary>
    public const string ObjectCreate = "obj_create";

    /// <summary>更新游戏对象</summary>
    public const string ObjectUpdate = "obj_update";

    /// <summary>删除游戏对象</summary>
    public const string ObjectDelete = "obj_delete";

    /// <summary>查询游戏对象</summary>
    public const string ObjectQuery = "obj_query";

    /// <summary>对象同步</summary>
    public const string ObjectSync = "obj_sync";

    // ========== 地图引擎命令同步 ==========

    /// <summary>地图命令请求 (C→S)</summary>
    public const string MapCommand = "map:command";

    /// <summary>地图状态增量广播 (S→C broadcast)</summary>
    public const string MapDelta = "map:delta";

    /// <summary>地图全量同步请求 (C→S)</summary>
    public const string MapFullSyncRequest = "map:fullsync_req";

    /// <summary>地图全量同步响应 (S→C)</summary>
    public const string MapFullSync = "map:fullsync";

    // ========== 房间管理 ==========

    /// <summary>创建房间</summary>
    public const string CreateRoom = "create_room";

    /// <summary>更新房间</summary>
    public const string UpdateRoom = "update_room";

    /// <summary>删除房间</summary>
    public const string DeleteRoom = "delete_room";

    /// <summary>获取房间列表</summary>
    public const string GetRooms = "get_rooms";

    // ========== 频道管理 ==========

    /// <summary>创建频道</summary>
    public const string CreateChannel = "create_channel";

    /// <summary>更新频道</summary>
    public const string UpdateChannel = "update_channel";

    /// <summary>删除频道</summary>
    public const string DeleteChannel = "delete_channel";

    /// <summary>获取频道列表</summary>
    public const string GetChannels = "get_channels";

    /// <summary>频道列表变更通知</summary>
    public const string ChannelsChanged = "channels_changed";

    // ========== 成员管理 ==========

    /// <summary>添加群组成员</summary>
    public const string AddMember = "grp_add_member";

    /// <summary>移除群组成员</summary>
    public const string RemoveMember = "grp_remove_member";

    /// <summary>获取成员列表</summary>
    public const string GetMembers = "get_members";

    /// <summary>更新成员信息</summary>
    public const string UpdateMember = "update_member";

    /// <summary>禁止成员</summary>
    public const string BanMember = "ban_member";

    // ========== 房间加入 ==========

    /// <summary>批准加入请求</summary>
    public const string ApproveJoin = "approve_join";

    /// <summary>拒绝加入请求</summary>
    public const string RejectJoin = "reject_join";

    /// <summary>加入请求</summary>
    public const string JoinRequest = "join_request";

    /// <summary>加入成功</summary>
    public const string JoinSuccess = "join_success";

    /// <summary>等待批准</summary>
    public const string JoinPending = "join_pending";

    /// <summary>加入被拒绝</summary>
    public const string JoinRejected = "join_rejected";

    /// <summary>成员加入</summary>
    public const string MemberJoined = "member_joined";

    // ========== TRPG功能 ==========

    /// <summary>在线状态</summary>
    public const string Presence = "presence";

    /// <summary>正在输入</summary>
    public const string Typing = "typing";

    /// <summary>骰子投掷</summary>
    public const string DiceRoll = "dice_roll";

    /// <summary>检定请求</summary>
    public const string CheckRequest = "check_request";

    /// <summary>检定响应</summary>
    public const string CheckResponse = "check_response";

    /// <summary>检定结果</summary>
    public const string CheckResult = "check_result";

    /// <summary>房间邀请</summary>
    public const string RoomInvite = "room_invite";

    /// <summary>加入请求（旧）</summary>
    public const string JoinRequestOld = "join_request";

    /// <summary>接受邀请</summary>
    public const string InviteAccept = "invite_accept";

    /// <summary>拒绝邀请</summary>
    public const string InviteReject = "invite_reject";

    // ========== 流式数据 ==========

    /// <summary>流式数据传输</summary>
    public const string Stream = "stm";

    // ========== 搜索 ==========

    /// <summary>消息搜索</summary>
    public const string SearchMessage = "search_msg";

    /// <summary>增量查询</summary>
    public const string QueryIncremental = "qry_incremental";

    // ========== 已读回执 ==========

    /// <summary>已读回执</summary>
    public const string ReadReceipt = "read_receipt";

    // ========== 系统消息 ==========

    /// <summary>心跳检测</summary>
    public const string Ping = "ping";

    /// <summary>心跳响应</summary>
    public const string Pong = "pong";

    /// <summary>错误消息</summary>
    public const string Error = "error";

    // ========== 服务端推送事件（Discord 对齐：一事件一类型，非请求）==========

    /// <summary>开始输入（服务端推送）</summary>
    public const string TypingStart = "typing_start";

    /// <summary>停止输入（服务端推送）</summary>
    public const string TypingStop = "typing_stop";

    /// <summary>在线状态变更（服务端推送）</summary>
    public const string PresenceUpdate = "presence_update";

    /// <summary>骰子结果（服务端推送）</summary>
    public const string DiceResult = "dice_result";

    /// <summary>收到房间邀请（服务端推送给目标用户）</summary>
    public const string InviteReceived = "invite_received";

    /// <summary>收到加入请求（服务端推送给房主）</summary>
    public const string JoinRequested = "join_requested";

    /// <summary>邀请被接受（服务端推送给邀请者）</summary>
    public const string InviteAccepted = "invite_accepted";

    /// <summary>邀请被拒绝（服务端推送给邀请者）</summary>
    public const string InviteRejected = "invite_rejected";

    // ========== 自定义消息命名空间前缀 ==========

    /// <summary>
    /// 自定义消息前缀（客户端自定义功能）
    /// <para>示例: "custom:dice_roll_special"</para>
    /// <para>用途: 用户自定义的特殊功能</para>
    /// </summary>
    public const string CustomPrefix = "custom:";

    /// <summary>
    /// 插件消息前缀（第三方插件）
    /// <para>示例: "plugin:voice_chat"</para>
    /// <para>用途: 第三方开发的插件功能</para>
    /// </summary>
    public const string PluginPrefix = "plugin:";

    /// <summary>
    /// 模组消息前缀（特定游戏系统）
    /// <para>示例: "mod:cthulhu_sanity"</para>
    /// <para>用途: 特定TRPG系统的专有机制</para>
    /// </summary>
    public const string ModPrefix = "mod:";

    // ========== 辅助方法 ==========

    /// <summary>
    /// 检查是否是自定义消息类型
    /// </summary>
    public static bool IsCustomType(string messageType)
    {
        if (string.IsNullOrEmpty(messageType))
            return false;

        return messageType.StartsWith(CustomPrefix) ||
               messageType.StartsWith(PluginPrefix) ||
               messageType.StartsWith(ModPrefix);
    }

    /// <summary>
    /// 验证自定义消息类型格式是否有效
    /// <para>规则: [prefix]:[name]</para>
    /// <para>前缀: custom、plugin、mod</para>
    /// <para>名称: 小写字母、数字、下划线</para>
    /// </summary>
    public static bool IsValidCustomType(string messageType)
    {
        if (!IsCustomType(messageType))
            return false;

        // 验证格式：前缀:名称，名称只能包含小写字母、数字、下划线
        var pattern = @"^(custom|plugin|mod):[a-z0-9_]+$";
        return System.Text.RegularExpressions.Regex.IsMatch(messageType, pattern);
    }

    /// <summary>
    /// 获取自定义消息类型的前缀
    /// </summary>
    public static string? GetCustomPrefix(string messageType)
    {
        if (messageType.StartsWith(CustomPrefix))
            return CustomPrefix;
        if (messageType.StartsWith(PluginPrefix))
            return PluginPrefix;
        if (messageType.StartsWith(ModPrefix))
            return ModPrefix;
        return null;
    }

    /// <summary>
    /// 获取自定义消息类型的名称部分（去除前缀）
    /// </summary>
    public static string? GetCustomName(string messageType)
    {
        var prefix = GetCustomPrefix(messageType);
        if (prefix == null)
            return null;

        return messageType.Substring(prefix.Length);
    }
}
