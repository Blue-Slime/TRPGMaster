using System.Collections.Generic;

namespace MasterIM.Models;

/// <summary>
/// 加入房间成功后，服务端在 join_success 握手包里一次性回传的房间快照。
/// 客户端据此直接渲染房间标题与频道列表，无需额外 get_rooms / get_channels 往返。
/// </summary>
public class JoinInfo
{
    public string RoomId { get; set; } = string.Empty;
    public string RoomName { get; set; } = string.Empty;
    public string OwnerId { get; set; } = string.Empty;

    /// <summary>房间当前频道列表（已按 SortOrder 排序）。</summary>
    public List<Channel> Channels { get; set; } = new();
}
