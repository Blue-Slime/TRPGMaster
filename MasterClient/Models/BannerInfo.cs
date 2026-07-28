namespace MasterClient.Models;

/// <summary>
/// 海报/公告信息
/// </summary>
public class BannerInfo
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Subtitle { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Tag { get; set; } = string.Empty;
    public string Icon { get; set; } = "🎲";
    public BannerType Type { get; set; }
    public string ActionText { get; set; } = "了解更多";
    public string ActionUrl { get; set; } = string.Empty;
    public BannerGradient Gradient { get; set; } = new();
}

public enum BannerType
{
    Announcement,   // 公告
    Update,         // 更新
    Event,          // 活动
    Feature         // 功能介绍
}

public class BannerGradient
{
    public string StartColor { get; set; } = "#4c1d95";
    public string MiddleColor { get; set; } = "#7c3aed";
    public string EndColor { get; set; } = "#a78bfa";
}
