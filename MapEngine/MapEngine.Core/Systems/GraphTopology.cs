using MapEngine.Core.Components;

namespace MapEngine.Core.Systems;

/// <summary>
/// 一条解析后的通道：源节点、目标节点、以及承载它的组件。
/// <paramref name="Forward"/> 为 false 表示这是反向遍历双向边得到的（组件挂在 To 上）。
/// </summary>
public readonly record struct GraphEdge(
    GameObject From,
    GameObject To,
    GraphLinkComponent Link,
    bool Forward)
{
    /// <summary>沿本方向是否可走（单向边只允许正向）。</summary>
    public bool IsTraversable => Link.IsPassable && (Forward || Link.IsBidirectional);
}

/// <summary>
/// 拓扑图查询 —— 无状态 System，读 World 里的 GraphNode/GraphLink 组件。
///
/// 拓扑关系与父子结构<b>正交</b>：节点可以在树的任意深度，
/// 连接可以跨层级、跨分支。所以这里一律走 World 的扁平索引，不假设任何层级布局。
/// </summary>
public static class GraphTopology
{
    /// <summary>世界中所有拓扑节点（任意挂了 GraphNodeComponent 的对象）。</summary>
    public static IEnumerable<GameObject> AllNodes(World world)
        => world.AllObjects().Where(o => o.GetComponent<GraphNodeComponent>() is not null);

    /// <summary>对象自身是否是拓扑节点。</summary>
    public static bool IsNode(GameObject obj)
        => obj.GetComponent<GraphNodeComponent>() is not null;

    /// <summary>
    /// 从任意对象向上找最近的祖先节点（含自身）。
    /// Token 问"我在哪个节点里"就靠这个 —— 节点内部可以再嵌套普通对象或子节点。
    /// </summary>
    public static GameObject? FindEnclosingNode(GameObject obj)
    {
        for (var cur = obj; cur is not null; cur = cur.Parent)
            if (IsNode(cur)) return cur;
        return null;
    }

    /// <summary>按 Id 字符串解析节点（GraphLink.TargetNodeId 用的就是这个格式）。</summary>
    public static GameObject? ResolveNode(World world, string nodeId)
        => Guid.TryParse(nodeId, out var id) ? world.FindById(id) : null;

    // ── 邻接 ────────────────────────────────────────────────────────────────

    /// <summary>
    /// 节点的出边（本节点上挂的 GraphLink）。目标解析不到的悬空边会被跳过。
    /// </summary>
    public static IEnumerable<GraphEdge> OutgoingEdges(World world, GameObject node)
    {
        foreach (var link in node.GetComponents<GraphLinkComponent>())
        {
            var target = ResolveNode(world, link.TargetNodeId);
            if (target is not null)
                yield return new GraphEdge(node, target, link, Forward: true);
        }
    }

    /// <summary>
    /// 节点的入边（其他节点指向本节点的 GraphLink）。
    /// 双向边只存一份，所以入边靠全图反查 —— 节点规模大时应改用 <see cref="BuildAdjacency"/> 建索引。
    /// </summary>
    public static IEnumerable<GraphEdge> IncomingEdges(World world, GameObject node)
    {
        var nodeId = node.Id.ToString();
        foreach (var other in AllNodes(world))
        {
            if (ReferenceEquals(other, node)) continue;
            foreach (var link in other.GetComponents<GraphLinkComponent>())
            {
                if (string.Equals(link.TargetNodeId, nodeId, StringComparison.OrdinalIgnoreCase))
                    yield return new GraphEdge(other, node, link, Forward: false);
            }
        }
    }

    /// <summary>
    /// 从本节点<b>实际可走</b>的边：出边 + 双向入边，过滤掉封锁的。
    /// 这是移动逻辑该用的接口。
    /// </summary>
    public static IEnumerable<GraphEdge> TraversableEdges(
        World world, GameObject node, GraphVisibility? minVisibility = null)
    {
        foreach (var e in OutgoingEdges(world, node))
            if (e.IsTraversable && PassesVisibility(e.Link, minVisibility))
                yield return e;

        // 双向边的另一头：组件挂在对面，但本节点同样能走过去
        foreach (var e in IncomingEdges(world, node))
            if (e.Link.IsBidirectional && e.Link.IsPassable && PassesVisibility(e.Link, minVisibility))
                yield return new GraphEdge(node, e.From, e.Link, Forward: false);
    }

    /// <summary>从本节点一步可达的邻居节点（去重）。</summary>
    public static List<GameObject> Neighbors(
        World world, GameObject node, GraphVisibility? minVisibility = null)
    {
        var seen = new HashSet<Guid>();
        var result = new List<GameObject>();
        foreach (var e in TraversableEdges(world, node, minVisibility))
            if (seen.Add(e.To.Id))
                result.Add(e.To);
        return result;
    }

    private static bool PassesVisibility(GraphLinkComponent link, GraphVisibility? min)
        => min is null || link.Visibility >= min.Value;

    // ── 邻接表（批量查询用，避免入边 O(n²) 反查）──────────────────────────

    /// <summary>
    /// 预建全图邻接表。寻路/连通性分析等需要反复查邻居的场景应先建表，
    /// 否则每次 <see cref="IncomingEdges"/> 都要扫全图。
    /// </summary>
    public static Dictionary<Guid, List<GraphEdge>> BuildAdjacency(
        World world, GraphVisibility? minVisibility = null)
    {
        var nodes = AllNodes(world).ToList();
        var adjacency = new Dictionary<Guid, List<GraphEdge>>(nodes.Count);
        foreach (var n in nodes)
            adjacency[n.Id] = [];

        foreach (var node in nodes)
        {
            foreach (var link in node.GetComponents<GraphLinkComponent>())
            {
                if (!link.IsPassable || !PassesVisibility(link, minVisibility)) continue;

                var target = ResolveNode(world, link.TargetNodeId);
                if (target is null || !adjacency.ContainsKey(target.Id)) continue;

                adjacency[node.Id].Add(new GraphEdge(node, target, link, Forward: true));

                if (link.IsBidirectional)
                    adjacency[target.Id].Add(new GraphEdge(target, node, link, Forward: false));
            }
        }
        return adjacency;
    }

    // ── 寻路 ────────────────────────────────────────────────────────────────

    /// <summary>
    /// 按 Cost 求最短路（Dijkstra；拓扑图没有可用的几何启发式，A* 退化成 Dijkstra）。
    /// 返回自 start 到 goal 的节点序列（含两端）；不可达返回 null。
    /// </summary>
    public static List<GameObject>? FindPath(
        World world, GameObject start, GameObject goal, GraphVisibility? minVisibility = null)
    {
        if (ReferenceEquals(start, goal)) return [start];

        var adjacency = BuildAdjacency(world, minVisibility);
        if (!adjacency.ContainsKey(start.Id) || !adjacency.ContainsKey(goal.Id))
            return null;

        var dist = new Dictionary<Guid, double> { [start.Id] = 0 };
        var prev = new Dictionary<Guid, GameObject>();
        var settled = new HashSet<Guid>();
        var frontier = new PriorityQueue<GameObject, double>();
        frontier.Enqueue(start, 0);

        while (frontier.TryDequeue(out var current, out var currentDist))
        {
            if (!settled.Add(current.Id)) continue;
            if (current.Id == goal.Id) break;

            foreach (var edge in adjacency[current.Id])
            {
                // Cost 必须为正，否则 Dijkstra 的贪心前提不成立；非法值按 1 处理
                var w = edge.Link.Cost > 0 ? edge.Link.Cost : 1;
                var nd = currentDist + w;
                if (dist.TryGetValue(edge.To.Id, out var known) && known <= nd) continue;

                dist[edge.To.Id] = nd;
                prev[edge.To.Id] = current;
                frontier.Enqueue(edge.To, nd);
            }
        }

        if (!dist.ContainsKey(goal.Id)) return null;

        var path = new List<GameObject> { goal };
        for (var cur = goal; cur.Id != start.Id; )
        {
            if (!prev.TryGetValue(cur.Id, out var p)) return null;
            path.Add(p);
            cur = p;
        }
        path.Reverse();
        return path;
    }

    /// <summary>
    /// 从起点可达的全部节点（广度优先，不计代价）。
    /// 用于"这条封锁打开后玩家能去哪些地方"这类连通性分析。
    /// </summary>
    public static List<GameObject> Reachable(
        World world, GameObject start, GraphVisibility? minVisibility = null)
    {
        var adjacency = BuildAdjacency(world, minVisibility);
        var visited = new HashSet<Guid> { start.Id };
        var queue = new Queue<GameObject>();
        queue.Enqueue(start);
        var result = new List<GameObject>();

        while (queue.Count > 0)
        {
            var cur = queue.Dequeue();
            result.Add(cur);
            if (!adjacency.TryGetValue(cur.Id, out var edges)) continue;

            foreach (var e in edges)
                if (visited.Add(e.To.Id))
                    queue.Enqueue(e.To);
        }
        return result;
    }

    // ── 完整性维护 ──────────────────────────────────────────────────────────

    /// <summary>
    /// 找出所有悬空边（TargetNodeId 解析不到对象，或目标不是节点）。
    /// 删节点后应调用 <see cref="PruneDanglingLinks"/> 清理。
    /// </summary>
    public static List<(GameObject Owner, GraphLinkComponent Link)> FindDanglingLinks(World world)
    {
        var result = new List<(GameObject, GraphLinkComponent)>();
        foreach (var node in AllNodes(world))
        {
            foreach (var link in node.GetComponents<GraphLinkComponent>().ToList())
            {
                var target = ResolveNode(world, link.TargetNodeId);
                if (target is null || !IsNode(target))
                    result.Add((node, link));
            }
        }
        return result;
    }

    /// <summary>
    /// 摘除所有悬空边，返回清理条数。走 World.RemoveComponent 以保证 dirty 标记同步。
    /// </summary>
    public static int PruneDanglingLinks(World world)
    {
        var dangling = FindDanglingLinks(world);
        foreach (var (owner, link) in dangling)
            world.RemoveComponent(owner, link);
        return dangling.Count;
    }
}
