using MapEngine.Core.Scene;
using MapEngine.Core.Components;
using MapEngine.Core.Systems;
using Xunit;

namespace MapEngine.Tests;

public class VisionEngineTests
{
    [Fact]
    public void NoOccluders_FullCircleVisible()
    {
        var result = VisionEngine.ComputeVisibility(0, 0, 100, [], rayCount: 36);

        Assert.Equal(36, result.Polygon.Count);
        foreach (var (x, y) in result.Polygon)
        {
            var dist = Math.Sqrt(x * x + y * y);
            Assert.InRange(dist, 99.0, 101.0);
        }
    }

    [Fact]
    public void WallBlocksVision_ShorterRayOnBlockedSide()
    {
        var wall = new VisionEngine.Segment { X1 = 50, Y1 = -100, X2 = 50, Y2 = 100 };
        var result = VisionEngine.ComputeVisibility(0, 0, 200, [wall], rayCount: 360);

        var rightRay = result.Polygon[0];
        Assert.True(rightRay.X <= 51, $"Right ray should be blocked at wall x=50, got x={rightRay.X:F1}");

        var leftRay = result.Polygon[180];
        Assert.True(leftRay.X < -100, $"Left ray should reach full radius, got x={leftRay.X:F1}");
    }

    [Fact]
    public void ClosedDoor_BlocksVision()
    {
        // 新模型：门是 WallComponent with Door=DoorKind.Door + State=DoorState.Closed
        var go = new GameObject();
        var door = new WallComponent { X1 = 30, Y1 = -50, X2 = 30, Y2 = 50, Door = DoorKind.Door, State = DoorState.Closed };
        go.AddComponent(door);

        var floorGo = new GameObject();
        floorGo.AddComponent(new FloorComponent { Order = 0 });
        floorGo.Children.Add(go);
        go.Parent = floorGo;

        var segs = FloorVisionCollector.CollectOccluders(floorGo, SenseType.Sight);
        Assert.Single(segs);
    }

    [Fact]
    public void OpenDoor_DoesNotBlockVision()
    {
        var go = new GameObject();
        var door = new WallComponent { X1 = 30, Y1 = -50, X2 = 30, Y2 = 50, Door = DoorKind.Door, State = DoorState.Open };
        go.AddComponent(door);

        var floorGo = new GameObject();
        floorGo.AddComponent(new FloorComponent { Order = 0 });
        floorGo.Children.Add(go);
        go.Parent = floorGo;

        var segs = FloorVisionCollector.CollectOccluders(floorGo, SenseType.Sight);
        Assert.Empty(segs);
    }

    [Fact]
    public void PointInPolygon_InsideSquare()
    {
        var square = new VisionEngine.VisibilityResult
        {
            Polygon = [(-10, -10), (10, -10), (10, 10), (-10, 10)],
            EffectiveRadius = 10
        };

        Assert.True(VisionEngine.IsPointVisible(0, 0, square));
        Assert.True(VisionEngine.IsPointVisible(5, 5, square));
        Assert.False(VisionEngine.IsPointVisible(15, 0, square));
    }
}

public class FloorVisionCollectorTests
{
    [Fact]
    public void WallComponent_BlocksSight_AppearsInOccluders()
    {
        var wallGo = new GameObject();
        wallGo.AddComponent(WallPresets.Normal());
        wallGo.GetComponent<WallComponent>()!.X1 = 0; wallGo.GetComponent<WallComponent>()!.Y1 = 0;
        wallGo.GetComponent<WallComponent>()!.X2 = 100; wallGo.GetComponent<WallComponent>()!.Y2 = 0;

        var floorGo = new GameObject();
        floorGo.AddComponent(new FloorComponent { Order = 0 });
        floorGo.Children.Add(wallGo);
        wallGo.Parent = floorGo;

        var segs = FloorVisionCollector.CollectOccluders(floorGo, SenseType.Sight);
        Assert.Single(segs);
    }

    [Fact]
    public void InvisibleWall_DoesNotBlockSight()
    {
        var wallGo = new GameObject();
        wallGo.AddComponent(WallPresets.Invisible()); // Move=Normal, Sight=None

        var floorGo = new GameObject();
        floorGo.AddComponent(new FloorComponent { Order = 0 });
        floorGo.Children.Add(wallGo);
        wallGo.Parent = floorGo;

        var segs = FloorVisionCollector.CollectOccluders(floorGo, SenseType.Sight);
        Assert.Empty(segs);
    }
}
