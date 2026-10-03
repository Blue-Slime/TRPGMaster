# 坐标系统重构计划

## 问题现状

当前有两套独立的坐标转换逻辑：

### 1. 渲染管线（MapRenderPipeline.cs）
- **坐标系**：NDC（归一化设备坐标，-1 到 1）
- **特点**：需要宽高比补偿，分别计算 `radiusX` 和 `radiusY`
- **转换路径**：世界坐标 → Content坐标 → NDC坐标
- **公式**：
  ```csharp
  // Content → NDC
  var ndcX = (float)((contentX / scene.ViewportWidth) * 2.0 - 1.0);
  var ndcY = (float)(1.0 - (contentY / scene.ViewportHeight) * 2.0);
  
  // 半径转换（需要分别计算）
  var radiusX = (float)(worldRadius * zoom / ViewportWidth * 2.0);
  var radiusY = (float)(worldRadius * zoom / ViewportHeight * 2.0);
  ```

### 2. 预览层（MapEditorView.Tools.cs）
- **坐标系**：屏幕像素坐标（原点在左上角，Y向下）
- **特点**：正方形像素，不需要宽高比补偿
- **转换路径**：世界坐标 → Content坐标 → 屏幕像素
- **公式**：
  ```csharp
  // Content → Screen
  var screenX = viewportWidth / 2.0 + (contentX - cameraCenter.X) * zoom;
  var screenY = viewportHeight / 2.0 + (contentY - cameraCenter.Y) * zoom;
  
  // 半径转换（圆形，不需要补偿）
  var screenRadius = worldRadius * zoom;
  ```

## 问题
1. **开发者困惑**：何时需要宽高比补偿？何时不需要？
2. **重复逻辑**：坐标转换代码分散在各处
3. **维护成本**：修改坐标系统需要同步两处

---

## 重构方案：统一使用世界坐标 + 视口变换矩阵

### 核心思路
让预览层也使用**世界坐标**绘制，通过 Avalonia 的 `RenderTransform` 自动处理视口变换，就像 GL 渲染管线一样。

### 架构设计

```
用户输入（屏幕像素）
    ↓
屏幕 → 世界坐标转换（Input.cs）
    ↓
世界坐标操作（Tools.cs，所有预览都用世界坐标）
    ↓
设置 Canvas.RenderTransform（自动转换到屏幕像素）
    ↓
Avalonia 渲染（自动处理宽高比）
```

### 关键变更

#### 1. 添加视口变换矩阵（MapEditorView.Viewport.cs）
```csharp
/// <summary>世界坐标 → 屏幕像素的变换矩阵（供 _toolOverlayCanvas.RenderTransform 使用）</summary>
private Matrix GetWorldToScreenMatrix()
{
    var viewportSize = GetViewportSize();
    var zoom = _viewModel?.ZoomScale ?? 1.0;
    
    // 1. 平移：相机中心 → 视口中心
    var translateX = viewportSize.Width / 2.0 - _cameraContentCenter.X * zoom;
    var translateY = viewportSize.Height / 2.0 - _cameraContentCenter.Y * zoom;
    
    // 2. 缩放：世界单位 → 屏幕像素
    return Matrix.CreateScale(zoom, zoom) * Matrix.CreateTranslation(translateX, translateY);
}

/// <summary>屏幕像素 → 世界坐标（Content单位）</summary>
private Point ScreenToWorld(Point screenPos)
{
    var viewportSize = GetViewportSize();
    var zoom = _viewModel?.ZoomScale ?? 1.0;
    
    var contentX = _cameraContentCenter.X + (screenPos.X - viewportSize.Width / 2.0) / zoom;
    var contentY = _cameraContentCenter.Y + (screenPos.Y - viewportSize.Height / 2.0) / zoom;
    
    return new Point(contentX, contentY);
}
```

#### 2. 应用变换到 overlay Canvas（MapEditorView.Lifecycle.cs）
```csharp
private void InitializeToolOverlay()
{
    _toolOverlayCanvas = this.FindControl<Canvas>("ToolOverlayCanvas");
    UpdateOverlayTransform(); // 设置初始变换
}

private void UpdateOverlayTransform()
{
    if (_toolOverlayCanvas is null) return;
    _toolOverlayCanvas.RenderTransform = new MatrixTransform(GetWorldToScreenMatrix());
}
```

#### 3. 重写预览方法使用世界坐标（MapEditorView.Tools.cs）

**修改前（屏幕坐标）**：
```csharp
private void UpdateLinePreview(Point screenStart, Point screenEnd)
{
    if (_shapePreviewLine is null) return;
    _shapePreviewLine.StartPoint = screenStart;
    _shapePreviewLine.EndPoint = screenEnd;
}
```

**修改后（世界坐标）**：
```csharp
private void UpdateLinePreview(Point worldStart, Point worldEnd)
{
    if (_shapePreviewLine is null) return;
    // 直接用世界坐标，RenderTransform 自动转换到屏幕
    _shapePreviewLine.StartPoint = worldStart;
    _shapePreviewLine.EndPoint = worldEnd;
}
```

#### 4. 统一坐标转换入口（MapEditorView.Input.cs）
```csharp
private bool ToolOverlayPointerPressed(Point screenPos, PointerPressedEventArgs e)
{
    var worldPos = ScreenToWorld(screenPos); // 统一转换入口
    
    // 所有工具都用世界坐标
    switch (ActiveToolKey)
    {
        case "measure":
            BeginMeasure(worldPos); // 传世界坐标
            return true;
        case "shape":
            BeginShapeDrag(worldPos); // 传世界坐标
            return true;
    }
}
```

---

## 实现步骤

### Phase 1: 基础设施（30分钟）
- [ ] 在 `MapEditorView.Viewport.cs` 添加 `GetWorldToScreenMatrix()`
- [ ] 在 `MapEditorView.Viewport.cs` 添加 `ScreenToWorld()`
- [ ] 在 `MapEditorView.Lifecycle.cs` 添加 `UpdateOverlayTransform()`
- [ ] 在相机移动/缩放时调用 `UpdateOverlayTransform()`

### Phase 2: 输入层转换（20分钟）
- [ ] 修改 `MapEditorView.Input.cs` 的 `ToolOverlayPointerPressed/Moved/Released`
- [ ] 所有屏幕坐标统一转换为世界坐标后再传递给工具方法

### Phase 3: 工具层重写（1小时）
- [ ] **直线预览**：`UpdateLinePreview()` 改用世界坐标
- [ ] **矩形预览**：`UpdateRectPreview()` 改用世界坐标
- [ ] **圆形预览**：`UpdateCirclePreview()` 改用世界坐标
- [ ] **椭圆预览**：`UpdateEllipsePreview()` 改用世界坐标
- [ ] **扇形预览**：`UpdateConeWedgePreview()` 改用世界坐标（移除宽高比补偿）
- [ ] **多边形预览**：`UpdatePolygonPreview()` 改用世界坐标
- [ ] **测量工具**：`UpdateMeasure()` 改用世界坐标
- [ ] **激光笔**：`UpdateLaser()` 改用世界坐标

### Phase 4: 测试验证（30分钟）
- [ ] 测试所有形状工具的预览与落地一致性
- [ ] 测试缩放时预览不变形
- [ ] 测试非方形视口中的预览
- [ ] 回归测试：确保没有破坏现有功能

---

## 优势

### 1. 统一坐标系
- ✅ 预览和渲染都用世界坐标
- ✅ 不再需要两套转换逻辑
- ✅ 宽高比补偿由 RenderTransform 自动处理

### 2. 代码简化
- ✅ 移除预览层的手动坐标转换
- ✅ 移除扇形等形状的宽高比补偿代码
- ✅ 工具方法只关心世界坐标语义

### 3. 维护性提升
- ✅ 修改坐标系统只需改 `GetWorldToScreenMatrix()`
- ✅ 新增工具不需要理解坐标转换细节
- ✅ 预览与落地的一致性由架构保证

### 4. 性能
- ✅ RenderTransform 是 GPU 加速的
- ✅ 避免重复计算坐标转换

---

## 风险与注意事项

### 1. RenderTransform 的坐标系
- Avalonia 的 `Canvas.Children` 位置是**相对父容器**的
- `RenderTransform` 应用在 Canvas 本身，所有子元素自动继承变换
- 需要验证 `Canvas.RenderTransform` 对 `Line/Ellipse/Path` 的影响

### 2. 现有代码兼容性
- 多边形锚点模式的 `_polygonPoints` 列表需要从屏幕坐标改为世界坐标
- 墙体锚点模式的 `_wallPoints` 列表同样需要改为世界坐标
- 确保所有 `Point` 变量的语义明确

### 3. 测试覆盖
- 重点测试**缩放**和**平移**时的预览正确性
- 测试**非方形视口**（如分屏、缩放窗口）

---

## 回退策略

如果重构遇到阻塞问题，可以分阶段回退：
1. 保留现有屏幕坐标逻辑作为备份分支
2. 只重构部分工具（如直线、矩形）先验证可行性
3. 问题工具保持原有逻辑，逐步迁移
