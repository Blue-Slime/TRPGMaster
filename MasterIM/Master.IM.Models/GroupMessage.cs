using System;
using System.Collections.Generic;

namespace MasterIM.Models;

/// <summary>
/// 群组消息（v2.0 - MsgId 包含时间戳 + DisplayTime）
/// </summary>
public class GroupMessage
{
    /// <summary>
    /// 消息ID（包含时间戳的 int64）
    /// 结构：42位时间戳 + 22位序列号
    /// </summary>
    public long MsgId { get; set; }

    /// <summary>
    /// 发送时间（从 MsgId 解析，物理创建时间）
    /// </summary>
    public DateTime SendTime => MsgIdToDateTime(MsgId);

    /// <summary>
    /// 显示时间（逻辑显示时间，可调整用于移动）
    /// </summary>
    public DateTime DisplayTime { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 最后修改时间（独立字段，用于增量同步）
    /// </summary>
    public DateTime LastModified { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 版本号（保留但不用于同步逻辑）
    /// </summary>
    public int Version { get; set; } = 1;

    /// <summary>
    /// 所属频道ID（用于多频道路由：广播携带、客户端按此分拣到对应频道）
    /// </summary>
    public string ChannelId { get; set; } = string.Empty;

    /// <summary>
    /// 发送者ID
    /// </summary>
    public string SenderId { get; set; } = string.Empty;

    /// <summary>
    /// 消息内容
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// 引用的消息ID
    /// </summary>
    public long? ReplyToMsgId { get; set; }

    /// <summary>
    /// 引用的消息内容（冗余存储）
    /// </summary>
    public string? QuotedContent { get; set; }

    /// <summary>
    /// 角色扮演ID
    /// </summary>
    public string? RoleId { get; set; }

    /// <summary>
    /// 消息类型
    /// </summary>
    public string MessageType { get; set; } = "text";

    /// <summary>
    /// @提及的用户ID列表
    /// </summary>
    public List<string> MentionedUserIds { get; set; } = new();

    /// <summary>
    /// 是否@所有人
    /// </summary>
    public bool MentionAll { get; set; } = false;

    /// <summary>软删除标记</summary>
    public bool IsDeleted { get; set; } = false;

    /// <summary>是否置顶（房间内所有人可见的重要消息标记，对标 Discord 置顶）。</summary>
    public bool IsPinned { get; set; } = false;

    #region 时间戳转换

    // 基准时间（2024-01-01 00:00:00 UTC）
    private static readonly DateTime Epoch = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private const int SequenceBits = 22;

    /// <summary>
    /// 从 MsgId 提取发送时间
    /// </summary>
    public static DateTime MsgIdToDateTime(long msgId)
    {
        var timestamp = msgId >> SequenceBits;  // 提取前42位
        return Epoch.AddMilliseconds(timestamp);
    }

    /// <summary>
    /// 从时间戳生成 MsgId 的时间部分
    /// </summary>
    public static long DateTimeToMsgIdBase(DateTime time)
    {
        var timestamp = (long)(time.ToUniversalTime() - Epoch).TotalMilliseconds;
        return timestamp << SequenceBits;
    }

    #endregion
}
