using MapEngine.Core.Components;

namespace MapEngine.Core.Spatial;

/// <summary>
/// 空间索引:按包围盒登记 GameObject,支持区域查询。
/// P0 用均匀网格(UniformGridIndex)实现;P2 可无缝替换为四叉树,
/// 因为 World 只依赖此接口,不依赖具体实现。
/// </summary>
public interface ISpatialIndex
{
    /// <summary>登记对象及其包围盒。重复登记同一对象会先移除旧记录。</summary>
    void Insert(GameObject obj, in RectD bounds);

    /// <summary>移除对象。对象不在索引中时静默返回。</summary>
    void Remove(GameObject obj);

    /// <summary>更新对象包围盒(等价于 Remove+Insert,但实现可优化)。</summary>
    void Update(GameObject obj, in RectD bounds);

    /// <summary>查询与给定区域相交的所有对象(可能含少量假阳性,调用方需精确复检)。</summary>
    IEnumerable<GameObject> Query(in RectD area);

    /// <summary>已登记对象数量。</summary>
    int Count { get; }

    void Clear();
}
