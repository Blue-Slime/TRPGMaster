# 墙体系统存档数据结构实现完成

## 实施时间：2026-10-02

## 已完成内容

### 1. 核心数据结构（MapEngine.Core/World/CoreStructs.cs）

#### WallPathData
- **字段**：
  - `List<PointData> Points` — 锚点列表（墙体骨架）
  - `bool IsClosed` — 是否闭合路径
  - `int Sight` — 视线阻挡等级（0-2）
  - `int Move` — 移动阻挡等级（0-2）
  - `int Sound` — 声音阻挡等级（0-2）
  - `int Light` — 光照阻挡等级（0-2）
  - `double Thickness` — 墙体厚度（默认 5）
  - `string Color` — 墙体颜色（默认 "#E74C3C"）
  - `List<DoorSegmentData>? Doors` — 门窗段列表（可选）

#### DoorSegmentData
- **字段**：
  - `string Id` — 门窗唯一标识
  - `int StartAnchorIndex` — 起始锚点索引
  - `int EndAnchorIndex` — 结束锚点索引
  - `int Kind` — 门窗类型（0=门, 1=窗户, 2=拱门, 3=密门）
  - `int State` — 门状态（0=关闭, 1=开启, 2=锁定）
  - `int Swing` — 开门方向（0=向左, 1=向右, 2=双向）
  - `int? SightOverride` — 视线覆盖值（null=继承墙体）
  - `int? MoveOverride` — 移动覆盖值（null=继承墙体）

### 2. DTO 集成（MapEngine.Avalonia/Services/FakeProjectDataLoader.cs）

- 在 `HierarchyNodeDto` 中添加：
  ```csharp
  public WallPathData? WallPathV2 { get; set; }
  ```

### 3. 序列化集成（MapEngine.Avalonia/Services/SceneFileLoader.cs）

- 在 `GameObjectToDto()` 中添加 WallPathComponent 处理：
  - 提取 `WallPathComponent`
  - 转换 `Points` 列表为 `PointData`
  - 转换四感知等级（Sight/Move/Sound/Light）为 int
  - 转换 `Doors` 列表为 `DoorSegmentData`
  - 处理门窗的 `SightOverride`/`MoveOverride` 可空值

## 架构特点

### ✅ 遵循现有模式
- 使用 `struct` 定义数据结构（零 GC 开销）
- 枚举值存储为 `int`（JSON 兼容）
- 可选字段使用 `?` 标记（`List<DoorSegmentData>?`）
- 字段名与 Component 完全对应

### ✅ 存档路径完整
按照 [Map Component Persistence](map-component-persistence.md) 要求：
1. ✅ **MapEngine.Core/World/CoreStructs.cs** — WallPathData + DoorSegmentData 定义
2. ✅ **MapEngine.Avalonia/Services/FakeProjectDataLoader.cs** — HierarchyNodeDto.WallPathV2
3. ✅ **MapEngine.Avalonia/Services/SceneFileLoader.cs** — GameObjectToDto() 序列化逻辑
4. ⏳ **MapEngine.Avalonia/Services/SceneSerializer.cs** — FromDocument() 反序列化（待实现）
5. ⏳ **MapEngine.Avalonia/ViewModels/HierarchyItemViewModel.cs** — ViewModel 同步（待实现）

## 编译验证

```bash
✅ MapEngine.Core.csproj — 编译通过（0 错误 0 警告）
✅ MapEngine.Avalonia.csproj — 编译通过（0 错误 4 警告，与墙体无关）
```

## 下一步

### Day 2-4 任务（依赖此存档基础）
1. **SceneSerializer.FromDocument()** — 从 JSON 还原 WallPathComponent
2. **HierarchyItemViewModel** — 墙体属性面板双向绑定
3. **WallPathTool** — 绘制工具（复用 PolygonTool 逻辑）
4. **WallPathRenderer** — OpenGL 渲染（线段 + 门窗图标）
5. **DoorInteraction** — 右键菜单 + 状态切换

## 参考

- 现有组件范例：`ShapeData` / `TextData` / `GraphNodeData`
- 枚举映射：`SenseLevel` (0/10/20) → int, `DoorKind`/`DoorState` → int
- 序列化模式：`SceneFileLoader.cs:90-160`（Shape/Text/Token/GraphNode 处理）
