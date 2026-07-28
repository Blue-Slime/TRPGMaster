namespace MapEngine.Avalonia.Services;

public sealed class VisionConeDto
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = "视野锥";

    /// <summary>
    /// 中心偏转角度（相对朝向的偏移，-180 到 +180）
    /// </summary>
    public double CenterOffset { get; set; } = 0;

    /// <summary>
    /// 视距（格子数）
    /// </summary>
    public double Range { get; set; } = 12;

    /// <summary>
    /// 视场角度（FOV，0-360）
    /// </summary>
    public double FieldOfView { get; set; } = 90;

    /// <summary>
    /// 是否启用
    /// </summary>
    public bool IsEnabled { get; set; } = true;
}
