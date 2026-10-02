# Owlbear Rodeo Smoke & Spectre 插件分析

## 插件概述

**Smoke & Spectre** 是 Owlbear Rodeo 的动态战争迷雾扩展，继承自 Dynamic Fog of War。

### 核心功能

1. **个性化视野**
   - 每个玩家只能看到自己 token 的可见范围
   - 基于 token 位置动态计算视线

2. **光源系统**
   - 创建光源照亮地图
   - 光源与墙体交互产生阴影

3. **墙体系统**
   - 绘制墙体阻挡视线
   - 支持门（可开关状态）
   - UVTT 文件导入墙体数据

4. **Token 隐藏**
   - 特定玩家可见的隐藏 token
   - GM 专用视野控制

## 技术实现（根据搜索结果推断）

### 1. 数据结构

根据 [Owlbear SDK 文档](https://docs.owlbear.rodeo/extensions/reference/)，核心概念：

**Items**（场景对象）
- 类型包括: "FOG", "WALL", "IMAGE", "RULER", etc.
- 所有可见对象都是 Item
- 通过 Metadata 扩展自定义数据

**Walls**（墙体）
- 不可见的 Item
- 与 Dynamic Fog 系统交互
- 定义视线阻挡边界

**Fog**（迷雾）
- 场景级别的基础迷雾设置
- 扩展通过 API 动态修改可见区域

### 2. 渲染管线

根据搜索结果：
- **GPU 加速**: 使用 Vertex Shader + Fragment Shader 处理
- **性能因素**: 墙体/光源数量及参数影响性能
- **实时计算**: Token 移动时动态重新计算可见区域

**推测的渲染流程**：
```
1. Token 位置变化 → 触发视野计算
2. 从 Token 位置向外投射射线（Raycasting）
3. 射线与墙体求交，确定视线边界
4. 生成可见多边形（Visibility Polygon）
5. 在 GPU 上渲染迷雾遮罩（Fragment Shader）
6. 将可见区域叠加到地图上
```

### 3. API 架构（基于 Owlbear SDK）

**Scene API** - [文档](https://docs.owlbear.rodeo/extensions/apis/scene/)
- 管理场景中的所有对象
- 访问 Fog 设置
- 操作 Items（墙体/光源/Token）

**Items API** - [文档](https://docs.owlbear.rodeo/extensions/apis/scene/items)
- 创建/更新/删除墙体
- 查询场景中的对象
- 监听对象变化

**Fog API** - [文档](https://docs.owlbear.rodeo/extensions/apis/scene/fog)
- 设置基础迷雾状态
- 控制全局可见性

**Dynamic Fog Reference** - [文档](https://docs.owlbear.rodeo/extensions/reference/dynamic-fog)
- 动态迷雾系统的核心参考
- Shader 处理细节
- 性能优化指南

### 4. 墙体数据格式

根据 [Wall Item Reference](https://docs.owlbear.rodeo/extensions/reference/items/wall)：

```typescript
// 推测的墙体数据结构
interface Wall {
  id: string;
  type: "WALL";
  // 墙体线段的两个端点
  start: { x: number; y: number };
  end: { x: number; y: number };
  // 是否阻挡视线
  blocksVision: boolean;
  // 是否阻挡移动
  blocksMovement: boolean;
  // 门的状态（可选）
  doorState?: "open" | "closed";
  // 自定义元数据
  metadata?: Record<string, any>;
}
```

### 5. 视野计算算法（推测）

**射线投射 + 阴影多边形**：
```
1. 以 Token 为中心，获取视线范围内的所有墙体
2. 对每个墙体端点，从 Token 中心投射射线
3. 计算射线与墙体的交点
4. 对交点按角度排序，构建可见多边形
5. 应用光照衰减（如果有光源系统）
6. 生成最终的视野遮罩
```

经典算法参考：
- **Recursive Shadowcasting** (Roguelike FOV)
- **Raycasting with Sweep Line** (2D Visibility)
- **GPU Ray Marching** (Shader 实现)

## 与 MapEngine 的对比

### MapEngine 当前状态

根据 memory 记录：
- ✅ 基础迷雾工具（画笔涂抹，无视线计算）
- ✅ Token 渲染（Hybrid 双层架构）
- ✅ 地图对象管理（HierarchyItemViewModel）
- ❌ 动态 FOV（视野锥系统规划中）
- ❌ 墙体/门系统（未实现）
- ❌ 光源与阴影（未实现）

### 需要实现的功能

**P1: 墙体系统**
1. WallComponent（起点/终点/类型）
2. 墙体绘制工具（拖拽画线段）
3. 墙体渲染（编辑模式可见，游戏模式隐藏）
4. 存档往返（DTO + SceneSerializer）

**P2: 动态视野计算**
1. FOV 算法实现（Raycasting + Shadowcasting）
2. Token → 墙体 → 可见多边形
3. 多 Token 视野合并（多玩家）
4. 性能优化（空间分区/增量更新）

**P3: 迷雾渲染**
1. GPU Shader 渲染可见区域
2. 探索过/当前可见 两态迷雾
3. 平滑过渡动画
4. 玩家视角隔离（GM 看全图）

**P4: 光源系统**
1. LightComponent（位置/半径/颜色/衰减）
2. 光源与墙体交互产生阴影
3. 多光源叠加

## 实现建议

### 阶段 1: 墙体基础

```csharp
// MapEngine.Core/Components/WallComponent.cs
public class WallComponent : IComponent
{
    public Vector2 Start { get; set; }
    public Vector2 End { get; set; }
    public bool BlocksVision { get; set; } = true;
    public bool BlocksMovement { get; set; } = true;
    public WallType Type { get; set; } = WallType.Solid;
}

public enum WallType
{
    Solid,      // 实墙
    Door,       // 门（可开关）
    Window,     // 窗（阻挡移动，不阻挡视线）
    OneWay      // 单向墙（单向阻挡视线）
}
```

### 阶段 2: FOV 算法

```csharp
// MapEngine.Core/Systems/VisibilitySystem.cs
public class VisibilitySystem
{
    // 计算从 origin 出发的可见多边形
    public Polygon ComputeVisibility(
        Vector2 origin, 
        double range, 
        IEnumerable<Wall> walls)
    {
        // 1. 筛选范围内的墙体
        var nearbyWalls = walls.Where(w => 
            DistanceToSegment(origin, w) <= range);
        
        // 2. 收集所有端点
        var points = new List<Vector2>();
        foreach (var wall in nearbyWalls)
        {
            points.Add(wall.Start);
            points.Add(wall.End);
        }
        
        // 3. 对每个端点投射射线
        var rays = new List<Ray>();
        foreach (var point in points)
        {
            var angle = Math.Atan2(point.Y - origin.Y, 
                                   point.X - origin.X);
            // 投射三条射线：正对、左偏、右偏（处理端点精度）
            rays.Add(new Ray(origin, angle - 0.0001));
            rays.Add(new Ray(origin, angle));
            rays.Add(new Ray(origin, angle + 0.0001));
        }
        
        // 4. 求交并排序
        var intersections = new List<(double angle, Vector2 point)>();
        foreach (var ray in rays)
        {
            var hit = FindNearestIntersection(ray, nearbyWalls, range);
            if (hit != null)
                intersections.Add((ray.Angle, hit.Value));
        }
        intersections = intersections.OrderBy(i => i.angle).ToList();
        
        // 5. 构建多边形
        return new Polygon(intersections.Select(i => i.point));
    }
}
```

### 阶段 3: Shader 渲染

```glsl
// fog_of_war.frag
uniform sampler2D uVisibilityMask; // CPU 计算的可见多边形栅格化
uniform sampler2D uExploredMask;   // 已探索区域累积
uniform vec4 uFogColor;            // 迷雾颜色

void main() {
    vec2 uv = gl_FragCoord.xy / uResolution;
    float visible = texture(uVisibilityMask, uv).r;
    float explored = texture(uExploredMask, uv).r;
    
    if (visible > 0.5) {
        // 当前可见：完全显示
        gl_FragColor = vec4(0.0, 0.0, 0.0, 0.0);
    } else if (explored > 0.5) {
        // 探索过但当前不可见：半透明
        gl_FragColor = uFogColor * 0.5;
    } else {
        // 未探索：完全遮罩
        gl_FragColor = uFogColor;
    }
}
```

## 参考资源

1. **Owlbear Rodeo 官方文档**
   - [扩展 API 总览](https://docs.owlbear.rodeo/extensions/apis/)
   - [Scene API](https://docs.owlbear.rodeo/extensions/apis/scene/)
   - [Items API](https://docs.owlbear.rodeo/extensions/apis/scene/items)
   - [Fog API](https://docs.owlbear.rodeo/extensions/apis/scene/fog)
   - [Wall Item Reference](https://docs.owlbear.rodeo/extensions/reference/items/wall)
   - [Dynamic Fog Reference](https://docs.owlbear.rodeo/extensions/reference/dynamic-fog)

2. **社区插件**
   - [Peekaboo](https://extensions.owlbear.rodeo/peekaboo) - 视野检查器
   - [Scene Importer](https://github.com/Eppinguin/scene-importer) - UVTT 墙体导入
   - [UVTT Importer](https://github.com/Eppinguin/uvtt-importer) - 墙体/门数据导入

3. **FOV 算法参考**
   - [Red Blob Games: 2D Visibility](https://www.redblobgames.com/articles/visibility/)
   - [Recursive Shadowcasting in Python](http://www.roguebasin.com/index.php?title=FOV_using_recursive_shadowcasting)
   - [GPU Raycasting Tutorial](https://github.com/mattdesl/lwjgl-basics/wiki/2D-Pixel-Perfect-Shadows)

4. **相关博客**
   - [Realtime Dynamic Fog - Owlbear 2.3](https://blog.owlbear.rodeo/owlbear-rodeo-2-3-release-week-day-3/)
   - [Automatic Fog Detection - Owlbear 2.4](https://blog.owlbear.rodeo/owlbear-rodeo-2-4-release-notes/)
   - [How to Use UVTT Files](https://www.czepeku.com/blog/how-to-use-uvtt-files-with-owlbear-rodeo)

## 下一步行动

1. **研究现有代码** - 检查 MapEngine 当前迷雾实现
2. **设计墙体架构** - WallComponent + 编辑工具
3. **原型 FOV 算法** - 单元测试验证正确性
4. **集成渲染管线** - Shader + GPU 加速
5. **性能测试** - 大量墙体场景压测

