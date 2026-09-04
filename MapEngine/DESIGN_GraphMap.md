# 网状地图（路由地图）设计文档

## 1. 概述

### 1.1 目标
在现有的正交网格地图基础上，新增**网状地图（Graph Map）**模式，用节点和边的图结构表示抽象的区域连接关系，适用于：
- 大尺度区域探索（城镇、地下城层级、世界地图）
- 抽象空间关系（房间连接图、事件节点树）
- 路径选择和导航（路线规划、分支剧情）

### 1.2 与现有网格地图的关系
- **网格地图**：像素级精确定位，适合战术战斗、详细场景
- **网状地图**：拓扑关系，适合探索、导航、宏观布局
- **共存方式**：一个场景可以同时有网格层和网状层，或某个网状节点"链接"到一个详细网格地图

---

## 2. 数据模型

### 2.1 核心实体

#### **GraphMapNode（节点/区域）**
```csharp
public class GraphMapNode
{
    /// <summary>唯一标识</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>显示名称（"城门", "迷雾森林", "地下城入口"）</summary>
    public string DisplayName { get; set; } = "";

    /// <summary>描述文本（供 GM 和玩家查看）</summary>
    public string Description { get; set; } = "";

    /// <summary>节点类型（用于图标和行为区分）</summary>
    public GraphNodeType NodeType { get; set; } = GraphNodeType.Location;

    /// <summary>画布位置（用于编辑器布局，非游戏内空间坐标）</summary>
    public Vector2 CanvasPosition { get; set; }

    /// <summary>视觉样式（颜色、图标资产引用）</summary>
    public GraphNodeStyle Style { get; set; } = new();

    /// <summary>可见性（对玩家隐藏未探索节点）</summary>
    public VisibilityState Visibility { get; set; } = VisibilityState.Hidden;

    /// <summary>关联的详细地图场景 ID（可选，点击节点可跳转到网格地图）</summary>
    public string? LinkedSceneId { get; set; }

    /// <summary>标签（用于搜索和筛选，如 "商店", "危险", "已探索"）</summary>
    public List<string> Tags { get; set; } = new();

    /// <summary>自定义属性（扩展字段，JSON 字典）</summary>
    public Dictionary<string, object> CustomData { get; set; } = new();
}

public enum GraphNodeType
{
    Location,      // 普通地点
    POI,           // 兴趣点（重要地标）
    Encounter,     // 遭遇事件
    Transition,    // 过渡节点（连接两个区域的中间点）
    Hub,           // 枢纽（多条路径交汇）
}

public enum VisibilityState
{
    Hidden,        // 完全隐藏（玩家看不到）
    Revealed,      // 已揭示（显示但未访问）
    Visited,       // 已访问（玩家到过）
}

public class GraphNodeStyle
{
    public string IconAssetRef { get; set; } = "";  // 资产哈希或路径
    public string Color { get; set; } = "#4A90E2";   // 十六进制颜色
    public float Size { get; set; } = 48f;           // 渲染尺寸（像素）
    public string Shape { get; set; } = "circle";    // circle | square | diamond | custom
}
```

#### **GraphMapEdge（连接/通道）**
```csharp
public class GraphMapEdge
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>起始节点 ID</summary>
    public string FromNodeId { get; set; } = "";

    /// <summary>目标节点 ID</summary>
    public string ToNodeId { get; set; } = "";

    /// <summary>连接类型（影响视觉和行为）</summary>
    public GraphEdgeType EdgeType { get; set; } = GraphEdgeType.Normal;

    /// <summary>是否双向（单向箭头 vs 双向线）</summary>
    public bool IsBidirectional { get; set; } = true;

    /// <summary>标签文本（显示在边上，如 "需要钥匙", "2小时"）</summary>
    public string Label { get; set; } = "";

    /// <summary>可见性</summary>
    public VisibilityState Visibility { get; set; } = VisibilityState.Hidden;

    /// <summary>是否可通行（被阻挡的路径显示为虚线或红色）</summary>
    public bool IsPassable { get; set; } = true;

    /// <summary>移动代价（用于寻路算法，可选）</summary>
    public float Cost { get; set; } = 1f;

    /// <summary>视觉样式</summary>
    public GraphEdgeStyle Style { get; set; } = new();

    public Dictionary<string, object> CustomData { get; set; } = new();
}

public enum GraphEdgeType
{
    Normal,        // 普通通道
    Road,          // 道路（实线）
    Secret,        // 秘密通道（虚线，默认隐藏）
    Dangerous,     // 危险路径（红色警告）
    Teleport,      // 传送门（特殊视觉）
}

public class GraphEdgeStyle
{
    public string Color { get; set; } = "#666666";
    public float Width { get; set; } = 2f;
    public string LineStyle { get; set; } = "solid";  // solid | dashed | dotted
    public bool ShowArrow { get; set; } = false;      // 单向时显示箭头
}
```

#### **GraphMapData（网状地图容器）**
```csharp
public class GraphMapData
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "未命名网状地图";
    public string Description { get; set; } = "";

    /// <summary>所有节点</summary>
    public List<GraphMapNode> Nodes { get; set; } = new();

    /// <summary>所有连接</summary>
    public List<GraphMapEdge> Edges { get; set; } = new();

    /// <summary>背景图片（可选，作为底图参考）</summary>
    public string? BackgroundImageAssetRef { get; set; }

    /// <summary>画布尺寸（编辑器视口，非游戏空间）</summary>
    public Vector2 CanvasSize { get; set; } = new(2000, 2000);

    /// <summary>元数据（创建时间、作者等）</summary>
    public Dictionary<string, object> Metadata { get; set; } = new();
}
```

---

## 3. 架构集成

### 3.1 存储层
- **文件格式**：`{room}/scenes/{sceneName}.graphmap`（JSON）
- **与现有场景并存**：
  - `main.scene` —— 网格地图（现有格式）
  - `worldmap.graphmap` —— 网状地图（新格式）
  - 一个房间可以有多个网状地图和多个网格地图

### 3.2 数据访问
```csharp
namespace MapEngine.Core.GraphMap;

public class GraphMapManager
{
    /// <summary>加载网状地图</summary>
    public GraphMapData Load(string roomId, string graphMapName);

    /// <summary>保存网状地图</summary>
    public void Save(string roomId, GraphMapData graphMap);

    /// <summary>获取房间所有网状地图列表</summary>
    public List<string> ListGraphMaps(string roomId);
}
```

### 3.3 编辑器集成
- **UI 布局**：
  - 左侧工具栏：新建节点、新建连接、选择/移动、删除
  - 中央画布：拖拽节点布局，点击连接编辑属性
  - 右侧属性面板：当前选中节点/边的详细属性编辑
  - 顶部：切换网格地图/网状地图模式

- **交互操作**：
  - **添加节点**：点击画布空白处，弹出"新建节点"对话框
  - **连接节点**：从节点 A 拖拽到节点 B 创建边
  - **编辑节点**：双击节点打开属性面板
  - **移动节点**：拖拽节点改变 CanvasPosition
  - **删除边/节点**：选中后按 Delete 键
  - **自动布局**：提供力导向布局算法，自动整理节点分布

### 3.4 渲染层
```csharp
namespace MapEngine.Render.GraphMap;

public class GraphMapRenderer
{
    /// <summary>渲染整个网状地图到画布</summary>
    public void Render(SKCanvas canvas, GraphMapData graphMap, GraphMapViewState viewState);

    /// <summary>渲染单个节点</summary>
    private void RenderNode(SKCanvas canvas, GraphMapNode node, bool isSelected);

    /// <summary>渲染边（带贝塞尔曲线，避免重叠）</summary>
    private void RenderEdge(SKCanvas canvas, GraphMapEdge edge, GraphMapNode fromNode, GraphMapNode toNode);

    /// <summary>渲染标签文本</summary>
    private void RenderLabel(SKCanvas canvas, string text, Vector2 position);
}

public class GraphMapViewState
{
    public Vector2 PanOffset { get; set; }       // 平移偏移
    public float ZoomLevel { get; set; } = 1f;   // 缩放级别
    public string? SelectedNodeId { get; set; }   // 当前选中节点
    public string? SelectedEdgeId { get; set; }   // 当前选中边
    public bool ShowHiddenElements { get; set; } // GM 模式：显示隐藏节点/边
}
```

---

## 4. 功能特性

### 4.1 GM 功能
- **迷雾探索**：初始所有节点/边为 `Hidden`，GM 可逐步 `Reveal`
- **动态修改可通行性**：运行时标记某条路径为"阻断"
- **快速跳转**：点击节点可跳转到关联的详细网格地图
- **批量操作**：按标签批量显示/隐藏节点（如"显示所有商店"）

### 4.2 玩家视图
- **只看到 `Revealed`/`Visited` 的节点和边**
- **高亮当前位置**：特殊颜色标记玩家所在节点
- **路径规划**：点击目标节点显示可达路径（基于 `IsPassable` 和 `Cost`）

### 4.3 协同编辑
- **实时同步**：节点位置、可见性、属性变更通过 WebSocket 广播
- **冲突处理**：基于时间戳的最后写入胜出（LWW）

---

## 5. 实现路线图

### Phase 1: 核心数据模型（本轮）
- [x] 定义 `GraphMapNode`、`GraphMapEdge`、`GraphMapData` 数据结构
- [ ] 实现 `GraphMapManager`（加载/保存/列表）
- [ ] 单元测试：创建、序列化、反序列化

### Phase 2: 基础渲染
- [ ] `GraphMapRenderer` 基础实现（节点圆形、边直线）
- [ ] 视口控制（平移、缩放）
- [ ] 选中高亮

### Phase 3: 编辑器 UI
- [ ] Avalonia 视图：`GraphMapEditorView`
- [ ] 工具栏：添加节点/边、删除、选择工具
- [ ] 属性面板：编辑节点/边属性
- [ ] 拖拽布局

### Phase 4: 高级特性
- [ ] 贝塞尔曲线边（避免重叠）
- [ ] 自动布局算法（力导向）
- [ ] 寻路算法（A* 基于 Cost）
- [ ] 节点分组/层级

### Phase 5: 网络同步
- [ ] WebSocket 协议扩展（graph_node_update, graph_edge_update）
- [ ] 增量同步（只发送变更的节点/边）

---

## 6. 示例用例

### 用例 1：地下城探索
```
节点：
- "入口大厅" (Visited, 蓝色)
- "宝库" (Revealed, 金色, 标签: "需要钥匙")
- "地牢" (Hidden, 红色)
- "秘密通道" (Hidden, 虚线连接到宝库)

边：
- 入口大厅 <-> 宝库 (Normal, IsPassable=false, Label="铁门锁住")
- 入口大厅 -> 地牢 (Dangerous, 红色)
- 宝库 <-> 秘密通道 (Secret, 虚线, Hidden)
```

### 用例 2：世界地图
```
节点：
- "起始村庄" (Hub, Visited)
- "北方森林" (Location, Revealed)
- "东部港口" (POI, Revealed, LinkedSceneId="port_scene_001")
- "西部山脉" (Encounter, Hidden)

边：
- 起始村庄 <-> 北方森林 (Road, Cost=2, Label="1天")
- 起始村庄 <-> 东部港口 (Road, Cost=3, Label="2天")
- 北方森林 -> 西部山脉 (Dangerous, IsBidirectional=false)
```

---

## 7. 技术考量

### 7.1 性能
- **节点数上限**：建议 ≤ 500 节点/图（超过需要分层或分区）
- **渲染优化**：视口裁剪，只渲染可见区域的节点/边
- **碰撞检测**：使用空间哈希加速点击选择

### 7.2 兼容性
- **向后兼容**：现有网格地图不受影响，`.scene` 和 `.graphmap` 独立存在
- **混合场景**：场景切换时，UI 切换到对应的渲染器

### 7.3 扩展性
- **自定义节点类型**：通过 `CustomData` 扩展字段支持模组
- **脚本钩子**：节点点击事件、边通过事件可触发自定义逻辑

---

## 8. 待决策问题

1. **节点位置是否支持网格吸附？**
   - 提案：提供可选的"对齐网格"模式，CanvasPosition 吸附到 64px 网格
   
2. **边的曲率如何自动计算？**
   - 提案：两个节点间如果有多条边，自动应用不同的控制点偏移

3. **是否需要节点分层（Z-Order）？**
   - 提案：添加 `ZIndex` 字段，支持节点重叠时的渲染顺序

4. **玩家 Token 如何在网状地图上表示？**
   - 提案：`GraphMapData` 添加 `PlayerMarkers: List<PlayerMarker>`，记录玩家当前所在节点 ID

5. **网状地图和网格地图的切换动画？**
   - 提案：Phase 4 实现，提供淡入淡出过渡效果

---

## 9. 文件结构

```
MapEngine.Core/
  GraphMap/
    Models/
      GraphMapNode.cs
      GraphMapEdge.cs
      GraphMapData.cs
      GraphMapEnums.cs
    GraphMapManager.cs          // 持久化管理
    GraphMapQueryService.cs     // 查询（寻路、邻居节点）
    GraphMapValidator.cs        // 数据校验（孤立节点、无效引用）

MapEngine.Render/
  GraphMap/
    GraphMapRenderer.cs         // SkiaSharp 渲染
    GraphMapLayoutEngine.cs     // 自动布局算法
    GraphMapViewState.cs        // 视口状态

MapEngine.Avalonia/
  Views/
    GraphMapEditorView.axaml    // 编辑器主视图
    Partials/
      GraphMapEditorView.Toolbar.cs
      GraphMapEditorView.Canvas.cs
      GraphMapEditorView.Properties.cs
  ViewModels/
    GraphMapEditorViewModel.cs

MasterIM.Server/
  MapPersistence/
    GraphMapStore.cs            // 服务端存储
  WebSocket/
    IMServer_GraphMapSync.cs    // 网络同步协议
```

---

## 10. 审阅清单

请审阅以下设计要点：

- [ ] **数据模型是否完整**：节点/边的属性是否满足 TRPG 场景需求？
- [ ] **可见性机制是否合理**：`Hidden/Revealed/Visited` 三态是否足够？
- [ ] **架构集成方案**：与现有 `.scene` 格式并存的方式是否合理？
- [ ] **编辑器 UX**：操作流程（添加节点、连接、编辑）是否直观？
- [ ] **网络同步粒度**：节点/边级别的增量同步是否合适？
- [ ] **待决策问题**：是否需要补充其他考量点？

---

## 附录：数据示例

### 示例 1：简单地下城网状地图 JSON
```json
{
  "id": "dungeon_001",
  "name": "幽暗地穴",
  "nodes": [
    {
      "id": "node_entrance",
      "displayName": "入口",
      "nodeType": "Location",
      "canvasPosition": { "x": 100, "y": 300 },
      "visibility": "Visited",
      "style": { "color": "#4A90E2", "size": 48 }
    },
    {
      "id": "node_treasure",
      "displayName": "宝库",
      "nodeType": "POI",
      "canvasPosition": { "x": 400, "y": 300 },
      "visibility": "Revealed",
      "style": { "color": "#FFD700", "size": 64, "iconAssetRef": "treasure_icon_hash" },
      "tags": ["重要", "需要钥匙"]
    }
  ],
  "edges": [
    {
      "id": "edge_01",
      "fromNodeId": "node_entrance",
      "toNodeId": "node_treasure",
      "edgeType": "Normal",
      "isBidirectional": true,
      "isPassable": false,
      "label": "铁门锁住",
      "visibility": "Revealed",
      "style": { "color": "#FF0000", "width": 3, "lineStyle": "solid" }
    }
  ],
  "canvasSize": { "x": 1920, "y": 1080 }
}
```
