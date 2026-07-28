using System.Collections.Generic;

namespace MasterIM.Models;

/// <summary>
/// 一条消息上某个 emoji 的反应聚合（对标 Discord reaction）。
/// 服务端在反应变更时广播、历史加载时批量返回；客户端据此渲染反应条。
/// Count = UserIds.Count；当前用户是否已反应 = UserIds.Contains(自己)。
/// </summary>
public class ReactionUpdate
{
    public long MsgId { get; set; }
    public string ChannelId { get; set; } = string.Empty;
    public string Emoji { get; set; } = string.Empty;

    /// <summary>对该 emoji 反应过的用户 ID 列表（去重）。空列表表示该 emoji 已无人反应，客户端应移除该反应条。</summary>
    public List<string> UserIds { get; set; } = new();
}
