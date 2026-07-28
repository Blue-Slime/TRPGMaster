using MapEngine.Core.Components;
using MapEngine.Core.Systems;

namespace MapEngine.Core.Scene;

/// <summary>
/// 楼层组件 — 挂在层级树中的 GameObject 上。
///
/// 层级结构示例：
///   大地块 (Terrain)
///     └─ 建筑A (Building)
///          ├─ 1F [FloorComponent, Order=0]
///          │    ├─ Wall_1 [WallComponent]       ← 统一多感知通道墙
///          │    ├─ Light_1 [LightComponent]
///          │    └─ Token_Goblin [TokenComponent]
///          ├─ 2F [FloorComponent, Order=1]
///          └─ Roof [FloorComponent, Order=2]
///
/// 视野计算时，只收集注视楼层（及其子对象）的 WallComponent 参与扫描。
/// 门 = WallComponent.Door != None，开门 = 该边从扫描候选集中剔除。
/// </summary>
public sealed class FloorComponent : ComponentBase
{
    public override string TypeName => "Floor";

    /// <summary>垂直顺序，支持负数（地下室）。数值大 = 更高楼层。</summary>
    public int Order { get; set; }

    /// <summary>是否允许被上层叠加显示（ShowBelow=true 时低层全景渲染，Z-range 深度剔除 overdraw）。</summary>
    public bool ShowBelow { get; set; } = true;

    /// <summary>叠加显示时的后处理特效。</summary>
    public FloorEffect GhostEffect { get; set; } = FloorEffect.None;

    public override IComponent Clone() => new FloorComponent
    {
        Order = Order,
        ShowBelow = ShowBelow,
        GhostEffect = GhostEffect
    };
}

public enum FloorEffect { None, Desaturate, Darken }

/// <summary>
/// 光源组件 — 挂在楼层子对象上。
/// </summary>
public sealed class LightComponent : ComponentBase
{
    public override string TypeName => "Light";

    public double BrightRadius { get; set; } = 20;
    public double DimRadius    { get; set; } = 40;
    public string Color        { get; set; } = "#FFDD88";
    public LightShape Shape    { get; set; } = LightShape.Point;
    public double ConeAngle    { get; set; } = 360;
    public double ConeDirection{ get; set; }

    public override IComponent Clone() => new LightComponent
    {
        BrightRadius = BrightRadius, DimRadius = DimRadius,
        Color = Color, Shape = Shape,
        ConeAngle = ConeAngle, ConeDirection = ConeDirection
    };
}

public enum LightShape { Point, Cone }

/// <summary>
/// 从层级树收集注视楼层的视野遮挡边，供 VisionSystem 扫描。
/// 门开启时直接跳过（开门 = 边不参与扫描）。
/// </summary>
public static class FloorVisionCollector
{
    public static List<VisionEngine.Segment> CollectOccluders(
        GameObject floorObject, SenseType senseType = SenseType.Sight)
    {
        var segments = new List<VisionEngine.Segment>();
        CollectRecursive(floorObject, segments, senseType);
        return segments;
    }

    private static void CollectRecursive(
        GameObject obj, List<VisionEngine.Segment> segments, SenseType senseType)
    {
        if (!obj.IsActive) return;

        var wall = obj.GetComponent<WallComponent>();
        if (wall is not null && wall.Blocks(senseType))
        {
            segments.Add(new VisionEngine.Segment
            {
                X1 = wall.X1, Y1 = wall.Y1,
                X2 = wall.X2, Y2 = wall.Y2
            });
        }

        foreach (var child in obj.Children)
            CollectRecursive(child, segments, senseType);
    }

    /// <summary>向上查找最近的 FloorComponent 所属 GameObject。</summary>
    public static GameObject? FindContainingFloor(GameObject obj)
    {
        var current = obj.Parent;
        while (current is not null)
        {
            if (current.GetComponent<FloorComponent>() is not null)
                return current;
            current = current.Parent;
        }
        return null;
    }
}
