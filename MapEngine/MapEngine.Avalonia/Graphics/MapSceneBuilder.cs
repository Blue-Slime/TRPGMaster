using System;
using System.Collections.Generic;
using System.IO;
using Avalonia;
using Avalonia.Media;
using MapEngine.Render;
using MapEngine.Avalonia.Services;
using MapEngine.Avalonia.ViewModels;

namespace MapEngine.Avalonia.Graphics;

public static class MapSceneBuilder
{
    private const int TestBattleMapColumns = 32;
    private const int TestBattleMapRows = 24;
    private static readonly string TestBattleMapPath = Path.Combine(AppContext.BaseDirectory, "Data", "BattleMaps", "test-battlemap.png");
    private static readonly MapRenderColor OriginAccentColor = new(1.0f, 0.92f, 0.28f, 1.0f);
    private static readonly MapRenderColor SelectedObjectColor = new(0.16f, 0.44f, 0.69f, 0.88f);
    private static readonly MapRenderColor PreviewObjectColor = new(0.92f, 0.74f, 0.20f, 0.88f);
    private static readonly MapRenderColor DefaultObjectColor = new(0.15f, 0.15f, 0.17f, 0.82f);
    private static readonly MapRenderColor TokenColor = new(0.90f, 0.55f, 0.15f, 0.88f);
    private static readonly MapRenderColor MapColor = new(0.20f, 0.65f, 0.35f, 0.75f);
    private static readonly MapRenderColor PropColor = new(0.45f, 0.45f, 0.50f, 0.80f);
    private static readonly MapRenderColor TokenShadowColor = new(0.03f, 0.03f, 0.04f, 0.42f);

    public static MapRenderScene? Build(MainWindowViewModel viewModel, Size viewportSize, Point cameraContentCenter)
    {
        if (viewportSize.Width <= 0 || viewportSize.Height <= 0)
        {
            return null;
        }

        var (wallLines, wallHandles) = (BuildWallLines(viewModel), BuildWallHandles(viewModel));
        var (visionCones, visionFans) = BuildVision(viewModel);

        return new MapRenderScene
        {
            ViewportWidth = viewportSize.Width,
            ViewportHeight = viewportSize.Height,
            Zoom = viewModel.ZoomScale <= 0 ? 1.0 : viewModel.ZoomScale,
            CameraCenterX = cameraContentCenter.X,
            CameraCenterY = cameraContentCenter.Y,
            WorldWidth = MapViewportConstants.ContentSize,
            WorldHeight = MapViewportConstants.ContentSize,
            BackgroundSprites = BuildBackgroundSprites(),
            Tiles = BuildTileRects(viewModel),
            GridRects = BuildGridRects(viewModel),
            Objects = BuildObjectRects(viewModel),
            Sprites = BuildSprites(viewModel),
            VisionCones = visionCones,
            VisionFans = visionFans,
            WallLines = wallLines,
            WallHandles = wallHandles
        };
    }

    private static IReadOnlyList<MapRenderRect> BuildTileRects(MainWindowViewModel viewModel)
    {
        if (File.Exists(TestBattleMapPath))
        {
            return Array.Empty<MapRenderRect>();
        }

        var tiles = new List<MapRenderRect>(viewModel.MapPreloadedTiles.Count);
        foreach (var tile in viewModel.MapPreloadedTiles)
        {
            tiles.Add(new MapRenderRect(
                tile.CanvasLeft,
                tile.CanvasTop,
                tile.Size,
                tile.Size,
                0,
                ParseColor(tile.Background),
                null,
                1.0f));
        }

        return tiles;
    }

    private static IReadOnlyList<MapRenderSprite> BuildBackgroundSprites()
    {
        if (!File.Exists(TestBattleMapPath))
        {
            return Array.Empty<MapRenderSprite>();
        }

        var width = TestBattleMapColumns * MapViewportConstants.CellSize;
        var height = TestBattleMapRows * MapViewportConstants.CellSize;
        return
        [
            new MapRenderSprite(
                MapViewportConstants.WorldOriginContent - (width / 2.0),
                MapViewportConstants.WorldOriginContent - (height / 2.0),
                width,
                height,
                0,
                TestBattleMapPath,
                new MapRenderColor(1.0f, 1.0f, 1.0f, 1.0f))
        ];
    }

    private static IReadOnlyList<MapRenderRect> BuildGridRects(MainWindowViewModel viewModel)
    {
        var gridRects = new List<MapRenderRect>(viewModel.MapGridLines.Count + 2);
        foreach (var line in viewModel.MapGridLines)
        {
            gridRects.Add(new MapRenderRect(
                line.CanvasLeft,
                line.CanvasTop,
                Math.Max(1, line.Width),
                Math.Max(1, line.Height),
                0,
                ParseColor(line.Stroke),
                null,
                1.0f));
        }

        gridRects.Add(new MapRenderRect(
            MapViewportConstants.WorldOriginContent - 48,
            MapViewportConstants.WorldOriginContent - 2,
            96,
            4,
            0,
            OriginAccentColor,
            null,
            1.0f));
        gridRects.Add(new MapRenderRect(
            MapViewportConstants.WorldOriginContent - 2,
            MapViewportConstants.WorldOriginContent - 48,
            4,
            96,
            0,
            OriginAccentColor,
            null,
            1.0f));

        return gridRects;
    }

    private static IReadOnlyList<MapRenderRect> BuildObjectRects(MainWindowViewModel viewModel)
    {
        var objects = new List<MapRenderRect>(viewModel.MapRenderableItems.Count);
        foreach (var item in viewModel.MapRenderableItems)
        {
            if (!item.ShouldRenderOnMap)
            {
                continue;
            }

            var spritePath = MapSpriteAssetResolver.ResolveSpritePath(item.SourceAssetPath, item.SourceAssetKind);
            if (!string.IsNullOrWhiteSpace(spritePath))
            {
                objects.Add(new MapRenderRect(
                    item.MapLeft + 3,
                    item.MapTop + 4,
                    30,
                    30,
                    0,
                    TokenShadowColor,
                    null,
                    1.0f));
            }

            var accentColor = ResolveObjectColor(item);
            if (item.IsSelected || item.IsPreviewInstance)
            {
                objects.Add(new MapRenderRect(
                    item.MapLeft - 2,
                    item.MapTop - 2,
                    40,
                    40,
                    item.Rotation,
                    accentColor,
                    null,
                    1.0f));
            }
            else if (string.IsNullOrWhiteSpace(spritePath))
            {
                objects.Add(new MapRenderRect(
                    item.MapLeft,
                    item.MapTop,
                    36,
                    36,
                    item.Rotation,
                    accentColor,
                    null,
                    1.0f));
            }
        }

        return objects;
    }

    private static IReadOnlyList<MapRenderSprite> BuildSprites(MainWindowViewModel viewModel)
    {
        var sprites = new List<MapRenderSprite>(viewModel.MapRenderableItems.Count);
        foreach (var item in viewModel.MapRenderableItems)
        {
            if (!item.ShouldRenderOnMap)
            {
                continue;
            }

            var spritePath = MapSpriteAssetResolver.ResolveSpritePath(item.SourceAssetPath, item.SourceAssetKind);
            if (string.IsNullOrWhiteSpace(spritePath))
            {
                continue;
            }

            var spriteWidth = MapViewportConstants.CellSize * item.ScaleX;
            var spriteHeight = MapViewportConstants.CellSize * item.ScaleY;

            sprites.Add(new MapRenderSprite(
                item.MapLeft,
                item.MapTop,
                Math.Max(MapViewportConstants.CellSize * 0.5, spriteWidth),
                Math.Max(MapViewportConstants.CellSize * 0.5, spriteHeight),
                item.Rotation,
                spritePath,
                new MapRenderColor(1.0f, 1.0f, 1.0f, (float)Math.Clamp(item.Opacity, 0.0, 1.0))));
        }

        return sprites;
    }

    private static (IReadOnlyList<MapRenderArc> arcs, IReadOnlyList<MapRenderPolygon> fans) BuildVision(MainWindowViewModel viewModel)
    {
        var arcs = new List<MapRenderArc>();
        var fans = new List<MapRenderPolygon>();

        // 收集所有阻挡视野的墙壁线段（世界坐标）
        var blockerSegments = CollectBlockerSegments(viewModel);

        foreach (var item in viewModel.MapRenderableItems)
        {
            if (!item.ShouldRenderOnMap || !item.VisionEnabled)
            {
                continue;
            }

            foreach (var cone in item.VisionCones)
            {
                if (!cone.IsEnabled || cone.Range <= 0)
                {
                    continue;
                }

                var centerX = item.MapLeft + (MapViewportConstants.CellSize * item.ScaleX) / 2.0;
                var centerY = item.MapTop + (MapViewportConstants.CellSize * item.ScaleY) / 2.0;
                var radius = cone.Range * MapViewportConstants.CellSize;

                var centerAngle = item.Orientation + cone.CenterOffset + item.Rotation;
                var halfFov = cone.FieldOfView / 2.0;
                var startAngle = centerAngle - halfFov;
                var endAngle = centerAngle + halfFov;

                var visionColor = new MapRenderColor(1.0f, 0.95f, 0.4f, 0.25f);

                if (blockerSegments.Count == 0)
                {
                    // 无墙壁，使用简单扇形
                    arcs.Add(new MapRenderArc(centerX, centerY, radius, startAngle, endAngle, visionColor));
                }
                else
                {
                    // Angular Sweep：按墙壁端点采样，几何精确
                    var edges = ComputeVisionPolygon(
                        centerX, centerY, radius, startAngle, endAngle, blockerSegments);

                    fans.Add(new MapRenderPolygon
                    {
                        CenterX = centerX,
                        CenterY = centerY,
                        FillColor = visionColor,
                        EdgeVertices = edges,
                        Mode = PolygonFillMode.Fan
                    });
                }
            }
        }

        return (arcs, fans);
    }

    private static List<(double x1, double y1, double x2, double y2)> CollectBlockerSegments(MainWindowViewModel viewModel)
    {
        var result = new List<(double, double, double, double)>();
        foreach (var item in viewModel.MapRenderableItems)
        {
            if (!item.ShouldRenderOnMap || !item.HasWallComponent) continue;
            var wall = item.GetComponent<MapEngine.Core.Components.WallComponent>();
            if (wall == null) continue;

            var objCenterX = item.MapLeft + (MapViewportConstants.CellSize * item.ScaleX) / 2.0;
            var objCenterY = item.MapTop + (MapViewportConstants.CellSize * item.ScaleY) / 2.0;
            var rotRad = item.Rotation * Math.PI / 180.0;
            var rotCos = Math.Cos(rotRad);
            var rotSin = Math.Sin(rotRad);

            if (wall.Blocks(MapEngine.Core.Components.SenseType.Sight))
            {
                var x1 = objCenterX + (wall.X1 * rotCos - wall.Y1 * rotSin);
                var y1 = objCenterY + (wall.X1 * rotSin + wall.Y1 * rotCos);
                var x2 = objCenterX + (wall.X2 * rotCos - wall.Y2 * rotSin);
                var y2 = objCenterY + (wall.X2 * rotSin + wall.Y2 * rotCos);
                result.Add((x1, y1, x2, y2));
            }
        }
        return result;
    }

    // 求射线 (ox,oy)+t*(dx,dy) 与线段 (x1,y1)-(x2,y2) 的交点参数 t，无交点返回 -1
    private static double RaySegmentIntersect(double ox, double oy, double dx, double dy,
        double x1, double y1, double x2, double y2, double maxT)
    {
        var sdx = x2 - x1;
        var sdy = y2 - y1;
        var denom = dx * sdy - dy * sdx;
        if (Math.Abs(denom) < 1e-9) return -1;
        var t = ((x1 - ox) * sdy - (y1 - oy) * sdx) / denom;
        var u = ((x1 - ox) * dy - (y1 - oy) * dx) / denom;
        if (t < 0 || t > maxT) return -1;
        if (u < 0 || u > 1) return -1;
        return t;
    }

    // Angular Sweep: 按墙壁端点+边界射线投射，返回边缘锚点序列（按角度升序）
    // 角度系统：度数, 0=右, 90=上 (逆时针, 与 AppendArc 一致用 -sin)
    private static List<(double X, double Y)> ComputeVisionPolygon(
        double centerX, double centerY,
        double radius,
        double startAngleDeg, double endAngleDeg,
        List<(double x1, double y1, double x2, double y2)> blockerSegments)
    {
        const double epsDeg = 0.05; // ε 偏移，让端点两侧射线擦边

        // 1. 收集候选角度
        var candidates = new List<double>(blockerSegments.Count * 6 + 2)
        {
            startAngleDeg,
            endAngleDeg
        };

        foreach (var seg in blockerSegments)
        {
            AddEndpointAngles(seg.x1, seg.y1, centerX, centerY, startAngleDeg, endAngleDeg, epsDeg, candidates);
            AddEndpointAngles(seg.x2, seg.y2, centerX, centerY, startAngleDeg, endAngleDeg, epsDeg, candidates);
        }

        // 端点过多时退化为均匀采样上限（保性能）
        const int maxCandidates = 256;
        if (candidates.Count > maxCandidates)
        {
            candidates.Clear();
            var step = (endAngleDeg - startAngleDeg) / 64.0;
            for (int i = 0; i <= 64; i++) candidates.Add(startAngleDeg + step * i);
        }
        else
        {
            // 补充少量均匀采样以避免过稀疏导致的圆弧失真
            var step = (endAngleDeg - startAngleDeg) / 16.0;
            for (int i = 0; i <= 16; i++) candidates.Add(startAngleDeg + step * i);
        }

        candidates.Sort();

        // 2. 对每个候选角度投射射线，取最近交点
        var edges = new List<(double X, double Y)>(candidates.Count);
        double lastAngle = double.NegativeInfinity;
        foreach (var a in candidates)
        {
            if (Math.Abs(a - lastAngle) < 1e-7) continue; // 去重
            lastAngle = a;

            var rad = a * Math.PI / 180.0;
            var dirX = Math.Cos(rad);
            var dirY = -Math.Sin(rad); // Y 翻转，与 AppendArc 一致
            var maxT = radius;
            foreach (var seg in blockerSegments)
            {
                var t = RaySegmentIntersect(centerX, centerY, dirX, dirY, seg.x1, seg.y1, seg.x2, seg.y2, maxT);
                if (t > 0 && t < maxT) maxT = t;
            }
            edges.Add((centerX + dirX * maxT, centerY + dirY * maxT));
        }
        return edges;
    }

    private static void AddEndpointAngles(
        double px, double py, double cx, double cy,
        double startAngleDeg, double endAngleDeg, double epsDeg,
        List<double> candidates)
    {
        // 端点相对中心的角度（与坐标系一致：atan2 用 -dy 因为 Y 翻转）
        var dx = px - cx;
        var dy = py - cy;
        var a = Math.Atan2(-dy, dx) * 180.0 / Math.PI; // [-180, 180]

        // 将角度归一化到 [startAngleDeg, startAngleDeg + 360)
        while (a < startAngleDeg) a += 360.0;
        while (a >= startAngleDeg + 360.0) a -= 360.0;

        // 检查是否在 [startAngleDeg, endAngleDeg] 范围内
        if (a > endAngleDeg) return;

        candidates.Add(a);
        if (a - epsDeg > startAngleDeg) candidates.Add(a - epsDeg);
        if (a + epsDeg < endAngleDeg) candidates.Add(a + epsDeg);
    }

    private static IReadOnlyList<MapRenderRect> BuildWallLines(MainWindowViewModel viewModel)
    {
        var lines = new List<MapRenderRect>();
        var wallColor = new MapRenderColor(0.8f, 0.2f, 0.2f, 1.0f);
        var doorClosedColor = new MapRenderColor(0.7f, 0.4f, 0.1f, 1.0f);
        var doorOpenColor = new MapRenderColor(0.3f, 0.7f, 0.3f, 0.6f);
        var windowColor = new MapRenderColor(0.4f, 0.6f, 0.9f, 0.8f);

        foreach (var item in viewModel.MapRenderableItems)
        {
            if (!item.ShouldRenderOnMap || !item.HasWallComponent)
                continue;

            var wall = item.GetComponent<MapEngine.Core.Components.WallComponent>();
            if (wall == null)
                continue;

            var color = wall.Door != MapEngine.Core.Components.DoorKind.None
                ? (wall.State == MapEngine.Core.Components.DoorState.Open ? doorOpenColor : doorClosedColor)
                : (wall.Sight == MapEngine.Core.Components.SenseLevel.None ? windowColor : wallColor);

            var objCenterX = item.MapLeft + (MapViewportConstants.CellSize * item.ScaleX) / 2.0;
            var objCenterY = item.MapTop + (MapViewportConstants.CellSize * item.ScaleY) / 2.0;
            var rotRad = item.Rotation * Math.PI / 180.0;
            var rotCos = Math.Cos(rotRad);
            var rotSin = Math.Sin(rotRad);

            {
                var (lx1, ly1) = (wall.X1 * rotCos - wall.Y1 * rotSin, wall.X1 * rotSin + wall.Y1 * rotCos);
                var (lx2, ly2) = (wall.X2 * rotCos - wall.Y2 * rotSin, wall.X2 * rotSin + wall.Y2 * rotCos);

                var x1 = objCenterX + lx1;
                var y1 = objCenterY + ly1;
                var x2 = objCenterX + lx2;
                var y2 = objCenterY + ly2;

                var dx = x2 - x1;
                var dy = y2 - y1;
                var length = Math.Sqrt(dx * dx + dy * dy);

                if (length < 0.1) goto nextItem;

                var angle   = Math.Atan2(dy, dx) * 180.0 / Math.PI;
                var centerX = (x1 + x2) / 2.0;
                var centerY = (y1 + y2) / 2.0;

                lines.Add(new MapRenderRect(
                    centerX - length / 2.0,
                    centerY - wall.Thickness / 2.0,
                    length, wall.Thickness, angle, color, null, 1.0f));
            }
            nextItem:;
        }

        return lines;
    }

    private static IReadOnlyList<MapRenderRect> BuildWallHandles(MainWindowViewModel viewModel)
    {
        var handles = new List<MapRenderRect>();
        var handleColor = new MapRenderColor(1.0f, 1.0f, 1.0f, 1.0f);
        const double handleSize = 8.0;

        foreach (var item in viewModel.MapRenderableItems)
        {
            if (!item.ShouldRenderOnMap || !item.HasWallComponent)
                continue;

            var wall = item.GetComponent<MapEngine.Core.Components.WallComponent>();
            if (wall == null)
                continue;

            var objCenterX = item.MapLeft + (MapViewportConstants.CellSize * item.ScaleX) / 2.0;
            var objCenterY = item.MapTop + (MapViewportConstants.CellSize * item.ScaleY) / 2.0;
            var rotRad = item.Rotation * Math.PI / 180.0;
            var rotCos = Math.Cos(rotRad);
            var rotSin = Math.Sin(rotRad);

            var pts = new[] { (X: wall.X1, Y: wall.Y1), (X: wall.X2, Y: wall.Y2) };
            foreach (var pt in pts)
            {
                var rx = pt.X * rotCos - pt.Y * rotSin;
                var ry = pt.X * rotSin + pt.Y * rotCos;
                var x = objCenterX + rx;
                var y = objCenterY + ry;

                handles.Add(new MapRenderRect(
                    x - handleSize / 2.0,
                    y - handleSize / 2.0,
                    handleSize,
                    handleSize,
                    0,
                    handleColor,
                    null,
                    1.0f));
            }
        }

        return handles;
    }

    private static MapRenderColor ResolveObjectColor(HierarchyItemViewModel item)
        => item.IsSelected
            ? SelectedObjectColor
            : item.IsPreviewInstance
                ? PreviewObjectColor
                : item.ObjectType switch
                {
                    "Token" => TokenColor,
                    "Map" => MapColor,
                    "Prop" or "StaticObject" => PropColor,
                    _ => DefaultObjectColor
                };

    private static MapRenderColor ParseColor(string value)
    {
        var color = Color.Parse(value);
        return new MapRenderColor(
            color.R / 255f,
            color.G / 255f,
            color.B / 255f,
            color.A / 255f);
    }
}
