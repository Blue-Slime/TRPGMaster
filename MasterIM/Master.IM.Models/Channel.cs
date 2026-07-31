using System;

namespace MasterIM.Models;

public class Channel
{
    public string ChannelId { get; set; } = string.Empty;
    public string RoomId { get; set; } = string.Empty;
    public string ChannelName { get; set; } = string.Empty;
    public DateTime CreateTime { get; set; } = DateTime.UtcNow;

    /// <summary>频道排序序号（拖拽排序用，越小越靠前）</summary>
    public int SortOrder { get; set; } = 0;

    /// <summary>
    /// 所属分类名称。null 或空字符串 = 默认分类"文字频道"。
    /// 客户端展示层按此字段分组折叠，服务端只负责持久化。
    /// </summary>
    public string? CategoryName { get; set; }
}
