# 墙体架构设计：Owlbear 模式

## 核心概念

### 当前架构（单线段组件）
```
GameObject "Wall_001"
  └─ WallComponent (单线段: X1,Y1 → X2,Y2)

GameObject "Wall_002"  
  └─ WallComponent (单线段: X2,Y2 → X3,Y3)
```

**问题**：
- ❌ 无法表达完整的墙体结构（一堵墙 = 多个独立对象）
- ❌ 门无法附着到墙体（门需要知道所在线段）
- ❌ 锚点编辑困难（每个线段独立，无法统一调整）

---

### 目标架构（Owlbear 式多段路径）
```
GameObject "Floor_Room1"  (房间根对象)
  ├─ GameObject "Wall_OuterWall"  (外墙 - 闭合路径)
  │    └─ WallStructureComponent
  │         - Points: [P1, P2, P3, P4] (4个锚点构成矩形)
  │         - IsClosed: true
  │         - Doors: [在P1-P2之间有门]
  │
  └─ GameObject "Wall_Partition"  (隔断墙 - 开放路径)
       └─ WallStructureComponent
            - Points: [P5, P6, P7] (L型墙)
            - IsClosed: false
            - Doors: []
```

**优势**：
- ✅ 一个对象 = 一堵完整的墙（语义清晰）
- ✅ 锚点统一编辑（拖拽任意锚点调整整堵墙）
- ✅ 门自然附着（记录所在线段索引）
- ✅ 符合层级树直觉（房间 → 墙 → 门）

---

## 组件设计

### 方案 A：扩展现有 WallComponent（不推荐）

```csharp
public sealed class WallComponent : ComponentBase
{
    // 保留单线段字段（向后兼容）
    public double X1 { get; set; }
    public double Y1 { get; set; }
    public double X2 { get; set; }
    public double Y2 { get; set; }
    
    // 新增多段路径字段（与单线段互斥）
    public List<(double X, double Y)>? Points { get; set; }
    public bool IsClosed { get; set; }
    public List<DoorData>? Doors { get; set; }
    
    // 判断是单线段模式还是路径模式
    public bool IsLegacySingleSegment => Points == null || Points.Count == 0;
}
```

**问题**：
- ❌ 语义混乱（一个组件两种用法）
- ❌ FOV 算法需要分支逻辑处理两种模式
- ❌ 存档格式臃肿（两套字段同时存在）

---

### 方案 B：新组件 + 旧组件共存（推荐）

```csharp
// === 保留旧组件（单线段，用于简单场景）===
public sealed class WallComponent : ComponentBase  // 不变
{
    public double X1 { get; set; }
    public double Y1 { get; set; }
    public double X2 { get; set; }
    public double Y2 { get; set; }
    public SenseLevel Sight { get; set; } = SenseLevel.Normal;
    public SenseLevel Move { get; set; } = SenseLevel.Normal;
    // ... 其他字段不变
}

// === 新组件（多段路径 + 门系统）===
public sealed class WallStructureComponent : ComponentBase, ISpatialComponent
{
    public override string TypeName => "WallStructure";
    
    /// <summary>墙体路径的所有锚点（本地坐标，相对父对象）</summary>
    public List<Vector2> Points { get; set; } = new();
    
    /// <summary>是否闭合路径（首尾相连）</summary>
    public bool IsClosed { get; set; } = false;
    
    /// <summary>默认感知阻挡等级（应用到所有线段，除非门覆盖）</summary>
    public SenseLevel Sight { get; set; } = SenseLevel.Normal;
    public SenseLevel Move { get; set; } = SenseLevel.Normal;
    public SenseLevel Sound { get; set; } = SenseLevel.Normal;
    public SenseLevel Light { get; set; } = SenseLevel.Normal;
    
    /// <summary>渲染参数</summary>
    public double Thickness { get; set; } = 5;
    public string Color { get; set; } = "#FF4444";
    
    /// <summary>门/窗附着点列表</summary>
    public List<DoorAttachment> Doors { get; set; } = new();
    
    // ISpatialComponent 实现：包围盒 = 所有点的 AABB
    public RectD GetLocalBounds()
    {
        if (Points.Count == 0) return RectD.Empty;
        var xs = Points.Select(p => p.X);
        var ys = Points.Select(p => p.Y);
        return new RectD(xs.Min(), ys.Min(), 
                        xs.Max() - xs.Min(), 
                        ys.Max() - ys.Min());
    }
    
    public override IComponent Clone() => new WallStructureComponent
    {
        Points = new List<Vector2>(Points),
        IsClosed = IsClosed,
        Sight = Sight, Move = Move, Sound = Sound, Light = Light,
        Thickness = Thickness, Color = Color,
        Doors = Doors.Select(d => d.Clone()).ToList()
    };
}

/// <summary>门/窗附着数据</summary>
public sealed class DoorAttachment
{
    /// <summary>唯一标识（用于交互选中）</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();
    
    /// <summary>门所在线段的起始锚点索引 [0, Points.Count-1)</summary>
    public int SegmentIndex { get; set; }
    
    /// <summary>门在线段上的归一化位置 [0.0, 1.0]</summary>
    public double Position { get; set; } = 0.5;
    
    /// <summary>门宽度（世界单位）</summary>
    public double Width { get; set; } = 60;
    
    /// <summary>门类型</summary>
    public DoorKind Kind { get; set; } = DoorKind.Door;
    
    /// <summary>门状态</summary>
    public DoorState State { get; set; } = DoorState.Closed;
    
    /// <summary>门的感知覆盖（null = 继承墙体默认值）</summary>
    public SenseLevel? SightOverride { get; set; }
    public SenseLevel? MoveOverride { get; set; }
    
    public DoorAttachment Clone() => new DoorAttachment
    {
        Id = Id, SegmentIndex = SegmentIndex, 
        Position = Position, Width = Width,
        Kind = Kind, State = State,
        SightOverride = SightOverride, 
        MoveOverride = MoveOverride
    };
}
```

---

## Inspector 显示设计

### WallStructureComponent 编辑器布局

```
┌─────────────────────────────────────┐
│ 墙体结构                            │
├─────────────────────────────────────┤
│ ☑ 闭合路径                          │
│ 锚点数量: 8                         │
├─────────────────────────────────────┤
│ 默认阻挡                            │
│   视线: [普通▼]  移动: [普通▼]     │
│   声音: [无▼]    光照: [普通▼]     │
├─────────────────────────────────────┤
│ 墙体厚度: [5]                       │
│ 颜色: [■ #FF4444] [选择颜色]       │
├─────────────────────────────────────┤
│ 门/窗 (2)                           │
│ ┌─────────────────────────────────┐│
│ │ 门 1 - 线段 2-3 中点            ││
│ │ 类型: [普通门▼] 状态: [关闭▼] ││
│ │ 宽度: [60]                      ││
│ │ [删除]                          ││
│ └─────────────────────────────────┘│
│ ┌─────────────────────────────────┐│
│ │ 窗 2 - 线段 5-6 位置 0.3        ││
│ │ 类型: [窗户▼] 状态: [开启▼]   ││
│ │ 宽度: [80]                      ││
│ │ 视线: [无▼] (覆盖墙体默认)    ││
│ │ [删除]                          ││
│ └─────────────────────────────────┘│
│ [+ 添加门]                          │
├─────────────────────────────────────┤
│ [📐 在画布上编辑锚点]               │
└─────────────────────────────────────┘
```

### 画布锚点编辑模式

**激活方式**：
1. 选中带 `WallStructureComponent` 的对象
2. 点击 Inspector 的"在画布上编辑锚点"按钮
3. 或工具栏切换到"墙体编辑模式"

**显示元素**：
```
地图画布
  ├─ 锚点 Handle（蓝色圆圈，半径 6px）
  │   - P0, P1, P2, ... Pn
  │   - 鼠标悬停放大到 8px
  │   - 选中时高亮为红色
  │
  ├─ 连线（虚线，灰色，1px）
  │   - P0→P1, P1→P2, ..., Pn-1→Pn
  │   - 闭合时：Pn→P0（绿色虚线）
  │
  ├─ 线段中点 Handle（灰色方块，4px，半透明）
  │   - 鼠标悬停显示"+ 插入锚点"提示
  │
  └─ 门标记（橙色矩形，宽度 = door.Width）
      - 根据 State 显示不同颜色
      - 关闭=橙色，开启=绿色，锁定=红色
      - 单击切换状态
```

**交互操作**：
```
拖拽锚点
  → 更新 Points[index]
  → 实时重绘墙体
  → CommandBus.Execute(UpdateWallAnchorCommand)

Shift + 单击线段
  → 在点击位置插入新锚点
  → Points.Insert(segmentIndex + 1, newPoint)

Delete 键 / 右键菜单"删除锚点"
  → 移除选中锚点（至少保留 2 个）
  → Points.RemoveAt(selectedIndex)

右键线段
  → 菜单: "添加门" / "添加窗" / "插入锚点"
  → 计算点击位置的 Position 值
  → Doors.Add(new DoorAttachment { SegmentIndex = i, Position = t })

右键门标记
  → 菜单: "切换状态" / "编辑属性" / "删除门"

Esc 键
  → 退出锚点编辑模式
  → 恢复普通选择工具
```

---

## FOV 算法集成

### 线段提取器（统一两种组件）

```csharp
public static List<WallSegment> ExtractSegments(
    World world, Vector2 origin, double range)
{
    var segments = new List<WallSegment>();
    
    // 1. 旧式单线段墙体
    foreach (var obj in world.GetObjectsWithComponent<WallComponent>())
    {
        var wall = obj.GetComponent<WallComponent>()!;
        var transform = obj.GetComponent<TransformComponent>();
        
        var p1 = TransformPoint(wall.X1, wall.Y1, transform);
        var p2 = TransformPoint(wall.X2, wall.Y2, transform);
        
        if (DistanceToSegment(origin, p1, p2) > range) 
            continue;
        
        segments.Add(new WallSegment
        {
            Start = p1, End = p2,
            BlocksSight = wall.Blocks(SenseType.Sight)
        });
    }
    
    // 2. 新式多段墙体（含门处理）
    foreach (var obj in world.GetObjectsWithComponent<WallStructureComponent>())
    {
        var ws = obj.GetComponent<WallStructureComponent>()!;
        var tf = obj.GetComponent<TransformComponent>();
        
        for (int i = 0; i < ws.Points.Count - 1; i++)
        {
            var p1 = TransformPoint(ws.Points[i], tf);
            var p2 = TransformPoint(ws.Points[i + 1], tf);
            
            if (DistanceToSegment(origin, p1, p2) > range) 
                continue;
            
            // 检查该线段上是否有开启的门
            var door = ws.Doors.FirstOrDefault(d => 
                d.SegmentIndex == i && d.State == DoorState.Open);
            
            if (door != null)
            {
                // 拆分线段：门两侧的墙段
                AddSegmentsAroundDoor(segments, p1, p2, door, ws);
            }
            else
            {
                segments.Add(new WallSegment 
                { 
                    Start = p1, End = p2,
                    BlocksSight = ws.Sight != SenseLevel.None 
                });
            }
        }
        
        // 闭合路径：首尾相连
        if (ws.IsClosed && ws.Points.Count > 2)
        {
            var p1 = TransformPoint(ws.Points[^1], tf);
            var p2 = TransformPoint(ws.Points[0], tf);
            segments.Add(new WallSegment { Start = p1, End = p2 });
        }
    }
    
    return segments;
}
```

---

## 存档格式（DTO）

```csharp
// MapEngine.Core/Data/WallStructureData.cs
public struct WallStructureData
{
    public PointData[] Points { get; set; }
    public bool IsClosed { get; set; }
    
    // 感知通道
    public int Sight { get; set; }
    public int Move { get; set; }
    public int Sound { get; set; }
    public int Light { get; set; }
    
    // 渲染
    public double Thickness { get; set; }
    public string Color { get; set; }
    
    // 门列表
    public DoorAttachmentData[]? Doors { get; set; }
}

public struct DoorAttachmentData
{
    public string Id { get; set; }
    public int SegmentIndex { get; set; }
    public double Position { get; set; }
    public double Width { get; set; }
    public int Kind { get; set; }     // 0=Door, 1=Secret, 2=Window
    public int State { get; set; }    // 0=Closed, 1=Open, 2=Locked
    public int? SightOverride { get; set; }
    public int? MoveOverride { get; set; }
}

public struct PointData
{
    public double X { get; set; }
    public double Y { get; set; }
}
```

---

## 实现优先级

### Phase 1: 数据结构与存档（1天）
- ✅ `WallStructureComponent.cs`
- ✅ `WallStructureData` DTO
- ✅ SceneSerializer 读写逻辑
- ✅ 单元测试（存档往返）

### Phase 2: Inspector 编辑器（1天）
- ✅ `WallStructureEditorViewModel.cs`
- ✅ `WallStructureEditor.axaml`
- ✅ 门列表增删改
- ✅ 数据绑定与命令

### Phase 3: 画布锚点编辑（2天）
- ✅ 锚点 Handle 渲染
- ✅ 拖拽编辑逻辑
- ✅ 插入/删除锚点
- ✅ 门标记显示与交互

### Phase 4: FOV 集成（1天）
- ✅ `WallSegmentExtractor.cs`
- ✅ 门开关对视野的影响
- ✅ 性能测试（100+ 墙段场景）

**总计：5天（含测试）**
```
