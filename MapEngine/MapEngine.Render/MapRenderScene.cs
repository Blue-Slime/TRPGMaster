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
