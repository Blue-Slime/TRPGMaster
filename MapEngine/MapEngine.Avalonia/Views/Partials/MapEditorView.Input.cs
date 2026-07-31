using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using MapEngine.Avalonia.Commands;
using MapEngine.Avalonia.Graphics;
using MapEngine.Avalonia.ViewModels;

namespace MapEngine.Avalonia.Views;

/// <summary>鼠标/指针输入：平移、拖拽对象、墙壁控制点、旋转/缩放 handle、滚轮缩放、HitTest。</summary>
public partial class MapEditorView
{
    private void MapViewportHost_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_viewModel is null || sender is not Control control)
        {
            return;
        }

        var point = e.GetCurrentPoint(control);
        var usePanTool = string.Equals(_viewModel.SelectedPrimaryTool?.Key, "pan", StringComparison.OrdinalIgnoreCase);
        var useSelectTool = string.Equals(_viewModel.SelectedPrimaryTool?.Key, "select", StringComparison.OrdinalIgnoreCase);

        // 工具覆盖层：measure/laser/shape/draw 优先消费左键事件
        if (point.Properties.IsLeftButtonPressed && ToolOverlayPointerPressed(point.Position, e))
        {
            e.Pointer.Capture(control);
            e.Handled = true;
            return;
        }

        if (point.Properties.IsMiddleButtonPressed || (usePanTool && point.Properties.IsLeftButtonPressed))
        {
            _isPanningViewport = true;
            _panStartPointerPosition = point.Position;
            _panStartContentCenter = _cameraContentCenter;
            CancelPendingMapFinalize();
            e.Pointer.Capture(control);
            e.Handled = true;
            return;
        }

        // 右键：墙壁控制点删除 或 对象上下文菜单
        if (useSelectTool && point.Properties.IsRightButtonPressed)
        {
            var (delItem, delIndex) = HitTestWallHandle(point.Position, control);
            if (delItem is not null && delIndex >= 0)
            {
                var wall = delItem.GetComponent<MapEngine.Core.Components.WallComponent>();
                if (wall is not null)
                {
                    _viewModel.CommandBus.Execute(new VmDeleteWallHandleCommand(
                        _viewModel, delItem.Id, delIndex));
                    e.Handled = true;
                    return;
                }
            }

            var hitObj = HitTestMapObject(point.Position, control);
            if (hitObj is not null)
            {
                _viewModel.SelectedHierarchyItem = hitObj;
                ShowMapObjectContextMenu(hitObj, control);
                e.Handled = true;
                return;
            }
        }

        if (useSelectTool && point.Properties.IsLeftButtonPressed)
        {
            // 双击线段 → 插入控制点
            if (e.ClickCount >= 2)
            {
                var (segItem, insertIdx, localPos) = HitTestWallSegment(point.Position, control);
                if (segItem is not null && insertIdx >= 0)
                {
                    _viewModel.CommandBus.Execute(new VmInsertWallHandleCommand(
                        _viewModel, segItem.Id, insertIdx, localPos));
                    e.Handled = true;
                    return;
                }
            }

            // 优先检测墙壁控制点
            var (wallItem, handleIndex) = HitTestWallHandle(point.Position, control);
            if (wallItem is not null && handleIndex >= 0)
            {
                _viewModel.SelectedHierarchyItem = wallItem;
                _isDraggingWallHandle = true;
                _wallHandleDragItem = wallItem;
                _wallHandleDragIndex = handleIndex;
                var wall = wallItem.GetComponent<MapEngine.Core.Components.WallComponent>();
                if (wall is not null && handleIndex < 2)
                {
                    _wallHandleDragStart = handleIndex == 0 ? (wall.X1, wall.Y1) : (wall.X2, wall.Y2);
                }
                _dragStartPointerPosition = point.Position;
                e.Pointer.Capture(control);
                e.Handled = true;
                return;
            }

            // 检测旋转 handle（圆圈，精灵正上方）
            var rotItem = HitTestRotateHandle(point.Position, control);
            if (rotItem is not null)
            {
                _isDraggingRotateHandle = true;
                _rotateHandleItem = rotItem;
                _rotateHandleStartRotation = rotItem.Rotation;
                var zs = _viewModel.ZoomScale <= 0 ? 1.0 : _viewModel.ZoomScale;
                var cx = _cameraContentCenter.X + (point.Position.X - control.Bounds.Width / 2.0) / zs;
                var cy = _cameraContentCenter.Y + (point.Position.Y - control.Bounds.Height / 2.0) / zs;
                var objCx = rotItem.MapLeft + rotItem.SpriteWidth / 2.0;
                var objCy = rotItem.MapTop  + rotItem.SpriteHeight / 2.0;
                _rotateHandleDragStartAngle = Math.Atan2(cy - objCy, cx - objCx) * 180.0 / Math.PI;
                e.Pointer.Capture(control);
                e.Handled = true;
                return;
            }

            // 检测缩放 handle（四角方块）
            var (scaleItem, scaleCorner) = HitTestScaleHandle(point.Position, control);
            if (scaleItem is not null)
            {
                _isDraggingScaleHandle = true;
                _scaleHandleItem = scaleItem;
                _scaleHandleCorner = scaleCorner;
                _scaleHandleStartScaleX = scaleItem.ScaleX;
                _scaleHandleStartScaleY = scaleItem.ScaleY;
                _scaleHandleDragStartPos = point.Position;
                e.Pointer.Capture(control);
                e.Handled = true;
                return;
            }

            var hit = HitTestMapObject(point.Position, control);
            if (hit is not null)
            {
                _viewModel.SelectedHierarchyItem = hit;
                _isDraggingObject = true;
                _dragStartPointerPosition = point.Position;
                _dragStartObjectX = hit.X;
                _dragStartObjectY = hit.Y;
                _dragChildSnapshots = [];
                SnapshotChildPositions(hit, _dragChildSnapshots);
                e.Pointer.Capture(control);
            }
            else
            {
                _viewModel.SelectedHierarchyItem = null;
            }
            e.Handled = true;
        }
    }

    private void MapViewportHost_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (_viewModel is null || sender is not Control control)
        {
            return;
        }

        // 工具覆盖层：如果工具正在拖拽则优先处理
        if (ToolOverlayPointerMoved(e.GetPosition(control), e))
        {
            e.Handled = true;
            return;
        }

        if (_isPanningViewport)
        {
            var currentPosition = e.GetPosition(control);
            var delta = currentPosition - _panStartPointerPosition;
            var zoomScale = _viewModel.ZoomScale <= 0 ? 1.0 : _viewModel.ZoomScale;
            SetCameraCenter(new Point(
                _panStartContentCenter.X - (delta.X / zoomScale),
                _panStartContentCenter.Y - (delta.Y / zoomScale)),
                zoomScale);
            e.Handled = true;
            return;
        }

        if (_isDraggingWallHandle && _wallHandleDragItem is not null)
        {
            var currentPosition = e.GetPosition(control);
            var delta = currentPosition - _dragStartPointerPosition;
            var zoomScale = _viewModel.ZoomScale <= 0 ? 1.0 : _viewModel.ZoomScale;
            var deltaWorldX = (float)(delta.X / zoomScale);
            var deltaWorldY = -(float)(delta.Y / zoomScale);

            var wall = _wallHandleDragItem.GetComponent<MapEngine.Core.Components.WallComponent>();
            if (wall is not null && _wallHandleDragIndex < 2)
            {
                var nx = _wallHandleDragStart.X + deltaWorldX;
                var ny = _wallHandleDragStart.Y - deltaWorldY;
                if (_wallHandleDragIndex == 0) { wall.X1 = nx; wall.Y1 = ny; }
                else                           { wall.X2 = nx; wall.Y2 = ny; }
                _mapSilkCanvas?.RequestFrame();
            }
            e.Handled = true;
            return;
        }

        if (_isDraggingRotateHandle && _rotateHandleItem is not null)
        {
            var zs = _viewModel.ZoomScale <= 0 ? 1.0 : _viewModel.ZoomScale;
            var cur = e.GetPosition(control);
            var cx = _cameraContentCenter.X + (cur.X - control.Bounds.Width / 2.0) / zs;
            var cy = _cameraContentCenter.Y + (cur.Y - control.Bounds.Height / 2.0) / zs;
            var objCx = _rotateHandleItem.MapLeft + _rotateHandleItem.SpriteWidth  / 2.0;
            var objCy = _rotateHandleItem.MapTop  + _rotateHandleItem.SpriteHeight / 2.0;
            var currentAngle = Math.Atan2(cy - objCy, cx - objCx) * 180.0 / Math.PI;
            var delta = currentAngle - _rotateHandleDragStartAngle;
            // 每 15° 吸附（Shift 临时禁用）
            if (!e.KeyModifiers.HasFlag(KeyModifiers.Shift))
                delta = Math.Round(delta / 15.0) * 15.0;
            _rotateHandleItem.Rotation = _rotateHandleStartRotation + delta;
            _mapSilkCanvas?.RequestFrame();
            e.Handled = true;
            return;
        }

        if (_isDraggingScaleHandle && _scaleHandleItem is not null)
        {
            var zs = _viewModel.ZoomScale <= 0 ? 1.0 : _viewModel.ZoomScale;
            var cur = e.GetPosition(control);
            var dx = (cur.X - _scaleHandleDragStartPos.X) / zs;
            var dy = -(cur.Y - _scaleHandleDragStartPos.Y) / zs;
            // 根据角点方向决定 dx/dy 的符号作用
            var signX = (_scaleHandleCorner == 1 || _scaleHandleCorner == 2) ? 1.0 : -1.0;
            var signY = (_scaleHandleCorner == 2 || _scaleHandleCorner == 3) ? -1.0 : 1.0;
            var cellSize = MapViewportConstants.CellSize;
            var newSx = Math.Max(0.5, _scaleHandleStartScaleX + signX * dx / cellSize);
            var newSy = Math.Max(0.5, _scaleHandleStartScaleY + signY * dy / cellSize);
            _scaleHandleItem.ScaleX = newSx;
            _scaleHandleItem.ScaleY = newSy;
            _mapSilkCanvas?.RequestFrame();
            e.Handled = true;
            return;
        }

        if (_isDraggingObject && _viewModel.SelectedHierarchyItem is not null)
        {
            var currentPosition = e.GetPosition(control);
            var delta = currentPosition - _dragStartPointerPosition;
            var zoomScale = _viewModel.ZoomScale <= 0 ? 1.0 : _viewModel.ZoomScale;
            var deltaWorldX = delta.X / zoomScale;
            var deltaWorldY = -(delta.Y / zoomScale);

            var item = _viewModel.SelectedHierarchyItem;
            var shiftHeld = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
            // snap-to-grid：拖拽预览也显示吸附后的位置，让玩家看到最终落点
            var newX = SnapToGrid(_dragStartObjectX + deltaWorldX, shiftHeld);
            var newY = SnapToGrid(_dragStartObjectY + deltaWorldY, shiftHeld);
            var moveDx = newX - item.X;
            var moveDy = newY - item.Y;

            item.X = newX;
            item.Y = newY;
            item.HasMapPosition = true;
            MoveChildrenPreview(item, moveDx, moveDy);

            // 向宿主上报实时拖拽位置（流式通道，不持久化）
            _viewModel?.Host?.SendStream("map_drag", new
            {
                id = item.Id.ToString(),
                x = newX,
                y = newY,
            });

            e.Handled = true;
        }
    }

    private void MapViewportHost_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (sender is not Control control)
        {
            return;
        }

        // 工具覆盖层：如果工具正在拖拽则优先处理
        if (ToolOverlayPointerReleased(e.GetPosition(control), e))
        {
            if (Equals(e.Pointer.Captured, control))
                e.Pointer.Capture(null);
            e.Handled = true;
            return;
        }

        if (_isPanningViewport)
        {
            _isPanningViewport = false;
            if (Equals(e.Pointer.Captured, control))
                e.Pointer.Capture(null);
            e.Handled = true;
            return;
        }

        if (_isDraggingWallHandle && _wallHandleDragItem is not null)
        {
            var wall = _wallHandleDragItem.GetComponent<MapEngine.Core.Components.WallComponent>();
            if (wall is not null && _wallHandleDragIndex < 2)
            {
                var finalPos = _wallHandleDragIndex == 0 ? (wall.X1, wall.Y1) : (wall.X2, wall.Y2);
                var moved = Math.Abs(finalPos.Item1 - _wallHandleDragStart.X) > 0.5 ||
                           Math.Abs(finalPos.Item2 - _wallHandleDragStart.Y) > 0.5;

                if (moved && _viewModel is not null)
                {
                    // 恢复到起始位置，让 Command 执行
                    if (_wallHandleDragIndex == 0) { wall.X1 = _wallHandleDragStart.X; wall.Y1 = _wallHandleDragStart.Y; }
                    else                           { wall.X2 = _wallHandleDragStart.X; wall.Y2 = _wallHandleDragStart.Y; }
                    _viewModel.CommandBus.Execute(new VmMoveWallHandleCommand(
                        _viewModel, _wallHandleDragItem.Id, _wallHandleDragIndex,
                        _wallHandleDragStart, (finalPos.Item1, finalPos.Item2)));
                }
            }

            _isDraggingWallHandle = false;
            _wallHandleDragItem = null;
            _wallHandleDragIndex = -1;
            if (Equals(e.Pointer.Captured, control))
                e.Pointer.Capture(null);
            e.Handled = true;
            return;
        }

        if (_isDraggingRotateHandle && _rotateHandleItem is not null && _viewModel is not null)
        {
            var finalRot = _rotateHandleItem.Rotation;
            if (Math.Abs(finalRot - _rotateHandleStartRotation) > 0.01)
            {
                // 恢复起始值，让 Command 执行
                _rotateHandleItem.Rotation = _rotateHandleStartRotation;
                _viewModel.CommandBus.Execute(new VmSetPropertyCommand(
                    _viewModel, _rotateHandleItem.Id, "Rotation", finalRot));
            }
            _isDraggingRotateHandle = false;
            _rotateHandleItem = null;
            if (Equals(e.Pointer.Captured, control))
                e.Pointer.Capture(null);
            e.Handled = true;
            return;
        }

        if (_isDraggingScaleHandle && _scaleHandleItem is not null && _viewModel is not null)
        {
            var finalSx = _scaleHandleItem.ScaleX;
            var finalSy = _scaleHandleItem.ScaleY;
            if (Math.Abs(finalSx - _scaleHandleStartScaleX) > 0.01 || Math.Abs(finalSy - _scaleHandleStartScaleY) > 0.01)
            {
                _scaleHandleItem.ScaleX = _scaleHandleStartScaleX;
                _scaleHandleItem.ScaleY = _scaleHandleStartScaleY;
                _viewModel.CommandBus.Execute(new VmSetPropertyCommand(
                    _viewModel, _scaleHandleItem.Id, "ScaleX", finalSx));
                _viewModel.CommandBus.Execute(new VmSetPropertyCommand(
                    _viewModel, _scaleHandleItem.Id, "ScaleY", finalSy));
            }
            _isDraggingScaleHandle = false;
            _scaleHandleItem = null;
            if (Equals(e.Pointer.Captured, control))
                e.Pointer.Capture(null);
            e.Handled = true;
            return;
        }

        if (_isDraggingObject && _viewModel?.SelectedHierarchyItem is not null)
        {
            var item = _viewModel.SelectedHierarchyItem;
            var finalX = item.X;
            var finalY = item.Y;

            if (Math.Abs(finalX - _dragStartObjectX) > 0.5 || Math.Abs(finalY - _dragStartObjectY) > 0.5)
            {
                // 恢复所有位置到起始状态，让 Command 统一执行
                item.X = _dragStartObjectX;
                item.Y = _dragStartObjectY;
                if (_dragChildSnapshots is not null)
                {
                    foreach (var (child, ox, oy) in _dragChildSnapshots)
                    {
                        child.X = ox;
                        child.Y = oy;
                    }
                }
                // Command 构造时已计算好 old/new，Execute 直接赋值
                _viewModel.CommandBus.Execute(new VmMoveObjectCommand(
                    _viewModel, item.Id, _dragStartObjectX, _dragStartObjectY, finalX, finalY));
            }

            _isDraggingObject = false;
            _dragChildSnapshots = null;
            if (Equals(e.Pointer.Captured, control))
                e.Pointer.Capture(null);
            e.Handled = true;
        }
    }

    private void MapViewportHost_PointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        _isPanningViewport = false;
        _isDraggingObject = false;
        _isDraggingWallHandle = false;
        _isDraggingRotateHandle = false;
        _rotateHandleItem = null;
        _isDraggingScaleHandle = false;
        _scaleHandleItem = null;
        CancelToolOverlay();
    }

    /// <summary>
    /// 检测鼠标是否命中旋转 handle（精灵正上方的小方块）。
    /// 坐标逻辑与 BuildSelectionHandles 对齐：handle 中心 = 精灵中心 + 旋转后的 (0, -(hh+rotOffset+rotSize/2))。
    /// </summary>
    private HierarchyItemViewModel? HitTestRotateHandle(Point screenPos, Control viewport)
    {
        if (_viewModel is null) return null;
        var zs = _viewModel.ZoomScale <= 0 ? 1.0 : _viewModel.ZoomScale;
        var cx = _cameraContentCenter.X + (screenPos.X - viewport.Bounds.Width  / 2.0) / zs;
        var cy = _cameraContentCenter.Y + (screenPos.Y - viewport.Bounds.Height / 2.0) / zs;

        const double rotSize   = 10.0;
        const double rotOffset = 20.0;
        const double hitR      = rotSize + 4.0; // 命中半径稍宽松

        foreach (var item in _viewModel.MapRenderableItems)
        {
            if (!item.IsSelected || !item.ShouldRenderOnMap) continue;
            var objCx = item.MapLeft + item.SpriteWidth  / 2.0;
            var objCy = item.MapTop  + item.SpriteHeight / 2.0;
            var hh    = item.SpriteHeight / 2.0;
            var localY = -(hh + rotOffset + rotSize / 2.0);
            var rotRad = item.Rotation * Math.PI / 180.0;
            var rc = Math.Cos(rotRad);
            var rs = Math.Sin(rotRad);
            var handleX = objCx + (0.0 * rc - localY * rs);
            var handleY = objCy + (0.0 * rs + localY * rc);
            var dx = cx - handleX;
            var dy = cy - handleY;
            if (dx * dx + dy * dy <= hitR * hitR) return item;
        }
        return null;
    }

    /// <summary>
    /// 检测鼠标是否命中四角缩放 handle。
    /// 返回 (item, cornerIndex) 其中 corner: 0=左上 1=右上 2=右下 3=左下。
    /// </summary>
    private (HierarchyItemViewModel? item, int corner) HitTestScaleHandle(Point screenPos, Control viewport)
    {
        if (_viewModel is null) return (null, -1);
        var zs = _viewModel.ZoomScale <= 0 ? 1.0 : _viewModel.ZoomScale;
        var cx = _cameraContentCenter.X + (screenPos.X - viewport.Bounds.Width  / 2.0) / zs;
        var cy = _cameraContentCenter.Y + (screenPos.Y - viewport.Bounds.Height / 2.0) / zs;

        const double cornerSize = 9.0;
        const double hitR       = cornerSize / 2.0 + 4.0;

        foreach (var item in _viewModel.MapRenderableItems)
        {
            if (!item.IsSelected || !item.ShouldRenderOnMap) continue;
            var objCx = item.MapLeft + item.SpriteWidth  / 2.0;
            var objCy = item.MapTop  + item.SpriteHeight / 2.0;
            var hw    = item.SpriteWidth  / 2.0;
            var hh    = item.SpriteHeight / 2.0;
            var rotRad = item.Rotation * Math.PI / 180.0;
            var rc = Math.Cos(rotRad);
            var rs = Math.Sin(rotRad);

            (double lx, double ly)[] corners = [(-hw, -hh), (hw, -hh), (hw, hh), (-hw, hh)];
            for (int i = 0; i < corners.Length; i++)
            {
                var (lx, ly) = corners[i];
                var wx = objCx + (lx * rc - ly * rs);
                var wy = objCy + (lx * rs + ly * rc);
                var dx = cx - wx;
                var dy = cy - wy;
                if (dx * dx + dy * dy <= hitR * hitR) return (item, i);
            }
        }
        return (null, -1);
    }

    private (HierarchyItemViewModel? item, int handleIndex) HitTestWallHandle(Point screenPosition, Control viewport)
    {
        if (_viewModel is null) return (null, -1);

        var zoomScale = _viewModel.ZoomScale <= 0 ? 1.0 : _viewModel.ZoomScale;
        var contentX = _cameraContentCenter.X + (screenPosition.X - viewport.Bounds.Width / 2.0) / zoomScale;
        var contentY = _cameraContentCenter.Y + (screenPosition.Y - viewport.Bounds.Height / 2.0) / zoomScale;

        const double handleSize = 8.0;
        const double hitTolerance = handleSize / 2.0 + 2.0;

        foreach (var item in _viewModel.MapRenderableItems)
        {
            if (!item.ShouldRenderOnMap || !item.HasWallComponent) continue;

            var wall = item.GetComponent<MapEngine.Core.Components.WallComponent>();
            if (wall is null || wall.NoCutaway) continue; // 编辑把手：仅 NoCutaway=false 时显示

            var objCenterX = item.MapLeft + (MapViewportConstants.CellSize * item.ScaleX) / 2.0;
            var objCenterY = item.MapTop + (MapViewportConstants.CellSize * item.ScaleY) / 2.0;
            var rotRad = item.Rotation * Math.PI / 180.0;
            var rotCos = Math.Cos(rotRad);
            var rotSin = Math.Sin(rotRad);

            var pts = new[] { (wall.X1, wall.Y1), (wall.X2, wall.Y2) };
            for (int i = 0; i < pts.Length; i++)
            {
                var (px, py) = pts[i];
                var rx = px * rotCos - py * rotSin;
                var ry = px * rotSin + py * rotCos;
                var handleX = objCenterX + rx;
                var handleY = objCenterY + ry;

                var dx = contentX - handleX;
                var dy = contentY - handleY;
                var dist = Math.Sqrt(dx * dx + dy * dy);

                if (dist <= hitTolerance)
                    return (item, i);
            }
        }

        return (null, -1);
    }

    // 返回 (item, handleIndex, 线段中点本地坐标) — 新模型单段线，insertIndex 恒返回 1
    private (HierarchyItemViewModel? item, int insertIndex, (double X, double Y) localPos) HitTestWallSegment(Point screenPosition, Control viewport)
    {
        if (_viewModel is null) return (null, -1, default);

        var zoomScale = _viewModel.ZoomScale <= 0 ? 1.0 : _viewModel.ZoomScale;
        var contentX = _cameraContentCenter.X + (screenPosition.X - viewport.Bounds.Width / 2.0) / zoomScale;
        var contentY = _cameraContentCenter.Y + (screenPosition.Y - viewport.Bounds.Height / 2.0) / zoomScale;

        foreach (var item in _viewModel.MapRenderableItems)
        {
            if (!item.ShouldRenderOnMap || !item.HasWallComponent) continue;

            var wall = item.GetComponent<MapEngine.Core.Components.WallComponent>();
            if (wall is null) continue;

            var objCenterX = item.MapLeft + (MapViewportConstants.CellSize * item.ScaleX) / 2.0;
            var objCenterY = item.MapTop + (MapViewportConstants.CellSize * item.ScaleY) / 2.0;
            var rotRad = item.Rotation * Math.PI / 180.0;
            var rotCos = Math.Cos(rotRad);
            var rotSin = Math.Sin(rotRad);
            var hitTolerance = Math.Max(6.0, wall.Thickness / 2.0 + 4.0);

            var x1 = objCenterX + (wall.X1 * rotCos - wall.Y1 * rotSin);
            var y1 = objCenterY + (wall.X1 * rotSin + wall.Y1 * rotCos);
            var x2 = objCenterX + (wall.X2 * rotCos - wall.Y2 * rotSin);
            var y2 = objCenterY + (wall.X2 * rotSin + wall.Y2 * rotCos);

            var dx = x2 - x1;
            var dy = y2 - y1;
            var lenSq = dx * dx + dy * dy;
            if (lenSq < 0.01) continue;
            var t = ((contentX - x1) * dx + (contentY - y1) * dy) / lenSq;
            if (t < 0 || t > 1) continue;
            var nearX = x1 + t * dx;
            var nearY = y1 + t * dy;
            var dist = Math.Sqrt((contentX - nearX) * (contentX - nearX) + (contentY - nearY) * (contentY - nearY));

            if (dist <= hitTolerance)
            {
                var midLocal = ((wall.X1 + wall.X2) / 2.0, (wall.Y1 + wall.Y2) / 2.0);
                return (item, 1, midLocal);
            }
        }

        return (null, -1, default);
    }

    private HierarchyItemViewModel? HitTestMapObject(Point screenPosition, Control viewport)
    {
        if (_viewModel is null) return null;

        var zoomScale = _viewModel.ZoomScale <= 0 ? 1.0 : _viewModel.ZoomScale;
        var contentX = _cameraContentCenter.X + (screenPosition.X - viewport.Bounds.Width / 2.0) / zoomScale;
        var contentY = _cameraContentCenter.Y + (screenPosition.Y - viewport.Bounds.Height / 2.0) / zoomScale;

        HierarchyItemViewModel? closest = null;
        var closestDist = double.MaxValue;

        foreach (var item in _viewModel.MapRenderableItems)
        {
            if (!item.ShouldRenderOnMap) continue;
            if (!item.SpriteHitTestEnabled) continue;

            var left = item.MapLeft;
            var top = item.MapTop;
            var right = left + item.SpriteWidth;
            var bottom = top + item.SpriteHeight;

            if (contentX >= left && contentX <= right && contentY >= top && contentY <= bottom)
            {
                var cx = left + item.SpriteWidth / 2.0;
                var cy = top + item.SpriteHeight / 2.0;
                var dist = Math.Sqrt((contentX - cx) * (contentX - cx) + (contentY - cy) * (contentY - cy));
                if (dist < closestDist)
                {
                    closestDist = dist;
                    closest = item;
                }
            }
        }

        return closest;
    }

    /// <summary>
    /// 将世界坐标吸附到最近的格子中心。
    /// 格子中心 = k * CellSize（k 为整数），因为世界原点在内容中心。
    /// Shift 键按下时临时禁用吸附；全局开关从 ViewModel.IsSnapToGrid 读取。
    /// </summary>
    private double SnapToGrid(double worldCoord, bool shiftHeld)
    {
        if (shiftHeld || _viewModel?.IsSnapToGrid == false) return worldCoord;
        var cellSize = MapViewportConstants.CellSize;
        return Math.Round(worldCoord / cellSize) * cellSize;
    }

    private static void MoveChildrenPreview(HierarchyItemViewModel parent, double dx, double dy)
    {
        foreach (var child in parent.Children)
        {
            if (child.HasMapPosition)
            {
                child.X += dx;
                child.Y += dy;
            }
            MoveChildrenPreview(child, dx, dy);
        }
    }

    private static void SnapshotChildPositions(HierarchyItemViewModel parent, List<(HierarchyItemViewModel, double, double)> list)
    {
        foreach (var child in parent.Children)
        {
            if (child.HasMapPosition)
                list.Add((child, child.X, child.Y));
            SnapshotChildPositions(child, list);
        }
    }

    private void MapViewportHost_PointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (_viewModel is null) return;

        var stepDirection = Math.Sign(e.Delta.Y);
        if (stepDirection == 0) return;

        var oldZoom = _viewModel.ZoomScale <= 0 ? 1.0 : _viewModel.ZoomScale;
        var pointer = sender is Control control ? e.GetPosition(control) : default;
        var viewportSize = GetViewportSize();
        if (viewportSize.Width <= 0 || viewportSize.Height <= 0) return;

        // 几何步进（≈12%/档），而非线性 +10%，在宽范围内手感均匀
        var newZoomRaw = stepDirection > 0
            ? oldZoom * MainWindowViewModel.ZoomStepFactor
            : oldZoom / MainWindowViewModel.ZoomStepFactor;

        var anchorContentX = _cameraContentCenter.X + ((pointer.X - (viewportSize.Width / 2.0)) / oldZoom);
        var anchorContentY = _cameraContentCenter.Y + ((pointer.Y - (viewportSize.Height / 2.0)) / oldZoom);
        _suppressZoomViewportPreservation = true;
        _viewModel.SetZoomFromInteraction(newZoomRaw);
        _suppressZoomViewportPreservation = false;
        var newZoom = _viewModel.ZoomScale <= 0 ? 1.0 : _viewModel.ZoomScale;
        SetCameraCenter(new Point(
            anchorContentX - ((pointer.X - (viewportSize.Width / 2.0)) / newZoom),
            anchorContentY - ((pointer.Y - (viewportSize.Height / 2.0)) / newZoom)),
            newZoom);
        e.Handled = true;
    }

}
