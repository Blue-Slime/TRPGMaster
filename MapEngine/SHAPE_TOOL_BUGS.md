# 形状工具问题诊断与修复计划

## 🐛 已确认问题清单

### P0 - 阻塞性问题（立即修复）

#### 1. 墙体不显示 ⚠️
**现象**：使用墙体工具绘制墙体后不显示

**根本原因**：
- 楼层过滤逻辑：新建墙体的 Floor 默认为 0
- 如果 `ActiveCharacter` 为 null，`playerFloor` 也是 0
- 但 `ShouldRenderWalls()` 的逻辑可能有 bug

**诊断步骤**：
1. ✅ 已添加调试输出到 MapSceneBuilder.cs 第 587-594 行
2. 运行应用，绘制墙体，查看输出窗口的 `[DEBUG]` 消息
3. 确认墙体的 Floor/BuildingId 和过滤参数

**修复方案**（待确认）：
- 方案 A：修复 `ShouldRenderWalls()` 逻辑
- 方案 B：墙体工具创建时明确设置 Floor=当前关注楼层

#### 2. FOV 不被墙体裁剪 ⚠️
**现象**：Token 视锥不被墙体阻挡

**可能原因**：
- 墙体未显示 → FOV 提取不到墙体
- 或者墙体坐标系转换错误

**依赖**：必须先修复问题 1（墙体显示）

---

### P1 - 核心功能缺陷（优先修复）

#### 3. 控制点失效 ⚠️
**现象**：形状绘制后，选中时的控制点无法拖拽

**原因分析**：
- ShapeComponent 存储的是相对坐标（Points 相对 center）
- 但控制点可能期望绝对坐标
- Transform 组件（Rotation/ScaleX/ScaleY）未同步到渲染

**修复步骤**：
1. 检查 `BuildWallHandles()` 是否正确计算控制点位置
2. 检查拖拽逻辑是否更新了正确的属性

#### 4. 旋转不生效 ⚠️
**现象**：拖拽旋转控制点时图形不旋转

**原因**：
- `item.Rotation` 属性被更新
- 但 `ShapeComponent` 渲染时未应用旋转变换

**修复**：在 `MapSceneBuilder.BuildShapes()` 中应用 Transform

#### 5. 缩放不生效 ⚠️
**现象**：拖拽缩放控制点无效果

**原因**：同上，ScaleX/ScaleY 未应用到渲染

**修复**：应用 Transform 矩阵

#### 6. 直线控制点错误 ⚠️
**现象**：直线应该显示两个端点控制，但显示的是矩形包围框

**原因**：
- 直线的 ShapeComponent 存储了两个端点（PolylinePoints）
- 但控制点生成逻辑可能按矩形包围框处理

**修复**：在 `BuildWallHandles()` 中为直线特殊处理

#### 7. 圆形绘制位移 ⚠️
**现象**：绘制圆形时，松手后圆心位置偏移

**原因**：
- MapEditorView.Tools.cs 第 531-537 行：circle 模式强制 side = Min(w,h)
- 但设置位置时用的是 `_toolDragStart`（起点），而非计算的中心

**修复**：
```csharp
// 当前代码（错误）
Canvas.SetLeft(_shapePreviewEllipse, _toolDragStart.X);
Canvas.SetTop(_shapePreviewEllipse, _toolDragStart.Y);

// 应该改为（中心对齐）
Canvas.SetLeft(_shapePreviewEllipse, _toolDragStart.X - side/2);
Canvas.SetTop(_shapePreviewEllipse, _toolDragStart.Y - side/2);
```

#### 8. 扇形压扁 ⚠️
**现象**：扇形预览正常，松手后纵向压缩

**原因**：
- 预览时用的是屏幕坐标（正确）
- 提交时转世界坐标，但 Y 轴翻转未正确处理

**定位**：MapEditorView.Tools.cs 第 612-620 行

**修复**：检查 `dirDeg` 计算，确保 Y 轴翻转一致

#### 9. 凹多边形错误 ⚠️
**现象**：绘制凹多边形时预览正常，确定后出现错误

**原因**：
- Avalonia Polyline 可以渲染凹多边形
- 但 Skia/GL 渲染可能用了三角化算法
- 简单三角化算法不支持凹多边形

**修复方案**：
- 方案 A：使用 Ear Clipping 算法（支持凹多边形）
- 方案 B：提示用户只支持凸多边形
- 方案 C：检测凹多边形，自动转换为多个凸多边形

---

### P2 - 体验优化（次要）

#### 10. 无吸附功能 ⚠️
**现象**：绘制时不吸附到网格

**原因**：`IsSnapToGrid` 属性存在但未实现

**修复**：在 CommitShape/CommitDraw 中应用吸附逻辑

#### 11. 虚线切换未实现 ❌
**现象**：无法切换虚线/点线样式

**原因**：
- ViewModel 有 `CycleStrokeStyleCommand`
- 但未绑定到快捷键或工具栏按钮

**修复**：
- 添加快捷键绑定（如 Shift+S）
- 或在工具栏添加切换按钮

---

## 🔧 修复优先级

### 立即修复（今天）
1. **墙体不显示** - 诊断 ShouldRenderWalls 逻辑
2. **圆形位移** - 简单坐标修正（5 分钟）
3. **扇形压扁** - Y 轴翻转修正（10 分钟）

### 优先修复（本周）
4. **控制点失效** - Transform 同步
5. **旋转/缩放不生效** - 应用 Transform 矩阵
6. **直线控制点** - 特殊处理

### 后续优化
7. **凹多边形** - 三角化算法升级
8. **吸附功能** - 网格吸附实现
9. **虚线切换** - 快捷键绑定

---

## 📋 测试步骤（修复后验证）

### 墙体测试
1. [ ] 切换到墙体工具（工具栏 🧱）
2. [ ] 在地图上单击 3 次设置锚点
3. [ ] 双击闭合路径
4. [ ] **预期**：立即显示红色墙体线条
5. [ ] **预期**：墙体锚点显示为蓝色圆圈

### FOV 测试
1. [ ] 创建一个 Token
2. [ ] Inspector → 添加视锥（Range=60, FOV=90）
3. [ ] 绘制一条墙穿过视锥
4. [ ] **预期**：视锥被墙体裁剪（墙后变暗）

### 形状工具测试
1. [ ] 圆形：拖拽绘制，松手后圆心应在拖拽起点
2. [ ] 扇形：拖拽绘制，松手后形状不变形
3. [ ] 矩形：拖拽旋转控制点，图形应旋转
4. [ ] 直线：控制点应在两端，不是矩形框

---

## 🔍 调试工具

### 查看调试输出
```bash
# Windows（Visual Studio 输出窗口）
# 或使用 DebugView (Sysinternals)

# 墙体渲染调试信息会输出：
[DEBUG] 墙体: Floor=0, BuildingId=, FocusFloor=0, PlayerFloor=0, PlayerBuilding=
[DEBUG] 墙体被楼层过滤隐藏！  # 如果出现这行说明被过滤了
```

### 手动测试命令
```bash
cd "G:/跑团大师/02-新项目"
dotnet run --project MasterClient/MasterClient.csproj
```

---

**下一步**：运行应用，绘制墙体，查看调试输出，确认问题根源。
