# 形状控制点系统重写设计

## 问题
当前控制点系统为所有对象生成统一的控制点（四角缩放 + 顶部旋转），不符合形状编辑习惯：
- 直线应该显示端点控制点而非矩形框
- 扇形/锥形还在压扁（坐标系问题未解决）
- 扇形绘制逻辑不符合 Photoshop/枭熊2 习惯（应该先拖半径，再定角度）

## 设计目标

### 1. 直线控制点（ShapeType = "line"）
```
控制点布局：
    ○ 端点1（可拖动，修改起点）
    │
    ├─ ⊕ 中点旋转控制点（拖动旋转整条线）
    │
    ○ 端点2（可拖动，修改终点）
```
- **2 个端点控制点**：蓝色圆圈，半径 6px，拖动改变线段端点坐标
- **1 个中点旋转控制点**：蓝色圆圈（空心），半径 8px，拖动旋转整条线
- **无四角缩放控制点**：直线没有矩形框概念

### 2. 矩形/椭圆控制点（rect / ellipse）
```
保持当前逻辑：
    □ 四角缩放控制点
    ⊕ 顶部旋转控制点
```

### 3. 扇形/锥形控制点（wedge / cone）
**当前问题**：
- 绘制时直接拉出固定角度扇形（不符合习惯）
- 渲染时压扁（坐标系翻转未彻底修复）

**Photoshop 行为**：
1. 点击起点 → 拖拽确定半径（预览圆形）
2. 松手 → 移动鼠标确定起始角度（圆形变成扇形，跟随鼠标旋转）
3. 再次点击 → 确定扇形范围

**枭熊2 行为**（简化版）：
1. 拖拽起点 → 终点：半径 = 距离，方向 = 起点到终点的角度
2. 扇形角度固定（如 60°），但可在 Inspector 修改

**推荐方案**（折中）：
- **保持当前拖拽逻辑**：拖拽确定半径和方向（与枭熊2一致）
- **修复渲染压扁问题**：彻底排查坐标系转换
- **控制点配置**：
  - 圆心：蓝色圆点（可拖动整体）
  - 圆弧端点：2 个控制点（拖动调整角度范围）
  - 半径控制点：1 个（拖动调整半径）

### 4. 多边形控制点（polygon）
```
每个顶点一个控制点：
    ○───○
    │   │
    ○───○
```
- **N 个顶点控制点**：每个顶点可独立拖动
- **无旋转控制点**：拖动顶点即可调整形状

### 5. 自由笔触（freehand）
```
与多边形相同，每个轨迹点一个控制点
```

## 实现计划

### Phase 1: 重写控制点生成逻辑（MapSceneBuilder.cs）
```csharp
private static IReadOnlyList<MapRenderRect> BuildSelectionHandles(MainWindowViewModel viewModel)
{
    var handles = new List<MapRenderRect>();
    
    foreach (var item in viewModel.MapRenderableItems)
    {
        if (!item.IsSelected || !item.ShouldRenderOnMap) continue;
        
        var shape = item.GetComponent<ShapeComponent>();
        if (shape is not null)
        {
            // 根据 ShapeType 生成不同控制点
            switch (shape.ShapeType)
            {
                case "line":
                    BuildLineHandles(handles, item, shape);
                    break;
                case "wedge":
                case "cone":
                    BuildWedgeHandles(handles, item, shape);
                    break;
                case "polygon":
                case "freehand":
                    BuildPolygonHandles(handles, item, shape);
                    break;
                default: // rect, ellipse
                    BuildDefaultHandles(handles, item);
                    break;
            }
        }
        else
        {
            // 非形状对象：使用默认控制点（四角 + 旋转）
            BuildDefaultHandles(handles, item);
        }
    }
    
    return handles;
}
```

### Phase 2: 重写控制点命中测试（MapEditorView.Input.cs）
```csharp
private enum HandleType
{
    Rotate,      // 旋转控制点
    Scale,       // 缩放控制点（四角）
    LineEndpoint,// 直线端点
    LineRotate,  // 直线中点旋转
    Vertex,      // 多边形顶点
}

private (HierarchyItemViewModel? item, HandleType type, int index) HitTestShapeHandle(...)
{
    // 根据形状类型检测不同控制点
}
```

### Phase 3: 修复扇形渲染压扁问题
**排查路径**：
1. ✅ MapEditorView.Tools.cs L617 - 角度计算（已修复取反）
2. ⏳ MapSceneBuilder.cs L872 - 渲染时 Y 轴翻转
3. ⏳ 检查 `shape.Rotation` 赋值时机和单位

**当前怀疑**：
- `shape.Rotation` 存的是世界坐标角度（Y向上）
- 渲染时 `cy - sin(angle)` 翻转回屏幕坐标（Y向下）
- 但 `shape.Direction` 也参与计算，可能重复翻转

### Phase 4: 扇形绘制交互优化（可选）
如需实现 Photoshop 式两步绘制：
1. 添加状态机：`WedgeDragState { None, DraggingRadius, SelectingAngle }`
2. 第一次拖拽：显示圆形预览，松手记录半径
3. 移动鼠标：扇形跟随鼠标旋转
4. 再次点击：确定扇形

**注意**：此为增强功能，非本次 bug 修复范围。

## 测试计划

### 直线控制点
1. 绘制直线
2. 选中后应显示：2 个端点控制点 + 1 个中点旋转控制点
3. 拖动端点 → 线段端点移动
4. 拖动中点 → 整条线旋转（保持长度）

### 扇形渲染
1. 绘制扇形
2. **向上拖拽** → 扇形向上（不翻转）
3. **向下拖拽** → 扇形向下（不翻转）
4. **向左/右拖拽** → 扇形不压扁

### 多边形控制点
1. 绘制多边形
2. 选中后每个顶点显示控制点
3. 拖动任意顶点 → 形状变化

## 当前优先级
1. ✅ 墙体显示（已完成）
2. ✅ 圆形位移（已完成）
3. ⚠️ 扇形压扁 + 翻转（待彻底修复）
4. 🎯 **直线控制点重写**（本次任务）
5. 🎯 **扇形控制点设计**（本次任务）
6. ⏳ 扇形两步式绘制（后续优化）
