using MapEngine.Core.Spatial;

namespace MapEngine.Core.Components;

/// <summary>开门方向。</summary>
public enum DoorSwing
{
    /// <summary>无旋转方向（拱门/窗户）</summary>
    None,
    /// <summary>向左开启</summary>
    Left,
    /// <summary>向右开启</summary>
    Right
}

/// <summary>
/// 门窗线段定义。通过锚点索引区间标记墙体路径上的门/窗位置。
/// 一个 DoorSegment 可覆盖多条连续线段（例如宽门占据 2-3 个锚点间距）。
/// </summary>
public sealed class DoorSegment
{
    /// <summary>门窗唯一标识（用于同步和选中）</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>起始锚点索引（包含）</summary>
    public int StartAnchorIndex { get; set; }

    /// <summary>结束锚点索引（包含）</summary>
    public int EndAnchorIndex { get; set; }

    /// <summary>门窗类型</summary>
    public DoorKind Kind { get; set; } = DoorKind.Door;

    /// <summary>门状态（窗户和拱门忽略此字段）</summary>
    public DoorState State { get; set; } = DoorState.Closed;

    /// <summary>开门方向（拱门和窗户通常为 None）</summary>
    public DoorSwing Swing { get; set; } = DoorSwing.None;

    /// <summary>视觉感知覆盖（null = 继承墙体设置）</summary>
    public SenseLevel? SightOverride { get; set; }

    /// <summary>移动感知覆盖（null = 继承墙体设置）</summary>
    public SenseLevel? MoveOverride { get; set; }

    /// <summary>
    /// 获取此门窗覆盖的线段索引列表。
    /// 例如：StartAnchorIndex=2, EndAnchorIndex=4 → 覆盖线段 [2-3, 3-4]
    /// </summary>
    public List<int> GetCoveredSegments()
    {
        var result = new List<int>();
        for (int i = StartAnchorIndex; i < EndAnchorIndex; i++)
        {
            result.Add(i);
        }
        return result;
    }

    /// <summary>深拷贝门窗数据</summary>
    public DoorSegment Clone() => new()
    {
        Id = Id,
        StartAnchorIndex = StartAnchorIndex,
        EndAnchorIndex = EndAnchorIndex,
        Kind = Kind,
        State = State,
        Swing = Swing,
        SightOverride = SightOverride,
        MoveOverride = MoveOverride
    };
}

/// <summary>
/// 墙体路径组件——用锚点列表定义可编辑的墙体折线/多边形。
/// 支持在线段上标记门窗位置，集成四感知通道（Foundry 式多感知）。
///
/// 设计要点：
/// - 锚点数组定义路径几何（世界坐标，非相对中心）
/// - 门窗通过锚点索引区间定义（方便拖拽调整）
/// - 完全复用矢量编辑工具（与 ShapeComponent.Polygon 一致）
/// - FOV 算法根据门窗状态动态生成阻挡线段
/// </summary>
public sealed class WallPathComponent : ComponentBase, ISpatialComponent
{
    public override string TypeName => "WallPath";

    // ── 路径几何 ────────────────────────────────────────────────────────────
    /// <summary>锚点数组（世界坐标）</summary>
    public List<(double X, double Y)> Points { get; set; } = [];

    /// <summary>是否闭合路径（首尾连接）</summary>
    public bool IsClosed { get; set; }

    // ── 四感知通道（默认值：普通墙体全阻挡）───────────────────────────────
    /// <summary>视觉感知阻挡等级</summary>
    public SenseLevel Sight { get; set; } = SenseLevel.Normal;

    /// <summary>移动感知阻挡等级</summary>
    public SenseLevel Move { get; set; } = SenseLevel.Normal;

    /// <summary>声音感知阻挡等级</summary>
    public SenseLevel Sound { get; set; } = SenseLevel.Normal;

    /// <summary>光照感知阻挡等级</summary>
    public SenseLevel Light { get; set; } = SenseLevel.Normal;

    // ── 渲染参数 ────────────────────────────────────────────────────────────
    /// <summary>墙体厚度（世界单位）</summary>
    public double Thickness { get; set; } = 5;

    /// <summary>墙体颜色 #RRGGBB / #AARRGGBB</summary>
    public string Color { get; set; } = "#D32F2F";

    // ── 门窗系统 ────────────────────────────────────────────────────────────
    /// <summary>门窗列表（每个 DoorSegment 覆盖一段锚点区间）</summary>
    public List<DoorSegment> Doors { get; set; } = [];

    // ── ISpatialComponent 实现 ─────────────────────────────────────────────
    /// <summary>计算路径包围盒（用于空间索引）</summary>
    public RectD GetLocalBounds()
    {
        if (Points.Count == 0)
            return new RectD(0, 0, 0, 0);

        double minX = double.MaxValue, minY = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue;

        foreach (var (x, y) in Points)
        {
            if (x < minX) minX = x;
            if (x > maxX) maxX = x;
            if (y < minY) minY = y;
            if (y > maxY) maxY = y;
        }

        return new RectD(minX, minY, maxX - minX, maxY - minY);
    }

    /// <summary>深拷贝组件数据</summary>
    public override IComponent Clone() => new WallPathComponent
    {
        Points = [.. Points],
        IsClosed = IsClosed,
        Sight = Sight,
        Move = Move,
        Sound = Sound,
        Light = Light,
        Thickness = Thickness,
        Color = Color,
        Doors = Doors.Select(d => d.Clone()).ToList()
    };
}
