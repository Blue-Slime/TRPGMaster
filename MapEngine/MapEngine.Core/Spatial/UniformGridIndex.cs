using MapEngine.Core.Components;

namespace MapEngine.Core.Spatial;

/// <summary>
/// 均匀网格空间索引。把世界切成固定边长的格子,对象按其包围盒覆盖的格子登记。
/// 实现简单、插入/查询稳定,适合 P0 打通闭环。
/// 局限:超大对象或极稀疏分布时效率不如四叉树 —— 那是 P2 的替换目标。
/// 本类非线程安全,调用方(World)负责串行访问。
/// </summary>
public sealed class UniformGridIndex : ISpatialIndex
{
    private readonly double _cellSize;
    private readonly Dictionary<(int cx, int cy), HashSet<GameObject>> _cells = new();
    private readonly Dictionary<GameObject, RectD> _bounds = new();

    public UniformGridIndex(double cellSize = 256.0)
    {
        if (cellSize <= 0) throw new ArgumentOutOfRangeException(nameof(cellSize));
        _cellSize = cellSize;
    }

    public int Count => _bounds.Count;

    public void Insert(GameObject obj, in RectD bounds)
    {
        if (_bounds.ContainsKey(obj)) Remove(obj);

        _bounds[obj] = bounds;
        foreach (var key in CellsCovering(bounds))
        {
            if (!_cells.TryGetValue(key, out var set))
                _cells[key] = set = [];
            set.Add(obj);
        }
    }

    public void Remove(GameObject obj)
    {
        if (!_bounds.TryGetValue(obj, out var bounds)) return;

        foreach (var key in CellsCovering(bounds))
        {
            if (_cells.TryGetValue(key, out var set))
            {
                set.Remove(obj);
                if (set.Count == 0) _cells.Remove(key);
            }
        }
        _bounds.Remove(obj);
    }

    public void Update(GameObject obj, in RectD bounds)
    {
        // 若覆盖格子未变,可省去搬迁;简单起见先 Remove+Insert,后续可优化。
        Insert(obj, bounds);
    }

    public IEnumerable<GameObject> Query(in RectD area)
    {
        // HashSet 去重:同一对象跨多格时只返回一次。
        // 不用 yield(迭代器不允许 in 参数),直接物化成 List。
        var seen = new HashSet<GameObject>();
        var result = new List<GameObject>();
        foreach (var key in CellsCovering(area))
        {
            if (!_cells.TryGetValue(key, out var set)) continue;
            foreach (var obj in set)
            {
                if (seen.Add(obj) && _bounds[obj].Intersects(area))
                    result.Add(obj);
            }
        }
        return result;
    }

    public void Clear()
    {
        _cells.Clear();
        _bounds.Clear();
    }

    private IEnumerable<(int, int)> CellsCovering(RectD b)
    {
        int minX = (int)Math.Floor(b.MinX / _cellSize);
        int minY = (int)Math.Floor(b.MinY / _cellSize);
        int maxX = (int)Math.Floor(b.MaxX / _cellSize);
        int maxY = (int)Math.Floor(b.MaxY / _cellSize);

        for (int cx = minX; cx <= maxX; cx++)
            for (int cy = minY; cy <= maxY; cy++)
                yield return (cx, cy);
    }
}
