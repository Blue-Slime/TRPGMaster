# 网状节点系统设计文档 v2（基于组件化对象模型）

## 1. 核心理念

### 1.1 统一对象模型
- **所有实体都是 `MapObject`**：场景、地图、Token、网状节点都是对象树中的节点
- **组件化扩展**：通过附加 `Component` 给对象赋予特殊能力
- **连接信息作为组件**：`GraphConnectionComponent` 让普通对象变成网状节点

### 1.2 设计原则
```
MapObject (基础对象)
├─ TransformComponent (位置/旋转/缩放，所有对象都有)
├─ RenderComponent (可选，控制是否渲染)
├─ ContainerComponent (可选，允许包含子对象)
├─ GraphNodeComponent (可选，标记为网状节点)
└─ GraphConnectionComponent (可选，定义到其他节点的连接)
```

**关键点**：
- 任何对象都可以通过添加 `GraphNodeComponent` 变成网状节点
- 任何对象都可以通过添加 `GraphConnectionComponent` 建立连接
- 连接信息是**组件**而非独立实体（边不是对象，是对象的属性）

---

## 2. 数据模型

### 2.1 基础对象（已有）
```csharp
public class MapObject
{
    public string Id { get; set; }
    public string Name { get; set; }
    public string Type { get; set; }  // "token", "terrain", "graphnode", "scene_container"
    
    /// <summary>子对象（容器功能）</summary>
    public List<MapObject> Children { get; set; } = new();
    
    /// <summary>组件列表（扩展功能）</summary>
    public List<Component> Components { get; set; } = new();
    
    /// <summary>获取特定类型的组件</summary>
    public T? GetComponent<T>() where T : Component;
    
    /// <summary>添加组件</summary>
    public void AddComponent(Component component);
}
```

### 2.2 核心组件

#### **GraphNodeComponent（网状节点标记）**
```csharp
public class GraphNodeComponent : Component
{
    /// <summary>节点类型（用于编辑器分类和图标选择）</summary>
    public GraphNodeType NodeType { get; set; } = GraphNodeType.Location;
    
    /// <summary>节点显示名称（可选，不设置则用对象的 Name）</summary>
    public string? DisplayName { get; set; }
    
    /// <summary>描述文本</summary>
    public string Description { get; set; } = "";
    
    /// <summary>可见性状态（对玩家的揭示状态）</summary>
    public VisibilityState Visibility { get; set; } = VisibilityState.Hidden;
    
    /// <summary>节点视觉样式（图标、颜色、大小）</summary>
    public GraphNodeStyle Style { get; set; } = new();
    
    /// <summary>标签（用于查询和筛选）</summary>
    public List<string> Tags { get; set; } = new();
}

public enum GraphNodeType
{
    Location,      // 普通地点
    POI,           // 兴趣点
    Encounter,     // 遭遇事件
    Hub,           // 枢纽
    SceneContainer // 场景容器（内嵌详细地图）
}

public enum VisibilityState
{
    Hidden,    // 对玩家隐藏
    Revealed,  // 已揭示但未访问
    Visited    // 已访问
}
```

#### **GraphConnectionComponent（连接信息）**
```csharp
/// <summary>
/// 连接组件：附加到节点对象上，表示"从此节点到目标节点的连接"。
/// 一个对象可以有多个此组件，每个代表一条出边。
/// </summary>
public class GraphConnectionComponent : Component
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    
    /// <summary>目标节点对象 ID（模拟指针）</summary>
    public string TargetNodeId { get; set; } = "";
    
    /// <summary>连接类型</summary>
    public GraphConnectionType ConnectionType { get; set; } = GraphConnectionType.Normal;
    
    /// <summary>是否双向（false 表示单向箭头）</summary>
    public bool IsBidirectional { get; set; } = true;
    
    /// <summary>标签文本（显示在连接线上）</summary>
    public string Label { get; set; } = "";
    
    /// <summary>可见性</summary>
    public VisibilityState Visibility { get; set; } = VisibilityState.Hidden;
    
    /// <summary>是否可通行（用于寻路和条件限制）</summary>
    public bool IsPassable { get; set; } = true;
    
    /// <summary>移动代价（用于寻路算法）</summary>
    public float Cost { get; set; } = 1f;
    
    /// <summary>连接线视觉样式</summary>
    public GraphConnectionStyle Style { get; set; } = new();
    
    /// <summary>自定义数据（扩展字段）</summary>
    public Dictionary<string, object> CustomData { get; set; } = new();
}

public enum GraphConnectionType
{
    Normal,        // 普通连接
    Road,          // 道路
    Secret,        // 秘密通道
    Dangerous,     // 危险路径
    Teleport,      // 传送门
    OneWay         // 单向通道（强制单向，忽略 IsBidirectional）
}

public class GraphConnectionStyle
{
    public string Color { get; set; } = "#666666";
    public float Width { get; set; } = 2f;
    public string LineStyle { get; set; } = "solid";  // solid | dashed | dotted
    public bool ShowArrow { get; set; } = false;
}
```

#### **SceneContainerComponent（场景容器）**
```csharp
/// <summary>
/// 场景容器组件：表示此对象内部包含一个完整的子场景。
/// 可以用于嵌套地图（大地图上的节点包含小场景）。
/// </summary>
public class SceneContainerComponent : Component
{
    /// <summary>内嵌场景的引用方式</summary>
    public SceneReferenceType ReferenceType { get; set; } = SceneReferenceType.Inline;
    
    /// <summary>
    /// 外部场景文件引用（ReferenceType = External 时使用）
    /// 格式："{sceneName}.scene" 或 完整路径
    /// </summary>
    public string? ExternalSceneRef { get; set; }
    
    /// <summary>
    /// 内嵌场景数据（ReferenceType = Inline 时使用）
    /// 直接存储在此对象的 Children 中
    /// </summary>
    public SceneMetadata InlineSceneMetadata { get; set; } = new();
    
    /// <summary>进入场景的初始位置（可选）</summary>
    public Vector2? EntryPoint { get; set; }
    
    /// <summary>退出场景时的返回位置（可选）</summary>
    public Vector2? ExitPoint { get; set; }
}

public enum SceneReferenceType
{
    Inline,    // 内嵌：场景对象直接作为此对象的子对象
    External   // 外部引用：场景存储在独立文件中
}

public class SceneMetadata
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public Vector2 Size { get; set; } = new(2000, 2000);
    public string BackgroundImageAssetRef { get; set; } = "";
}
```

---

## 3. 使用示例

### 3.1 创建网状节点
```csharp
// 示例 1：纯网状节点（不渲染，只作为逻辑连接点）
var nodeVillage = new MapObject
{
    Id = "node_village",
    Name = "新手村",
    Type = "graphnode"
};
nodeVillage.AddComponent(new GraphNodeComponent 
{ 
    NodeType = GraphNodeType.Location,
    Visibility = VisibilityState.Visited,
    Style = new GraphNodeStyle { Color = "#4A90E2", Size = 48 }
});

// 示例 2：带场景容器的节点（内部包含详细地图）
var nodeForest = new MapObject
{
    Id = "node_forest",
    Name = "迷雾森林",
    Type = "graphnode"
};
nodeForest.AddComponent(new GraphNodeComponent 
{ 
    NodeType = GraphNodeType.SceneContainer,
    Tags = new List<string> { "危险区域" }
});
nodeForest.AddComponent(new SceneContainerComponent
{
    ReferenceType = SceneReferenceType.External,
    ExternalSceneRef = "forest_detail.scene"
});

// 示例 3：网状节点内嵌小场景（内联方式）
var nodeShop = new MapObject
{
    Id = "node_shop",
    Name = "杂货铺",
    Type = "graphnode"
};
nodeShop.AddComponent(new GraphNodeComponent());
nodeShop.AddComponent(new SceneContainerComponent
{
    ReferenceType = SceneReferenceType.Inline,
    InlineSceneMetadata = new SceneMetadata { Name = "商店内部", Size = new(800, 600) }
});
// 直接在 nodeShop.Children 中添加内部场景的对象
nodeShop.Children.Add(new MapObject { Name = "柜台", Type = "furniture" });
nodeShop.Children.Add(new MapObject { Name = "NPC 店主", Type = "token" });
```

### 3.2 建立连接
```csharp
// 新手村 → 迷雾森林（双向道路）
var connection1 = new GraphConnectionComponent
{
    TargetNodeId = nodeForest.Id,
    ConnectionType = GraphConnectionType.Road,
    IsBidirectional = true,
    Label = "北方道路",
    Cost = 2f,
    Visibility = VisibilityState.Revealed
};
nodeVillage.AddComponent(connection1);

// 新手村 → 商店（单向传送门）
var connection2 = new GraphConnectionComponent
{
    TargetNodeId = nodeShop.Id,
    ConnectionType = GraphConnectionType.Teleport,
    IsBidirectional = false,
    Label = "传送阵",
    Style = new GraphConnectionStyle { Color = "#9B59B6", LineStyle = "dashed" }
};
nodeVillage.AddComponent(connection2);

// 迷雾森林 → 新手村（秘密通道，初始隐藏）
var connection3 = new GraphConnectionComponent
{
    TargetNodeId = nodeVillage.Id,
    ConnectionType = GraphConnectionType.Secret,
    IsBidirectional = false,
    Visibility = VisibilityState.Hidden,
    IsPassable = false,  // 初始被封锁
    Label = "需要钥匙"
};
nodeForest.AddComponent(connection3);
```

### 3.3 对象树结构
```
Scene (根场景 "世界地图")
├─ Layer "区域节点"
│  ├─ MapObject (id: node_village) ← 网状节点
│  │  ├─ GraphNodeComponent
│  │  ├─ GraphConnectionComponent (to: node_forest)  ← 连接信息作为组件
│  │  └─ GraphConnectionComponent (to: node_shop)
│  │
│  ├─ MapObject (id: node_forest)
│  │  ├─ GraphNodeComponent
│  │  ├─ SceneContainerComponent (ref: "forest_detail.scene")
│  │  └─ GraphConnectionComponent (to: node_village)
│  │
│  └─ MapObject (id: node_shop)
│     ├─ GraphNodeComponent
│     ├─ SceneContainerComponent (inline)
│     └─ Children (内嵌场景的对象)
│        ├─ MapObject (柜台)
│        └─ MapObject (NPC 店主)
│
└─ Layer "背景装饰"  ← 可选：世界地图的视觉装饰
   └─ MapObject (背景图)
```

---

## 4. 查询和寻路 API

### 4.1 GraphQueryService（图查询服务）
```csharp
public class GraphQueryService
{
    private readonly Scene _scene;
    
    /// <summary>获取场景中所有网状节点</summary>
    public List<MapObject> GetAllGraphNodes()
    {
        return _scene.FindAllObjectsWithComponent<GraphNodeComponent>();
    }
    
    /// <summary>获取节点的所有出边连接</summary>
    public List<GraphConnectionComponent> GetOutgoingConnections(MapObject node)
    {
        return node.GetComponents<GraphConnectionComponent>();
    }
    
    /// <summary>获取指向此节点的所有入边连接</summary>
    public List<(MapObject source, GraphConnectionComponent connection)> GetIncomingConnections(MapObject node)
    {
        var result = new List<(MapObject, GraphConnectionComponent)>();
        foreach (var otherNode in GetAllGraphNodes())
        {
            foreach (var conn in otherNode.GetComponents<GraphConnectionComponent>())
            {
                if (conn.TargetNodeId == node.Id)
                    result.Add((otherNode, conn));
            }
        }
        return result;
    }
    
    /// <summary>获取节点的所有邻居（直接相连的节点）</summary>
    public List<MapObject> GetNeighbors(MapObject node, bool includeHidden = false)
    {
        var neighbors = new List<MapObject>();
        foreach (var conn in GetOutgoingConnections(node))
        {
            if (!includeHidden && conn.Visibility == VisibilityState.Hidden)
                continue;
                
            var target = _scene.FindObjectById(conn.TargetNodeId);
            if (target != null)
                neighbors.Add(target);
        }
        return neighbors;
    }
    
    /// <summary>A* 寻路算法（基于连接的 Cost）</summary>
    public List<MapObject>? FindPath(MapObject start, MapObject goal, 
        bool respectPassability = true)
    {
        // 标准 A* 实现，使用 GraphConnectionComponent.Cost 作为边权重
        // 如果 respectPassability = true，则跳过 IsPassable = false 的连接
        // ... 实现略
    }
    
    /// <summary>查询节点内部是否包含场景</summary>
    public Scene? GetContainedScene(MapObject node)
    {
        var container = node.GetComponent<SceneContainerComponent>();
        if (container == null) return null;
        
        if (container.ReferenceType == SceneReferenceType.Inline)
        {
            // 从 node.Children 构建内嵌场景
            return BuildInlineScene(node, container.InlineSceneMetadata);
        }
        else
        {
            // 加载外部场景文件
            return _sceneLoader.Load(container.ExternalSceneRef);
        }
    }
}
```

### 4.2 对象内元素查询连接信息
```csharp
// 用例：Token 在某个节点内部，想知道"我可以移动到哪里"
public class TokenMovementSystem
{
    public List<MapObject> GetAvailableDestinations(MapObject token)
    {
        // 找到 token 所在的父节点（网状节点）
        var currentNode = FindParentGraphNode(token);
        if (currentNode == null) 
            return new List<MapObject>();
        
        // 查询节点的所有可通行连接
        var queryService = new GraphQueryService(_scene);
        var connections = queryService.GetOutgoingConnections(currentNode)
            .Where(c => c.IsPassable && c.Visibility != VisibilityState.Hidden);
        
        // 返回所有目标节点
        return connections.Select(c => _scene.FindObjectById(c.TargetNodeId))
                          .Where(n => n != null)
                          .ToList();
    }
    
    private MapObject? FindParentGraphNode(MapObject obj)
    {
        var current = obj.Parent;
        while (current != null)
        {
            if (current.GetComponent<GraphNodeComponent>() != null)
                return current;
            current = current.Parent;
        }
        return null;
    }
}
```

---

## 5. 渲染策略

### 5.1 分离渲染和逻辑
```csharp
public class GraphNodeComponent : Component
{
    /// <summary>是否渲染此节点（false = 纯逻辑节点，不显示）</summary>
    public bool EnableRendering { get; set; } = true;
    
    /// <summary>渲染模式</summary>
    public NodeRenderMode RenderMode { get; set; } = NodeRenderMode.IconOnly;
}

public enum NodeRenderMode
{
    Hidden,           // 完全不渲染（纯逻辑）
    IconOnly,         // 只渲染图标（用于网状图视图）
    ContainerContent, // 渲染内部场景内容（用于嵌套地图）
    Both              // 同时渲染图标和内容
}
```

### 5.2 GraphMapRenderer（网状图视图渲染器）
```csharp
public class GraphMapRenderer
{
    public void Render(SKCanvas canvas, Scene scene, GraphMapViewState viewState)
    {
        var queryService = new GraphQueryService(scene);
        var allNodes = queryService.GetAllGraphNodes();
        
        // 1. 先渲染所有连接线
        foreach (var node in allNodes)
        {
            var nodeComponent = node.GetComponent<GraphNodeComponent>();
            if (!ShouldRenderNode(nodeComponent, viewState))
                continue;
                
            foreach (var conn in queryService.GetOutgoingConnections(node))
            {
                if (!ShouldRenderConnection(conn, viewState))
                    continue;
                    
                var targetNode = scene.FindObjectById(conn.TargetNodeId);
                if (targetNode != null)
                    RenderConnection(canvas, node, targetNode, conn, viewState);
            }
        }
        
        // 2. 再渲染所有节点（保证节点在连接线上层）
        foreach (var node in allNodes)
        {
            var nodeComponent = node.GetComponent<GraphNodeComponent>();
            if (!ShouldRenderNode(nodeComponent, viewState))
                continue;
                
            RenderNode(canvas, node, nodeComponent, viewState);
        }
    }
    
    private bool ShouldRenderNode(GraphNodeComponent? comp, GraphMapViewState state)
    {
        if (comp == null || !comp.EnableRendering) 
            return false;
        if (comp.RenderMode == NodeRenderMode.Hidden) 
            return false;
        if (!state.ShowHiddenElements && comp.Visibility == VisibilityState.Hidden)
            return false;
        return true;
    }
    
    private void RenderNode(SKCanvas canvas, MapObject node, 
        GraphNodeComponent nodeComp, GraphMapViewState state)
    {
        var transform = node.GetComponent<TransformComponent>();
        var pos = transform?.Position ?? Vector2.Zero;
        
        // 应用视口变换（平移 + 缩放）
        pos = state.TransformToViewport(pos);
        
        // 根据节点类型选择图标和颜色
        var style = nodeComp.Style;
        var color = SKColor.Parse(style.Color);
        var size = style.Size * state.ZoomLevel;
        
        // 渲染节点图标/形状
        using var paint = new SKPaint { Color = color, IsAntialias = true };
        
        if (style.Shape == "circle")
            canvas.DrawCircle(pos.X, pos.Y, size / 2, paint);
        else if (style.Shape == "square")
            canvas.DrawRect(pos.X - size/2, pos.Y - size/2, size, size, paint);
        
        // 渲染节点名称
        if (state.ShowLabels)
        {
            var label = nodeComp.DisplayName ?? node.Name;
            RenderLabel(canvas, label, pos + new Vector2(0, size / 2 + 5));
        }
        
        // 高亮选中节点
        if (state.SelectedNodeId == node.Id)
        {
            paint.Color = SKColors.Yellow;
            paint.Style = SKPaintStyle.Stroke;
            paint.StrokeWidth = 3;
            canvas.DrawCircle(pos.X, pos.Y, size / 2 + 5, paint);
        }
    }
    
    private void RenderConnection(SKCanvas canvas, MapObject from, MapObject to,
        GraphConnectionComponent conn, GraphMapViewState state)
    {
        var fromPos = from.GetComponent<TransformComponent>()?.Position ?? Vector2.Zero;
        var toPos = to.GetComponent<TransformComponent>()?.Position ?? Vector2.Zero;
        
        fromPos = state.TransformToViewport(fromPos);
        toPos = state.TransformToViewport(toPos);
        
        var style = conn.Style;
        using var paint = new SKPaint
        {
            Color = SKColor.Parse(style.Color),
            StrokeWidth = style.Width * state.ZoomLevel,
            IsAntialias = true
        };
        
        // 设置线条样式
        if (style.LineStyle == "dashed")
            paint.PathEffect = SKPathEffect.CreateDash(new[] { 10f, 5f }, 0);
        else if (style.LineStyle == "dotted")
            paint.PathEffect = SKPathEffect.CreateDash(new[] { 2f, 3f }, 0);
        
        // 绘制连接线
        canvas.DrawLine(fromPos.X, fromPos.Y, toPos.X, toPos.Y, paint);
        
        // 绘制箭头（如果是单向连接）
        if (!conn.IsBidirectional || conn.ConnectionType == GraphConnectionType.OneWay)
        {
            DrawArrowHead(canvas, fromPos, toPos, paint);
        }
        
        // 绘制标签
        if (!string.IsNullOrEmpty(conn.Label) && state.ShowLabels)
        {
            var midpoint = (fromPos + toPos) / 2;
            RenderLabel(canvas, conn.Label, midpoint);
        }
    }
}
```

---

## 6. 编辑器集成

### 6.1 工具模式
```csharp
public enum GraphEditMode
{
    Select,           // 选择/移动节点
    AddNode,          // 添加新节点
    AddConnection,    // 连接两个节点（拖拽创建）
    Delete            // 删除节点/连接
}
```

### 6.2 交互逻辑
```csharp
public class GraphMapEditorViewModel
{
    public GraphEditMode CurrentMode { get; set; }
    
    /// <summary>点击画布添加节点</summary>
    public void OnCanvasClick(Vector2 position)
    {
        if (CurrentMode == GraphEditMode.AddNode)
        {
            var newNode = new MapObject
            {
                Name = "新节点",
                Type = "graphnode"
            };
            newNode.AddComponent(new TransformComponent { Position = position });
            newNode.AddComponent(new GraphNodeComponent());
            
            _scene.AddObject(newNode, _currentLayer);
            NotifyChange();
        }
    }
    
    /// <summary>拖拽创建连接</summary>
    public void OnDragConnection(MapObject fromNode, Vector2 endPos)
    {
        if (CurrentMode != GraphEditMode.AddConnection) 
            return;
        
        // 检测鼠标松开位置是否在另一个节点上
        var targetNode = FindNodeAtPosition(endPos);
        if (targetNode != null && targetNode.Id != fromNode.Id)
        {
            var connection = new GraphConnectionComponent
            {
                TargetNodeId = targetNode.Id,
                IsBidirectional = true
            };
            fromNode.AddComponent(connection);
            NotifyChange();
        }
    }
    
    /// <summary>双击节点打开属性面板或进入内部场景</summary>
    public void OnNodeDoubleClick(MapObject node)
    {
        var sceneContainer = node.GetComponent<SceneContainerComponent>();
        if (sceneContainer != null)
        {
            // 进入内嵌场景编辑模式
            var innerScene = _graphQueryService.GetContainedScene(node);
            if (innerScene != null)
            {
                _navigationStack.Push(_currentScene);
                SwitchToScene(innerScene);
            }
        }
        else
        {
            // 打开节点属性编辑面板
            ShowNodePropertiesPanel(node);
        }
    }
}
```

---

## 7. 序列化示例

### 7.1 JSON 格式（网状节点嵌入对象树）
```json
{
  "id": "world_map_scene",
  "name": "世界地图",
  "layers": [
    {
      "name": "区域节点",
      "objects": [
        {
          "id": "node_village",
          "name": "新手村",
          "type": "graphnode",
          "components": [
            {
              "type": "TransformComponent",
              "position": { "x": 100, "y": 300 }
            },
            {
              "type": "GraphNodeComponent",
              "nodeType": "Location",
              "visibility": "Visited",
              "style": {
                "color": "#4A90E2",
                "size": 48,
                "shape": "circle"
              },
              "enableRendering": true,
              "renderMode": "IconOnly"
            },
            {
              "type": "GraphConnectionComponent",
              "id": "conn_01",
              "targetNodeId": "node_forest",
              "connectionType": "Road",
              "isBidirectional": true,
              "label": "北方道路",
              "cost": 2.0,
              "visibility": "Revealed",
              "isPassable": true,
              "style": {
                "color": "#666666",
                "width": 2,
                "lineStyle": "solid"
              }
            }
          ],
          "children": []
        },
        {
          "id": "node_forest",
          "name": "迷雾森林",
          "type": "graphnode",
          "components": [
            {
              "type": "TransformComponent",
              "position": { "x": 400, "y": 300 }
            },
            {
              "type": "GraphNodeComponent",
              "nodeType": "SceneContainer",
              "visibility": "Revealed"
            },
            {
              "type": "SceneContainerComponent",
              "referenceType": "External",
              "externalSceneRef": "forest_detail.scene"
            }
          ],
          "children": []
        },
        {
          "id": "node_shop",
          "name": "商店",
          "type": "graphnode",
          "components": [
            {
              "type": "TransformComponent",
              "position": { "x": 250, "y": 150 }
            },
            {
              "type": "GraphNodeComponent",
              "nodeType": "POI"
            },
            {
              "type": "SceneContainerComponent",
              "referenceType": "Inline",
              "inlineSceneMetadata": {
                "name": "商店内部",
                "size": { "x": 800, "y": 600 }
              }
            }
          ],
          "children": [
            {
              "id": "shop_counter",
              "name": "柜台",
              "type": "furniture",
              "components": [
                {
                  "type": "TransformComponent",
                  "position": { "x": 400, "y": 300 }
                }
              ]
            },
            {
              "id": "shop_npc",
              "name": "店主",
              "type": "token",
              "components": [
                {
                  "type": "TransformComponent",
                  "position": { "x": 350, "y": 280 }
                }
              ]
            }
          ]
        }
      ]
    }
  ]
}
```

---

## 8. 优势总结

### 8.1 统一模型
- 所有实体都是 `MapObject`，无需区分"节点对象"和"普通对象"
- 编辑器、序列化、撤销/重做系统无需改动

### 8.2 灵活组合
- 任何对象都可以通过添加组件变成网状节点
- 一个对象可以同时是 Token、网状节点、场景容器（多重身份）
- 连接信息作为组件，支持一对多（一个节点多条出边）

### 8.3 层级与网状共存
- 网状节点仍然在对象树中，享受父子关系和层级管理
- 通过 `GraphConnectionComponent` 建立跨层级的逻辑连接
- 既有树状结构（空间布局），又有图状结构（拓扑连接）

### 8.4 查询便利
- 节点内部对象可以通过 `FindParentGraphNode` 查询所在节点
- 节点的连接信息直接通过 `GetComponents<GraphConnectionComponent>()` 获取
- 支持寻路、邻居查询、可达性分析

### 8.5 渲染灵活
- `EnableRendering` 和 `RenderMode` 控制节点是否显示
- 同一个节点可以在网状图视图中显示为图标，在详细视图中显示为完整场景
- 编辑器可以提供"网状图视图"和"场景视图"两种模式切换

---

## 9. 实现路线

### Phase 1: 组件定义（本轮）
- [x] 定义 `GraphNodeComponent`
- [x] 定义 `GraphConnectionComponent`
- [x] 定义 `SceneContainerComponent`
- [ ] 实现组件序列化/反序列化

### Phase 2: 查询服务
- [ ] 实现 `GraphQueryService`
- [ ] 实现 A* 寻路算法
- [ ] 实现邻居查询、连接查询

### Phase 3: 渲染器
- [ ] 实现 `GraphMapRenderer`（网状图视图）
- [ ] 实现连接线渲染（直线、贝塞尔曲线、箭头）
- [ ] 实现视口控制（平移、缩放）

### Phase 4: 编辑器
- [ ] 添加/删除节点工具
- [ ] 拖拽创建连接工具
- [ ] 节点属性编辑面板
- [ ] 场景容器导航（进入/退出内嵌场景）

### Phase 5: 网络同步
- [ ] 同步 `GraphNodeComponent` 变更
- [ ] 同步 `GraphConnectionComponent` 增删改
- [ ] 实时显示其他玩家在网状图上的位置

---

## 10. 待决策问题

1. **双向连接的存储方式**
   - 方案 A：只在一个节点上存 `IsBidirectional=true` 的连接
   - 方案 B：在两个节点上各存一个连接，保持冗余
   - **推荐 A**：避免冗余，查询入边时遍历全图

2. **删除节点时如何处理指向它的连接**
   - 方案 A：自动删除所有指向该节点的连接
   - 方案 B：保留"悬空连接"，渲染时显示为错误
   - **推荐 A**：编辑器执行删除时自动清理

3. **内嵌场景的坐标系**
   - 问题：内嵌场景的对象坐标是相对父节点还是绝对坐标？
   - **推荐**：相对坐标，父节点的 `TransformComponent` 作为局部坐标原点

4. **网状图视图的默认布局**
   - 手动布局：编辑器拖拽定位
   - 自动布局：力导向算法自动排列
   - **推荐**：提供"自动布局"按钮，手动调整优先

---

请审阅此设计，重点关注：
- 组件化方案是否合理
- 连接信息作为组件是否易用
- 查询 API 是否满足移动逻辑需求
- 渲染策略是否灵活
