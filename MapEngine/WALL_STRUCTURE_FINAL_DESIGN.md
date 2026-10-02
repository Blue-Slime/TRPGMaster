# 墙体系统最终设计：完全矢量化 + 锚点区间门窗

## 核心理念

**墙体 = 矢量路径对象**（与 Shape 一致）
- 一个对象 = 一条完整的墙体路径
- 支持开放路径（L型墙、走廊隔断）和闭合路径（房间外墙）
- 门窗 = 路径上的特殊锚点区间（两个锚点定义门窗的起止位置）

---

## 组件定义

```csharp
// MapEngine.Core/Components/WallPathComponent.cs
public sealed class WallPathComponent : ComponentBase, ISpatialComponent
{
    public override string TypeName => "WallPath";
    
    /// <summary>墙体路径的所有锚点（本地坐标）</summary>
    public List<Vector2> Points { get; set; } = new();
    
    /// <summary>是否闭合路径（首尾自动连接）</summary>
    public bool IsClosed { get; set; } = false;
    
    /// <summary>默认感知阻挡等级（应用到所有线段）</summary>
    public SenseLevel Sight { get; set; } = SenseLevel.Normal;
    public SenseLevel Move { get; set; } = SenseLevel.Normal;
    public SenseLevel Sound { get; set; } = SenseLevel.Normal;
    public SenseLevel Light { get; set; } = SenseLevel.Normal;
    
    /// <summary>渲染参数</summary>
    public double Thickness { get; set; } = 5;
    public string Color { get; set; } = "#FF4444";
    
    /// <summary>门窗列表（锚点区间定义）</summary>
    public List<DoorSegment> Doors { get; set; } = new();
    
    public RectD GetLocalBounds()
    {
        if (Points.Count == 0) return RectD.Empty;
        var xs = Points.Select(p => p.X);
        var ys = Points.Select(p => p.Y);
        return new RectD(xs.Min(), ys.Min(), 
                        xs.Max() - xs.Min(), 
                        ys.Max() - ys.Min());
    }
    
    public override IComponent Clone() => new WallPathComponent
    {
        Points = new List<Vector2>(Points),
        IsClosed = IsClosed,
        Sight = Sight, Move = Move, Sound = Sound, Light = Light,
        Thickness = Thickness, Color = Color,
        Doors = Doors.Select(d => d.Clone()).ToList()
    };
}

/// <summary>
/// 门窗定义：路径上的锚点区间 [StartIndex, EndIndex]。
/// 区间内的线段按门窗规则处理（开启时不阻挡视线）。
/// </summary>
public sealed class DoorSegment
{
    /// <summary>唯一标识</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();
    
    /// <summary>门窗起始锚点索引（包含）</summary>
    public int StartAnchorIndex { get; set; }
    
    /// <summary>门窗结束锚点索引（包含）</summary>
    public int EndAnchorIndex { get; set; }
    
    /// <summary>门窗类型</summary>
    public DoorKind Kind { get; set; } = DoorKind.Door;
    
    /// <summary>门窗状态</summary>
    public DoorState State { get; set; } = DoorState.Closed;
    
    /// <summary>门窗方向（用于渲染开门方向）</summary>
    public DoorSwing Swing { get; set; } = DoorSwing.Right;
    
    /// <summary>感知覆盖（null = 继承墙体默认值）</summary>
    public SenseLevel? SightOverride { get; set; }
    public SenseLevel? MoveOverride { get; set; }
    
    /// <summary>
    /// 获取门窗覆盖的线段索引列表。
    /// 例如：StartAnchor=2, EndAnchor=4 → 覆盖线段 [2-3, 3-4]
    /// </summary>
    public List<int> GetCoveredSegments()
    {
        var segments = new List<int>();
        for (int i = StartAnchorIndex; i < EndAnchorIndex; i++)
            segments.Add(i);
        return segments;
    }
    
    public DoorSegment Clone() => new DoorSegment
    {
        Id = Id,
        StartAnchorIndex = StartAnchorIndex,
        EndAnchorIndex = EndAnchorIndex,
        Kind = Kind, State = State, Swing = Swing,
        SightOverride = SightOverride,
        MoveOverride = MoveOverride
    };
}

public enum DoorKind
{
    Door,       // 普通门（可开关）
    SecretDoor, // 密门（GM 专属显示）
    Window,     // 窗户（视线可穿透，移动阻挡）
    Archway     // 拱门（永久开放，不可关闭）
}

public enum DoorState
{
    Closed,  // 关闭（阻挡视线+移动）
    Open,    // 开启（不阻挡）
    Locked   // 锁定（阻挡+无法交互）
}

public enum DoorSwing
{
    None,    // 无开门方向（窗户/拱门）
    Left,    // 向左开
    Right    // 向右开
}
```

---

## 绘制工具：复用矢量编辑逻辑

### 工具模式：墙体绘制

**激活**：工具栏选择"墙体工具"（与 Shape 工具并列）

**绘制流程**（与 Shape.Polygon 一致）：
```
1. 单击画布 → 添加第一个锚点
2. 移动鼠标 → 实时预览线段
3. 单击 → 添加第二个锚点
4. 继续单击 → 添加更多锚点（L型/U型/任意折线）
5. 双击 / Enter → 完成路径（开放路径）
6. 或首尾接近时提示"闭合路径？"→ 点击确认（闭合路径）
7. Esc → 取消绘制
```

**辅助功能**：
- **Shift**：吸附 45° 角度（与上一锚点对齐）
- **Ctrl+Z**：撤销上一个锚点
- **右键**：删除上一个锚点

**创建结果**：
```csharp
var wall = new GameObject("墙体");
wall.AddComponent(new WallPathComponent
{
    Points = [...绘制的锚点...],
    IsClosed = ...用户选择...,
    Sight = SenseLevel.Normal,
    Move = SenseLevel.Normal,
    // 其他默认值
});
```

---

## 锚点编辑：复用 Shape 编辑器

**选中墙体对象时**：
- 显示所有锚点 Handle（与 Shape.Polygon 一致）
- 拖拽锚点 → 调整墙体形状
- Shift+单击线段 → 插入新锚点
- 选中锚点 + Delete → 删除锚点（至少保留 2 个）
- 门窗区间的锚点显示为特殊颜色（橙色）

**锚点 Handle 渲染**：
```
普通锚点：蓝色圆圈（6px，可拖拽）
门窗起点：橙色圆圈 + "D" 标记
门窗终点：橙色圆圈 + "D" 标记
首尾锚点（闭合路径）：绿色圆圈
```

---

## 门窗添加流程

### 交互方式 1：选择锚点区间

```
1. 选中墙体对象（进入锚点编辑模式）
2. 框选或 Shift+单击选择两个锚点（例如 P3 和 P5）
3. 右键 → 菜单："创建门" / "创建窗" / "创建拱门"
4. 系统创建 DoorSegment
   {
       StartAnchorIndex = 3,
       EndAnchorIndex = 5,
       Kind = DoorKind.Door,
       State = DoorState.Closed
   }
5. 在 P3-P4、P4-P5 线段上渲染门标记
```

### 交互方式 2：在线段上直接添加门（推荐）

```
1. 选中墙体对象
2. 右键单击某条线段（例如 P2-P3 之间）
3. 菜单："添加门（自动插入锚点）"
4. 系统执行：
   a. 在点击位置插入两个新锚点 P2a 和 P2b
      （间距 = 门宽度，例如 60 单位）
   b. 更新 Points 数组：
      [P0, P1, P2, P2a, P2b, P3, P4, ...]
   c. 创建 DoorSegment
      {
          StartAnchorIndex = 3,  // P2a 的索引
          EndAnchorIndex = 4,    // P2b 的索引
          Kind = DoorKind.Door
      }
5. 用户可拖拽 P2a/P2b 调整门的宽度和位置
```

**优势**：
- ✅ 门的位置精确（锚点定义，不依赖浮点比例）
- ✅ 门宽度可视化调整（拖拽两个锚点）
- ✅ 删除门 = 删除两个锚点 + DoorSegment 记录
- ✅ 复用现有的锚点编辑逻辑（无需新交互）

---

## 门窗渲染

### 画布显示（编辑模式）

```
墙体线段：红色实线（5px）
门窗区间：
  - 关闭：橙色虚线 + 门图标（🚪）
  - 开启：绿色虚线 + 开门弧线
  - 锁定：红色虚线 + 锁图标（🔒）
```

### 渲染实现

```csharp
// MapSceneBuilder.cs - 提取墙体渲染数据
foreach (var obj in scene.GetObjectsWithComponent<WallPathComponent>())
{
    var wall = obj.GetComponent<WallPathComponent>()!;
    var transform = obj.GetComponent<TransformComponent>();
    
    // 标记哪些线段被门窗覆盖
    var doorSegments = new HashSet<int>();
    foreach (var door in wall.Doors)
    {
        foreach (var segIdx in door.GetCoveredSegments())
            doorSegments.Add(segIdx);
    }
    
    // 渲染墙体线段
    for (int i = 0; i < wall.Points.Count - 1; i++)
    {
        var p1 = TransformPoint(wall.Points[i], transform);
        var p2 = TransformPoint(wall.Points[i + 1], transform);
        
        if (doorSegments.Contains(i))
        {
            // 门窗区间：虚线渲染
            var door = wall.Doors.First(d => d.GetCoveredSegments().Contains(i));
            scene.WallLines.Add(new MapRenderRect
            {
                /* 虚线样式，颜色根据 door.State */
            });
            
            // 门图标（在区间中点）
            if (i == door.StartAnchorIndex)
            {
                var doorCenter = GetDoorCenter(wall.Points, door);
                scene.DoorMarkers.Add(new MapRenderDoor
                {
                    CenterX = doorCenter.X,
                    CenterY = doorCenter.Y,
                    Kind = door.Kind,
                    State = door.State,
                    Swing = door.Swing
                });
            }
        }
        else
        {
            // 普通墙体：实线渲染
            scene.WallLines.Add(new MapRenderRect
            {
                /* 实线样式 */
            });
        }
    }
    
    // 闭合路径：首尾相连
    if (wall.IsClosed && wall.Points.Count > 2)
    {
        var p1 = TransformPoint(wall.Points[^1], transform);
        var p2 = TransformPoint(wall.Points[0], transform);
        scene.WallLines.Add(/* ... */);
    }
}
```

---

## FOV 算法集成

### 线段提取（处理门窗）

```csharp
public static List<WallSegment> ExtractFOVSegments(
    World world, Vector2 origin, double range)
{
    var segments = new List<WallSegment>();
    
    foreach (var obj in world.GetObjectsWithComponent<WallPathComponent>())
    {
        var wall = obj.GetComponent<WallPathComponent>()!;
        var tf = obj.GetComponent<TransformComponent>();
        
        // 遍历所有线段
        for (int i = 0; i < wall.Points.Count - 1; i++)
        {
            var p1 = TransformPoint(wall.Points[i], tf);
            var p2 = TransformPoint(wall.Points[i + 1], tf);
            
            if (DistanceToSegment(origin, p1, p2) > range)
                continue;
            
            // 检查该线段是否被门窗覆盖
            var door = wall.Doors.FirstOrDefault(d => 
                d.GetCoveredSegments().Contains(i));
            
            if (door != null)
            {
                // 门窗逻辑
                bool blocks = door.State switch
                {
                    DoorState.Open => false,  // 开启：不阻挡
                    DoorState.Closed => door.Kind switch
                    {
                        DoorKind.Window => false,  // 窗户关闭：视线可穿透
                        _ => true                  // 普通门关闭：阻挡
                    },
                    DoorState.Locked => true  // 锁定：阻挡
                };
                
                if (blocks)
                {
                    segments.Add(new WallSegment 
                    { 
                        Start = p1, End = p2,
                        BlocksSight = true 
                    });
                }
                // 开启的门/窗：跳过该线段（不阻挡视线）
            }
            else
            {
                // 普通墙体：按默认阻挡规则
                segments.Add(new WallSegment 
                { 
                    Start = p1, End = p2,
                    BlocksSight = wall.Sight != SenseLevel.None 
                });
            }
        }
        
        // 闭合路径
        if (wall.IsClosed && wall.Points.Count > 2)
        {
            var p1 = TransformPoint(wall.Points[^1], tf);
            var p2 = TransformPoint(wall.Points[0], tf);
            segments.Add(new WallSegment { Start = p1, End = p2 });
        }
    }
    
    return segments;
}
```

---

## 存档格式

```csharp
// MapEngine.Core/Data/WallPathData.cs
public struct WallPathData
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
    
    // 门窗列表
    public DoorSegmentData[]? Doors { get; set; }
}

public struct DoorSegmentData
{
    public string Id { get; set; }
    public int StartAnchorIndex { get; set; }
    public int EndAnchorIndex { get; set; }
    public int Kind { get; set; }     // 0=Door, 1=Secret, 2=Window, 3=Archway
    public int State { get; set; }    // 0=Closed, 1=Open, 2=Locked
    public int Swing { get; set; }    // 0=None, 1=Left, 2=Right
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

## Inspector 显示

```
┌─────────────────────────────────────┐
│ 墙体路径                            │
├─────────────────────────────────────┤
│ ☑ 闭合路径                          │
│ 锚点数量: 12                        │
│ 门窗数量: 3                         │
├─────────────────────────────────────┤
│ 默认阻挡                            │
│   视线: [普通▼]  移动: [普通▼]     │
│   声音: [无▼]    光照: [普通▼]     │
├─────────────────────────────────────┤
│ 渲染                                │
│   厚度: [5]                         │
│   颜色: [■ #FF4444] [选择]         │
├─────────────────────────────────────┤
│ 门窗列表                            │
│ ┌─────────────────────────────────┐│
│ │ 🚪 门 1 - 锚点 3→5 (2段)       ││
│ │ 类型: [普通门▼] 状态: [关闭▼] ││
│ │ 开门方向: [向右▼]              ││
│ │ [删除门] [在画布上定位]        ││
│ └─────────────────────────────────┘│
│ ┌─────────────────────────────────┐│
│ │ 🪟 窗 2 - 锚点 7→8 (1段)       ││
│ │ 类型: [窗户▼] 状态: [开启▼]   ││
│ │ 视线: [无▼] (覆盖)             ││
│ │ [删除窗]                        ││
│ └─────────────────────────────────┘│
│                                     │
│ 💡 提示：右键墙体线段可添加门窗     │
└─────────────────────────────────────┘
```

---

## 实现计划（4天）

### Day 1: 组件与存档
- ✅ `WallPathComponent.cs`
- ✅ `WallPathData` DTO
- ✅ SceneSerializer 读写
- ✅ 单元测试（存档往返）

### Day 2: 绘制工具
- ✅ 墙体绘制工具（复用 Shape.Polygon 逻辑）
- ✅ 锚点编辑模式（复用现有 Handle 系统）
- ✅ 闭合路径检测与提示

### Day 3: 门窗系统
- ✅ 右键线段添加门（自动插入锚点）
- ✅ 门窗区间渲染（虚线 + 图标）
- ✅ Inspector 门窗列表编辑器
- ✅ 门状态切换（单击门图标）

### Day 4: FOV 集成
- ✅ WallSegmentExtractor（门窗阻挡逻辑）
- ✅ 单元测试（开门/关门对视野的影响）
- ✅ 性能测试（100+ 墙段场景）

**总计：4天**
