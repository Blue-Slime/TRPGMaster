using System;
using System.Collections.Generic;
using MapEngine.Render.UI;

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

    /// <summary>
    /// FOV 多边形的顶点距离（与 FogRevealedPolygons 平行，用于计算渐变效果）。
    /// 每个子列表对应一个多边形的每个顶点到视野中心的距离。
    /// </summary>
    public IReadOnlyList<IReadOnlyList<double>> FogVertexDistances { get; init; } = Array.Empty<IReadOnlyList<double>>();

    /// <summary>
    /// FOV 视野中心点列表（与 FogRevealedPolygons 平行，用于计算每个像素到中心的距离）。
    /// </summary>
    public IReadOnlyList<(double X, double Y)> FogOrigins { get; init; } = Array.Empty<(double X, double Y)>();

    /// <summary>
    /// FOV 渐变距离比例（0.0-0.5，占视野半径的比例）。
    /// 例如 0.2 表示视野边缘 20% 区域有渐变效果。
    /// </summary>
    public double FogFadeDistance { get; init; } = 0.2;

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

    /// <summary>
    /// 拓扑连线。必须在 GraphNodes 之前绘制，否则线会盖在节点图标上。
    /// </summary>
    public IReadOnlyList<MapRenderGraphLink> GraphLinks { get; init; } = Array.Empty<MapRenderGraphLink>();

    /// <summary>
    /// 拓扑节点图标。有 TexturePath 的走贴图，否则按 Shape 画纯色形状。
    /// </summary>
    public IReadOnlyList<MapRenderGraphNode> GraphNodes { get; init; } = Array.Empty<MapRenderGraphNode>();

    /// <summary>
    /// Token 名称标签（Skia 层渲染）。
    /// </summary>
    public IReadOnlyList<SkiaLabel> TokenLabels { get; init; } = Array.Empty<SkiaLabel>();

    /// <summary>
    /// Token HP 条（Skia 层渲染）。
    /// </summary>
    public IReadOnlyList<SkiaHealthBar> TokenHealthBars { get; init; } = Array.Empty<SkiaHealthBar>();

    /// <summary>
    /// Token 状态徽章（Skia 层渲染）。
    /// </summary>
    public IReadOnlyList<SkiaBadge> TokenBadges { get; init; } = Array.Empty<SkiaBadge>();

    /// <summary>
    /// 幽灵 Token 标记（其他楼层的 Token，半透明显示 + 楼层标签）。
    /// </summary>
    public IReadOnlyList<GhostToken> GhostTokens { get; init; } = Array.Empty<GhostToken>();
}

// ─────────────────────────────────────────────────────────────────────────────
// 拓扑图数据结构
// ─────────────────────────────────────────────────────────────────────────────

public enum GraphNodeShape { Circle, Square, Diamond }

/// <summary>
/// 一个拓扑节点的渲染指令。坐标系为世界内容坐标（与 WallLines 等一致）。
/// </summary>
public sealed class MapRenderGraphNode
{
    /// <summary>对象 Id，供点击命中回查</summary>
    public string ObjectId { get; init; } = string.Empty;

    public double CenterX { get; init; }
    public double CenterY { get; init; }
    /// <summary>直径（世界单位）</summary>
    public double Size { get; init; } = 48;

    public GraphNodeShape Shape { get; init; } = GraphNodeShape.Circle;
    public MapRenderColor FillColor { get; init; }

    /// <summary>图标贴图路径（已由 assetRef 解析成实际路径）。空则画纯色形状。</summary>
    public string? TexturePath { get; init; }

    /// <summary>未揭示节点整体降透明度，GM 才看得到</summary>
    public float Opacity { get; init; } = 1f;

    /// <summary>是否画选中高亮环</summary>
    public bool IsSelected { get; init; }
}

/// <summary>
/// 一条拓扑连线的渲染指令。两端坐标已由 SceneBuilder 解析好，
/// 渲染层不需要再查节点。
/// </summary>
public sealed class MapRenderGraphLink
{
    /// <summary>边 Id，供点击命中回查</summary>
    public string LinkId { get; init; } = string.Empty;

    public double X1 { get; init; }
    public double Y1 { get; init; }
    public double X2 { get; init; }
    public double Y2 { get; init; }

    public MapRenderColor Color { get; init; }
    public float Width { get; init; } = 2f;
    public VectorStrokeStyle StrokeStyle { get; init; } = VectorStrokeStyle.Solid;

    /// <summary>单向边在终点画箭头</summary>
    public bool ShowArrow { get; init; }

    /// <summary>封锁的边画成半透明，直观区分“有路但走不通”</summary>
    public float Opacity { get; init; } = 1f;

    public bool IsSelected { get; init; }
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
    MapRenderColor TintColor,
    string Shape);

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

// ─────────────────────────────────────────────────────────────────────────────
// Token UI 数据结构（Skia 层渲染）
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Token 名称标签（Skia 层渲染，带阴影和描边）。
/// 坐标系：世界内容坐标，Y 轴向上。
/// </summary>
public sealed class SkiaLabel
{
    /// <summary>标签文本（Token 名称）。</summary>
    public string Text { get; init; } = string.Empty;
    /// <summary>标签中心 X（世界内容坐标）。</summary>
    public double CenterX { get; init; }
    /// <summary>标签中心 Y（世界内容坐标）。</summary>
    public double CenterY { get; init; }
    /// <summary>字体大小（逻辑像素）。</summary>
    public float FontSize { get; init; } = 14f;
    /// <summary>文本颜色。</summary>
    public MapRenderColor Color { get; init; } = new(1f, 1f, 1f, 1f);
}

/// <summary>
/// Token HP 条（Skia 层渲染，带渐变填充）。
/// 坐标系：世界内容坐标，Y 轴向上。
/// </summary>
public sealed class SkiaHealthBar
{
    /// <summary>HP 条左上角 X（世界内容坐标）。</summary>
    public double X { get; init; }
    /// <summary>HP 条左上角 Y（世界内容坐标）。</summary>
    public double Y { get; init; }
    /// <summary>HP 条宽度（世界单位）。</summary>
    public double Width { get; init; }
    /// <summary>HP 条高度（世界单位）。</summary>
    public double Height { get; init; }
    /// <summary>当前 HP 百分比（0.0 - 1.0）。</summary>
    public float Percentage { get; init; }
    /// <summary>当前 HP 值。</summary>
    public int CurrentHP { get; init; }
    /// <summary>最大 HP 值。</summary>
    public int MaxHP { get; init; }
}

/// <summary>
/// Token 状态徽章（Skia 层渲染，图标 + 数字）。
/// 坐标系：世界内容坐标，Y 轴向上。
/// </summary>
public sealed class SkiaBadge
{
    /// <summary>徽章中心 X（世界内容坐标）。</summary>
    public double CenterX { get; init; }
    /// <summary>徽章中心 Y（世界内容坐标）。</summary>
    public double CenterY { get; init; }
    /// <summary>徽章大小（边长，世界单位）。</summary>
    public double Size { get; init; }
    /// <summary>图标文本（emoji）。</summary>
    public string Icon { get; init; } = string.Empty;
    /// <summary>堆叠计数（显示在右上角）。</summary>
    public int StackCount { get; init; }
    /// <summary>背景颜色。</summary>
    public MapRenderColor BackgroundColor { get; init; }
}

/// <summary>
/// 幽灵 Token 标记（其他楼层的 Token，半透明显示 + 楼层标签）。
/// 坐标系：世界内容坐标，Y 轴向上。
/// </summary>
public sealed class GhostToken
{
    /// <summary>Token 位置 X（世界内容坐标）。</summary>
    public double X { get; init; }
    /// <summary>Token 位置 Y（世界内容坐标）。</summary>
    public double Y { get; init; }
    /// <summary>Token 宽度（世界单位）。</summary>
    public double Width { get; init; }
    /// <summary>Token 高度（世界单位）。</summary>
    public double Height { get; init; }
    /// <summary>图标路径（已解析的素材路径）。</summary>
    public string TexturePath { get; init; } = string.Empty;
    /// <summary>楼层标签（"99F↓" 或 "100F↑"）。</summary>
    public string FloorLabel { get; init; } = string.Empty;
    /// <summary>对象 ID（供点击回查）。</summary>
    public string ItemId { get; init; } = string.Empty;
}
