using MapEngine.Avalonia.Services;
using MapEngine.Avalonia.ViewModels;
using MapEngine.Core.Components;
using MapEngine.Core.Data;
using Xunit;

namespace MapEngine.Tests;

/// <summary>
/// 拓扑节点在编辑器层的验证：DTO 存档往返、工具交互、命中测试。
/// 覆盖 SceneSerializer 之外那条独立的 DTO 持久化路径（漏了会静默丢数据）。
/// </summary>
public class GraphEditorTests
{
    private static HierarchyItemViewModel MakeNodeItem(
        MainWindowViewModel vm, string id, string name, double x, double y,
        double size = 48, GraphVisibility vis = GraphVisibility.Revealed)
        => vm.BuildHierarchyItemPublic(new HierarchyNodeDto
        {
            Id = id,
            Name = name,
            Icon = "🕸",              // ShouldRenderOnMap 要求 Icon 非空
            ObjectType = "GraphNode",
            IsActive = true,
            HasMapPosition = true,
            X = x,
            Y = y,
            ScaleX = 1,
            ScaleY = 1,
            GraphNodeV2 = new GraphNodeData
            {
                Visibility = (int)vis,
                Size = size,
            },
        });

    // ── DTO 存档往返（与 SceneSerializer 并行的第二条路径）─────────────────

    [Fact]
    public void HierarchyItem_MountsGraphNodeFromDto()
    {
        var vm = new MainWindowViewModel();
        var item = vm.BuildHierarchyItemPublic(new HierarchyNodeDto
        {
            Id = "n1",
            Name = "枢纽",
            ObjectType = "GraphNode",
            GraphNodeV2 = new GraphNodeData
            {
                Kind = (int)GraphNodeKind.Hub,
                DisplayName = "中央枢纽",
                Description = "四路交汇",
                Visibility = (int)GraphVisibility.Visited,
                RenderMode = (int)GraphNodeRenderMode.Both,
                Color = "#FF8800",
                Size = 96,
                Shape = "diamond",
            },
        });

        var comp = item.GetComponent<GraphNodeComponent>();
        Assert.NotNull(comp);
        Assert.Equal(GraphNodeKind.Hub, comp!.Kind);
        Assert.Equal("中央枢纽", comp.DisplayName);
        Assert.Equal("四路交汇", comp.Description);
        Assert.Equal(GraphVisibility.Visited, comp.Visibility);
        Assert.Equal(GraphNodeRenderMode.Both, comp.RenderMode);
        Assert.Equal("#FF8800", comp.Color);
        Assert.Equal(96, comp.Size);
        Assert.Equal("diamond", comp.Shape);
    }

    [Fact]
    public void HierarchyItem_MountsMultipleGraphLinksFromDto()
    {
        var vm = new MainWindowViewModel();
        var item = vm.BuildHierarchyItemPublic(new HierarchyNodeDto
        {
            Id = "n1",
            Name = "起点",
            ObjectType = "GraphNode",
            GraphNodeV2 = new GraphNodeData(),
            GraphLinksV2 =
            [
                new GraphLinkData { LinkId = "L1", TargetNodeId = "t1", Label = "大路", Cost = 2 },
                new GraphLinkData { LinkId = "L2", TargetNodeId = "t2", Label = "密道",
                                    Kind = (int)GraphLinkKind.Secret, IsBidirectional = false,
                                    IsPassable = false },
            ],
        });

        var links = item.BackingObject.GetComponents<GraphLinkComponent>().ToList();
        Assert.Equal(2, links.Count);

        var road = links.Single(l => l.LinkId == "L1");
        Assert.Equal("大路", road.Label);
        Assert.Equal(2, road.Cost);
        Assert.True(road.IsBidirectional);

        var secret = links.Single(l => l.LinkId == "L2");
        Assert.Equal(GraphLinkKind.Secret, secret.Kind);
        Assert.False(secret.IsBidirectional);
        Assert.False(secret.IsPassable);
    }

    [Fact]
    public void DtoRoundTrip_PreservesGraphNodeAndLinks()
    {
        var vm = new MainWindowViewModel();
        var item = vm.BuildHierarchyItemPublic(new HierarchyNodeDto
        {
            Id = "n1",
            Name = "节点",
            ObjectType = "GraphNode",
            GraphNodeV2 = new GraphNodeData
            {
                Kind = (int)GraphNodeKind.Encounter,
                DisplayName = "遭遇点",
                Visibility = (int)GraphVisibility.Revealed,
                Size = 64,
                Shape = "square",
            },
            GraphLinksV2 =
            [
                new GraphLinkData
                {
                    LinkId = "keep", TargetNodeId = "target-x",
                    Kind = (int)GraphLinkKind.Teleport, Label = "传送",
                    IsBidirectional = false, IsPassable = false, Cost = 4.5,
                    StrokeStyle = 1,
                },
            ],
        });

        // 存档：VM → DTO
        var dto = vm.SnapshotHierarchyPublic(item);

        Assert.NotNull(dto.GraphNodeV2);
        Assert.Equal((int)GraphNodeKind.Encounter, dto.GraphNodeV2!.Value.Kind);
        Assert.Equal("遭遇点", dto.GraphNodeV2.Value.DisplayName);
        Assert.Equal(64, dto.GraphNodeV2.Value.Size);
        Assert.Equal("square", dto.GraphNodeV2.Value.Shape);

        Assert.NotNull(dto.GraphLinksV2);
        var savedLink = Assert.Single(dto.GraphLinksV2!);
        Assert.Equal("keep", savedLink.LinkId);
        Assert.Equal("target-x", savedLink.TargetNodeId);
        Assert.Equal((int)GraphLinkKind.Teleport, savedLink.Kind);
        Assert.False(savedLink.IsBidirectional);
        Assert.False(savedLink.IsPassable);
        Assert.Equal(4.5, savedLink.Cost);
        Assert.Equal(1, savedLink.StrokeStyle);

        // 读档：DTO → VM，字段必须一模一样回来
        var reloaded = vm.BuildHierarchyItemPublic(dto);
        var comp = reloaded.GetComponent<GraphNodeComponent>()!;
        Assert.Equal(GraphNodeKind.Encounter, comp.Kind);
        Assert.Equal(64, comp.Size);

        var link = reloaded.BackingObject.GetComponents<GraphLinkComponent>().Single();
        Assert.Equal("keep", link.LinkId);
        Assert.Equal(GraphLinkKind.Teleport, link.Kind);
        Assert.Equal(4.5, link.Cost);
        Assert.Equal(StrokeStyle.Dashed, link.StrokeStyle);
    }

    // ── 命中测试 ────────────────────────────────────────────────────────────

    [Fact]
    public void HitTestGraphNode_FindsNodeWithinRadius()
    {
        var vm = new MainWindowViewModel();
        var node = MakeNodeItem(vm, "n1", "节点", 100, 200, size: 48);
        vm.MapRenderableItems.Add(node);

        // 半径 24：圆心命中、边缘内命中、边缘外不命中
        Assert.Same(node, vm.HitTestGraphNode(100, 200));
        Assert.Same(node, vm.HitTestGraphNode(115, 200));
        Assert.Null(vm.HitTestGraphNode(130, 200));
    }

    [Fact]
    public void HitTestGraphNode_PicksNearestWhenOverlapping()
    {
        var vm = new MainWindowViewModel();
        var big = MakeNodeItem(vm, "n1", "大", 0, 0, size: 200);
        var small = MakeNodeItem(vm, "n2", "小", 20, 0, size: 200);
        vm.MapRenderableItems.Add(big);
        vm.MapRenderableItems.Add(small);

        // 两个都覆盖 (18,0)，但 small 圆心更近
        Assert.Same(small, vm.HitTestGraphNode(18, 0));
        // (2,0) 离 big 更近
        Assert.Same(big, vm.HitTestGraphNode(2, 0));
    }

    [Fact]
    public void HitTestGraphNode_IgnoresNonRenderedNodes()
    {
        var vm = new MainWindowViewModel();
        var node = MakeNodeItem(vm, "n1", "隐形锚点", 0, 0);
        // 纯逻辑节点不画，也就点不到
        node.GetComponent<GraphNodeComponent>()!.RenderMode = GraphNodeRenderMode.None;
        vm.MapRenderableItems.Add(node);

        Assert.Null(vm.HitTestGraphNode(0, 0));
    }

    [Fact]
    public void HitTestGraphNode_IgnoresPlainObjects()
    {
        var vm = new MainWindowViewModel();
        var plain = vm.BuildHierarchyItemPublic(new HierarchyNodeDto
        {
            Id = "p1", Name = "普通对象", Icon = "📦", ObjectType = "Sprite",
            IsActive = true, HasMapPosition = true, X = 0, Y = 0, ScaleX = 1, ScaleY = 1,
        });
        vm.MapRenderableItems.Add(plain);

        Assert.Null(vm.HitTestGraphNode(0, 0));
    }

    // ── 工具交互 ────────────────────────────────────────────────────────────

    [Fact]
    public void CreateGraphNodeAt_AddsNodeWithGraphComponent()
    {
        var vm = new MainWindowViewModel();
        var created = vm.CreateGraphNodeAt(150, -80);

        Assert.NotNull(created);
        var comp = created!.GetComponent<GraphNodeComponent>();
        Assert.NotNull(comp);
        // 新建节点默认对玩家可见，省掉“建完看不见”的困惑
        Assert.Equal(GraphVisibility.Revealed, comp!.Visibility);
        Assert.Equal(150, created.X);
        Assert.Equal(-80, created.Y);
        Assert.Same(created, vm.SelectedHierarchyItem);
    }

    [Fact]
    public void CreateGraphNodeAt_IsUndoable()
    {
        var vm = new MainWindowViewModel();
        var created = vm.CreateGraphNodeAt(10, 10);
        Assert.NotNull(created);

        vm.CommandBus.Undo();
        Assert.Null(vm.FindHierarchyById(created!.Id));
    }

    [Fact]
    public void CompleteGraphLinkDrag_CreatesLinkBetweenNodes()
    {
        var vm = new MainWindowViewModel();
        var a = vm.CreateGraphNodeAt(0, 0)!;
        var b = vm.CreateGraphNodeAt(100, 0)!;

        Assert.True(vm.BeginGraphLinkDrag(a));
        Assert.True(vm.IsDraggingGraphLink);
        Assert.True(vm.CompleteGraphLinkDrag(b));
        Assert.False(vm.IsDraggingGraphLink);

        var link = a.BackingObject.GetComponents<GraphLinkComponent>().Single();
        Assert.Equal(b.BackingObject.Id.ToString(), link.TargetNodeId);
        Assert.True(link.IsBidirectional);
        Assert.Equal(GraphVisibility.Revealed, link.Visibility);
    }

    [Fact]
    public void CompleteGraphLinkDrag_RejectsSelfLoop()
    {
        var vm = new MainWindowViewModel();
        var a = vm.CreateGraphNodeAt(0, 0)!;

        vm.BeginGraphLinkDrag(a);
        Assert.False(vm.CompleteGraphLinkDrag(a));
        Assert.Empty(a.BackingObject.GetComponents<GraphLinkComponent>());
    }

    [Fact]
    public void CompleteGraphLinkDrag_RejectsDuplicateLink()
    {
        var vm = new MainWindowViewModel();
        var a = vm.CreateGraphNodeAt(0, 0)!;
        var b = vm.CreateGraphNodeAt(100, 0)!;

        vm.BeginGraphLinkDrag(a);
        Assert.True(vm.CompleteGraphLinkDrag(b));

        // 同向重复连接应被拒，避免叠一堆同样的边
        vm.BeginGraphLinkDrag(a);
        Assert.False(vm.CompleteGraphLinkDrag(b));
        Assert.Single(a.BackingObject.GetComponents<GraphLinkComponent>());
    }

    [Fact]
    public void CompleteGraphLinkDrag_RejectsNonNodeTarget()
    {
        var vm = new MainWindowViewModel();
        var a = vm.CreateGraphNodeAt(0, 0)!;
        var plain = vm.BuildHierarchyItemPublic(new HierarchyNodeDto
        {
            Id = "p1", Name = "普通对象", ObjectType = "Sprite",
        });

        vm.BeginGraphLinkDrag(a);
        Assert.False(vm.CompleteGraphLinkDrag(plain));
        Assert.Empty(a.BackingObject.GetComponents<GraphLinkComponent>());
    }

    [Fact]
    public void CompleteGraphLinkDrag_NullTargetCancels()
    {
        var vm = new MainWindowViewModel();
        var a = vm.CreateGraphNodeAt(0, 0)!;

        vm.BeginGraphLinkDrag(a);
        Assert.False(vm.CompleteGraphLinkDrag(null));
        Assert.False(vm.IsDraggingGraphLink);
        Assert.Empty(a.BackingObject.GetComponents<GraphLinkComponent>());
    }

    [Fact]
    public void BeginGraphLinkDrag_RejectsNonNode()
    {
        var vm = new MainWindowViewModel();
        var plain = vm.BuildHierarchyItemPublic(new HierarchyNodeDto
        {
            Id = "p1", Name = "普通对象", ObjectType = "Sprite",
        });

        Assert.False(vm.BeginGraphLinkDrag(plain));
        Assert.False(vm.IsDraggingGraphLink);
    }

    [Fact]
    public void SwitchingSubTool_CancelsPendingLinkDrag()
    {
        var vm = new MainWindowViewModel();
        var a = vm.CreateGraphNodeAt(0, 0)!;

        // 先切到 link 模式，再起线
        vm.GraphSubTool = "link";
        vm.BeginGraphLinkDrag(a);
        Assert.True(vm.IsDraggingGraphLink);

        // 换子工具应丢弃半成品，否则回到 link 模式会接着上次起点连
        vm.GraphSubTool = "node";
        Assert.False(vm.IsDraggingGraphLink);
    }

    [Fact]
    public void GraphSubTool_TogglesExclusiveFlags()
    {
        var vm = new MainWindowViewModel();

        vm.GraphSubTool = "link";
        Assert.True(vm.IsGraphLinkSubTool);
        Assert.False(vm.IsGraphNodeSubTool);

        vm.GraphSubTool = "node";
        Assert.True(vm.IsGraphNodeSubTool);
        Assert.False(vm.IsGraphLinkSubTool);
    }

    [Fact]
    public void PruneDanglingGraphLinks_ClearsLinksToDeletedNodes()
    {
        var vm = new MainWindowViewModel();
        var a = vm.CreateGraphNodeAt(0, 0)!;
        var b = vm.CreateGraphNodeAt(100, 0)!;

        vm.BeginGraphLinkDrag(a);
        vm.CompleteGraphLinkDrag(b);
        Assert.Single(a.BackingObject.GetComponents<GraphLinkComponent>());

        // 从层级树删掉目标节点，边就悬空了（编辑器里 VM 树才是权威）
        b.Parent!.Children.Remove(b);
        vm.UnregisterHierarchyItemRecursive(b);

        Assert.Equal(1, vm.PruneDanglingGraphLinks());
        Assert.Empty(a.BackingObject.GetComponents<GraphLinkComponent>());
    }

    // ── Inspector 编辑器 ────────────────────────────────────────────────────

    [Fact]
    public void RebuildComponentEditors_CreatesGraphEditors()
    {
        var vm = new MainWindowViewModel();
        var item = vm.BuildHierarchyItemPublic(new HierarchyNodeDto
        {
            Id = "n1", Name = "节点", ObjectType = "GraphNode",
            GraphNodeV2 = new GraphNodeData(),
            GraphLinksV2 =
            [
                new GraphLinkData { LinkId = "L1", TargetNodeId = "t1" },
                new GraphLinkData { LinkId = "L2", TargetNodeId = "t2" },
            ],
        });

        Assert.Single(item.ComponentEditors.OfType<GraphNodeComponentEditor>());
        // 每条边一个编辑器块，不能被折叠成一个
        Assert.Equal(2, item.ComponentEditors.OfType<GraphLinkComponentEditor>().Count());
    }

    [Fact]
    public void GraphLinkEditor_WritesToItsOwnLink()
    {
        var vm = new MainWindowViewModel();
        var item = vm.BuildHierarchyItemPublic(new HierarchyNodeDto
        {
            Id = "n1", Name = "节点", ObjectType = "GraphNode",
            GraphNodeV2 = new GraphNodeData(),
            GraphLinksV2 =
            [
                new GraphLinkData { LinkId = "L1", TargetNodeId = "t1" },
                new GraphLinkData { LinkId = "L2", TargetNodeId = "t2" },
            ],
        });

        var editors = item.ComponentEditors.OfType<GraphLinkComponentEditor>().ToList();
        var e2 = editors.Single(e => e.LinkId == "L2");
        e2.Label = "只改这条";
        e2.Cost = 7;

        var links = item.BackingObject.GetComponents<GraphLinkComponent>().ToList();
        Assert.Equal("", links.Single(l => l.LinkId == "L1").Label);
        Assert.Equal("只改这条", links.Single(l => l.LinkId == "L2").Label);
        Assert.Equal(7, links.Single(l => l.LinkId == "L2").Cost);
    }

    [Fact]
    public void GraphLinkEditor_ClampsNonPositiveCost()
    {
        var vm = new MainWindowViewModel();
        var item = vm.BuildHierarchyItemPublic(new HierarchyNodeDto
        {
            Id = "n1", Name = "节点", ObjectType = "GraphNode",
            GraphNodeV2 = new GraphNodeData(),
            GraphLinksV2 = [new GraphLinkData { LinkId = "L1", TargetNodeId = "t1" }],
        });

        var editor = item.ComponentEditors.OfType<GraphLinkComponentEditor>().Single();
        // 0/负代价会破坏 Dijkstra 的贪心前提，必须被夹住
        editor.Cost = 0;
        Assert.True(editor.Cost > 0);
        editor.Cost = -5;
        Assert.True(editor.Cost > 0);
    }

    [Fact]
    public void GraphNodeBridgeProperties_ReadWriteComponent()
    {
        var vm = new MainWindowViewModel();
        var item = vm.BuildHierarchyItemPublic(new HierarchyNodeDto
        {
            Id = "n1", Name = "节点", ObjectType = "GraphNode",
            GraphNodeV2 = new GraphNodeData(),
        });

        Assert.True(item.HasGraphNodeComponent);

        item.GraphNodeDisplayName = "新名字";
        item.GraphNodeKindIndex = (int)GraphNodeKind.Hub;
        item.GraphNodeVisibilityIndex = (int)GraphVisibility.Visited;
        item.GraphNodeSize = 128;
        item.GraphNodeShape = "diamond";
        item.ApplyGraphNodeColor("#112233");

        var comp = item.GetComponent<GraphNodeComponent>()!;
        Assert.Equal("新名字", comp.DisplayName);
        Assert.Equal(GraphNodeKind.Hub, comp.Kind);
        Assert.Equal(GraphVisibility.Visited, comp.Visibility);
        Assert.Equal(128, comp.Size);
        Assert.Equal("diamond", comp.Shape);
        Assert.Equal("#112233", comp.Color);
    }

    [Fact]
    public void GraphNodeSize_IsClampedToSaneRange()
    {
        var vm = new MainWindowViewModel();
        var item = vm.BuildHierarchyItemPublic(new HierarchyNodeDto
        {
            Id = "n1", Name = "节点", ObjectType = "GraphNode",
            GraphNodeV2 = new GraphNodeData(),
        });

        item.GraphNodeSize = 99999;
        Assert.True(item.GraphNodeSize <= 512);
        item.GraphNodeSize = -10;
        Assert.True(item.GraphNodeSize >= 8);
    }
}
