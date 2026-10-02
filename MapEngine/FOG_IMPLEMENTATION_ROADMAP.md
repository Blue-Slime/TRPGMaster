# MapEngine 当前迷雾实现总结

## 已实现功能

### 1. 基础迷雾系统（手动绘制）

**数据结构**：
```csharp
// MainWindowViewModel.Fog.cs
public List<FogRegion> FogRevealedRegions { get; } = new();

public sealed class FogRegion
{
    public (double X, double Y, double Width, double Height) Bounds { get; }
    public IReadOnlyList<(double X, double Y)> Points { get; }
    
    public static FogRegion FromRect(double wx, double wy, double w, double h)
}
```

**工具交互**：
- `FogRevealRect()` - GM 拖拽擦除迷雾（揭示区域）
- `FogPaintRect()` - GM 绘制遮挡（简单实现：删除重叠区域）
- `FogClearAll()` - 全图变暗
- `FogRevealAll()` - 全图揭示（添加超大矩形）

**渲染管线**（GPU 双 Pass）：
```csharp
// MapRenderPipeline.cs:1340-1390
// Pass 1: 绘制全屏暗色层（半透明黑色）
_gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
// 绘制 NDC 全屏四边形

// Pass 2: 揭示区域抠孔（写入 alpha=0）
_gl.BlendFunc(BlendingFactor.Zero, BlendingFactor.Zero);
_gl.ColorMask(false, false, false, true);  // 只写 alpha 通道
// 三角扇三角化每个多边形，写入透明 alpha
```

**优点**：
- ✅ GPU 加速渲染，性能良好
- ✅ 支持任意凸多边形（虽然当前只用矩形）
- ✅ 双 Pass 透明度抠孔，边缘平滑

**局限性**：
- ❌ 纯手动绘制，无视线计算
- ❌ 不支持 Token 动态视野
- ❌ 没有"探索过/当前可见"两态
- ❌ 没有墙体系统

## 与 Owlbear Smoke & Spectre 的差距

| 功能 | MapEngine | Owlbear S&S |
|------|-----------|-------------|
| 手动迷雾绘制 | ✅ | ✅ |
| Token 动态视野 | ❌ | ✅ |
| 墙体/门系统 | ❌ | ✅ |
| 光源与阴影 | ❌ | ✅ |
| 探索过/当前可见 | ❌ | ✅ |
| UVTT 导入 | ❌ | ✅ |
| 玩家视角隔离 | ❌ | ✅ |

## 实现路线图

### Phase 1: 墙体系统（基础设施）

**目标**：让 GM 能绘制墙体线段，存档往返，编辑模式可见。

**任务清单**：
1. ✅ 创建 `WallComponent.cs`
   ```csharp
   public class WallComponent : IComponent
   {
       public Vector2 Start { get; set; }
       public Vector2 End { get; set; }
       public bool BlocksVision { get; set; } = true;
       public bool BlocksMovement { get; set; } = true;
       public WallType Type { get; set; } = WallType.Solid;
   }
   ```

2. ✅ 墙体绘制工具（Tools.cs）
   - 墙体子工具激活时，拖拽画线段
   - 按住 Shift 吸附 45° 角度
   - 双击/Esc 结束当前墙体

3. ✅ 墙体渲染（MapSceneBuilder + Pipeline）
   - 编辑模式：红色实线，端点圆圈 handle
   - 游戏模式：完全隐藏
   - 选中墙体：高亮显示，可拖拽端点

4. ✅ 存档往返（5 路径规则）
   - `WallData` DTO 结构
   - `SceneSerializer` 读写
   - `HierarchyNodeDto` 映射
   - `MainWindowViewModel` 快照/构建
   - `MapSceneBuilder` 提取到渲染场景

**工作量估计**：2-3 天（参考 GraphNode 实现）

### Phase 2: FOV 算法（核心逻辑）

**目标**：给定 Token 位置和墙体列表，计算可见多边形。

**算法选择**：
- **Raycasting with Sweep Line**（推荐）
  - 对墙体端点投射射线
  - 求交排序构建可见多边形
  - 算法简单，易调试
  
- **Recursive Shadowcasting**（备选）
  - Roguelike 常用，性能更好
  - 但实现复杂，边缘情况多

**实现文件**：
```csharp
// MapEngine.Core/Systems/VisibilitySystem.cs
public static class VisibilitySystem
{
    // 主入口
    public static Polygon ComputeVisibility(
        Vector2 origin, 
        double range, 
        IEnumerable<WallComponent> walls);
    
    // 辅助方法
    private static Vector2? RayIntersectSegment(Ray ray, Vector2 p1, Vector2 p2);
    private static bool IsWallBlocking(WallComponent wall, Vector2 from, Vector2 to);
}
```

**测试策略**：
- 单墙体：L型、T型、十字
- 多墙体：房间、走廊、开门/关门
- 边缘情况：共线、端点重合、视野边界

**工作量估计**：3-4 天（算法 + 调试 + 测试）

### Phase 3: 动态迷雾渲染

**目标**：Token 移动时实时更新可见区域，渲染"探索过/当前可见"两态。

**数据结构扩展**：
```csharp
// 每个 Token 的当前可见多边形
public Dictionary<string, Polygon> TokenVisibilityCache { get; } = new();

// 全局累积的"探索过"区域（Union 所有历史可见区域）
public List<Polygon> ExploredRegions { get; } = new();
```

**渲染升级**（三态迷雾）：
```glsl
// fog_dynamic.frag
uniform sampler2D uCurrentVisible;  // 当前 Token 视野（实时计算）
uniform sampler2D uExplored;        // 历史探索区域（累积）

void main() {
    float visible = texture(uCurrentVisible, uv).r;
    float explored = texture(uExplored, uv).r;
    
    if (visible > 0.5) {
        gl_FragColor = vec4(0.0, 0.0, 0.0, 0.0);  // 完全透明（当前可见）
    } else if (explored > 0.5) {
        gl_FragColor = vec4(0.0, 0.0, 0.0, 0.5);  // 半透明（探索过）
    } else {
        gl_FragColor = vec4(0.0, 0.0, 0.0, 0.9);  // 不透明（未探索）
    }
}
```

**性能优化**：
- 空间分区：只检测 Token 视野内的墙体
- 增量更新：Token 未移动时跳过计算
- GPU 光栅化：可见多边形在 GPU 端绘制到纹理

**工作量估计**：4-5 天（渲染集成 + 性能优化）

### Phase 4: 光源系统（增强效果）

**目标**：光源照亮地图，墙体产生阴影。

**组件定义**：
```csharp
public class LightComponent : IComponent
{
    public double Radius { get; set; } = 100;
    public string Color { get; set; } = "#FFDD88";
    public double Intensity { get; set; } = 1.0;
    public LightFalloff Falloff { get; set; } = LightFalloff.Quadratic;
}
```

**算法扩展**：
- FOV 算法同时计算光照可达范围
- 墙体投射阴影多边形（Shadow Polygon）
- 多光源叠加（加法混合）

**工作量估计**：3-4 天

## 技术挑战与解决方案

### 挑战 1: 多 Token 视野合并

**问题**：多个玩家 Token 的可见区域需要 Union，复杂度 O(n²)。

**方案**：
- CPU 端合并：使用 Clipper2 库做多边形布尔运算
- GPU 端合并：每个 Token 绘制到同一张纹理，OR 混合

### 挑战 2: 探索过区域累积

**问题**：历史探索区域无限增长，性能下降。

**方案**：
- 定期简化多边形（Douglas-Peucker 算法）
- 限制探索区域数量（合并相邻区域）
- 保存为 RLE 压缩纹理

### 挑战 3: 联机同步

**问题**：墙体/视野数据需要同步给所有客户端。

**方案**：
- 墙体：随地图一次性同步（场景数据）
- 视野：仅同步 GM 视角，玩家视角客户端本地计算
- 探索过区域：定期增量同步（delta 协议）

## 参考代码位置

**当前迷雾实现**：
- `MapEngine.Avalonia/ViewModels/Partials/MainWindowViewModel.Fog.cs` - 数据与逻辑
- `MapEngine.Render/Pipeline/MapRenderPipeline.cs:1340-1390` - GPU 渲染

**复用架构**（可参考的实现模式）：
- `GraphNodeComponent` - 多实例组件存档
- `ShapeComponent` - 矢量几何渲染
- `VisionConeComponent` - 视野锥（局部实现）

**外部库选择**：
- **Clipper2** - 多边形布尔运算（C# 原生）
- **SkiaSharp** - 多边形光栅化（已集成）

## 总结

MapEngine 已有 **扎实的迷雾渲染基础**（GPU 双 Pass 抠孔），只需补充：
1. 墙体系统（数据结构 + 工具）
2. FOV 算法（射线投射）
3. 动态更新（Token 移动触发）
4. 联机同步（delta 协议）

整体工作量约 **12-15 天**，可分阶段交付，每个 Phase 独立可测试。
