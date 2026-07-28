namespace MapEngine.Core.Scene;

/// <summary>
/// 网格系统：吸附、测距、坐标转换。
/// 支持正方形网格和六角网格（第一版只实现正方形）。
/// </summary>
public sealed class GridSystem
{
    public double CellSize { get; set; } = 50;
    public double FeetPerCell { get; set; } = 5;
    public GridType Type { get; set; } = GridType.Square;
    public DiagonalRule DiagonalRule { get; set; } = DiagonalRule.Alternating;

    /// <summary>
    /// 将世界坐标吸附到最近的格子中心。
    /// </summary>
    public (double X, double Y) SnapToGrid(double worldX, double worldY)
    {
        var cellX = Math.Round(worldX / CellSize) * CellSize;
        var cellY = Math.Round(worldY / CellSize) * CellSize;
        return (cellX, cellY);
    }

    /// <summary>
    /// 将世界坐标转换为格子坐标（整数）。
    /// </summary>
    public (int Col, int Row) WorldToCell(double worldX, double worldY)
    {
        return ((int)Math.Floor(worldX / CellSize), (int)Math.Floor(worldY / CellSize));
    }

    /// <summary>
    /// 将格子坐标转换为世界坐标（格子中心）。
    /// </summary>
    public (double X, double Y) CellToWorld(int col, int row)
    {
        return ((col + 0.5) * CellSize, (row + 0.5) * CellSize);
    }

    /// <summary>
    /// 计算两点之间的距离（格子数），考虑对角线规则。
    /// D&D 5e: 1-2-1 交替对角线 或 1-1-1 简单对角线。
    /// </summary>
    public double MeasureDistanceInCells(double x1, double y1, double x2, double y2)
    {
        var (c1, r1) = WorldToCell(x1, y1);
        var (c2, r2) = WorldToCell(x2, y2);

        var dx = Math.Abs(c2 - c1);
        var dy = Math.Abs(r2 - r1);

        return DiagonalRule switch
        {
            DiagonalRule.Simple => Math.Max(dx, dy),
            DiagonalRule.Alternating => ComputeAlternatingDiagonal(dx, dy),
            DiagonalRule.Euclidean => Math.Sqrt(dx * dx + dy * dy),
            _ => Math.Max(dx, dy)
        };
    }

    /// <summary>
    /// 计算两点之间的距离（ft 或 m）。
    /// </summary>
    public double MeasureDistanceInFeet(double x1, double y1, double x2, double y2)
    {
        return MeasureDistanceInCells(x1, y1, x2, y2) * FeetPerCell;
    }

    /// <summary>
    /// 格式化距离显示。
    /// </summary>
    public string FormatDistance(double x1, double y1, double x2, double y2)
    {
        var cells = MeasureDistanceInCells(x1, y1, x2, y2);
        var feet = cells * FeetPerCell;
        return $"{feet:F0} ft ({cells:F0} 格)";
    }

    /// <summary>
    /// D&D 5e 1-2-1 交替对角线规则：
    /// 第奇数次对角移动花费 1 格，第偶数次花费 2 格。
    /// 等价于 straight + diag/2（向上取整）。
    /// </summary>
    private static double ComputeAlternatingDiagonal(int dx, int dy)
    {
        var straight = Math.Abs(dx - dy);
        var diag = Math.Min(dx, dy);
        return straight + diag + diag / 2;
    }
}

public enum GridType
{
    Square,
    HexPointy,
    HexFlat
}

/// <summary>
/// 对角线移动规则（D&D 5e 常用）。
/// </summary>
public enum DiagonalRule
{
    /// <summary>1-1-1: 对角线和直线等价（Chebyshev 距离）</summary>
    Simple,
    /// <summary>1-2-1: 交替对角线（D&D 5e 可选规则）</summary>
    Alternating,
    /// <summary>欧几里得距离</summary>
    Euclidean
}
