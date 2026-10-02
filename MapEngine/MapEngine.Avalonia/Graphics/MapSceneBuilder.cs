using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using MapEngine.Core.Components;
using MapEngine.Core.Scene;
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
        var (graphNodes, graphLinks) = BuildGraph(viewModel);
        var (tokenLabels, tokenHealthBars, tokenBadges) = BuildTokenUI(viewModel);

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
            WallHandles = wallHandles,
            SelectionHandles = BuildSelectionHandles(viewModel),
            VectorShapes = BuildVectorShapes(viewModel),
            GraphLinks = graphLinks,
            GraphNodes = graphNodes,
            LightSources = BuildLightSources(viewModel),
            // ConditionBadges 已移至 TokenUIManager（Avalonia UI 层），此处不再在 GL 层渲染
            ConditionBadges = Array.Empty<MapRenderConditionBadge>(),
            FogEnabled = viewModel.IsFogEnabled,
            FogRevealedPolygons = viewModel.IsFogEnabled
                ? viewModel.FogRevealedRegions.Select(r => r.Points).ToList()
                : Array.Empty<IReadOnlyList<(double, double)>>(),
            TokenLabels = tokenLabels,
            TokenHealthBars = tokenHealthBars,
            TokenBadges = tokenBadges,
        };
    }

    private static IReadOnlyList<MapRenderLight> BuildLightSources(MainWindowViewModel viewModel)
    {
        var lights = new List<MapRenderLight>();
        foreach (var item in viewModel.MapRenderableItems)
        {
            if (!item.ShouldRenderOnMap) continue;
            var light = item.GetComponent<LightComponent>();
            if (light is null || light.BrightRadius <= 0 && light.DimRadius <= 0) continue;

            var cx = item.MapLeft + (MapViewportConstants.CellSize * item.ScaleX) / 2.0;
            var cy = item.MapTop  + (MapViewportConstants.CellSize * item.ScaleY) / 2.0;

            var color = ParseHexColor(light.Color, defaultAlpha: 0.55f);

            lights.Add(new MapRenderLight
            {
                CenterX      = cx,
                CenterY      = cy,
                BrightRadius = light.BrightRadius * MapViewportConstants.CellSize,
                DimRadius    = light.DimRadius    * MapViewportConstants.CellSize,
                Color        = color,
                IsCone       = light.Shape == LightShape.Cone,
                ConeDirection = light.ConeDirection,
                ConeHalfAngle = light.ConeAngle / 2.0,
            });
        }
        return lights;
    }

    /// <summary>解析 #RRGGBB 或 #AARRGGBB 颜色字符串为渲染色。解析失败返回暖黄色。</summary>
    private static MapRenderColor ParseHexColor(string hex, float defaultAlpha)
    {
        try
        {
            var s = hex.TrimStart('#');
            if (s.Length == 6)
            {
                var r = Convert.ToInt32(s[..2], 16) / 255f;
                var g = Convert.ToInt32(s[2..4], 16) / 255f;
                var b = Convert.ToInt32(s[4..6], 16) / 255f;
                return new MapRenderColor(r, g, b, defaultAlpha);
            }
            if (s.Length == 8)
            {
                var a = Convert.ToInt32(s[..2], 16) / 255f;
                var r = Convert.ToInt32(s[2..4], 16) / 255f;
                var g = Convert.ToInt32(s[4..6], 16) / 255f;
                var b = Convert.ToInt32(s[6..8], 16) / 255f;
                return new MapRenderColor(r, g, b, a);
            }
        }
        catch { /* fall through */ }
        return new MapRenderColor(1f, 0.87f, 0.53f, defaultAlpha);
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
                new MapRenderColor(1.0f, 1.0f, 1.0f, 1.0f),
                "Rectangle")
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

            // Shape/Text 由专门的通道绘制（BuildVectorShapes / 文本覆盖层），
            // 不能再叠一个 36px 占位方块，否则矢量图形上会糊一块纯色。
            if (item.ObjectType is "Shape" or "Text")
            {
                continue;
            }

            // 阴影方块已移除 — 后续通过 alpha 轮廓描边实现选中高亮（参考 token-alpha-clipping-plan.md）

            var spritePath = MapSpriteAssetResolver.ResolveSpritePath(item.AssetRef);
            var accentColor = ResolveObjectColor(item);
            // MapObjectSize(36) < SpriteWidth(50)；矩形必须在格子内居中，否则比格子小的矩形会偏向左上角
            var bodySize   = MapViewportConstants.MapObjectSize;
            var bodyOffset = (item.SpriteWidth  - bodySize) / 2.0;  // = (50-36)/2 = 7 at scale=1
            var bodyOffY   = (item.SpriteHeight - bodySize) / 2.0;
            if (item.IsSelected || item.IsPreviewInstance)
            {
                // 选框比 body 大 2px（各边），保持同一视觉中心
                objects.Add(new MapRenderRect(
                    item.MapLeft + bodyOffset - 2,
                    item.MapTop  + bodyOffY   - 2,
                    bodySize + 4,
                    bodySize + 4,
                    item.Rotation,
                    accentColor,
                    null,
                    1.0f));
            }
            else if (string.IsNullOrWhiteSpace(spritePath))
            {
                objects.Add(new MapRenderRect(
                    item.MapLeft + bodyOffset,
                    item.MapTop  + bodyOffY,
                    bodySize,
                    bodySize,
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

            var spritePath = MapSpriteAssetResolver.ResolveSpritePath(item.AssetRef);
            if (string.IsNullOrWhiteSpace(spritePath))
            {
                continue;
            }

            var spriteWidth = MapViewportConstants.CellSize * item.ScaleX;
            var spriteHeight = MapViewportConstants.CellSize * item.ScaleY;

            // 获取 Token Shape（如果有 TokenComponent）
            var tokenShape = item.BackingObject?.GetComponent<MapEngine.Core.Components.TokenComponent>()?.Shape ?? "Rectangle";

            sprites.Add(new MapRenderSprite(
                item.MapLeft,
                item.MapTop,
                Math.Max(MapViewportConstants.CellSize * 0.5, spriteWidth),
                Math.Max(MapViewportConstants.CellSize * 0.5, spriteHeight),
                item.Rotation,
                spritePath,
                new MapRenderColor(1.0f, 1.0f, 1.0f, (float)Math.Clamp(item.Opacity, 0.0, 1.0)),
                tokenShape));
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
            if (!item.ShouldRenderOnMap)
                continue;

            // ── 旧墙体组件（单线段）────────────────────────────────────
            if (item.HasWallComponent)
            {
                var wall = item.GetComponent<MapEngine.Core.Components.WallComponent>();
                if (wall == null) continue;

                var color = wall.Door != MapEngine.Core.Components.DoorKind.None
                    ? (wall.State == MapEngine.Core.Components.DoorState.Open ? doorOpenColor : doorClosedColor)
                    : (wall.Sight == MapEngine.Core.Components.SenseLevel.None ? windowColor : wallColor);

                var objCenterX = item.MapLeft + (MapViewportConstants.CellSize * item.ScaleX) / 2.0;
                var objCenterY = item.MapTop + (MapViewportConstants.CellSize * item.ScaleY) / 2.0;
                var rotRad = item.Rotation * Math.PI / 180.0;
                var rotCos = Math.Cos(rotRad);
                var rotSin = Math.Sin(rotRad);

                var (lx1, ly1) = (wall.X1 * rotCos - wall.Y1 * rotSin, wall.X1 * rotSin + wall.Y1 * rotCos);
                var (lx2, ly2) = (wall.X2 * rotCos - wall.Y2 * rotSin, wall.X2 * rotSin + wall.Y2 * rotCos);

                var x1 = objCenterX + lx1;
                var y1 = objCenterY + ly1;
                var x2 = objCenterX + lx2;
                var y2 = objCenterY + ly2;

                var dx = x2 - x1;
                var dy = y2 - y1;
                var length = Math.Sqrt(dx * dx + dy * dy);

                if (length < 0.1) continue;

                var angle   = Math.Atan2(dy, dx) * 180.0 / Math.PI;
                var centerX = (x1 + x2) / 2.0;
                var centerY = (y1 + y2) / 2.0;

                lines.Add(new MapRenderRect(
                    centerX - length / 2.0,
                    centerY - wall.Thickness / 2.0,
                    length, wall.Thickness, angle, color, null, 1.0f));
            }

            // ── 墙体路径组件（多锚点）──────────────────────────────────
            if (item.ObjectType == "WallPathV2")
            {
                var wallPath = item.GetComponent<MapEngine.Core.Components.WallPathComponent>();
                if (wallPath == null || wallPath.Points.Count < 2) continue;

                var thickness = wallPath.Thickness;
                var baseColor = ParseHexColor(wallPath.Color, defaultAlpha: 1.0f);

                // 构建门窗区段集合（用于着色区分）
                var doorSegments = new HashSet<int>();
                var doorColors = new Dictionary<int, MapRenderColor>();
                foreach (var door in wallPath.Doors)
                {
                    var color = door.Kind switch
                    {
                        MapEngine.Core.Components.DoorKind.Window => windowColor,
                        _ => door.State == MapEngine.Core.Components.DoorState.Open ? doorOpenColor : doorClosedColor
                    };

                    foreach (var segIdx in door.GetCoveredSegments())
                    {
                        doorSegments.Add(segIdx);
                        doorColors[segIdx] = color;
                    }
                }

                // 绘制每条线段
                int segmentCount = wallPath.IsClosed ? wallPath.Points.Count : wallPath.Points.Count - 1;
                for (int i = 0; i < segmentCount; i++)
                {
                    var p1 = wallPath.Points[i];
                    var p2 = wallPath.Points[(i + 1) % wallPath.Points.Count];

                    var x1 = MapViewportConstants.WorldOriginContent + p1.X;
                    var y1 = MapViewportConstants.WorldOriginContent - p1.Y;
                    var x2 = MapViewportConstants.WorldOriginContent + p2.X;
                    var y2 = MapViewportConstants.WorldOriginContent - p2.Y;

                    var dx = x2 - x1;
                    var dy = y2 - y1;
                    var length = Math.Sqrt(dx * dx + dy * dy);

                    if (length < 0.1) continue;

                    var angle = Math.Atan2(dy, dx) * 180.0 / Math.PI;
                    var centerX = (x1 + x2) / 2.0;
                    var centerY = (y1 + y2) / 2.0;

                    var segmentColor = doorSegments.Contains(i) && doorColors.TryGetValue(i, out var dc)
                        ? dc
                        : baseColor;

                    lines.Add(new MapRenderRect(
                        centerX - length / 2.0,
                        centerY - thickness / 2.0,
                        length, thickness, angle, segmentColor, null, 1.0f));
                }
            }
        }

        return lines;
    }

    private static IReadOnlyList<MapRenderRect> BuildWallHandles(MainWindowViewModel viewModel)
    {
        var handles = new List<MapRenderRect>();
        var normalHandleColor = new MapRenderColor(0.22f, 0.59f, 0.93f, 1.0f); // 蓝色：普通锚点
        var doorHandleColor = new MapRenderColor(0.95f, 0.61f, 0.07f, 1.0f);   // 橙色：门窗锚点
        const double handleSize = 8.0;

        foreach (var item in viewModel.MapRenderableItems)
        {
            if (!item.ShouldRenderOnMap)
                continue;

            // ── 旧墙体组件（单线段）────────────────────────────────────
            if (item.HasWallComponent)
            {
                var wall = item.GetComponent<MapEngine.Core.Components.WallComponent>();
                if (wall == null) continue;

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
                        normalHandleColor,
                        null,
                        1.0f));
                }
            }

            // ── 墙体路径组件（多锚点可编辑）──────────────────────────
            if (item.ObjectType == "WallPathV2" && item.IsSelected)
            {
                var wallPath = item.GetComponent<MapEngine.Core.Components.WallPathComponent>();
                if (wallPath == null || wallPath.Points.Count == 0) continue;

                // 构建门窗锚点索引集合（用于着色区分）
                var doorAnchorIndices = new HashSet<int>();
                foreach (var door in wallPath.Doors)
                {
                    doorAnchorIndices.Add(door.StartAnchorIndex);
                    doorAnchorIndices.Add(door.EndAnchorIndex);
                }

                // 为每个锚点生成 Handle
                for (int i = 0; i < wallPath.Points.Count; i++)
                {
                    var (wx, wy) = wallPath.Points[i];
                    var cx = MapViewportConstants.WorldOriginContent + wx;
                    var cy = MapViewportConstants.WorldOriginContent - wy;

                    var color = doorAnchorIndices.Contains(i) ? doorHandleColor : normalHandleColor;

                    handles.Add(new MapRenderRect(
                        cx - handleSize / 2.0,
                        cy - handleSize / 2.0,
                        handleSize,
                        handleSize,
                        0,
                        color,
                        null,
                        1.0f));
                }

                // ── 门窗图标（在锚点区间的中点绘制） ──────────────────
                foreach (var door in wallPath.Doors)
                {
                    if (door.StartAnchorIndex >= wallPath.Points.Count || door.EndAnchorIndex >= wallPath.Points.Count)
                        continue;

                    var p1 = wallPath.Points[door.StartAnchorIndex];
                    var p2 = wallPath.Points[door.EndAnchorIndex];

                    // 门窗图标绘制在起止锚点的中点
                    var iconWorldX = (p1.X + p2.X) / 2.0;
                    var iconWorldY = (p1.Y + p2.Y) / 2.0;

                    var iconCX = MapViewportConstants.WorldOriginContent + iconWorldX;
                    var iconCY = MapViewportConstants.WorldOriginContent - iconWorldY;

                    // 门窗图标颜色
                    var iconColor = door.Kind switch
                    {
                        MapEngine.Core.Components.DoorKind.Window => new MapRenderColor(0.4f, 0.6f, 0.9f, 1.0f), // 蓝色：窗户
                        MapEngine.Core.Components.DoorKind.Archway => new MapRenderColor(0.3f, 0.7f, 0.3f, 1.0f), // 绿色：拱门
                        MapEngine.Core.Components.DoorKind.Secret => new MapRenderColor(0.8f, 0.2f, 0.8f, 1.0f), // 紫色：密门
                        _ => door.State switch
                        {
                            MapEngine.Core.Components.DoorState.Open => new MapRenderColor(0.3f, 0.7f, 0.3f, 0.9f),   // 绿色：开启
                            MapEngine.Core.Components.DoorState.Locked => new MapRenderColor(0.9f, 0.2f, 0.2f, 1.0f), // 红色：锁定
                            _ => new MapRenderColor(0.95f, 0.61f, 0.07f, 1.0f)  // 橙色：关闭
                        }
                    };

                    const double iconSize = 10.0;
                    handles.Add(new MapRenderRect(
                        iconCX - iconSize / 2.0,
                        iconCY - iconSize / 2.0,
                        iconSize,
                        iconSize,
                        0,
                        iconColor,
                        null,
                        1.0f));
                }
            }
        }

        return handles;
    }

    /// <summary>
    /// 为选中对象生成旋转 handle（正上方圆形占位矩形）和四角缩放 handle。
    /// 每个选中对象生成 6 个 handle rect：
    ///   [0]   旋转 handle（正上方，空心圆用细边框近似 = 两个重叠矩形）
    ///   [1-4] 四角缩放 handle（左上/右上/右下/左下）
    ///   [5]   旋转连线（精灵中心到旋转 handle 的细线）
    /// 碰撞检测在 View 层用世界坐标进行，与渲染是独立的。
    /// </summary>
    private static IReadOnlyList<MapRenderRect> BuildSelectionHandles(MainWindowViewModel viewModel)
    {
        var handles = new List<MapRenderRect>();
        var handleColor   = new MapRenderColor(1.0f, 1.0f, 1.0f, 1.0f);        // 白色填充
        var handleBorder  = new MapRenderColor(0.10f, 0.55f, 0.90f, 1.0f);     // 蓝色边框
        var lineColor     = new MapRenderColor(1.0f, 1.0f, 1.0f, 0.60f);       // 连接线半透明白
        const double cornerSize  = 9.0;   // 四角 handle 边长（内容像素）
        const double rotSize     = 10.0;  // 旋转 handle 直径
        const double rotOffset   = 20.0;  // 距精灵顶边的距离
        const double lineThick   = 2.0;   // 连接线粗细

        foreach (var item in viewModel.MapRenderableItems)
        {
            if (!item.IsSelected || !item.ShouldRenderOnMap) continue;

            var cx   = item.MapLeft + item.SpriteWidth  / 2.0;
            var cy   = item.MapTop  + item.SpriteHeight / 2.0;
            var hw   = item.SpriteWidth  / 2.0;
            var hh   = item.SpriteHeight / 2.0;
            var rotRad = item.Rotation * Math.PI / 180.0;
            var rc   = Math.Cos(rotRad);
            var rs   = Math.Sin(rotRad);

            // ── 旋转连接线（精灵中心 → 旋转 handle 中心）──────────────────
            // 旋转 handle 在精灵局部坐标 (0, -(hh + rotOffset + rotSize/2)) 处
            var rotHandleLocalY = -(hh + rotOffset + rotSize / 2.0);
            var rotHandleWorldX = cx + (0.0 * rc - rotHandleLocalY * rs);
            var rotHandleWorldY = cy + (0.0 * rs + rotHandleLocalY * rc);

            // 连线：从精灵顶边中点到旋转 handle 中心
            var lineTopLocalY   = -hh;
            var lineTopWorldX   = cx + (0.0 * rc - lineTopLocalY * rs);
            var lineTopWorldY   = cy + (0.0 * rs + lineTopLocalY * rc);
            var lineDx = rotHandleWorldX - lineTopWorldX;
            var lineDy = rotHandleWorldY - lineTopWorldY;
            var lineLen = Math.Sqrt(lineDx * lineDx + lineDy * lineDy);
            if (lineLen > 1.0)
            {
                var lineAngle = Math.Atan2(lineDy, lineDx) * 180.0 / Math.PI;
                handles.Add(new MapRenderRect(
                    (lineTopWorldX + rotHandleWorldX) / 2.0 - lineLen / 2.0,
                    (lineTopWorldY + rotHandleWorldY) / 2.0 - lineThick / 2.0,
                    lineLen, lineThick, lineAngle, lineColor, null, 1.0f));
            }

            // ── 旋转 handle（外框蓝色 + 内填白色，模拟空心圆）─────────────
            const double borderThick = 2.0;
            handles.Add(new MapRenderRect(
                rotHandleWorldX - rotSize / 2.0 - borderThick,
                rotHandleWorldY - rotSize / 2.0 - borderThick,
                rotSize + borderThick * 2.0, rotSize + borderThick * 2.0,
                item.Rotation, handleBorder, null, 1.0f));
            handles.Add(new MapRenderRect(
                rotHandleWorldX - rotSize / 2.0,
                rotHandleWorldY - rotSize / 2.0,
                rotSize, rotSize,
                item.Rotation, handleColor, null, 1.0f));

            // ── 四角缩放 handle ────────────────────────────────────────────
            // 局部坐标的四个角：(-hw,-hh) (hw,-hh) (hw,hh) (-hw,hh)
            var corners = new[]
            {
                (-hw, -hh), (hw, -hh), (hw, hh), (-hw, hh)
            };
            foreach (var (lx, ly) in corners)
            {
                var wx = cx + (lx * rc - ly * rs);
                var wy = cy + (lx * rs + ly * rc);
                // 蓝色边框
                handles.Add(new MapRenderRect(
                    wx - cornerSize / 2.0 - borderThick,
                    wy - cornerSize / 2.0 - borderThick,
                    cornerSize + borderThick * 2.0, cornerSize + borderThick * 2.0,
                    item.Rotation, handleBorder, null, 1.0f));
                // 白色填充
                handles.Add(new MapRenderRect(
                    wx - cornerSize / 2.0,
                    wy - cornerSize / 2.0,
                    cornerSize, cornerSize,
                    item.Rotation, handleColor, null, 1.0f));
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

    private static IReadOnlyList<MapVectorShape> BuildVectorShapes(MainWindowViewModel viewModel)
    {
        var shapes = new List<MapVectorShape>();

        foreach (var item in viewModel.MapRenderableItems)
        {
            if (!item.ShouldRenderOnMap || item.ObjectType != "Shape")
                continue;

            var comp = item.GetComponent<ShapeComponent>();
            if (comp == null) continue;

            var stroke = ParseColor(comp.StrokeColor);
            var fill   = ParseColor(comp.FillColor);
            var cx = MapViewportConstants.WorldOriginContent + item.X;
            var cy = MapViewportConstants.WorldOriginContent - item.Y;

            var shapeType = comp.ShapeType switch
            {
                "line"     => VectorShapeType.Line,
                "ellipse"  => VectorShapeType.Ellipse,
                "circle"   => VectorShapeType.Ellipse,
                "cone"     => VectorShapeType.Cone,
                "wedge"    => VectorShapeType.Wedge,
                "polygon"  => VectorShapeType.Polygon,
                "freehand" => VectorShapeType.Freehand,
                _          => VectorShapeType.Rect,
            };

            var strokeStyle = comp.StrokeStyle switch
            {
                StrokeStyle.Dashed => VectorStrokeStyle.Dashed,
                StrokeStyle.Dotted => VectorStrokeStyle.Dotted,
                _                  => VectorStrokeStyle.Solid,
            };

            // 多边形/自由笔触：顶点转为绝对内容坐标
            var absPoints = comp.Points.Count > 0
                ? comp.Points.Select(p => (cx + p.X, cy - p.Y)).ToList()
                : (IReadOnlyList<(double, double)>)Array.Empty<(double, double)>();

            shapes.Add(new MapVectorShape
            {
                Type        = shapeType,
                CenterX     = cx,
                CenterY     = cy,
                Width       = comp.Width,
                Height      = comp.Height,
                X2          = cx + comp.X2,
                Y2          = cy - comp.Y2,
                Points      = absPoints,
                Direction   = comp.Rotation,
                HalfAngle   = comp.ConeAngle,
                Radius      = comp.ConeRadius,
                StrokeColor = stroke,
                FillColor   = fill,
                StrokeWidth = (float)comp.StrokeWidth,
                IsFilled    = comp.IsFilled,
                StrokeStyle = strokeStyle,
            });
        }

        return shapes;
    }

    /// <summary>
    /// 构建拓扑图渲染数据（节点图标 + 连线）。
    ///
    /// 连线两端坐标在此解析完毕，渲染层不再回查节点。
    /// 非 GM 视角下隐藏未揭示的节点/边；GM 视角保留但降透明度。
    /// </summary>
    private static (IReadOnlyList<MapRenderGraphNode> nodes, IReadOnlyList<MapRenderGraphLink> links)
        BuildGraph(MainWindowViewModel viewModel)
    {
        var nodes = new List<MapRenderGraphNode>();
        var links = new List<MapRenderGraphLink>();

        // 先建 Id→节点 的索引，连线要按 TargetNodeId 找对端坐标
        var nodeItems = new Dictionary<string, HierarchyItemViewModel>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in viewModel.MapRenderableItems)
        {
            if (item.GetComponent<GraphNodeComponent>() is not null)
                nodeItems[item.BackingObject.Id.ToString()] = item;
        }

        // GM 能看到未揭示内容（降透明度提示），玩家完全看不到
        var isGm = viewModel.Host?.Role is null or MapEngine.Core.Hosting.UserRole.GM;
        var selectedId = viewModel.SelectedHierarchyItem?.BackingObject.Id.ToString();

        static (double X, double Y) CenterOf(HierarchyItemViewModel item)
            => (MapViewportConstants.WorldOriginContent + item.X,
                MapViewportConstants.WorldOriginContent - item.Y);

        // ── 连线（先于节点，保证节点图标压在线上）─────────────────────────
        foreach (var (fromId, fromItem) in nodeItems)
        {
            if (!fromItem.ShouldRenderOnMap) continue;

            foreach (var link in fromItem.BackingObject.GetComponents<GraphLinkComponent>())
            {
                if (link.Visibility == GraphVisibility.Hidden && !isGm) continue;
                if (!nodeItems.TryGetValue(link.TargetNodeId, out var toItem)) continue; // 悬空边跳过
                if (!toItem.ShouldRenderOnMap) continue;

                var (x1, y1) = CenterOf(fromItem);
                var (x2, y2) = CenterOf(toItem);

                // 未揭示（GM 视角）0.35，封锁 0.5，正常 1.0
                var opacity = link.Visibility == GraphVisibility.Hidden ? 0.35f
                            : link.IsPassable ? 1f : 0.5f;

                links.Add(new MapRenderGraphLink
                {
                    LinkId      = link.LinkId,
                    X1 = x1, Y1 = y1,
                    X2 = x2, Y2 = y2,
                    Color       = ParseColor(link.Color),
                    Width       = (float)link.Width,
                    StrokeStyle = link.StrokeStyle switch
                    {
                        StrokeStyle.Dashed => VectorStrokeStyle.Dashed,
                        StrokeStyle.Dotted => VectorStrokeStyle.Dotted,
                        _                  => VectorStrokeStyle.Solid,
                    },
                    ShowArrow   = !link.IsBidirectional,
                    Opacity     = opacity,
                    IsSelected  = false,
                });
            }
        }

        // ── 节点图标 ──────────────────────────────────────────────────────
        foreach (var (id, item) in nodeItems)
        {
            if (!item.ShouldRenderOnMap) continue;

            var comp = item.GetComponent<GraphNodeComponent>()!;
            if (comp.RenderMode is GraphNodeRenderMode.None or GraphNodeRenderMode.Content)
                continue; // 纯逻辑节点 / 只铺内容不画图标
            if (comp.Visibility == GraphVisibility.Hidden && !isGm) continue;

            var (cx, cy) = CenterOf(item);

            // 图标走素材库解析，解析不到就退回纯色形状
            var texturePath = string.IsNullOrEmpty(comp.IconAssetRef)
                ? null
                : MapSpriteAssetResolver.ResolveSpritePath(comp.IconAssetRef);

            nodes.Add(new MapRenderGraphNode
            {
                ObjectId    = id,
                CenterX     = cx,
                CenterY     = cy,
                Size        = comp.Size,
                Shape       = comp.Shape?.ToLowerInvariant() switch
                {
                    "square"  => GraphNodeShape.Square,
                    "diamond" => GraphNodeShape.Diamond,
                    _         => GraphNodeShape.Circle,
                },
                FillColor   = ParseColor(comp.Color),
                TexturePath = string.IsNullOrEmpty(texturePath) ? null : texturePath,
                Opacity     = comp.Visibility == GraphVisibility.Hidden ? 0.4f : 1f,
                IsSelected  = id.Equals(selectedId, StringComparison.OrdinalIgnoreCase),
            });
        }

        return (nodes, links);
    }

    /// <summary>
    /// 【已废弃】构建 Token 状态徽章 - 已移至 TokenUIManager（Avalonia UI 层）。
    /// GL 层不再渲染徽章，改由 UI overlay 层渲染 emoji 文本。
    /// </summary>
    [Obsolete("ConditionBadges 已移至 TokenUIManager，此方法保留供参考")]
    private static IReadOnlyList<MapRenderConditionBadge> BuildConditionBadges(MainWindowViewModel viewModel)
    {
        var badges = new List<MapRenderConditionBadge>();
        const double badgeSize = 28.0;    // 徽章圆角矩形边长（世界单位）
        const double badgeSpacing = 4.0;  // 徽章之间的间距
        const double badgeOffset = 8.0;   // Token 底边到徽章顶边的距离
        const int maxVisibleBadges = 5;   // 最多显示 5 个徽章

        foreach (var item in viewModel.MapRenderableItems)
        {
            if (!item.ShouldRenderOnMap) continue;
            var token = item.GetComponent<TokenComponent>();
            if (token is null || token.Conditions.Count == 0) continue;

            // Token 底边中心点
            var tokenBottom = item.MapTop + item.SpriteHeight;
            var tokenCenterX = item.MapLeft + item.SpriteWidth / 2.0;

            // 显示的状态数量（最多 5 个）
            var visibleCount = Math.Min(token.Conditions.Count, maxVisibleBadges);
            var totalWidth = visibleCount * badgeSize + (visibleCount - 1) * badgeSpacing;
            var startX = tokenCenterX - totalWidth / 2.0;

            // 绘制前 5 个状态徽章
            for (var i = 0; i < visibleCount; i++)
            {
                var condition = token.Conditions[i];
                var badgeX = startX + i * (badgeSize + badgeSpacing);
                var badgeY = tokenBottom + badgeOffset;

                var bgColor = ParseHexColor(condition.ColorHex, defaultAlpha: 0.9f);

                badges.Add(new MapRenderConditionBadge(
                    badgeX,
                    badgeY,
                    badgeSize,
                    condition.Icon,
                    condition.StackCount,
                    bgColor
                ));
            }

            // TODO: 超出 5 个时显示 +N 徽章（需要额外渲染逻辑支持文本）
        }

        return badges;
    }

    private static MapRenderColor ParseColor(string value)
    {
        var color = Color.Parse(value);
        return new MapRenderColor(
            color.R / 255f,
            color.G / 255f,
            color.B / 255f,
            color.A / 255f);
    }

    /// <summary>
    /// 构建 Token UI 数据（名称标签 + HP 条 + 状态徽章）。
    /// 仅渲染 ShouldRenderOnMap=true 且 Name 非空的 Token。
    /// </summary>
    private static (
        IReadOnlyList<SkiaLabel> labels,
        IReadOnlyList<SkiaHealthBar> healthBars,
        IReadOnlyList<SkiaBadge> badges
    ) BuildTokenUI(MainWindowViewModel viewModel)
    {
        var labels = new List<SkiaLabel>();
        var healthBars = new List<SkiaHealthBar>();
        var badges = new List<SkiaBadge>();

        const double labelOffsetY = 30.0;      // 名称标签位于 Token 上方 30px
        const double hpBarOffsetY = 20.0;      // HP 条位于 Token 上方 20px
        const double hpBarWidth = 48.0;        // HP 条宽度
        const double hpBarHeight = 6.0;        // HP 条高度
        const double badgeSize = 24.0;         // 徽章边长
        const double badgeSpacing = 4.0;       // 徽章间距
        const int maxBadges = 5;               // 最多显示 5 个徽章

        foreach (var item in viewModel.MapRenderableItems)
        {
            if (!item.ShouldRenderOnMap)
                continue;

            var token = item.GetComponent<TokenComponent>();
            if (token is null || string.IsNullOrWhiteSpace(token.TokenName))
                continue;

            // Token 中心点
            var tokenCenterX = item.MapLeft + item.SpriteWidth / 2.0;
            var tokenTop = item.MapTop;

            // ── 名称标签（Token 上方 30px）──────────────────────────────
            labels.Add(new SkiaLabel
            {
                Text = token.TokenName,
                CenterX = tokenCenterX,
                CenterY = tokenTop - labelOffsetY,
                FontSize = 14f,
                Color = new MapRenderColor(1f, 1f, 1f, 1f)
            });

            // ── HP 条（Token 上方 20px）────────────────────────────────
            var hpPercentage = token.MaxHP > 0
                ? Math.Clamp(token.CurrentHP / (float)token.MaxHP, 0f, 1f)
                : 1f;

            healthBars.Add(new SkiaHealthBar
            {
                X = tokenCenterX - hpBarWidth / 2.0,
                Y = tokenTop - hpBarOffsetY,
                Width = hpBarWidth,
                Height = hpBarHeight,
                Percentage = hpPercentage,
                CurrentHP = token.CurrentHP,
                MaxHP = token.MaxHP
            });

            // ── 状态徽章（Token 右上角）────────────────────────────────
            if (token.Conditions.Count > 0)
            {
                var visibleCount = Math.Min(token.Conditions.Count, maxBadges);
                var totalWidth = visibleCount * badgeSize + (visibleCount - 1) * badgeSpacing;
                var startX = tokenCenterX + item.SpriteWidth / 2.0 - totalWidth;

                for (var i = 0; i < visibleCount; i++)
                {
                    var condition = token.Conditions[i];
                    var badgeCenterX = startX + i * (badgeSize + badgeSpacing) + badgeSize / 2.0;
                    var badgeCenterY = tokenTop + badgeSize / 2.0;

                    var bgColor = ParseHexColor(condition.ColorHex, defaultAlpha: 0.9f);

                    badges.Add(new SkiaBadge
                    {
                        CenterX = badgeCenterX,
                        CenterY = badgeCenterY,
                        Size = badgeSize,
                        Icon = condition.Icon,
                        StackCount = condition.StackCount,
                        BackgroundColor = bgColor
                    });
                }
            }
        }

        return (labels, healthBars, badges);
    }
}
