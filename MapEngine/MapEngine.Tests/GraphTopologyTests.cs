using System.Text.Json;
using MapEngine.Core;
using MapEngine.Core.Commands;
using MapEngine.Core.Components;
using MapEngine.Core.Networking;
using MapEngine.Core.Systems;
using Xunit;

namespace MapEditor.Tests;

/// <summary>
/// 网状节点系统测试：存档 roundtrip、拓扑查询、寻路、网络同步。
/// </summary>
public class GraphTopologyTests
{
    // ── 构造辅助 ────────────────────────────────────────────────────────────

    private static GameObject MakeNode(string name, GraphVisibility vis = GraphVisibility.Revealed)
    {
        var go = new GameObject { Name = name, ObjectType = "GraphNode" };
        go.AddComponent(new TransformComponent());
        go.AddComponent(new GraphNodeComponent { Visibility = vis });
        return go;
    }

    private static GraphLinkComponent Link(
        GameObject target, double cost = 1, bool bidirectional = true,
        bool passable = true, GraphVisibility vis = GraphVisibility.Revealed)
        => new()
        {
            TargetNodeId = target.Id.ToString(),
            Cost = cost,
            IsBidirectional = bidirectional,
            IsPassable = passable,
            Visibility = vis
        };

    // ── 存档 roundtrip ──────────────────────────────────────────────────────

    [Fact]
    public void SceneSerializer_RoundTrip_PreservesGraphNodeAndLinks()
    {
        var target = MakeNode("目标");
        var node = new GameObject { Name = "起点", ObjectType = "GraphNode" };
        node.AddComponent(new GraphNodeComponent
        {
            Kind         = GraphNodeKind.Hub,
            DisplayName  = "中央枢纽",
            Description  = "四条路交汇",
            Visibility   = GraphVisibility.Visited,
            RenderMode   = GraphNodeRenderMode.Both,
            IconAssetRef = "abc123hash",
            Color        = "#FF8800",
            Size         = 96,
            Shape        = "diamond",
        });
        node.AddComponent(new GraphLinkComponent
        {
            LinkId          = "link-fixed-1",
            TargetNodeId    = target.Id.ToString(),
            Kind            = GraphLinkKind.Secret,
            IsBidirectional = false,
            Label           = "需要钥匙",
            Visibility      = GraphVisibility.Hidden,
            IsPassable      = false,
            Cost            = 7.5,
            Color           = "#123456",
            Width           = 4,
            StrokeStyle     = StrokeStyle.Dotted,
        });
        // 第二条边，验证多实例组件不会被吞掉
        node.AddComponent(new GraphLinkComponent
        {
            LinkId       = "link-fixed-2",
            TargetNodeId = target.Id.ToString(),
            Kind         = GraphLinkKind.Road,
            Label        = "大路",
        });

        var json = SceneSerializer.Serialize(SceneSerializer.ToDocument([node, target]));
        var restored = SceneSerializer.FromDocument(SceneSerializer.Deserialize(json)!);

        var gn = restored[0].GetComponent<GraphNodeComponent>()!;
        Assert.Equal(GraphNodeKind.Hub, gn.Kind);
        Assert.Equal("中央枢纽", gn.DisplayName);
        Assert.Equal("四条路交汇", gn.Description);
        Assert.Equal(GraphVisibility.Visited, gn.Visibility);
        Assert.Equal(GraphNodeRenderMode.Both, gn.RenderMode);
        Assert.Equal("abc123hash", gn.IconAssetRef);
        Assert.Equal("#FF8800", gn.Color);
        Assert.Equal(96, gn.Size);
        Assert.Equal("diamond", gn.Shape);

        var links = restored[0].GetComponents<GraphLinkComponent>().ToList();
        Assert.Equal(2, links.Count);

        var secret = links.Single(l => l.LinkId == "link-fixed-1");
        Assert.Equal(target.Id.ToString(), secret.TargetNodeId);
        Assert.Equal(GraphLinkKind.Secret, secret.Kind);
        Assert.False(secret.IsBidirectional);
        Assert.Equal("需要钥匙", secret.Label);
        Assert.Equal(GraphVisibility.Hidden, secret.Visibility);
        Assert.False(secret.IsPassable);
        Assert.Equal(7.5, secret.Cost);
        Assert.Equal("#123456", secret.Color);
        Assert.Equal(4, secret.Width);
        Assert.Equal(StrokeStyle.Dotted, secret.StrokeStyle);

        Assert.Equal(GraphLinkKind.Road, links.Single(l => l.LinkId == "link-fixed-2").Kind);
    }

    [Fact]
    public void GraphLinkComponent_Clone_AssignsFreshLinkId()
    {
        var original = new GraphLinkComponent { LinkId = "orig", TargetNodeId = "t", Label = "路" };
        var clone = (GraphLinkComponent)original.Clone();

        // 复制出的边必须是新边，否则同步/选中会撞 ID
        Assert.NotEqual("orig", clone.LinkId);
        Assert.NotEmpty(clone.LinkId);
        Assert.Equal("t", clone.TargetNodeId);
        Assert.Equal("路", clone.Label);
    }

    // ── 层级 × 拓扑 正交性 ──────────────────────────────────────────────────

    [Fact]
    public void FindEnclosingNode_WalksUpThroughNestedObjects()
    {
        var world = new World();
        var outerNode = MakeNode("大区域");
        var innerNode = MakeNode("子区域");
        var token = new GameObject { Name = "玩家", ObjectType = "Token" };
        token.AddComponent(new TransformComponent());

        world.AddObject(outerNode);
        world.AddObject(innerNode, outerNode);   // 节点里嵌节点
        world.AddObject(token, innerNode);       // token 在内层节点里

        // 从 token 往上找，应停在最近的祖先节点
        Assert.Same(innerNode, GraphTopology.FindEnclosingNode(token));
        // 节点自身就是节点
        Assert.Same(innerNode, GraphTopology.FindEnclosingNode(innerNode));
        Assert.Same(outerNode, GraphTopology.FindEnclosingNode(outerNode));
    }

    [Fact]
    public void FindEnclosingNode_ReturnsNullWhenNoAncestorIsNode()
    {
        var world = new World();
        var plainParent = new GameObject { Name = "普通图层" };
        var child = new GameObject { Name = "普通对象" };
        world.AddObject(plainParent);
        world.AddObject(child, plainParent);

        Assert.Null(GraphTopology.FindEnclosingNode(child));
    }

    [Fact]
    public void Links_CanCrossHierarchyBranches()
    {
        var world = new World();
        var branchA = new GameObject { Name = "分支A" };
        var branchB = new GameObject { Name = "分支B" };
        var deepA = MakeNode("A 深层节点");
        var deepB = MakeNode("B 深层节点");

        world.AddObject(branchA);
        world.AddObject(branchB);
        world.AddObject(deepA, branchA);
        world.AddObject(deepB, branchB);

        // 跨分支连接：拓扑与父子结构正交
        world.AddComponent(deepA, Link(deepB));

        var neighbors = GraphTopology.Neighbors(world, deepA);
        Assert.Single(neighbors);
        Assert.Same(deepB, neighbors[0]);
    }

    // ── 邻接与方向 ──────────────────────────────────────────────────────────

    [Fact]
    public void TraversableEdges_BidirectionalLink_WalkableFromBothEnds()
    {
        var world = new World();
        var a = MakeNode("A");
        var b = MakeNode("B");
        world.AddObject(a);
        world.AddObject(b);
        world.AddComponent(a, Link(b, bidirectional: true));

        // 组件只挂在 A 上，但双向边两头都能走
        Assert.Contains(b, GraphTopology.Neighbors(world, a));
        Assert.Contains(a, GraphTopology.Neighbors(world, b));
    }

    [Fact]
    public void TraversableEdges_OneWayLink_OnlyWalkableForward()
    {
        var world = new World();
        var a = MakeNode("A");
        var b = MakeNode("B");
        world.AddObject(a);
        world.AddObject(b);
        world.AddComponent(a, Link(b, bidirectional: false));

        Assert.Contains(b, GraphTopology.Neighbors(world, a));
        Assert.Empty(GraphTopology.Neighbors(world, b));
    }

    [Fact]
    public void TraversableEdges_ImpassableLink_IsExcluded()
    {
        var world = new World();
        var a = MakeNode("A");
        var b = MakeNode("B");
        world.AddObject(a);
        world.AddObject(b);
        world.AddComponent(a, Link(b, passable: false));

        Assert.Empty(GraphTopology.Neighbors(world, a));
        // 入边侧同样被过滤
        Assert.Empty(GraphTopology.Neighbors(world, b));
    }

    [Fact]
    public void Neighbors_RespectsMinVisibility()
    {
        var world = new World();
        var a = MakeNode("A");
        var revealed = MakeNode("已揭示");
        var hidden = MakeNode("隐藏");
        world.AddObject(a);
        world.AddObject(revealed);
        world.AddObject(hidden);
        world.AddComponent(a, Link(revealed, vis: GraphVisibility.Revealed));
        world.AddComponent(a, Link(hidden, vis: GraphVisibility.Hidden));

        // GM 视角（不设阈值）看到两条
        Assert.Equal(2, GraphTopology.Neighbors(world, a).Count);

        // 玩家视角只看到已揭示的
        var playerVisible = GraphTopology.Neighbors(world, a, GraphVisibility.Revealed);
        Assert.Single(playerVisible);
        Assert.Same(revealed, playerVisible[0]);
    }

    [Fact]
    public void IncomingEdges_FindsLinksPointingAtNode()
    {
        var world = new World();
        var hub = MakeNode("枢纽");
        var from1 = MakeNode("来源1");
        var from2 = MakeNode("来源2");
        world.AddObject(hub);
        world.AddObject(from1);
        world.AddObject(from2);
        world.AddComponent(from1, Link(hub));
        world.AddComponent(from2, Link(hub));

        var incoming = GraphTopology.IncomingEdges(world, hub).ToList();
        Assert.Equal(2, incoming.Count);
        Assert.All(incoming, e => Assert.Same(hub, e.To));
    }

    // ── 寻路 ────────────────────────────────────────────────────────────────

    [Fact]
    public void FindPath_PicksCheaperRoute()
    {
        var world = new World();
        var start = MakeNode("起点");
        var detour = MakeNode("绕路");
        var mid = MakeNode("捷径中转");
        var goal = MakeNode("终点");
        foreach (var n in new[] { start, detour, mid, goal }) world.AddObject(n);

        // 直达但很贵：start → detour(cost 50) → goal(cost 50)
        world.AddComponent(start, Link(detour, cost: 50));
        world.AddComponent(detour, Link(goal, cost: 50));
        // 便宜的两跳：start → mid(1) → goal(1)
        world.AddComponent(start, Link(mid, cost: 1));
        world.AddComponent(mid, Link(goal, cost: 1));

        var path = GraphTopology.FindPath(world, start, goal);
        Assert.NotNull(path);
        Assert.Equal(["起点", "捷径中转", "终点"], path!.Select(n => n.Name));
    }

    [Fact]
    public void FindPath_RoutesAroundBlockedLink()
    {
        var world = new World();
        var start = MakeNode("起点");
        var blocked = MakeNode("封锁路");
        var open = MakeNode("通路");
        var goal = MakeNode("终点");
        foreach (var n in new[] { start, blocked, open, goal }) world.AddObject(n);

        // 最短但被封锁
        world.AddComponent(start, Link(blocked, cost: 1, passable: false));
        world.AddComponent(blocked, Link(goal, cost: 1));
        // 较长但可走
        world.AddComponent(start, Link(open, cost: 10));
        world.AddComponent(open, Link(goal, cost: 10));

        var path = GraphTopology.FindPath(world, start, goal);
        Assert.NotNull(path);
        Assert.Equal(["起点", "通路", "终点"], path!.Select(n => n.Name));
    }

    [Fact]
    public void FindPath_ReturnsNullWhenUnreachable()
    {
        var world = new World();
        var a = MakeNode("孤岛A");
        var b = MakeNode("孤岛B");
        world.AddObject(a);
        world.AddObject(b);

        Assert.Null(GraphTopology.FindPath(world, a, b));
    }

    [Fact]
    public void FindPath_SameNodeReturnsSingletonPath()
    {
        var world = new World();
        var a = MakeNode("A");
        world.AddObject(a);

        var path = GraphTopology.FindPath(world, a, a);
        Assert.NotNull(path);
        Assert.Single(path!);
    }

    [Fact]
    public void FindPath_OneWayLink_NotTraversableBackward()
    {
        var world = new World();
        var a = MakeNode("A");
        var b = MakeNode("B");
        world.AddObject(a);
        world.AddObject(b);
        world.AddComponent(a, Link(b, bidirectional: false));

        Assert.NotNull(GraphTopology.FindPath(world, a, b));
        Assert.Null(GraphTopology.FindPath(world, b, a));
    }

    [Fact]
    public void Reachable_FollowsTransitiveConnections()
    {
        var world = new World();
        var a = MakeNode("A");
        var b = MakeNode("B");
        var c = MakeNode("C");
        var isolated = MakeNode("孤立");
        foreach (var n in new[] { a, b, c, isolated }) world.AddObject(n);
        world.AddComponent(a, Link(b));
        world.AddComponent(b, Link(c));

        var reachable = GraphTopology.Reachable(world, a);
        Assert.Equal(3, reachable.Count);
        Assert.DoesNotContain(isolated, reachable);
    }

    // ── 引用完整性 ──────────────────────────────────────────────────────────

    [Fact]
    public void PruneDanglingLinks_RemovesLinksToDeletedNodes()
    {
        var world = new World();
        var a = MakeNode("A");
        var doomed = MakeNode("将被删");
        world.AddObject(a);
        world.AddObject(doomed);
        world.AddComponent(a, Link(doomed));

        Assert.Empty(GraphTopology.FindDanglingLinks(world));

        world.RemoveObject(doomed);
        Assert.Single(GraphTopology.FindDanglingLinks(world));

        var pruned = GraphTopology.PruneDanglingLinks(world);
        Assert.Equal(1, pruned);
        Assert.Empty(GraphTopology.FindDanglingLinks(world));
        Assert.Empty(a.GetComponents<GraphLinkComponent>());
    }

    [Fact]
    public void DanglingLinks_DoNotBreakNeighborQuery()
    {
        var world = new World();
        var a = MakeNode("A");
        world.AddObject(a);
        // 指向一个从未存在过的 Id
        world.AddComponent(a, new GraphLinkComponent { TargetNodeId = Guid.NewGuid().ToString() });

        // 悬空边应被静默跳过，不抛异常
        Assert.Empty(GraphTopology.Neighbors(world, a));
    }

    // ── 网络同步 ────────────────────────────────────────────────────────────

    [Fact]
    public void CommandSerializer_RoundTrip_UpdateGraphNode()
    {
        var world = new World();
        var node = MakeNode("A", GraphVisibility.Hidden);
        world.AddObject(node);

        var cmd = new WorldUpdateGraphNodeCommand(node.Id, "Visibility", (int)GraphVisibility.Visited);
        var json = CommandSerializer.Serialize(cmd);
        Assert.Contains("\"command_type\":\"UpdateGraphNode\"", json);

        var restored = RoundTrip(json, world);
        restored.Execute(world);

        Assert.Equal(GraphVisibility.Visited, node.GetComponent<GraphNodeComponent>()!.Visibility);
    }

    [Fact]
    public void UpdateGraphNodeCommand_Undo_RestoresOldValue()
    {
        var world = new World();
        var node = MakeNode("A");
        var comp = node.GetComponent<GraphNodeComponent>()!;
        comp.DisplayName = "旧名";
        world.AddObject(node);

        var cmd = new WorldUpdateGraphNodeCommand(node.Id, "DisplayName", "新名");
        cmd.Execute(world);
        Assert.Equal("新名", comp.DisplayName);

        cmd.Undo(world);
        Assert.Equal("旧名", comp.DisplayName);
    }

    [Fact]
    public void SerializeInverse_UpdateGraphNode_CarriesOldValue()
    {
        var world = new World();
        var node = MakeNode("A");
        node.GetComponent<GraphNodeComponent>()!.Size = 48;
        world.AddObject(node);

        var cmd = new WorldUpdateGraphNodeCommand(node.Id, "Size", 96.0);
        cmd.Execute(world);

        var inverseJson = CommandSerializer.SerializeInverse(cmd);
        Assert.Contains("\"command_type\":\"UpdateGraphNode\"", inverseJson);
        Assert.Contains("48", inverseJson);
    }

    [Fact]
    public void CommandSerializer_RoundTrip_AddGraphLink_PreservesAllFields()
    {
        var world = new World();
        var a = MakeNode("A");
        var b = MakeNode("B");
        world.AddObject(a);
        world.AddObject(b);

        var link = new GraphLinkComponent
        {
            LinkId          = "stable-link-id",
            TargetNodeId    = b.Id.ToString(),
            Kind            = GraphLinkKind.Teleport,
            IsBidirectional = false,
            Label           = "传送阵",
            Visibility      = GraphVisibility.Revealed,
            IsPassable      = false,
            Cost            = 3.25,
            Color           = "#9B59B6",
            Width           = 5,
            StrokeStyle     = StrokeStyle.Dashed,
        };

        var json = CommandSerializer.Serialize(new WorldAddGraphLinkCommand(a.Id, link));
        Assert.Contains("\"command_type\":\"AddGraphLink\"", json);

        RoundTrip(json, world).Execute(world);

        var applied = a.GetComponents<GraphLinkComponent>().Single();
        // LinkId 必须沿用发起端的，否则后续 UpdateGraphLink 会失配
        Assert.Equal("stable-link-id", applied.LinkId);
        Assert.Equal(b.Id.ToString(), applied.TargetNodeId);
        Assert.Equal(GraphLinkKind.Teleport, applied.Kind);
        Assert.False(applied.IsBidirectional);
        Assert.Equal("传送阵", applied.Label);
        Assert.Equal(GraphVisibility.Revealed, applied.Visibility);
        Assert.False(applied.IsPassable);
        Assert.Equal(3.25, applied.Cost);
        Assert.Equal("#9B59B6", applied.Color);
        Assert.Equal(5, applied.Width);
        Assert.Equal(StrokeStyle.Dashed, applied.StrokeStyle);
    }

    [Fact]
    public void CommandSerializer_RoundTrip_UpdateGraphLink_TargetsCorrectLinkById()
    {
        var world = new World();
        var a = MakeNode("A");
        var b = MakeNode("B");
        world.AddObject(a);
        world.AddObject(b);

        var l1 = Link(b);
        l1.LinkId = "one";
        var l2 = Link(b);
        l2.LinkId = "two";
        world.AddComponent(a, l1);
        world.AddComponent(a, l2);

        var cmd = new WorldUpdateGraphLinkCommand(a.Id, "two", "Label", "改这条");
        var json = CommandSerializer.Serialize(cmd);
        RoundTrip(json, world).Execute(world);

        // 只有 LinkId 匹配的那条被改
        Assert.Equal("", l1.Label);
        Assert.Equal("改这条", l2.Label);
    }

    [Fact]
    public void RemoveGraphLinkCommand_Undo_RestoresLinkWithSameId()
    {
        var world = new World();
        var a = MakeNode("A");
        var b = MakeNode("B");
        world.AddObject(a);
        world.AddObject(b);

        var link = Link(b);
        link.LinkId = "keep-me";
        link.Label = "原标签";
        world.AddComponent(a, link);

        var cmd = new WorldRemoveGraphLinkCommand(a.Id, "keep-me");
        cmd.Execute(world);
        Assert.Empty(a.GetComponents<GraphLinkComponent>());

        cmd.Undo(world);
        var restored = a.GetComponents<GraphLinkComponent>().Single();
        Assert.Equal("keep-me", restored.LinkId);
        Assert.Equal("原标签", restored.Label);
    }

    [Fact]
    public void SerializeInverse_RemoveGraphLink_RebuildsFullLink()
    {
        var world = new World();
        var a = MakeNode("A");
        var b = MakeNode("B");
        world.AddObject(a);
        world.AddObject(b);

        var link = Link(b, cost: 9);
        link.LinkId = "gone";
        link.Label = "密道";
        link.Kind = GraphLinkKind.Secret;
        world.AddComponent(a, link);

        var cmd = new WorldRemoveGraphLinkCommand(a.Id, "gone");
        cmd.Execute(world);

        // 逆命令是 AddGraphLink，且要带回完整字段
        var inverseJson = CommandSerializer.SerializeInverse(cmd);
        Assert.Contains("\"command_type\":\"AddGraphLink\"", inverseJson);

        RoundTrip(inverseJson, world).Execute(world);
        var back = a.GetComponents<GraphLinkComponent>().Single();
        Assert.Equal("gone", back.LinkId);
        Assert.Equal("密道", back.Label);
        Assert.Equal(GraphLinkKind.Secret, back.Kind);
        Assert.Equal(9, back.Cost);
    }

    [Fact]
    public void SerializeInverse_AddGraphLink_IsRemoveWithSameLinkId()
    {
        var a = MakeNode("A");
        var b = MakeNode("B");
        var link = Link(b);
        link.LinkId = "abc";

        var inverseJson = CommandSerializer.SerializeInverse(new WorldAddGraphLinkCommand(a.Id, link));
        Assert.Contains("\"command_type\":\"RemoveGraphLink\"", inverseJson);
        Assert.Contains("abc", inverseJson);
    }

    /// <summary>模拟一次网络往返：序列化后的 JSON 经服务端/对端反序列化成命令。</summary>
    private static IWorldCommand RoundTrip(string json, World world)
    {
        using var doc = JsonDocument.Parse(json);
        var type = doc.RootElement.GetProperty("command_type").GetString()!;
        var prms = doc.RootElement.GetProperty("params");
        return CommandSerializer.Deserialize(type, prms, world);
    }
}
