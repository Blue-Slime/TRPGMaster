using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using MapEngine.Avalonia.ViewModels;
using MapEngine.Core.Components;
using MapEngine.Avalonia.Commands;
using ShapePath = Avalonia.Controls.Shapes.Path;

namespace MapEngine.Avalonia.Views;

/// <summary>
/// 工具实现：Measure / Laser / Shape(4子工具) / Draw。
/// 所有临时 overlay 元素写入 _toolOverlayCanvas（不走 GL 管线）。
/// Shape/Draw 落地为 HierarchyItem，通过 CommandBus 持久化。
///
/// ShapeSubTool 取值：
///   直线    : line
///   常用形状: rect | circle | ellipse
///   桌游形状: cone | wedge
///   锚点多边形: polygon（点击添加锚点，双击或按 Enter 闭合）
/// </summary>
public partial class MapEditorView
{
    // ── 共用 overlay 状态 ─────────────────────────────────────────────────
    private bool   _isToolDragging;
    private Point  _toolDragStart;

    // ── Measure ───────────────────────────────────────────────────────────
    private Line?      _measureLine;
    private TextBlock? _measureLabel;

    // ── Laser ─────────────────────────────────────────────────────────────
    private Ellipse?             _laserDot;
    private readonly List<Point> _laserTrail = [];
    private Polyline?            _laserTrailLine;

    // ── Shape — 拖拽预览 ──────────────────────────────────────────────────
    private Rectangle? _shapePreviewRect;
    private Ellipse?   _shapePreviewEllipse;
    private Line?      _shapePreviewLine;
    private ShapePath? _shapePreviewPath;   // cone / wedge

    // ── Shape — polygon 锚点模式 ──────────────────────────────────────────
    private readonly List<Point> _polygonPoints = [];   // 已确认的屏幕锚点
    private Polyline?            _polygonPreview;       // 已确认段
    private Line?                _polygonGhostEdge;     // 鼠标跟随的虚边

    // ── Wall — 墙体锚点模式 ───────────────────────────────────────────────
    private readonly List<Point> _wallPoints = [];      // 已确认的屏幕锚点
    private Polyline?            _wallPreview;          // 已确认段
    private Line?                _wallGhostEdge;        // 鼠标跟随的虚边

    // ── Draw ──────────────────────────────────────────────────────────────
    private Polyline?            _drawPolyline;
    private readonly List<Point> _drawPoints = [];

    // ── Fog ───────────────────────────────────────────────────────────────
    private Rectangle? _fogPreviewRect;

    // ─────────────────────────────────────────────────────────────────────
    // 当前工具 key 快捷访问
    // 单选工具：取 SelectedPrimaryTool.Key
    // toggle 工具（laser）：IsSelected=true 时叠加优先——若 laser 已激活则始终
    // 路由 laser 事件，无论主工具是什么
    private string ActiveToolKey =>
        _viewModel?.SelectedPrimaryTool?.Key ?? "select";

    /// <summary>laser 是 toggle 工具，IsSelected 独立于 SelectedPrimaryTool。</summary>
    private bool IsLaserActive =>
        _viewModel?.PrimaryTools.FirstOrDefault(t => t.Key == "laser")?.IsSelected ?? false;

    // ─────────────────────────────────────────────────────────────────────
    // 统一入口：由 Input.cs 调用
    // ─────────────────────────────────────────────────────────────────────

    private bool ToolOverlayPointerPressed(Point screenPos, PointerPressedEventArgs e)
    {
        if (_toolOverlayCanvas is null || _viewModel is null) return false;

        // toggle 工具（laser）优先：不管主工具是什么，激光激活时接管事件
        if (IsLaserActive)
        {
            BeginLaser(screenPos);
            return true;
        }

        switch (ActiveToolKey)
        {
            case "measure":
                BeginMeasure(screenPos);
                return true;
            case "shape":
                var sub = _viewModel.ShapeSubTool ?? "rect";
                if (sub == "polygon")
                {
                    PolygonAddPoint(screenPos, e.ClickCount >= 2);
                    return true;
                }
                BeginShape(screenPos);
                return true;
            case "wall":
                WallAddPoint(screenPos, e.ClickCount >= 2);
                return true;
            case "draw":
                BeginDraw(screenPos);
                return true;
            case "text":
                // 文本是"单击定点 + 弹窗输入"，没有拖拽会话
                PlaceText(screenPos);
                return true;
            case "fog":
                BeginFog(screenPos);
                return true;
            case "graph":
                return GraphPointerPressed(screenPos);
        }
        return false;
    }

    // ─────────────────────────────────────────────────────────────────────
    // Graph（拓扑节点 / 连线）
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>将 Content 坐标转换为 GameObject 坐标（翻转 Y 轴并减去原点偏移）。</summary>
    private (double X, double Y) ContentToGameObject(Point contentPos)
    {
        var gameX = contentPos.X - MapViewportConstants.WorldOriginContent;
        var gameY = -(contentPos.Y - MapViewportConstants.WorldOriginContent);
        return (gameX, gameY);
    }

    /// <summary>
    /// 拓扑工具按下：node 子工具建节点（点已有节点则只选中），link 子工具起线。
    /// </summary>
    private bool GraphPointerPressed(Point screenPos)
    {
        if (_viewModel is null) return false;

        var contentPos = ScreenToWorld(screenPos);
        var (wx, wy) = ContentToGameObject(contentPos);
        var hit = _viewModel.HitTestGraphNode(wx, wy);

        if (_viewModel.IsGraphLinkSubTool)
        {
            if (hit is null)
            {
                _viewModel.StatusMessage = "请从一个拓扑节点开始连线";
                return true;
            }
            if (_viewModel.BeginGraphLinkDrag(hit))
            {
                _isToolDragging = true;
                _toolDragStart = screenPos;
                BeginGraphLinkPreview(screenPos);
            }
            return true;
        }

        // node 子工具：命中已有节点就只选中，避免叠着建一堆
        if (hit is not null)
        {
            _viewModel.SelectedHierarchyItem = hit;
            _viewModel.StatusMessage = $"已选中节点 {hit.Name}";
            return true;
        }

        _viewModel.CreateGraphNodeAt(wx, wy);
        return true;
    }

    /// <summary>连线拖拽的橡皮筋预览线。</summary>
    private Line? _graphLinkPreviewLine;

    private void BeginGraphLinkPreview(Point screenPos)
    {
        if (_toolOverlayCanvas is null) return;

        _graphLinkPreviewLine = new Line
        {
            Stroke = new SolidColorBrush(Color.Parse("#4A90E2")),
            StrokeThickness = 2,
            StrokeDashArray = [6, 3],
            StartPoint = screenPos,
            EndPoint = screenPos,
            IsHitTestVisible = false,
        };
        _toolOverlayCanvas.Children.Add(_graphLinkPreviewLine);
    }

    private void UpdateGraphLinkPreview(Point screenPos)
    {
        if (_graphLinkPreviewLine is not null)
            _graphLinkPreviewLine.EndPoint = screenPos;
    }

    /// <summary>松开：命中节点则建连线，否则取消。</summary>
    private void CommitGraphLink(Point screenPos)
    {
        if (_viewModel is null) return;

        var contentPos = ScreenToWorld(screenPos);
        var (wx, wy) = ContentToGameObject(contentPos);
        var target = _viewModel.HitTestGraphNode(wx, wy);
        _viewModel.CompleteGraphLinkDrag(target);

        _isToolDragging = false;
        ClearOverlay();
        _graphLinkPreviewLine = null;
    }

    /// <summary>
    /// 文本工具：在点击处放置文本标注。弹窗拿到内容后走 CommandBus（可撤销）。
    /// 取消或空串则不创建对象。
    /// </summary>
    private async void PlaceText(Point screenPos)
    {
        if (_viewModel is null) return;

        var vpSize = GetViewportSize();
        var zs = _viewModel.ZoomScale <= 0 ? 1.0 : _viewModel.ZoomScale;
        var wx = _cameraContentCenter.X + (screenPos.X - vpSize.Width  / 2.0) / zs - MapViewportConstants.WorldOriginContent;
        var wy = -((_cameraContentCenter.Y + (screenPos.Y - vpSize.Height / 2.0) / zs) - MapViewportConstants.WorldOriginContent);

        var text = await PromptForNameAsync("添加文本", string.Empty);
        if (string.IsNullOrWhiteSpace(text)) return;

        _viewModel.CommandBus.Execute(new VmCreateTextCommand(_viewModel, wx, wy, text.Trim()));
    }

    private bool ToolOverlayPointerMoved(Point screenPos, PointerEventArgs e)
    {
        var key = ActiveToolKey;

        // toggle 工具（laser）优先
        if (IsLaserActive && _isToolDragging)
        {
            UpdateLaser(screenPos);
            return true;
        }

        // polygon：鼠标移动时更新虚边（无需按住）
        if (key == "shape" && (_viewModel?.ShapeSubTool ?? "") == "polygon")
        {
            PolygonUpdateGhost(screenPos);
            return _polygonPoints.Count > 0;
        }

        // wall：鼠标移动时更新虚边（无需按住）
        if (key == "wall")
        {
            WallUpdateGhost(screenPos);
            return _wallPoints.Count > 0;
        }

        if (!_isToolDragging) return false;

        switch (key)
        {
            case "measure": UpdateMeasure(screenPos); return true;
            case "shape":   UpdateShape(screenPos);   return true;
            case "draw":    UpdateDraw(screenPos);    return true;
            case "fog":     UpdateFog(screenPos);     return true;
            case "graph":   UpdateGraphLinkPreview(screenPos); return true;
        }
        return false;
    }

    private bool ToolOverlayPointerReleased(Point screenPos, PointerReleasedEventArgs e)
    {
        // toggle 工具（laser）优先
        if (IsLaserActive && _isToolDragging)
        {
            CommitLaser();
            return true;
        }

        // polygon：released 不触发 commit（由双击或 Enter 触发）
        if (ActiveToolKey == "shape" && (_viewModel?.ShapeSubTool ?? "") == "polygon")
            return _polygonPoints.Count > 0;

        // wall：released 不触发 commit（由双击或 Enter 触发）
        if (ActiveToolKey == "wall")
            return _wallPoints.Count > 0;

        if (!_isToolDragging) return false;

        switch (ActiveToolKey)
        {
            case "measure": CommitMeasure();           return true;
            case "shape":   CommitShape(screenPos);    return true;
            case "draw":    CommitDraw();               return true;
            case "fog":     CommitFog(screenPos);       return true;
            case "graph":   CommitGraphLink(screenPos); return true;
        }
        return false;
    }

    /// <summary>工具取消（PointerCaptureLost 或 Escape）</summary>
    private void CancelToolOverlay()
    {
        _isToolDragging = false;
        // 半成品连线也要一并丢掉，否则下次按下会接着上次的起点连
        _viewModel?.CancelGraphLinkDrag();
        _graphLinkPreviewLine = null;
        ClearOverlay();
    }

    /// <summary>是否有未闭合的锚点多边形（供 Enter/Esc 快捷键判断）。</summary>
    private bool HasPendingPolygon => _polygonPoints.Count > 0;

    /// <summary>闭合并提交当前锚点多边形（Enter 快捷键）。</summary>
    private void CommitPendingPolygon() => PolygonCommit();

    /// <summary>是否有未完成的墙体路径（供 Enter/Esc 快捷键判断）。</summary>
    private bool HasPendingWall => _wallPoints.Count > 0;

    /// <summary>完成并提交当前墙体路径（Enter 快捷键）。</summary>
    private void CommitPendingWall() => WallCommit(false);

    // ─────────────────────────────────────────────────────────────────────
    // Measure
    // ─────────────────────────────────────────────────────────────────────

    private void BeginMeasure(Point screenPos)
    {
        ClearOverlay();
        _isToolDragging = true;
        _toolDragStart  = screenPos;

        var worldPos = ScreenToWorld(screenPos);
        _measureLine = new Line
        {
            StartPoint = worldPos,
            EndPoint   = worldPos,
            Stroke     = new SolidColorBrush(Color.Parse("#F59E0B")),
            StrokeThickness = 2,
            StrokeDashArray = [6, 4],
            IsHitTestVisible = false,
        };
        _measureLabel = new TextBlock
        {
            Foreground = new SolidColorBrush(Color.Parse("#F59E0B")),
            Background = new SolidColorBrush(Color.Parse("#CC1A1C23")),
            FontSize   = 12,
            FontWeight = FontWeight.SemiBold,
            Padding    = new Thickness(5, 2),
            IsHitTestVisible = false,
        };

        _toolOverlayCanvas!.Children.Add(_measureLine);
        _toolOverlayCanvas!.Children.Add(_measureLabel);
    }

    private void UpdateMeasure(Point screenPos)
    {
        if (_measureLine is null || _measureLabel is null || _viewModel is null) return;

        var worldEnd = ScreenToWorld(screenPos);
        _measureLine.EndPoint = worldEnd;

        // 计算世界坐标距离
        var worldStart = ScreenToWorld(_toolDragStart);
        var wx1 = worldStart.X;
        var wy1 = worldStart.Y;
        var wx2 = worldEnd.X;
        var wy2 = worldEnd.Y;

        var cellSize = MapViewportConstants.CellSize;
        var distCells = Math.Sqrt((wx2 - wx1) * (wx2 - wx1) + (wy2 - wy1) * (wy2 - wy1)) / cellSize;
        var feet   = distCells * (_viewModel.FeetPerCell > 0 ? _viewModel.FeetPerCell : 5);

        _measureLabel.Text = $"{distCells:F1} 格  ·  {feet:F0} 尺";

        // label 跟随线段中点偏上（世界坐标）
        var mx = (wx1 + wx2) / 2.0 + 8;
        var my = (wy1 + wy2) / 2.0 - 22;
        Canvas.SetLeft(_measureLabel, mx);
        Canvas.SetTop(_measureLabel,  my);
    }

    private void CommitMeasure()
    {
        _isToolDragging = false;
        // 量距结果保留显示 1.5 秒后自动清除
        var timer = new System.Timers.Timer(1500) { AutoReset = false };
        timer.Elapsed += (_, _) =>
            Dispatcher.UIThread.Post(ClearOverlay);
        timer.Start();
    }

    // ─────────────────────────────────────────────────────────────────────
    // Laser
    // ─────────────────────────────────────────────────────────────────────

    private void BeginLaser(Point screenPos)
    {
        ClearOverlay();
        _isToolDragging = true;
        _laserTrail.Clear();
        var worldPos = ScreenToWorld(screenPos);
        _laserTrail.Add(worldPos);

        _laserTrailLine = new Polyline
        {
            Stroke = new SolidColorBrush(Color.Parse("#EF4444")),
            StrokeThickness = 3,
            Opacity = 0.75,
            IsHitTestVisible = false,
        };
        _laserDot = new Ellipse
        {
            Width  = 14,
            Height = 14,
            Fill   = new SolidColorBrush(Color.Parse("#EF4444")),
            IsHitTestVisible = false,
        };

        _toolOverlayCanvas!.Children.Add(_laserTrailLine);
        _toolOverlayCanvas!.Children.Add(_laserDot);
        MoveLaserDot(worldPos);
    }

    private void UpdateLaser(Point screenPos)
    {
        if (_laserTrailLine is null || _laserDot is null) return;

        var worldPos = ScreenToWorld(screenPos);
        _laserTrail.Add(worldPos);
        // 只保留最近 60 个点避免无限增长
        if (_laserTrail.Count > 60) _laserTrail.RemoveAt(0);

        _laserTrailLine.Points = new AvaloniaList<Point>(_laserTrail);
        MoveLaserDot(worldPos);
    }

    private void MoveLaserDot(Point p)
    {
        if (_laserDot is null) return;
        Canvas.SetLeft(_laserDot, p.X - 7);
        Canvas.SetTop(_laserDot,  p.Y - 7);
    }

    private void CommitLaser()
    {
        _isToolDragging = false;
        // 松手后淡出消失：简单定时清除
        var timer = new System.Timers.Timer(400) { AutoReset = false };
        timer.Elapsed += (_, _) =>
            Dispatcher.UIThread.Post(ClearOverlay);
        timer.Start();
    }

    // ─────────────────────────────────────────────────────────────────────
    // Shape（矩形 / 椭圆 / 线段，子工具由 ViewModel.ShapeSubTool 控制）
    // ─────────────────────────────────────────────────────────────────────

    private void BeginShape(Point screenPos)
    {
        ClearOverlay();
        _isToolDragging = true;
        _toolDragStart  = screenPos;  // 仍缓存屏幕坐标，UpdateShape 时转换

        var stroke = new SolidColorBrush(Color.Parse("#845EF7"));
        var fill   = new SolidColorBrush(Color.Parse("#40845EF7"));

        switch (_viewModel?.ShapeSubTool ?? "rect")
        {
            case "circle":
            case "ellipse":
                _shapePreviewEllipse = new Ellipse
                {
                    Stroke = stroke, StrokeThickness = 2, Fill = fill,
                    IsHitTestVisible = false,
                };
                _toolOverlayCanvas!.Children.Add(_shapePreviewEllipse);
                break;
            case "line":
                // 线段使用世界坐标定位
                var worldStart = ScreenToWorld(screenPos);
                _shapePreviewLine = new Line
                {
                    StartPoint = worldStart, EndPoint = worldStart,
                    Stroke = stroke, StrokeThickness = 2,
                    IsHitTestVisible = false,
                };
                _toolOverlayCanvas!.Children.Add(_shapePreviewLine);
                break;
            case "cone":
            case "wedge":
                _shapePreviewPath = new ShapePath
                {
                    Stroke = stroke, StrokeThickness = 2, Fill = fill,
                    IsHitTestVisible = false,
                };
                _toolOverlayCanvas!.Children.Add(_shapePreviewPath);
                break;
            default: // rect
                _shapePreviewRect = new Rectangle
                {
                    Stroke = stroke, StrokeThickness = 2, Fill = fill,
                    IsHitTestVisible = false,
                };
                _toolOverlayCanvas!.Children.Add(_shapePreviewRect);
                break;
        }
    }

    private void UpdateShape(Point screenPos)
    {
        // 转换为世界坐标
        var worldStart = ScreenToWorld(_toolDragStart);
        var worldEnd = ScreenToWorld(screenPos);

        var x = Math.Min(worldStart.X, worldEnd.X);
        var y = Math.Min(worldStart.Y, worldEnd.Y);
        var w = Math.Abs(worldEnd.X - worldStart.X);
        var h = Math.Abs(worldEnd.Y - worldStart.Y);

        if (_shapePreviewRect is not null)
        {
            Canvas.SetLeft(_shapePreviewRect, x);
            Canvas.SetTop(_shapePreviewRect,  y);
            _shapePreviewRect.Width  = w;
            _shapePreviewRect.Height = h;
        }
        else if (_shapePreviewEllipse is not null)
        {
            // circle：强制 w==h（取短边），中心对齐拖拽起点
            if (_viewModel?.ShapeSubTool == "circle")
            {
                var side = Math.Min(w, h);
                Canvas.SetLeft(_shapePreviewEllipse, worldStart.X - side / 2.0);
                Canvas.SetTop(_shapePreviewEllipse,  worldStart.Y - side / 2.0);
                _shapePreviewEllipse.Width  = side;
                _shapePreviewEllipse.Height = side;
            }
            else
            {
                Canvas.SetLeft(_shapePreviewEllipse, x);
                Canvas.SetTop(_shapePreviewEllipse,  y);
                _shapePreviewEllipse.Width  = w;
                _shapePreviewEllipse.Height = h;
            }
        }
        else if (_shapePreviewLine is not null)
        {
            _shapePreviewLine.EndPoint = worldEnd;
        }
        else if (_shapePreviewPath is not null)
        {
            UpdateConeWedgePreview(worldStart, worldEnd);
        }
    }

    private void UpdateConeWedgePreview(Point origin, Point cursor)
    {
        if (_shapePreviewPath is null) return;
        // origin 和 cursor 现在都是世界坐标
        var dx = cursor.X - origin.X;
        var dy = cursor.Y - origin.Y;
        var radius = Math.Sqrt(dx * dx + dy * dy);
        if (radius < 2) return;

        var dirAngle = Math.Atan2(dy, dx);
        const double halfAngle = 30.0 * Math.PI / 180.0; // 30° half-angle
        var a1 = dirAngle - halfAngle;
        var a2 = dirAngle + halfAngle;
        var x1 = origin.X + radius * Math.Cos(a1);
        var y1 = origin.Y + radius * Math.Sin(a1);
        var x2 = origin.X + radius * Math.Cos(a2);
        var y2 = origin.Y + radius * Math.Sin(a2);

        var geo = new StreamGeometry();
        using var ctx = geo.Open();
        ctx.BeginFigure(origin, isFilled: true);
        ctx.LineTo(new Point(x1, y1));
        // 世界坐标是等比例的，直接用圆形弧
        ctx.ArcTo(new Point(x2, y2), new Size(radius, radius), 0, isLargeArc: false, SweepDirection.Clockwise);
        ctx.EndFigure(true);

        _shapePreviewPath.Data = geo;
    }

    private void CommitShape(Point screenPos)
    {
        _isToolDragging = false;
        if (_viewModel is null) return;

        var zs     = _viewModel.ZoomScale <= 0 ? 1.0 : _viewModel.ZoomScale;
        var vpSize = GetViewportSize();

        double ScrToWorldX(double sx) => _cameraContentCenter.X + (sx - vpSize.Width  / 2.0) / zs - MapViewportConstants.WorldOriginContent;
        double ScrToWorldY(double sy) => -((_cameraContentCenter.Y + (sy - vpSize.Height / 2.0) / zs) - MapViewportConstants.WorldOriginContent);

        var wx1 = ScrToWorldX(_toolDragStart.X);
        var wy1 = ScrToWorldY(_toolDragStart.Y);
        var wx2 = ScrToWorldX(screenPos.X);
        var wy2 = ScrToWorldY(screenPos.Y);

        var sub = _viewModel.ShapeSubTool ?? "rect";
        var dxW = wx2 - wx1;
        var dyW = wy2 - wy1;

        switch (sub)
        {
            case "line":
                if (Math.Abs(dxW) < 0.5 && Math.Abs(dyW) < 0.5) break;
                _viewModel.CommandBus.Execute(new VmCreateShapeCommand(_viewModel, sub,
                    wx1, wy1, 0, 0, wx1, wy1, wx2, wy2));
                break;

            case "cone":
            case "wedge":
            {
                var radius = Math.Sqrt(dxW * dxW + dyW * dyW);
                if (radius < 2) break;
                // 世界坐标 Y 向上，但渲染时 Y 轴会翻转，所以角度需要取反匹配
                var dirDeg = Math.Atan2(-dyW, dxW) * 180.0 / Math.PI;
                _viewModel.CommandBus.Execute(new VmCreateShapeCommand(_viewModel, sub,
                    wx1, wy1, radius, 0, wx1, wy1, wx2, wy2, dirDeg, 30.0));
                break;
            }

            default: // rect / ellipse / circle
            {
                var minX = Math.Min(wx1, wx2); var maxX = Math.Max(wx1, wx2);
                var minY = Math.Min(wy1, wy2); var maxY = Math.Max(wy1, wy2);
                var cx = (minX + maxX) / 2.0;  var cy = (minY + maxY) / 2.0;
                var ww = sub == "circle" ? Math.Min(maxX - minX, maxY - minY) : maxX - minX;
                var wh = sub == "circle" ? ww : maxY - minY;
                if (ww < 1.0 && wh < 1.0) break;
                _viewModel.CommandBus.Execute(new VmCreateShapeCommand(_viewModel, sub,
                    cx, cy, ww, wh, wx1, wy1, wx2, wy2));
                break;
            }
        }

        ClearOverlay();
    }

    // ─────────────────────────────────────────────────────────────────────
    // Polygon（锚点多边形：单击加锚点，双击闭合提交）
    // ─────────────────────────────────────────────────────────────────────

    private void PolygonAddPoint(Point screenPos, bool closeAndCommit)
    {
        if (closeAndCommit)
        {
            PolygonCommit();
            return;
        }

        if (_polygonPoints.Count == 0)
        {
            ClearOverlay();
            _polygonPreview = new Polyline
            {
                Stroke = new SolidColorBrush(Color.Parse("#845EF7")),
                StrokeThickness = 2,
                StrokeJoin      = PenLineJoin.Round,
                Fill            = new SolidColorBrush(Color.Parse("#30845EF7")),
                IsHitTestVisible = false,
            };
            _toolOverlayCanvas!.Children.Add(_polygonPreview);

            _polygonGhostEdge = new Line
            {
                Stroke = new SolidColorBrush(Color.Parse("#80845EF7")),
                StrokeThickness = 1.5,
                StrokeDashArray = new AvaloniaList<double> { 4, 4 },
                IsHitTestVisible = false,
            };
            _toolOverlayCanvas!.Children.Add(_polygonGhostEdge);
        }

        var worldPos = ScreenToWorld(screenPos);
        _polygonPoints.Add(worldPos);
        if (_polygonPreview is not null)
            _polygonPreview.Points = new AvaloniaList<Point>(_polygonPoints);
    }

    private void PolygonUpdateGhost(Point screenPos)
    {
        if (_polygonGhostEdge is null || _polygonPoints.Count == 0) return;
        var worldPos = ScreenToWorld(screenPos);
        _polygonGhostEdge.StartPoint = _polygonPoints[^1];
        _polygonGhostEdge.EndPoint   = worldPos;
    }

    private void PolygonCommit()
    {
        if (_viewModel is null || _polygonPoints.Count < 3) { ClearOverlay(); return; }

        // _polygonPoints 现在已经是世界坐标
        var worldPts = new List<(double X, double Y)>(_polygonPoints.Count);
        foreach (var p in _polygonPoints)
        {
            // 转换为 GameObject 坐标系（Y轴翻转，原点偏移）
            var gameX = p.X - MapViewportConstants.WorldOriginContent;
            var gameY = -(p.Y - MapViewportConstants.WorldOriginContent);
            worldPts.Add((gameX, gameY));
        }

        double sumX = 0, sumY = 0;
        foreach (var (wx, wy) in worldPts) { sumX += wx; sumY += wy; }
        var centerX = sumX / worldPts.Count;
        var centerY = sumY / worldPts.Count;

        var relPts = new List<(double X, double Y)>(worldPts.Count);
        foreach (var (wx, wy) in worldPts)
            relPts.Add((wx - centerX, wy - centerY));

        _viewModel.CommandBus.Execute(new VmCreatePolygonCommand(_viewModel, centerX, centerY, relPts));
        ClearOverlay();
    }

    // ─────────────────────────────────────────────────────────────────────
    // Wall（墙体绘制：单击添加锚点，双击完成）
    // ─────────────────────────────────────────────────────────────────────

    private void WallAddPoint(Point screenPos, bool commitNow)
    {
        if (commitNow)
        {
            WallCommit(false);
            return;
        }

        if (_wallPoints.Count == 0)
        {
            ClearOverlay();
            _wallPreview = new Polyline
            {
                Stroke = new SolidColorBrush(Color.Parse("#EF4444")),
                StrokeThickness = 5,
                StrokeJoin      = PenLineJoin.Round,
                IsHitTestVisible = false,
            };
            _toolOverlayCanvas!.Children.Add(_wallPreview);

            _wallGhostEdge = new Line
            {
                Stroke = new SolidColorBrush(Color.Parse("#80EF4444")),
                StrokeThickness = 3,
                StrokeDashArray = new AvaloniaList<double> { 6, 4 },
                IsHitTestVisible = false,
            };
            _toolOverlayCanvas!.Children.Add(_wallGhostEdge);
        }

        var worldPos = ScreenToWorld(screenPos);
        _wallPoints.Add(worldPos);
        if (_wallPreview is not null)
            _wallPreview.Points = new AvaloniaList<Point>(_wallPoints);

        // 检测首尾接近（< 20 世界单位）→ 提示闭合
        if (_wallPoints.Count >= 3)
        {
            var first = _wallPoints[0];
            var last = _wallPoints[^1];
            var dx = last.X - first.X;
            var dy = last.Y - first.Y;
            var dist = Math.Sqrt(dx * dx + dy * dy);
            if (dist < 20)
            {
                _viewModel!.StatusMessage = "接近起点，双击闭合路径";
            }
        }
    }

    private void WallUpdateGhost(Point screenPos)
    {
        if (_wallGhostEdge is null || _wallPoints.Count == 0) return;
        var worldPos = ScreenToWorld(screenPos);
        _wallGhostEdge.StartPoint = _wallPoints[^1];
        _wallGhostEdge.EndPoint   = worldPos;
    }

    private void WallCommit(bool isClosed)
    {
        if (_viewModel is null || _wallPoints.Count < 2)
        {
            ClearOverlay();
            return;
        }

        // _wallPoints 现在已经是世界坐标
        var worldPts = new List<(double X, double Y)>(_wallPoints.Count);
        foreach (var p in _wallPoints)
        {
            // 转换为 GameObject 坐标系（Y轴翻转，原点偏移）
            var gameX = p.X - MapViewportConstants.WorldOriginContent;
            var gameY = -(p.Y - MapViewportConstants.WorldOriginContent);
            worldPts.Add((gameX, gameY));
        }

        // 检测首尾自动闭合（距离 < 20 世界单位）
        if (!isClosed && worldPts.Count >= 3)
        {
            var first = _wallPoints[0];
            var last = _wallPoints[^1];
            var dx = last.X - first.X;
            var dy = last.Y - first.Y;
            var dist = Math.Sqrt(dx * dx + dy * dy);
            if (dist < 20)
            {
                isClosed = true;
                worldPts.RemoveAt(worldPts.Count - 1); // 移除重复的闭合点
            }
        }

        _viewModel.CreateWallPathAt(worldPts, isClosed);
        ClearOverlay();
    }

    // ─────────────────────────────────────────────────────────────────────
    // Draw（自由绘制）
    // ─────────────────────────────────────────────────────────────────────

    private void BeginDraw(Point screenPos)
    {
        ClearOverlay();
        _isToolDragging = true;
        _drawPoints.Clear();
        var worldPos = ScreenToWorld(screenPos);
        _drawPoints.Add(worldPos);

        _drawPolyline = new Polyline
        {
            Stroke = new SolidColorBrush(Color.Parse("#F59E0B")),
            StrokeThickness = 3,
            StrokeLineCap   = PenLineCap.Round,
            StrokeJoin      = PenLineJoin.Round,
            IsHitTestVisible = false,
        };
        _toolOverlayCanvas!.Children.Add(_drawPolyline);
    }

    private void UpdateDraw(Point screenPos)
    {
        if (_drawPolyline is null) return;

        var worldPos = ScreenToWorld(screenPos);

        // 抽稀：与上一个点距离 > 4 世界单位才加入，减少顶点数
        if (_drawPoints.Count > 0)
        {
            var last = _drawPoints[^1];
            var dx = worldPos.X - last.X;
            var dy = worldPos.Y - last.Y;
            if (dx * dx + dy * dy < 16) return;
        }

        _drawPoints.Add(worldPos);
        _drawPolyline.Points = new AvaloniaList<Point>(_drawPoints);
    }

    private void CommitDraw()
    {
        _isToolDragging = false;
        if (_viewModel is null || _drawPoints.Count < 2) { ClearOverlay(); return; }

        // _drawPoints 现在已经是世界坐标
        var worldPts = new List<(double X, double Y)>(_drawPoints.Count);
        foreach (var p in _drawPoints)
        {
            // 转换为 GameObject 坐标系（Y轴翻转，原点偏移）
            var gameX = p.X - MapViewportConstants.WorldOriginContent;
            var gameY = -(p.Y - MapViewportConstants.WorldOriginContent);
            worldPts.Add((gameX, gameY));
        }

        double sumX = 0, sumY = 0;
        foreach (var (wx, wy) in worldPts) { sumX += wx; sumY += wy; }
        var centerX = sumX / worldPts.Count;
        var centerY = sumY / worldPts.Count;

        var relPts = new List<(double X, double Y)>(worldPts.Count);
        foreach (var (wx, wy) in worldPts)
            relPts.Add((wx - centerX, wy - centerY));

        _viewModel.CommandBus.Execute(new VmCreateDrawingCommand(_viewModel, centerX, centerY, relPts));
        ClearOverlay();
    }

    // ─────────────────────────────────────────────────────────────────────
    // 清理 overlay
    // ─────────────────────────────────────────────────────────────────────

    private void ClearOverlay()
    {
        _toolOverlayCanvas?.Children.Clear();
        _measureLine    = null;
        _measureLabel   = null;
        _laserDot       = null;
        _laserTrailLine = null;
        _laserTrail.Clear();
        _shapePreviewRect     = null;
        _shapePreviewEllipse  = null;
        _shapePreviewLine     = null;
        _shapePreviewPath     = null;
        _polygonPreview   = null;
        _polygonGhostEdge = null;
        _polygonPoints.Clear();
        _wallPreview      = null;
        _wallGhostEdge    = null;
        _wallPoints.Clear();
        _drawPolyline = null;
        _drawPoints.Clear();
        _fogPreviewRect = null;
    }

    // ─────────────────────────────────────────────────────────────────────
    // Fog — 矩形拖拽绘制/擦除迷雾
    // ─────────────────────────────────────────────────────────────────────

    private void BeginFog(Point screenPos)
    {
        ClearOverlay();
        _isToolDragging = true;
        _toolDragStart  = screenPos;

        bool isErase = _viewModel?.FogSubMode == "erase";
        _fogPreviewRect = new Rectangle
        {
            Stroke          = new SolidColorBrush(isErase ? Color.Parse("#F59E0B") : Color.Parse("#6366F1")),
            StrokeThickness = 2,
            StrokeDashArray = [6, 3],
            Fill            = new SolidColorBrush(isErase
                ? Color.FromArgb(40, 251, 191, 36)
                : Color.FromArgb(40, 99, 102, 241)),
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(_fogPreviewRect, screenPos.X);
        Canvas.SetTop(_fogPreviewRect, screenPos.Y);
        _toolOverlayCanvas?.Children.Add(_fogPreviewRect);
    }

    private void UpdateFog(Point screenPos)
    {
        if (_fogPreviewRect is null) return;
        var x = Math.Min(_toolDragStart.X, screenPos.X);
        var y = Math.Min(_toolDragStart.Y, screenPos.Y);
        var w = Math.Abs(screenPos.X - _toolDragStart.X);
        var h = Math.Abs(screenPos.Y - _toolDragStart.Y);
        Canvas.SetLeft(_fogPreviewRect, x);
        Canvas.SetTop(_fogPreviewRect, y);
        _fogPreviewRect.Width  = w;
        _fogPreviewRect.Height = h;
    }

    private void CommitFog(Point screenPos)
    {
        _isToolDragging = false;
        if (_viewModel is null) return;

        var vpSize = GetViewportSize();
        var zs = _viewModel.ZoomScale <= 0 ? 1.0 : _viewModel.ZoomScale;

        // 屏幕坐标转世界坐标（左上角和右下角）
        Point ToWorld(Point p) => new(
            _cameraContentCenter.X + (p.X - vpSize.Width  / 2.0) / zs - MapViewportConstants.WorldOriginContent,
            -((_cameraContentCenter.Y + (p.Y - vpSize.Height / 2.0) / zs) - MapViewportConstants.WorldOriginContent));

        var p1 = ToWorld(_toolDragStart);
        var p2 = ToWorld(screenPos);
        var wx = Math.Min(p1.X, p2.X);
        var wy = Math.Min(p1.Y, p2.Y);
        var ww = Math.Abs(p2.X - p1.X);
        var wh = Math.Abs(p2.Y - p1.Y);

        if (ww < 0.5 && wh < 0.5)
        {
            ClearOverlay();
            return;
        }

        if (_viewModel.FogSubMode == "erase")
            _viewModel.FogRevealRect(wx, wy, ww, wh);
        else
            _viewModel.FogPaintRect(wx, wy, ww, wh);

        ClearOverlay();
    }
}
