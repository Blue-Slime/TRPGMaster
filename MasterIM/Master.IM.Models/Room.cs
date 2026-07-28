using System;

namespace MasterIM.Models;

public class Room
{
    public string RoomId { get; set; } = string.Empty;
    public string RoomName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string OwnerId { get; set; } = string.Empty;
    public string HostUserId { get; set; } = string.Empty;  // 房主ID（别名，兼容新代码）
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool IsPublic { get; set; } = true;
    public bool RequireApproval { get; set; } = false;  // 是否需要房主批准加入

    public Room()
    {
        // 保持 HostUserId 和 OwnerId 同步
        HostUserId = OwnerId;
    }
}

