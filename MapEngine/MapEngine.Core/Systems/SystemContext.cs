using MapEngine.Core.Spatial;

namespace MapEngine.Core.Systems;

/// <summary>
/// System 执行时的上下文。携带 World、本轮触发的脏标记与脏区域,
/// 以及一个可选的时间步长(用于未来的动画/物理 System)。
/// </summary>
public sealed class SystemContext
{
    public SystemContext(World world, DirtyFlags triggeredFlags, RectD? dirtyRegion, double deltaSeconds)
    {
        World = world;
        TriggeredFlags = triggeredFlags;
        DirtyRegion = dirtyRegion;
        DeltaSeconds = deltaSeconds;
    }

    public World World { get; }

    /// <summary>本轮 Scheduler 启动时 World 累积的脏标记快照。</summary>
    public DirtyFlags TriggeredFlags { get; }

    /// <summary>本轮累积的脏区域(可能为 null = 全量重算)。</summary>
    public RectD? DirtyRegion { get; }

    /// <summary>距上一轮的时间(秒)。事件驱动重算时为 0。</summary>
    public double DeltaSeconds { get; }
}
