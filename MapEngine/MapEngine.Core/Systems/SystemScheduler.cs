namespace MapEngine.Core.Systems;

/// <summary>
/// 系统调度器。按 Order 升序运行注册的 System。
///
/// 两种驱动:
/// - Tick(delta):帧驱动,跑所有 System(用于连续动画;当前引擎以事件驱动为主,少用)。
/// - RunDirty():事件驱动增量重算 —— 只跑 Interest 与 World.Dirty 有交集的 System,
///   跑完清空 World 的脏状态。这是 P2 增量架构的执行核心。
/// </summary>
public sealed class SystemScheduler
{
    private readonly List<ISystem> _systems = [];
    private bool _sorted = true;

    public IReadOnlyList<ISystem> Systems => _systems;

    public void Register(ISystem system)
    {
        ArgumentNullException.ThrowIfNull(system);
        _systems.Add(system);
        _sorted = false;
    }

    public bool Unregister(ISystem system) => _systems.Remove(system);

    /// <summary>帧驱动:无条件跑全部 System(按 Order)。</summary>
    public void Tick(World world, double deltaSeconds)
    {
        EnsureSorted();
        var ctx = new SystemContext(world, world.Dirty, world.DirtyRegion, deltaSeconds);
        foreach (var s in _systems)
            s.Execute(ctx);
        world.ClearDirty();
    }

    /// <summary>
    /// 事件驱动:只跑关心当前脏标记的 System,跑完清脏。
    /// World 无脏时直接返回,零开销。返回本轮实际执行的 System 数。
    /// </summary>
    public int RunDirty(World world)
    {
        if (world.Dirty == DirtyFlags.None) return 0;

        EnsureSorted();
        var ctx = new SystemContext(world, world.Dirty, world.DirtyRegion, 0.0);

        int ran = 0;
        foreach (var s in _systems)
        {
            if ((s.Interest & ctx.TriggeredFlags) != DirtyFlags.None)
            {
                s.Execute(ctx);
                ran++;
            }
        }
        world.ClearDirty();
        return ran;
    }

    private void EnsureSorted()
    {
        if (_sorted) return;
        _systems.Sort((a, b) => a.Order.CompareTo(b.Order));
        _sorted = true;
    }
}
