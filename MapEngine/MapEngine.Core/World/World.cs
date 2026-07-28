using MapEngine.Core.Components;
using MapEngine.Core.Spatial;

namespace MapEngine.Core;

/// <summary>
/// 场景世界容器 —— ECS-lite 的运行时中枢。
///
/// 职责:
/// 1. 持有根 GameObject 列表 + 扁平 Id→对象索引(O(1) 查找)。
/// 2. <b>统一登记</b>:所有对象/组件的增删都过 World 单一入口,
///    在此维护空间索引 + 打 dirty 标记。组件自己不登记(见 IComponent 注释)。
/// 3. dirty 追踪:累积变更类别 + 脏区域,供 SystemScheduler 做增量重算。
///
/// 非线程安全:约定在单一逻辑线程串行访问(编辑线程)。
/// </summary>
public sealed class World
{
    private readonly List<GameObject> _roots = [];
    private readonly Dictionary<Guid, GameObject> _index = [];
    private readonly ISpatialIndex _spatial;

    public World(ISpatialIndex? spatialIndex = null)
        => _spatial = spatialIndex ?? new UniformGridIndex();

    public IReadOnlyList<GameObject> Roots => _roots;
    public ISpatialIndex Spatial => _spatial;
    public int ObjectCount => _index.Count;

    // ── dirty 追踪 ──────────────────────────────────────────
    public DirtyFlags Dirty { get; private set; } = DirtyFlags.None;
    private RectD? _dirtyRegion;
    public RectD? DirtyRegion => _dirtyRegion;

    public event EventHandler<WorldChangedEventArgs>? Changed;

    /// <summary>打脏标记并可选累积脏区域(区域取并集)。</summary>
    public void MarkDirty(DirtyFlags flags, RectD? region = null)
    {
        if (flags == DirtyFlags.None) return;
        Dirty |= flags;
        if (region is { } r)
            _dirtyRegion = _dirtyRegion is { } prev ? prev.Union(r) : r;
        Changed?.Invoke(this, new WorldChangedEventArgs(flags, region));
    }

    /// <summary>清空脏状态。SystemScheduler 在一轮重算完成后调用。</summary>
    public void ClearDirty()
    {
        Dirty = DirtyFlags.None;
        _dirtyRegion = null;
    }

    // ── 对象生命周期(统一登记入口)──────────────────────────

    /// <summary>
    /// 把对象(及其整棵子树)加入世界。递归登记进 Id 索引 + 空间索引,并打 dirty。
    /// parent 为 null 时作为根对象;否则挂到 parent 下。
    /// </summary>
    public GameObject AddObject(GameObject obj, GameObject? parent = null)
    {
        ArgumentNullException.ThrowIfNull(obj);

        if (parent is null)
        {
            obj.Parent = null;
            if (!_roots.Contains(obj)) _roots.Add(obj);
        }
        else
        {
            if (!_index.ContainsKey(parent.Id))
                throw new InvalidOperationException($"父对象 {parent.Id} 不在世界中。");
            obj.Parent = parent;
            if (!parent.Children.Contains(obj)) parent.Children.Add(obj);
        }

        RegisterRecursive(obj);
        MarkDirty(DirtyFlags.Hierarchy | DirtyFlags.Spatial | DirtyFlags.Vision, BoundsOf(obj));
        return obj;
    }

    /// <summary>把对象(及其子树)移出世界。递归注销索引,并打 dirty。</summary>
    public bool RemoveObject(GameObject obj)
    {
        ArgumentNullException.ThrowIfNull(obj);
        if (!_index.ContainsKey(obj.Id)) return false;

        var region = BoundsOf(obj);

        if (obj.Parent is { } p) p.Children.Remove(obj);
        else _roots.Remove(obj);
        obj.Parent = null;

        UnregisterRecursive(obj);
        MarkDirty(DirtyFlags.Hierarchy | DirtyFlags.Spatial | DirtyFlags.Vision, region);
        return true;
    }

    public GameObject? FindById(Guid id) => _index.GetValueOrDefault(id);

    /// <summary>遍历世界中所有对象(深度优先)。</summary>
    public IEnumerable<GameObject> AllObjects()
    {
        foreach (var root in _roots)
            foreach (var o in Descend(root))
                yield return o;

        static IEnumerable<GameObject> Descend(GameObject o)
        {
            yield return o;
            foreach (var c in o.Children)
                foreach (var d in Descend(c))
                    yield return d;
        }
    }

    // ── 组件生命周期(统一登记入口)──────────────────────────

    /// <summary>
    /// 给已入世界的对象挂组件。挂载后刷新该对象的空间登记(组件可能带来空间特征)并打 dirty。
    /// 对象未入世界时抛异常 —— 游离期请直接用 GameObject.AddComponent。
    /// </summary>
    public T AddComponent<T>(GameObject obj, T component) where T : class, IComponent
    {
        if (!_index.ContainsKey(obj.Id))
            throw new InvalidOperationException($"对象 {obj.Id} 不在世界中,不能经 World 挂组件。");

        obj.AddComponent(component);
        ReindexObject(obj);
        MarkDirty(DirtyFlags.Vision | DirtyFlags.Render, BoundsOf(obj));
        return component;
    }

    public bool RemoveComponent(GameObject obj, IComponent component)
    {
        if (!_index.ContainsKey(obj.Id)) return false;

        var region = BoundsOf(obj);
        if (!obj.RemoveComponent(component)) return false;

        ReindexObject(obj);
        MarkDirty(DirtyFlags.Vision | DirtyFlags.Render, region);
        return true;
    }

    /// <summary>
    /// 对象空间特征变化(如墙端点被拖动)后调用,刷新其空间索引登记并打 Spatial+Vision 脏。
    /// </summary>
    public void NotifySpatialChanged(GameObject obj)
    {
        if (!_index.ContainsKey(obj.Id)) return;
        ReindexObject(obj);
        MarkDirty(DirtyFlags.Spatial | DirtyFlags.Vision, BoundsOf(obj));
    }

    // ── 内部:递归登记/注销/重索引 ──────────────────────────

    private void RegisterRecursive(GameObject obj)
    {
        obj.World = this;
        _index[obj.Id] = obj;
        if (TryGetBounds(obj, out var b)) _spatial.Insert(obj, b);
        foreach (var child in obj.Children) RegisterRecursive(child);
    }

    private void UnregisterRecursive(GameObject obj)
    {
        foreach (var child in obj.Children) UnregisterRecursive(child);
        _spatial.Remove(obj);
        _index.Remove(obj.Id);
        obj.World = null;
    }

    private void ReindexObject(GameObject obj)
    {
        if (TryGetBounds(obj, out var b)) _spatial.Update(obj, b);
        else _spatial.Remove(obj);
    }

    /// <summary>由对象的 ISpatialComponent 组件推导包围盒(取并集)。无空间组件返回 false。</summary>
    private static bool TryGetBounds(GameObject obj, out RectD bounds)
    {
        RectD? acc = null;
        foreach (var c in obj.Components)
        {
            if (c is ISpatialComponent sp)
            {
                var lb = sp.GetLocalBounds();
                acc = acc is { } prev ? prev.Union(lb) : lb;
            }
        }
        if (acc is { } r) { bounds = r; return true; }
        bounds = default;
        return false;
    }

    /// <summary>取对象包围盒用于 dirty 区域;无空间特征时返回 null(不缩小脏区域)。</summary>
    private static RectD? BoundsOf(GameObject obj)
        => TryGetBounds(obj, out var b) ? b : null;

    // ── S4: 序列化/存档 ──────────────────────────────────────

    /// <summary>
    /// 保存 World 为 SceneDocument(使用现有的 SceneSerializer)。
    /// 联机时服务端调用此方法生成权威快照,客户端通过 diff 同步。
    /// </summary>
    public string SaveToJson()
    {
        var doc = Components.SceneSerializer.ToDocument(_roots);
        return Components.SceneSerializer.Serialize(doc);
    }

    /// <summary>
    /// 从 SceneDocument JSON 加载 World。
    /// 清空当前世界,反序列化所有对象并重建索引。
    /// </summary>
    public void LoadFromJson(string json)
    {
        var doc = Components.SceneSerializer.Deserialize(json)
            ?? throw new InvalidOperationException("Failed to deserialize SceneDocument");

        // 清空现有世界
        foreach (var root in _roots.ToList())
            RemoveObject(root);

        // 加载新对象树
        var roots = Components.SceneSerializer.FromDocument(doc);
        foreach (var root in roots)
            AddObject(root);

        ClearDirty(); // 加载后视为干净状态
    }

    /// <summary>
    /// 静态工厂:从 JSON 创建新 World。
    /// 联机时客户端首次加入房间用此方法初始化本地 World。
    /// </summary>
    public static World FromJson(string json, ISpatialIndex? spatialIndex = null)
    {
        var world = new World(spatialIndex);
        world.LoadFromJson(json);
        return world;
    }
}

/// <summary>World 变更事件参数。</summary>
public sealed class WorldChangedEventArgs(DirtyFlags flags, RectD? region) : EventArgs
{
    public DirtyFlags Flags { get; } = flags;
    public RectD? Region { get; } = region;
}
