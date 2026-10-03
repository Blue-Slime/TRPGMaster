namespace MapEngine.Core.Systems;

/// <summary>
/// 2D 射线投射视野计算引擎。
/// 从观察点向四周发射射线，遇到墙壁/关闭的门则截断，
/// 生成可见区域多边形（用于渲染迷雾遮罩）。
/// </summary>
public static class VisionEngine
{
    public struct Segment
    {
        public double X1, Y1, X2, Y2;
    }

    public struct VisibilityResult
    {
        public List<(double X, double Y)> Polygon;
        public double EffectiveRadius;
        /// <summary>
        /// Distance from origin to each polygon vertex (parallel to Polygon list).
        /// Used for calculating fade gradients at vision edges.
        /// </summary>
        public List<double> VertexDistances;
    }

    /// <summary>
    /// 计算从 (ox, oy) 出发、在给定半径内、被墙壁遮挡后的可见区域多边形。
    /// </summary>
    public static VisibilityResult ComputeVisibility(
        double ox, double oy,
        double radius,
        IReadOnlyList<Segment> occluders,
        int rayCount = 360,
        double fadeDistance = 0.2)
    {
        var polygon = new List<(double X, double Y)>(rayCount);
        var distances = new List<double>(rayCount);
        var angleStep = 2.0 * Math.PI / rayCount;

        for (int i = 0; i < rayCount; i++)
        {
            var angle = i * angleStep;
            var dx = Math.Cos(angle);
            var dy = Math.Sin(angle);

            var hitDist = radius;

            for (int s = 0; s < occluders.Count; s++)
            {
                var seg = occluders[s];
                var dist = RaySegmentIntersect(ox, oy, dx, dy, seg.X1, seg.Y1, seg.X2, seg.Y2);
                if (dist.HasValue && dist.Value < hitDist)
                    hitDist = dist.Value;
            }

            polygon.Add((ox + dx * hitDist, oy + dy * hitDist));
            distances.Add(hitDist);
        }

        return new VisibilityResult
        {
            Polygon = polygon,
            EffectiveRadius = radius,
            VertexDistances = distances
        };
    }

    /// <summary>
    /// 检测点 (px, py) 是否在可见多边形内（用于判断某个格子是否可见）。
    /// </summary>
    public static bool IsPointVisible(double px, double py, VisibilityResult visibility)
    {
        return PointInPolygon(px, py, visibility.Polygon);
    }

    /// <summary>
    /// 射线与线段相交检测。返回交点距离，无交点返回 null。
    /// Ray: origin (ox,oy) direction (dx,dy)
    /// Segment: (x1,y1)→(x2,y2)
    /// </summary>
    private static double? RaySegmentIntersect(
        double ox, double oy, double dx, double dy,
        double x1, double y1, double x2, double y2)
    {
        var sx = x2 - x1;
        var sy = y2 - y1;

        var denom = dx * sy - dy * sx;
        if (Math.Abs(denom) < 1e-10)
            return null;

        var t = ((x1 - ox) * sy - (y1 - oy) * sx) / denom;
        var u = ((x1 - ox) * dy - (y1 - oy) * dx) / denom;

        if (t >= 0 && u >= 0 && u <= 1.0)
            return t;

        return null;
    }

    private static bool PointInPolygon(double px, double py, List<(double X, double Y)> polygon)
    {
        bool inside = false;
        int count = polygon.Count;
        for (int i = 0, j = count - 1; i < count; j = i++)
        {
            var (xi, yi) = polygon[i];
            var (xj, yj) = polygon[j];

            if (((yi > py) != (yj > py)) &&
                (px < (xj - xi) * (py - yi) / (yj - yi) + xi))
            {
                inside = !inside;
            }
        }
        return inside;
    }
}
