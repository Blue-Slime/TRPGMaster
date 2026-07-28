namespace MasterClient.Models;

/// <summary>
/// 公告信息
/// </summary>
public class AnnouncementInfo
{
    public int Id { get; set; }
    public string Tag { get; set; } = "公告";
    public string TagColor { get; set; } = "#2d4a6a";  // 默认深蓝色
    public string Title { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;  // 详情内容
    public DateTime PublishDate { get; set; } = DateTime.Now;
    public AnnouncementType Type { get; set; }
    public bool IsImportant { get; set; }
    public string Author { get; set; } = "系统";  // 发布者

    public string DateDisplay => PublishDate.ToString("MM-dd");
    public string FullDateDisplay => PublishDate.ToString("yyyy-MM-dd HH:mm");
}

public enum AnnouncementType
{
    Notice,      // 公告
    Update,      // 更新
    Event,       // 活动
    Maintenance  // 维护
}
