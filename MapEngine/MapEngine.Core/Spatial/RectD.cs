namespace MapEngine.Core.Spatial;

/// <summary>
/// 双精度轴对齐包围盒(AABB)。空间索引与 dirty 区域的通用几何单元。
/// </summary>
public readonly struct RectD
{
    public readonly double MinX, MinY, MaxX, MaxY;

    public RectD(double minX, double minY, double maxX, double maxY)
    {
        MinX = Math.Min(minX, maxX);
        MinY = Math.Min(minY, maxY);
        MaxX = Math.Max(minX, maxX);
        MaxY = Math.Max(minY, maxY);
    }

    public double Width  => MaxX - MinX;
    public double Height => MaxY - MinY;
    public double CenterX => (MinX + MaxX) * 0.5;
    public double CenterY => (MinY + MaxY) * 0.5;

    public static RectD FromSegment(double x1, double y1, double x2, double y2)
        => new(x1, y1, x2, y2);

    public static RectD FromCircle(double cx, double cy, double r)
        => new(cx - r, cy - r, cx + r, cy + r);

    public bool Intersects(in RectD o)
        => MinX <= o.MaxX && MaxX >= o.MinX && MinY <= o.MaxY && MaxY >= o.MinY;

    public bool Contains(double x, double y)
        => x >= MinX && x <= MaxX && y >= MinY && y <= MaxY;

    public RectD Union(in RectD o)
        => new(Math.Min(MinX, o.MinX), Math.Min(MinY, o.MinY),
               Math.Max(MaxX, o.MaxX), Math.Max(MaxY, o.MaxY));

    /// <summary>向外扩张 margin(用于把 dirty 区域放大一圈,覆盖边界情况)。</summary>
    public RectD Inflate(double margin)
        => new(MinX - margin, MinY - margin, MaxX + margin, MaxY + margin);

    public override string ToString() => $"RectD[{MinX:F1},{MinY:F1} → {MaxX:F1},{MaxY:F1}]";
}

/// <summary>
/// 具备空间范围的组件实现此接口,World 据此把宿主对象登记进空间索引。
/// 返回的是<b>本地</b>包围盒(相对宿主 Transform),World 负责叠加父链变换。
/// P0 阶段暂按本地=世界处理(不做层级变换),留待后续 TransformSystem 补全。
/// </summary>
public interface ISpatialComponent
{
    RectD GetLocalBounds();
}
