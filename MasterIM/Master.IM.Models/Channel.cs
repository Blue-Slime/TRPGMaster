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
}
