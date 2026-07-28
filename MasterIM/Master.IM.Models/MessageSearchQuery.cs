using System;
using System.Collections.Generic;

namespace MasterIM.Models;

/// <summary>
/// 高级消息搜索条件（对标 QQ 聊天记录搜索的多维筛选）。
/// 所有可选维度为空表示不限制该维度。
/// </summary>
public class MessageSearchQuery
{
    /// <summary>关键字（内容 LIKE 匹配）。可空=不按关键字过滤。</summary>
    public string Keyword { get; set; } = string.Empty;

    /// <summary>
    /// 搜索的频道范围。空=仅当前连接频道；含哨兵 "*" =全部频道；
    /// 否则=指定的频道集合。
    /// </summary>
    public List<string> ChannelIds { get; set; } = new();

    /// <summary>按发送人过滤。可空=不限发送人。</summary>
    public string? SenderId { get; set; }

    /// <summary>起始时间（含）。可空=不限起点。</summary>
    public DateTime? StartTime { get; set; }

    /// <summary>结束时间（含）。可空=不限终点。</summary>
    public DateTime? EndTime { get; set; }

    /// <summary>消息类型过滤（text/image/file/dice/system）。可空=不限类型。</summary>
    public string? MessageType { get; set; }

    /// <summary>结果上限。</summary>
    public int Limit { get; set; } = 100;

    /// <summary>全频道搜索哨兵值。</summary>
    public const string AllChannels = "*";
}
