using MapEngine.Core;
using MapEngine.Core.Components;
using MapEngine.Core.Systems;
using Xunit;

namespace MapEngine.Tests;

public class WallSegmentExtractorTests
{
    [Fact]
    public void ExtractFOVSegments_ClosedDoorBlocksSight()
    {
        // Arrange
        var world = new World();
        var roomWall = new GameObject();
        var wallPath = new WallPathComponent
        {
            Points = new List<(double, double)>
            {
                (0, 0), (100, 0), (150, 0), (210, 0), (300, 0)
            },
            IsClosed = false,
            Sight = SenseLevel.Normal
        };

        // 添加关闭的门（锚点2→3）
        wallPath.Doors.Add(new DoorSegment
        {
            StartAnchorIndex = 2,
            EndAnchorIndex = 3,
            Kind = DoorKind.Door,
            State = DoorState.Closed
        });

        roomWall.AddComponent(wallPath);
        world.AddObject(roomWall);

        // Act
        var segments = WallSegmentExtractor.ExtractFOVSegments(world, (150, 150), 500);

        // Assert: 4条线段，门所在线段应该阻挡
        Assert.Equal(4, segments.Count);
    }

    [Fact]
    public void ExtractFOVSegments_OpenDoorDoesNotBlockSight()
    {
        // Arrange
        var world = new World();
        var roomWall = new GameObject();
        var wallPath = new WallPathComponent
        {
            Points = new List<(double, double)>
            {
                (0, 0), (100, 0), (150, 0), (210, 0), (300, 0)
            },
            IsClosed = false,
            Sight = SenseLevel.Normal
        };

        // 添加开启的门
        wallPath.Doors.Add(new DoorSegment
        {
            StartAnchorIndex = 2,
            EndAnchorIndex = 3,
            Kind = DoorKind.Door,
            State = DoorState.Open
        });

        roomWall.AddComponent(wallPath);
        world.AddObject(roomWall);

        // Act
        var segments = WallSegmentExtractor.ExtractFOVSegments(world, (150, 150), 500);

        // Assert: 3条线段（门线段不应该出现）
        Assert.Equal(3, segments.Count);
    }

    [Fact]
    public void ExtractFOVSegments_WindowDoesNotBlockSight()
    {
        // Arrange
        var world = new World();
        var roomWall = new GameObject();
        var wallPath = new WallPathComponent
        {
            Points = new List<(double, double)>
            {
                (0, 0), (100, 0), (200, 0), (300, 0)
            },
            IsClosed = false,
            Sight = SenseLevel.Normal
        };

        // 添加窗户
        wallPath.Doors.Add(new DoorSegment
        {
            StartAnchorIndex = 1,
            EndAnchorIndex = 2,
            Kind = DoorKind.Window,
            State = DoorState.Closed
        });

        roomWall.AddComponent(wallPath);
        world.AddObject(roomWall);

        // Act
        var segments = WallSegmentExtractor.ExtractFOVSegments(world, (150, 150), 500);

        // Assert: 2条线段（窗户不阻挡视线）
        Assert.Equal(2, segments.Count);
    }

    [Fact]
    public void ExtractFOVSegments_ArchwayDoesNotBlockSight()
    {
        // Arrange
        var world = new World();
        var roomWall = new GameObject();
        var wallPath = new WallPathComponent
        {
            Points = new List<(double, double)>
            {
                (0, 0), (100, 0), (200, 0), (300, 0)
            },
            IsClosed = false,
            Sight = SenseLevel.Normal
        };

        // 添加拱门
        wallPath.Doors.Add(new DoorSegment
        {
            StartAnchorIndex = 1,
            EndAnchorIndex = 2,
            Kind = DoorKind.Archway,
            State = DoorState.Closed // 拱门忽略状态
        });

        roomWall.AddComponent(wallPath);
        world.AddObject(roomWall);

        // Act
        var segments = WallSegmentExtractor.ExtractFOVSegments(world, (150, 150), 500);

        // Assert: 2条线段（拱门不阻挡视线）
        Assert.Equal(2, segments.Count);
    }

    [Fact]
    public void ExtractFOVSegments_DistanceCulling()
    {
        // Arrange
        var world = new World();
        var roomWall = new GameObject();
        var wallPath = new WallPathComponent
        {
            Points = new List<(double, double)>
            {
                (0, 0), (100, 0), (100, 100), (0, 100)
            },
            IsClosed = true,
            Sight = SenseLevel.Normal
        };

        roomWall.AddComponent(wallPath);
        world.AddObject(roomWall);

        // Act: 观察点远离墙体
        var segments = WallSegmentExtractor.ExtractFOVSegments(world, (1000, 1000), 50);

        // Assert: 超出范围，无线段
        Assert.Equal(0, segments.Count);
    }

    [Fact]
    public void ExtractFOVSegments_LockedDoorBlocksSight()
    {
        // Arrange
        var world = new World();
        var roomWall = new GameObject();
        var wallPath = new WallPathComponent
        {
            Points = new List<(double, double)>
            {
                (0, 0), (100, 0), (200, 0)
            },
            IsClosed = false,
            Sight = SenseLevel.Normal
        };

        // 添加锁定的门
        wallPath.Doors.Add(new DoorSegment
        {
            StartAnchorIndex = 0,
            EndAnchorIndex = 1,
            Kind = DoorKind.Door,
            State = DoorState.Locked
        });

        roomWall.AddComponent(wallPath);
        world.AddObject(roomWall);

        // Act
        var segments = WallSegmentExtractor.ExtractFOVSegments(world, (100, 50), 500);

        // Assert: 锁定的门应该阻挡视线
        Assert.Equal(2, segments.Count);
    }

    [Fact]
    public void ExtractFOVSegments_SightOverrideWorks()
    {
        // Arrange
        var world = new World();
        var roomWall = new GameObject();
        var wallPath = new WallPathComponent
        {
            Points = new List<(double, double)>
            {
                (0, 0), (100, 0), (200, 0)
            },
            IsClosed = false,
            Sight = SenseLevel.Normal
        };

        // 添加门，但设置视觉覆盖为None（不阻挡）
        wallPath.Doors.Add(new DoorSegment
        {
            StartAnchorIndex = 0,
            EndAnchorIndex = 1,
            Kind = DoorKind.Door,
            State = DoorState.Closed,
            SightOverride = SenseLevel.None
        });

        roomWall.AddComponent(wallPath);
        world.AddObject(roomWall);

        // Act
        var segments = WallSegmentExtractor.ExtractFOVSegments(world, (100, 50), 500);

        // Assert: SightOverride=None 应该让门不阻挡
        Assert.Equal(1, segments.Count);
    }

    [Fact]
    public void ExtractFOVSegments_OldWallComponentWorks()
    {
        // Arrange
        var world = new World();
        var wall = new GameObject();
        var wallComp = new WallComponent
        {
            X1 = 0, Y1 = 0,
            X2 = 100, Y2 = 0,
            Sight = SenseLevel.Normal
        };

        wall.AddComponent(wallComp);
        world.AddObject(wall);

        // Act
        var segments = WallSegmentExtractor.ExtractFOVSegments(world, (50, 50), 200);

        // Assert: 应该提取到1条线段
        Assert.Single(segments);
        Assert.Equal(0, segments[0].X1);
        Assert.Equal(100, segments[0].X2);
    }
}
