namespace MapEngine.Core.Components;

/// <summary>节点在拓扑图里的语义分类。只影响编辑器默认图标与筛选，不影响连通性。</summary>
public enum GraphNodeKind
{
    /// <summary>普通地点</summary>
    Location,
    /// <summary>兴趣点/地标</summary>
    PointOfInterest,
    /// <summary>遭遇/事件节点</summary>
    Encounter,
    /// <summary>枢纽（多路交汇）</summary>
    Hub,
    /// <summary>纯逻辑锚点（通常不渲染）</summary>
    Anchor,
}

/// <summary>对玩家的揭示状态。GM 始终可见全部。</summary>
public enum GraphVisibility
{
    /// <summary>玩家看不到</summary>
    Hidden,
    /// <summary>已揭示，未到过</summary>
    Revealed,
    /// <summary>已到过</summary>
    Visited,
}

/// <summary>节点在拓扑视图下的渲染方式。</summary>
public enum GraphNodeRenderMode
{
    /// <summary>不画（纯逻辑节点）</summary>
    None,
    /// <summary>只画节点自身的图标/形状</summary>
    Icon,
    /// <summary>只画子对象内容（把节点当普通容器铺开）</summary>
    Content,
    /// <summary>图标 + 子对象内容都画</summary>
    Both,
}

/// <summary>
/// 把任意 GameObject 标记成拓扑图节点。
///
/// 这个组件<b>不</b>改变对象的容器性质 —— GameObject 本来就有 Children，
/// 所以"大地图里放节点"和"节点里放地图"是同一件事：都是父子关系，
/// 差别只在哪一层挂了本组件。节点可任意嵌套。
/// </summary>
public sealed class GraphNodeComponent : ComponentBase
{
    public override string TypeName => "GraphNode";

    /// <summary>语义分类（编辑器图标/筛选用）</summary>
    public GraphNodeKind Kind { get; set; } = GraphNodeKind.Location;

    /// <summary>覆盖显示名。留空则用 GameObject.Name。</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>节点描述（GM 备注 / 玩家可见简介）</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>对玩家的揭示状态</summary>
    public GraphVisibility Visibility { get; set; } = GraphVisibility.Hidden;

    /// <summary>拓扑视图渲染方式</summary>
    public GraphNodeRenderMode RenderMode { get; set; } = GraphNodeRenderMode.Icon;

    // ── 拓扑视图样式 ────────────────────────────────────────────────────────
    /// <summary>节点图标资产引用（内容哈希）。空则按 Shape 画纯色形状。</summary>
    public string IconAssetRef { get; set; } = string.Empty;
    /// <summary>节点颜色 #RRGGBB / #AARRGGBB</summary>
    public string Color { get; set; } = "#4A90E2";
    /// <summary>节点直径（世界单位）</summary>
    public double Size { get; set; } = 48;
    /// <summary>circle | square | diamond</summary>
    public string Shape { get; set; } = "circle";

    public override IComponent Clone() => new GraphNodeComponent
    {
        Kind         = Kind,
        DisplayName  = DisplayName,
        Description  = Description,
        Visibility   = Visibility,
        RenderMode   = RenderMode,
        IconAssetRef = IconAssetRef,
        Color        = Color,
        Size         = Size,
        Shape        = Shape,
    };
}

/// <summary>通道类型。影响默认线型与语义，不影响连通性判定。</summary>
public enum GraphLinkKind
{
    /// <summary>普通通道</summary>
    Normal,
    /// <summary>道路</summary>
    Road,
    /// <summary>密道（默认隐藏）</summary>
    Secret,
    /// <summary>危险路径</summary>
    Dangerous,
    /// <summary>传送</summary>
    Teleport,
}

/// <summary>
/// 一条出边。挂在源节点对象上，通过 <see cref="TargetNodeId"/> 指向目标节点（模拟指针）。
///
/// 一个对象可挂多个本组件 —— 每个代表一条独立通道，所以边不是对象，是节点的属性。
/// 双向通道只存一条（<see cref="IsBidirectional"/> = true），查入边靠反查，避免两头冗余。
/// 目标可以是任意层级的节点，连接不受父子结构约束。
/// </summary>
public sealed class GraphLinkComponent : ComponentBase
{
    public override string TypeName => "GraphLink";

    /// <summary>本条通道的稳定 ID（增删改同步、UI 选中用）</summary>
    public string LinkId { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>目标节点对象 Id（GameObject.Id 的字符串形式）</summary>
    public string TargetNodeId { get; set; } = string.Empty;

    /// <summary>通道类型</summary>
    public GraphLinkKind Kind { get; set; } = GraphLinkKind.Normal;

    /// <summary>true = 双向可走；false = 仅源→目标单向</summary>
    public bool IsBidirectional { get; set; } = true;

    /// <summary>通道标签（画在线上，如"需要钥匙""2 小时"）</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>对玩家的揭示状态</summary>
    public GraphVisibility Visibility { get; set; } = GraphVisibility.Hidden;

    /// <summary>当前是否可通行（false = 封锁，寻路绕开）</summary>
    public bool IsPassable { get; set; } = true;

    /// <summary>通行代价（寻路权重，须 &gt; 0）</summary>
    public double Cost { get; set; } = 1;

    // ── 连线样式 ────────────────────────────────────────────────────────────
    public string Color { get; set; } = "#8A8F98";
    public double Width { get; set; } = 2;
    /// <summary>连线线型</summary>
    public StrokeStyle StrokeStyle { get; set; } = StrokeStyle.Solid;

    public override IComponent Clone() => new GraphLinkComponent
    {
        // LinkId 不复制 —— 复制出的通道是新通道，否则同步/选中会撞 ID
        TargetNodeId    = TargetNodeId,
        Kind            = Kind,
        IsBidirectional = IsBidirectional,
        Label           = Label,
        Visibility      = Visibility,
        IsPassable      = IsPassable,
        Cost            = Cost,
        Color           = Color,
        Width           = Width,
        StrokeStyle     = StrokeStyle,
    };
}
