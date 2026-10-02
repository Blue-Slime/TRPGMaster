using MapEngine.Avalonia.Services;
using MapEngine.Avalonia.ViewModels;
using MapEngine.Core.Components;
using MapEngine.Core.Data;
using Xunit;
using DoorSegmentDto = MapEngine.Core.Data.DoorSegmentData;

namespace MapEngine.Tests;

/// <summary>
/// 墙体系统验证：DTO 存档往返、门窗区间计算、闭合路径、FOV 提取。
/// 覆盖 WallPathComponent 的完整持久化路径和感知通道逻辑。
/// </summary>
public class WallPathTests
{
    private static HierarchyItemViewModel MakeWallItem(
        MainWindowViewModel vm, string id, string name,
        List<(double x, double y)> points,
        bool isClosed = false)
        => vm.BuildHierarchyItemPublic(new HierarchyNodeDto
        {
            Id = id,
            Name = name,
            Icon = "🧱",
            ObjectType = "WallPath",
            IsActive = true,
            HasMapPosition = false,
            WallPathV2 = new WallPathData
            {
                Points = points.Select(p => new PointData { X = p.x, Y = p.y }).ToList(),
                IsClosed = isClosed,
                Sight = 20,  // SenseLevel.Normal
                Move = 20,   // SenseLevel.Normal
                Color = "#FF0000",
                Thickness = 5,
            },
        });

    // ── DTO 存档往返 ─────────────────────────────────────────────────────

    [Fact]
    public void HierarchyItem_MountsWallPathFromDto()
    {
        var vm = new MainWindowViewModel();
        var item = vm.BuildHierarchyItemPublic(new HierarchyNodeDto
        {
            Id = "w1",
            Name = "房间墙体",
            ObjectType = "WallPath",
            WallPathV2 = new WallPathData
            {
                Points = new List<PointData>
                {
                    new() { X = 0, Y = 0 },
                    new() { X = 100, Y = 0 },
                    new() { X = 100, Y = 100 },
                    new() { X = 0, Y = 100 },
                },
                IsClosed = true,
                Sight = 20,  // SenseLevel.Normal
                Move = 20,   // SenseLevel.Normal
                Color = "#FF8800",
                Thickness = 8,
                Doors = new List<DoorSegmentDto>
                {
                    new()
                    {
                        StartAnchorIndex = 1,
                        EndAnchorIndex = 2,
                        Kind = 0,  // DoorKind.None
                        State = 0, // DoorState.Closed
                    },
                },
            },
        });

        var comp = item.GetComponent<WallPathComponent>();
        Assert.NotNull(comp);
        Assert.Equal(4, comp!.Points.Count);
        Assert.True(comp.IsClosed);
        Assert.Equal(SenseLevel.Normal, comp.Sight);
        Assert.Equal(SenseLevel.Normal, comp.Move);
        Assert.Equal("#FF8800", comp.Color);
        Assert.Equal(8, comp.Thickness);

        var door = Assert.Single(comp.Doors);
        Assert.Equal(1, door.StartAnchorIndex);
        Assert.Equal(2, door.EndAnchorIndex);
        Assert.Equal(DoorKind.None, door.Kind);
        Assert.Equal(DoorState.Closed, door.State);
    }

    [Fact]
    public void DtoRoundTrip_PreservesWallPathAndDoors()
    {
        var vm = new MainWindowViewModel();
        var item = vm.BuildHierarchyItemPublic(new HierarchyNodeDto
        {
            Id = "w1",
            Name = "墙体",
            ObjectType = "WallPath",
            WallPathV2 = new WallPathData
            {
                Points = new List<PointData>
                {
                    new() { X = 10, Y = 20 },
                    new() { X = 30, Y = 40 },
                    new() { X = 50, Y = 60 },
                },
                IsClosed = false,
                Sight = 20,  // SenseLevel.Normal
                Move = 0,    // SenseLevel.None
                Color = "#00FF00",
                Thickness = 3,
                Doors = new List<DoorSegmentDto>
                {
                    new()
                    {
                        StartAnchorIndex = 0,
                        EndAnchorIndex = 1,
                        Kind = 3,  // DoorKind.Window
                        State = 1, // DoorState.Open
                    },
                    new()
                    {
                        StartAnchorIndex = 1,
                        EndAnchorIndex = 2,
                        Kind = 4,  // DoorKind.Archway
                        State = 2, // DoorState.Locked
                    },
                },
            },
        });

        // 存档：VM → DTO
        var dto = vm.SnapshotHierarchyPublic(item);

        Assert.NotNull(dto.WallPathV2);
        var saved = dto.WallPathV2!.Value;
        Assert.Equal(3, saved.Points.Count);
        Assert.Equal(10, saved.Points[0].X);
        Assert.Equal(60, saved.Points[2].Y);
        Assert.False(saved.IsClosed);
        Assert.Equal(0, saved.Move);
        Assert.Equal(20, saved.Sight);
        Assert.Equal("#00FF00", saved.Color);
        Assert.Equal(3, saved.Thickness);

        Assert.Equal(2, saved.Doors!.Count);
        var door1 = saved.Doors[0];
        Assert.Equal(0, door1.StartAnchorIndex);
        Assert.Equal(1, door1.EndAnchorIndex);
        Assert.Equal(3, door1.Kind);
        Assert.Equal(1, door1.State);

        // 读档：DTO → VM，字段必须一模一样回来
        var reloaded = vm.BuildHierarchyItemPublic(dto);
        var comp = reloaded.GetComponent<WallPathComponent>()!;
        Assert.Equal(3, comp.Points.Count);
        Assert.False(comp.IsClosed);
        Assert.Equal(SenseLevel.None, comp.Move);
        Assert.Equal(SenseLevel.Normal, comp.Sight);

        Assert.Equal(2, comp.Doors.Count);
        var reloadedDoor1 = comp.Doors[0];
        Assert.Equal(DoorKind.Window, reloadedDoor1.Kind);
        Assert.Equal(DoorState.Open, reloadedDoor1.State);
    }

    // ── 门窗区间计算 ─────────────────────────────────────────────────────

    [Fact]
    public void DoorSegment_GetCoveredSegments_SingleSegment()
    {
        var door = new DoorSegment
        {
            StartAnchorIndex = 2,
            EndAnchorIndex = 3,
        };

        var covered = door.GetCoveredSegments().ToList();
        Assert.Single(covered);
        Assert.Equal(2, covered[0]);
    }

    [Fact]
    public void DoorSegment_GetCoveredSegments_MultipleSegments()
    {
        // 门从锚点 1 到锚点 4，覆盖线段 [1-2, 2-3, 3-4]
        var door = new DoorSegment
        {
            StartAnchorIndex = 1,
            EndAnchorIndex = 4,
        };

        var covered = door.GetCoveredSegments().ToList();
        Assert.Equal(3, covered.Count);
        Assert.Contains(1, covered);
        Assert.Contains(2, covered);
        Assert.Contains(3, covered);
    }

    [Fact]
    public void DoorSegment_GetCoveredSegments_ReversedIndices()
    {
        // EndAnchorIndex < StartAnchorIndex 应返回空（防御性逻辑）
        var door = new DoorSegment
        {
            StartAnchorIndex = 5,
            EndAnchorIndex = 2,
        };

        var covered = door.GetCoveredSegments().ToList();
        Assert.Empty(covered);
    }

    // ── 闭合路径检测 ─────────────────────────────────────────────────────

    [Fact]
    public void WallPath_IsClosed_AffectsRendering()
    {
        var vm = new MainWindowViewModel();
        var item = MakeWallItem(vm, "w1", "开放路径",
            new List<(double, double)> { (0, 0), (100, 0), (100, 100) },
            isClosed: false);

        var comp = item.GetComponent<WallPathComponent>()!;
        Assert.False(comp.IsClosed);

        // 设置为闭合
        comp.IsClosed = true;
        Assert.True(comp.IsClosed);
    }

    // ── FOV 线段提取 ─────────────────────────────────────────────────────

    [Fact]
    public void WallPath_ExtractFOVSegments_AllClosed()
    {
        var vm = new MainWindowViewModel();
        var item = MakeWallItem(vm, "w1", "实墙",
            new List<(double, double)> { (0, 0), (100, 0), (100, 100) });

        var comp = item.GetComponent<WallPathComponent>()!;
        comp.Sight = SenseLevel.Normal;

        // 无门窗，所有线段都阻挡视线
        var segments = ExtractSegmentsForFOV(comp);
        Assert.Equal(2, segments.Count); // 3个点 → 2条线段
    }

    [Fact]
    public void WallPath_ExtractFOVSegments_OpenDoorDoesNotBlock()
    {
        var vm = new MainWindowViewModel();
        var item = MakeWallItem(vm, "w1", "带门墙",
            new List<(double, double)> { (0, 0), (100, 0), (200, 0) });

        var comp = item.GetComponent<WallPathComponent>()!;
        comp.Sight = SenseLevel.Normal;
        comp.Doors.Add(new DoorSegment
        {
            StartAnchorIndex = 1,
            EndAnchorIndex = 2,
            Kind = DoorKind.Door,
            State = DoorState.Open, // 开门不阻挡
        });

        var segments = ExtractSegmentsForFOV(comp);
        // 线段 0: (0,0)→(100,0) 正常
        // 线段 1: (100,0)→(200,0) 被开门覆盖，不阻挡
        Assert.Single(segments);
    }

    [Fact]
    public void WallPath_ExtractFOVSegments_ClosedDoorBlocks()
    {
        var vm = new MainWindowViewModel();
        var item = MakeWallItem(vm, "w1", "关门墙",
            new List<(double, double)> { (0, 0), (100, 0), (200, 0) });

        var comp = item.GetComponent<WallPathComponent>()!;
        comp.Sight = SenseLevel.Normal;
        comp.Doors.Add(new DoorSegment
        {
            StartAnchorIndex = 1,
            EndAnchorIndex = 2,
            Kind = DoorKind.Door,
            State = DoorState.Closed, // 关门阻挡
        });

        var segments = ExtractSegmentsForFOV(comp);
        Assert.Equal(2, segments.Count); // 两条线段都阻挡
    }

    [Fact]
    public void WallPath_ExtractFOVSegments_WindowDoesNotBlock()
    {
        var vm = new MainWindowViewModel();
        var item = MakeWallItem(vm, "w1", "窗墙",
            new List<(double, double)> { (0, 0), (100, 0), (200, 0) });

        var comp = item.GetComponent<WallPathComponent>()!;
        comp.Sight = SenseLevel.Normal;
        comp.Doors.Add(new DoorSegment
        {
            StartAnchorIndex = 1,
            EndAnchorIndex = 2,
            Kind = DoorKind.Window,
            State = DoorState.Closed, // 窗户即使关闭也不阻挡视线
        });

        var segments = ExtractSegmentsForFOV(comp);
        Assert.Single(segments); // 线段 0 阻挡，线段 1（窗户）不阻挡
    }

    [Fact]
    public void WallPath_ExtractFOVSegments_LockedDoorBlocks()
    {
        var vm = new MainWindowViewModel();
        var item = MakeWallItem(vm, "w1", "锁门墙",
            new List<(double, double)> { (0, 0), (100, 0), (200, 0) });

        var comp = item.GetComponent<WallPathComponent>()!;
        comp.Sight = SenseLevel.Normal;
        comp.Doors.Add(new DoorSegment
        {
            StartAnchorIndex = 1,
            EndAnchorIndex = 2,
            Kind = DoorKind.Door,
            State = DoorState.Locked, // 锁定门阻挡
        });

        var segments = ExtractSegmentsForFOV(comp);
        Assert.Equal(2, segments.Count);
    }

    [Fact]
    public void WallPath_ExtractFOVSegments_NoneSightLevel()
    {
        var vm = new MainWindowViewModel();
        var item = MakeWallItem(vm, "w1", "单向阻挡",
            new List<(double, double)> { (0, 0), (100, 0) });

        var comp = item.GetComponent<WallPathComponent>()!;
        comp.Sight = SenseLevel.None; // 不阻挡视线
        comp.Move = SenseLevel.Normal;

        var segments = ExtractSegmentsForFOV(comp);
        Assert.Empty(segments); // 不阻挡视线时，FOV 算法应忽略
    }

    // ── 辅助方法：提取 FOV 线段（模拟 FOV 算法调用逻辑）────────────────

    private List<(double x1, double y1, double x2, double y2)> ExtractSegmentsForFOV(WallPathComponent wall)
    {
        var segments = new List<(double, double, double, double)>();

        if (wall.Sight == SenseLevel.None)
            return segments;

        for (int i = 0; i < wall.Points.Count - 1; i++)
        {
            var p1 = wall.Points[i];
            var p2 = wall.Points[i + 1];

            var door = wall.Doors.FirstOrDefault(d => d.GetCoveredSegments().Contains(i));

            if (door != null)
            {
                // 门窗区间 - check if this door blocks vision
                bool blocks = door.State switch
                {
                    DoorState.Open => false,
                    DoorState.Closed => door.Kind != DoorKind.Window && door.Kind != DoorKind.Archway,
                    DoorState.Locked => true,
                    _ => true,
                };

                if (blocks)
                    segments.Add((p1.X, p1.Y, p2.X, p2.Y));
            }
            else
            {
                // 普通墙体
                segments.Add((p1.X, p1.Y, p2.X, p2.Y));
            }
        }

        // 闭合路径需要添加最后一条边
        if (wall.IsClosed && wall.Points.Count >= 2)
        {
            var first = wall.Points[0];
            var last = wall.Points[^1];
            segments.Add((last.X, last.Y, first.X, first.Y));
        }

        return segments;
    }
}
