using MapEngine.Core.Components;
using MapEngine.Core.Spatial;

namespace MapEngine.Core.Systems;

/// <summary>
/// FOV 线段提取器——从 World 中提取所有阻挡视线的墙体线段，
/// 处理墙体路径的门窗状态，生成用于视野计算的阻挡线段列表。
/// </summary>
public static class WallSegmentExtractor
{
    /// <summary>
    /// 线段结构（用于 VisionEngine.ComputeVisibility）
    /// </summary>
    public readonly struct WallSegment
    {
        public readonly double X1, Y1, X2, Y2;
        public readonly bool BlocksSight;

        public WallSegment(double x1, double y1, double x2, double y2, bool blocksSight = true)
        {
            X1 = x1;
            Y1 = y1;
            X2 = x2;
            Y2 = y2;
            BlocksSight = blocksSight;
        }
    }

    /// <summary>
    /// 从 World 中提取所有阻挡视线的墙体线段。
    /// </summary>
    /// <param name="world">世界对象</param>
    /// <param name="origin">视野原点（世界坐标）</param>
    /// <param name="range">视野范围（距离剔除优化）</param>
    /// <param name="senseType">感知类型（默认视觉）</param>
    /// <returns>阻挡线段列表（转换为 VisionEngine.Segment 格式）</returns>
    public static List<VisionEngine.Segment> ExtractFOVSegments(
        World world,
        (double X, double Y) origin,
        double range,
        SenseType senseType = SenseType.Sight)
    {
        var segments = new List<VisionEngine.Segment>();
        var rangeRect = RectD.FromCircle(origin.X, origin.Y, range);

        // 1. 处理 WallPathComponent（新墙体路径系统）
        foreach (var obj in world.AllObjects())
        {
            var wallPath = obj.GetComponent<WallPathComponent>();
            if (wallPath == null || wallPath.Points.Count < 2)
                continue;

            // Transform 支持（暂时假设无父级变换，直接使用世界坐标）
            var transform = obj.GetComponent<TransformComponent>();
            var (offsetX, offsetY) = transform != null ? (transform.X, transform.Y) : (0.0, 0.0);

            ExtractFromWallPath(wallPath, offsetX, offsetY, origin, range, rangeRect, senseType, segments);
        }

        // 2. 处理旧版 WallComponent（单线段墙体）
        foreach (var obj in world.AllObjects())
        {
            var wall = obj.GetComponent<WallComponent>();
            if (wall == null)
                continue;

            var transform = obj.GetComponent<TransformComponent>();
            var (offsetX, offsetY) = transform != null ? (transform.X, transform.Y) : (0.0, 0.0);

            ExtractFromWallComponent(wall, offsetX, offsetY, rangeRect, senseType, segments);
        }

        return segments;
    }

    /// <summary>
    /// 从 WallPathComponent 提取线段（处理门窗状态）
    /// </summary>
    private static void ExtractFromWallPath(
        WallPathComponent wallPath,
        double offsetX,
        double offsetY,
        (double X, double Y) origin,
        double range,
        RectD rangeRect,
        SenseType senseType,
        List<VisionEngine.Segment> segments)
    {
        var points = wallPath.Points;
        var loopEnd = wallPath.IsClosed ? points.Count : points.Count - 1;

        for (int i = 0; i < loopEnd; i++)
        {
            var (x1, y1) = points[i];
            var (x2, y2) = points[(i + 1) % points.Count];

            // 应用 Transform 偏移
            x1 += offsetX; y1 += offsetY;
            x2 += offsetX; y2 += offsetY;

            // 距离剔除优化
            var segRect = RectD.FromSegment(x1, y1, x2, y2);
            if (!rangeRect.Intersects(segRect))
                continue;

            // 检查是否被门窗覆盖
            var door = wallPath.Doors.FirstOrDefault(d => d.GetCoveredSegments().Contains(i));

            if (door != null)
            {
                // 门窗区间：根据类型和状态判断是否阻挡
                bool blocks = ShouldDoorBlock(door, senseType);
                if (!blocks)
                    continue; // 开启的门/窗户不阻挡
            }
            else
            {
                // 普通墙体：检查感知阻挡等级
                var level = GetSenseLevel(wallPath, senseType);
                if (level == SenseLevel.None)
                    continue;
            }

            // 添加阻挡线段
            segments.Add(new VisionEngine.Segment { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2 });
        }
    }

    /// <summary>
    /// 从旧版 WallComponent 提取线段
    /// </summary>
    private static void ExtractFromWallComponent(
        WallComponent wall,
        double offsetX,
        double offsetY,
        RectD rangeRect,
        SenseType senseType,
        List<VisionEngine.Segment> segments)
    {
        var x1 = wall.X1 + offsetX;
        var y1 = wall.Y1 + offsetY;
        var x2 = wall.X2 + offsetX;
        var y2 = wall.Y2 + offsetY;

        // 距离剔除
        var segRect = RectD.FromSegment(x1, y1, x2, y2);
        if (!rangeRect.Intersects(segRect))
            return;

        // 检查是否阻挡
        if (!wall.Blocks(senseType))
            return;

        segments.Add(new VisionEngine.Segment { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2 });
    }

    /// <summary>
    /// 判断门窗是否阻挡指定感知类型
    /// </summary>
    private static bool ShouldDoorBlock(DoorSegment door, SenseType senseType)
    {
        // 优先使用门窗自身的感知覆盖
        var overrideLevel = senseType switch
        {
            SenseType.Sight => door.SightOverride,
            SenseType.Move => door.MoveOverride,
            _ => null
        };

        if (overrideLevel.HasValue && overrideLevel.Value == SenseLevel.None)
            return false;

        // 根据门窗类型和状态判断
        return (door.Kind, door.State, senseType) switch
        {
            // 窗户：视线可穿透，但阻挡移动
            (DoorKind.Window, _, SenseType.Sight) => false,
            (DoorKind.Window, _, SenseType.Move) => true,

            // 拱门：所有感知都不阻挡
            (DoorKind.Archway, _, _) => false,

            // 普通门/密门：
            (_, DoorState.Open, SenseType.Sight) => false,  // 开启不阻挡视线
            (_, DoorState.Open, SenseType.Move) => false,   // 开启不阻挡移动
            (_, DoorState.Closed, _) => true,               // 关闭全阻挡
            (_, DoorState.Locked, _) => true,               // 锁定全阻挡

            _ => true
        };
    }

    /// <summary>
    /// 获取墙体路径的感知阻挡等级
    /// </summary>
    private static SenseLevel GetSenseLevel(WallPathComponent wallPath, SenseType senseType)
    {
        return senseType switch
        {
            SenseType.Sight => wallPath.Sight,
            SenseType.Move => wallPath.Move,
            SenseType.Sound => wallPath.Sound,
            SenseType.Light => wallPath.Light,
            _ => SenseLevel.None
        };
    }
}
