using MapEngine.Core;
using MapEngine.Core.Components;
using MapEngine.Core.Spatial;
using MapEngine.Core.Systems;
using Xunit;

namespace MapEditor.Tests;

/// <summary>
/// P0 ECS-lite 骨架测试:World 容器、统一登记、空间索引、dirty 追踪、System 调度。
/// </summary>
public class WorldSkeletonTests
{
    private static GameObject Wall(double x1, double y1, double x2, double y2)
    {
        var go = new GameObject { Name = "Wall" };
        go.AddComponent(new WallComponent { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2 });
        return go;
    }

    [Fact]
    public void AddObject_RegistersInIndexAndSpatial()
    {
        var world = new World();
        var wall = Wall(0, 0, 100, 0);

        world.AddObject(wall);

        Assert.Equal(1, world.ObjectCount);
        Assert.Same(wall, world.FindById(wall.Id));
        Assert.Same(world, wall.World);
        Assert.Equal(1, world.Spatial.Count);
    }

    [Fact]
    public void AddObject_WithChildren_RegistersWholeSubtree()
    {
        var world = new World();
        var parent = new GameObject { Name = "Floor" };
        var child = Wall(0, 0, 50, 0);
        parent.Children.Add(child);
        child.Parent = parent;

        world.AddObject(parent);

        Assert.Equal(2, world.ObjectCount);
        Assert.NotNull(world.FindById(child.Id));
        Assert.Equal(1, world.Spatial.Count); // 只有墙有空间特征
    }

    [Fact]
    public void RemoveObject_UnregistersSubtree()
    {
        var world = new World();
        var parent = new GameObject { Name = "Floor" };
        var child = Wall(0, 0, 50, 0);
        parent.Children.Add(child);
        child.Parent = parent;
        world.AddObject(parent);

        world.RemoveObject(parent);

        Assert.Equal(0, world.ObjectCount);
        Assert.Equal(0, world.Spatial.Count);
        Assert.Null(child.World);
    }

    [Fact]
    public void AddObject_MarksHierarchySpatialVisionDirty()
    {
        var world = new World();
        world.ClearDirty();

        world.AddObject(Wall(0, 0, 100, 0));

        Assert.True(world.Dirty.HasFlag(DirtyFlags.Hierarchy));
        Assert.True(world.Dirty.HasFlag(DirtyFlags.Spatial));
        Assert.True(world.Dirty.HasFlag(DirtyFlags.Vision));
    }

    [Fact]
    public void SpatialQuery_ReturnsIntersectingObjects()
    {
        var world = new World();
        var near = Wall(0, 0, 10, 0);
        var far = Wall(1000, 1000, 1010, 1000);
        world.AddObject(near);
        world.AddObject(far);

        var hits = world.Spatial.Query(new RectD(-5, -5, 20, 20)).ToList();

        Assert.Contains(near, hits);
        Assert.DoesNotContain(far, hits);
    }

    [Fact]
    public void NotifySpatialChanged_UpdatesIndex()
    {
        var world = new World();
        var wall = Wall(0, 0, 10, 0);
        world.AddObject(wall);

        // 把墙搬到远处
        var wc = wall.GetComponent<WallComponent>()!;
        wc.X1 = 1000; wc.Y1 = 1000; wc.X2 = 1010; wc.Y2 = 1000;
        world.NotifySpatialChanged(wall);

        Assert.Empty(world.Spatial.Query(new RectD(-5, -5, 20, 20)));
        Assert.Single(world.Spatial.Query(new RectD(990, 990, 1020, 1020)));
    }

    [Fact]
    public void Scheduler_RunDirty_OnlyRunsInterestedSystems()
    {
        var world = new World();
        var visionSys = new CountingSystem("Vision", DirtyFlags.Vision);
        var renderSys = new CountingSystem("Render", DirtyFlags.Render);
        var scheduler = new SystemScheduler();
        scheduler.Register(visionSys);
        scheduler.Register(renderSys);

        world.ClearDirty();
        world.MarkDirty(DirtyFlags.Vision);
        int ran = scheduler.RunDirty(world);

        Assert.Equal(1, ran);
        Assert.Equal(1, visionSys.RunCount);
        Assert.Equal(0, renderSys.RunCount);
        Assert.Equal(DirtyFlags.None, world.Dirty); // 跑完清脏
    }

    [Fact]
    public void Scheduler_RunDirty_NoDirty_RunsNothing()
    {
        var world = new World();
        var sys = new CountingSystem("X", DirtyFlags.All);
        var scheduler = new SystemScheduler();
        scheduler.Register(sys);

        world.ClearDirty();
        int ran = scheduler.RunDirty(world);

        Assert.Equal(0, ran);
        Assert.Equal(0, sys.RunCount);
    }

    [Fact]
    public void Scheduler_RespectsOrder()
    {
        var world = new World();
        var log = new List<string>();
        var scheduler = new SystemScheduler();
        scheduler.Register(new OrderedSystem("second", 10, log));
        scheduler.Register(new OrderedSystem("first", 1, log));

        world.MarkDirty(DirtyFlags.All);
        scheduler.RunDirty(world);

        Assert.Equal(new[] { "first", "second" }, log);
    }

    private sealed class CountingSystem(string name, DirtyFlags interest) : ISystem
    {
        public string Name { get; } = name;
        public DirtyFlags Interest { get; } = interest;
        public int RunCount { get; private set; }
        public void Execute(SystemContext ctx) => RunCount++;
    }

    private sealed class OrderedSystem(string name, int order, List<string> log) : ISystem
    {
        public string Name { get; } = name;
        public DirtyFlags Interest => DirtyFlags.All;
        public int Order { get; } = order;
        public void Execute(SystemContext ctx) => log.Add(Name);
    }
}
