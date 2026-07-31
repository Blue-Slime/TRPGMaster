using System;
using System.Collections.Generic;

namespace MapEngine.Render;

public sealed class MapRenderScene
{
    public double ViewportWidth { get; init; }

    public double ViewportHeight { get; init; }

    public double Zoom { get; init; } = 1.0;

    public double CameraCenterX { get; init; }

    public double CameraCenterY { get; init; }

    public double WorldWidth { get; init; }

    public double WorldHeight { get; init; }

    public IReadOnlyList<MapRenderSprite> BackgroundSprites { get; init; } = Array.Empty<MapRenderSprite>();

    public IReadOnlyList<MapRenderRect> Tiles { get; init; } = Array.Empty<MapRenderRect>();

    public IReadOnlyList<MapRenderRect> GridRects { get; init; } = Array.Empty<MapRenderRect>();

    public IReadOnlyList<MapRenderRect> Objects { get; init; } = Array.Empty<MapRenderRect>();

    public IReadOnlyList<MapRenderSprite> Sprites { get; init; } = Array.Empty<MapRenderSprite>();

    public IReadOnlyList<MapRenderArc> VisionCones { get; init; } = Array.Empty<MapRenderArc>();

    public IReadOnlyList<MapRenderPolygon> VisionFans { get; init; } = Array.Empty<MapRenderPolygon>();

    public IReadOnlyList<MapRenderRect> WallLines { get; init; } = Array.Empty<MapRenderRect>();

    public IReadOnlyList<MapRenderRect> WallHandles { get; init; } = Array.Empty<MapRenderRect>();

    /// <summary>
    /// 选中对象的旋转/缩放 handles。
    /// 索引语义：每组 6 个，前 1 = 旋转圆圈，后 4 = 四角缩放方块，最后 1 = 旋转连接线。
    /// SceneBuilder 负责填充，拖拽检测在 View 层用世界坐标碰撞。
    /// </summary>
    public IReadOnlyList<MapRenderRect> SelectionHandles { get; init; } = Array.Empty<MapRenderRect>();

    /// <summary>
    /// 矢量形状图层（形状绘制工具产生）。每个条目含完整几何+样式，由渲染管线分填充和描边两趟绘制。
    /// </summary>
    public IReadOnlyList<MapVectorShape> VectorShapes { get; init; } = Array.Empty<MapVectorShape>();

    /// <summary>
    /// 战争迷雾：已揭示区域的顶点列表。渲染时先绘全屏暗色层，再用这些多边形抠孔。
    /// 坐标系与 WallLines 等相同（世界内容坐标）。
    /// </summary>
    public IReadOnlyList<IReadOnlyList<(double X, double Y)>> FogRevealedPolygons { get; init; } = Array.Empty<IReadOnlyList<(double X, double Y)>>();

    /// <summary>战争迷雾是否启用。</summary>
    public bool FogEnabled { get; init; } = false;

    /// <summary>
    /// 光源列表，由 LightComponent 汇集而来。
    /// 渲染时在 Sprite 层之后、迷雾层之前以加法混合绘制光晕。
    /// </summary>
    public IReadOnlyList<MapRenderLight> LightSources { get; init; } = Array.Empty<MapRenderLight>();

    /// <summary>
    /// Token 状态徽章（底部徽章栏，Owlbear Rodeo 2 风格）。
    /// 每个徽章是一个圆角矩形 + emoji 图标，渲染在 Token Sprite 下方。
    /// </summary>
    public IReadOnlyList<MapRenderConditionBadge> ConditionBadges { get; init; } = Array.Empty<MapRenderConditionBadge>();
}

// ─────────────────────────────────────────────────────────────────────────────
// 矢量形状数据结构
// ─────────────────────────────────────────────────────────────────────────────

public enum VectorShapeType
{
    Line, Rect, Ellipse, Cone, Wedge, Polygon, Freehand
}

public enum VectorStrokeStyle { Solid, Dashed, Dotted }

/// <summary>
/// 一个矢量形状实例，携带足够的几何和样式信息供渲染管线直接消费。
/// 坐标系：世界内容坐标（与 WallLines 等相同）。
/// </summary>
public sealed class MapVectorShape
{
    public VectorShapeType Type { get; init; }

    // ── 中心 ──────────────────────────────────────────────────────────────
    public double CenterX { get; init; }
    public double CenterY { get; init; }

    // ── 矩形 / 椭圆 ───────────────────────────────────────────────────────
    public double Width  { get; init; }
    public double Height { get; init; }

    // ── 直线终点 ──────────────────────────────────────────────────────────
    public double X2 { get; init; }
    public double Y2 { get; init; }

    // ── 多边形 / 自由笔触顶点（绝对世界坐标）─────────────────────────────
    public IReadOnlyList<(double X, double Y)> Points { get; init; } = Array.Empty<(double, double)>();

    // ── 锥形 / 扇形 ───────────────────────────────────────────────────────
    /// <summary>方向角（度，0 = 右）</summary>
    public double Direction { get; init; }
    /// <summary>半角（度）</summary>
    public double HalfAngle { get; init; }
    /// <summary>半径（世界单位）</summary>
    public double Radius { get; init; }

    // ── 样式 ──────────────────────────────────────────────────────────────
    public MapRenderColor StrokeColor { get; init; }
    public MapRenderColor FillColor   { get; init; }
    public float StrokeWidth { get; init; } = 2f;
    public bool  IsFilled    { get; init; } = true;
    public VectorStrokeStyle StrokeStyle { get; init; } = VectorStrokeStyle.Solid;
}

public readonly record struct MapRenderColor(float R, float G, float B, float A);

public readonly record struct MapRenderRect(
    double X,
    double Y,
    double Width,
    double Height,
    double Rotation,
    MapRenderColor FillColor,
    string? TexturePath,
    float Opacity);

public readonly record struct MapRenderSprite(
    double X,
    double Y,
    double Width,
    double Height,
    double Rotation,
    string TexturePath,
    MapRenderColor TintColor);

/// <summary>
/// 扇形（用于视野锥渲染）
/// </summary>
 public readonly record struct MapRenderArc(
    double CenterX,
    double CenterY,
    double Radius,
    double StartAngleDegrees,
    double EndAngleDegrees,
    MapRenderColor FillColor);

/// <summary>
/// 通用多边形渲染（用于带遮挡的视野、迷雾遮罩等）。
/// 中心点 + 边缘顶点序列，支持多种填充模式以适应不同的渲染需求。
/// </summary>
public sealed class MapRenderPolygon
{
    public double CenterX { get; init; }
    public double CenterY { get; init; }
    public MapRenderColor FillColor { get; init; }
    public IReadOnlyList<(double X, double Y)> EdgeVertices { get; init; } = Array.Empty<(double, double)>();
    public PolygonFillMode Mode { get; init; } = PolygonFillMode.Fan;
}

public enum PolygonFillMode
{
    /// <summary>三角扇填充（视野锥可见区域）</summary>
    Fan,
    /// <summary>仅写入模板缓冲区，用于迷雾抠除等遮罩操作</summary>
    StencilMask,
    /// <summary>仅描边（调试可视化锚点用）</summary>
    OutlineOnly,
}

/// <summary>
/// 单个光源渲染数据。坐标系：世界内容坐标，Y 轴向上。
/// BrightRadius 内全亮，DimRadius 内渐暗，超出 DimRadius 无光。
/// </summary>
public sealed class MapRenderLight
{
    /// <summary>光源中心 X（世界内容坐标）。</summary>
    public double CenterX { get; init; }
    /// <summary>光源中心 Y（世界内容坐标）。</summary>
    public double CenterY { get; init; }
    /// <summary>亮圈半径（世界单位）。</summary>
    public double BrightRadius { get; init; }
    /// <summary>暗圈半径（世界单位）。</summary>
    public double DimRadius { get; init; }
    /// <summary>光色 RGB（0-1）。Alpha 为最大亮圈不透明度（默认 0.55）。</summary>
    public MapRenderColor Color { get; init; } = new(1f, 0.87f, 0.53f, 0.55f);
    /// <summary>点光源 / 锥形光。</summary>
    public bool IsCone { get; init; }
    /// <summary>锥形光方向角（度，0 = 右，逆时针）。</summary>
    public double ConeDirection { get; init; }
    /// <summary>锥形光半角（度）。</summary>
    public double ConeHalfAngle { get; init; }
}

/// <summary>
/// Token 状态徽章渲染数据（Owlbear Rodeo 2 风格底部徽章栏）。
/// 坐标系：世界内容坐标，Y 轴向上。
/// </summary>
public readonly record struct MapRenderConditionBadge(
    double X,
    double Y,
    double Size,
    string Icon,
    int StackCount,
    MapRenderColor BackgroundColor);
