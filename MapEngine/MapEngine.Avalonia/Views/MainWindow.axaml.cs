using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using MapEngine.Render;
using MapEngine.Avalonia.Commands;
using MapEngine.Avalonia.Controls;
using MapEngine.Avalonia.Graphics;
using MapEngine.Avalonia.Services;
using MapEngine.Avalonia.ViewModels;

namespace MapEngine.Avalonia.Views;

public partial class MapEditorView : UserControl
{
    private static readonly DataFormat<string> AssetDragFormat = DataFormat.CreateStringApplicationFormat("MapVttApp.AssetPath");
    private MainWindowViewModel? _viewModel;
    private Border? _mapViewportSurface;
    private SilkMapCanvas? _mapSilkCanvas;
    private ListBox? _assetItemsListBox;
    private bool _dragPlacementFinalized;
    private readonly DispatcherTimer _mapDragFinalizeTimer;
    private bool _mapViewportInitialized;
    private double _lastZoomScale = 1.0;
    private bool _isPanningViewport;
    private bool _isDraggingObject;
    private Point _dragStartPointerPosition;
    private double _dragStartObjectX;
    private double _dragStartObjectY;
    private List<(HierarchyItemViewModel Item, double X, double Y)>? _dragChildSnapshots;
    private bool _suppressZoomViewportPreservation;
    private Point _cameraContentCenter = new(MapViewportConstants.WorldOriginContent, MapViewportConstants.WorldOriginContent);
    private Point _panStartPointerPosition;
    private Point _panStartContentCenter;
    private bool _isDraggingWallHandle;
    private HierarchyItemViewModel? _wallHandleDragItem;
    private int _wallHandleDragIndex;
    private (double X, double Y) _wallHandleDragStart;

    // 素材库折叠状态：折叠前的行高（展开时恢复）
    private RowDefinition? _assetLibraryRow;
    private GridSplitter? _assetSplitter;
    private Control? _assetContent;
    private Button? _assetCollapseButton;
    private double _assetRowHeightBeforeCollapse = 250;
    private bool _assetLibraryCollapsed;

    // 左右栏折叠：操作 ColumnDefinition.Width（GridSplitter 原生调节列宽，折叠切 GridLength）
    private Border? _leftPanel;
    private Border? _rightPanel;
    private Button? _leftCollapseButton;
    private Button? _rightCollapseButton;
    private ColumnDefinition? _leftColumn;
    private ColumnDefinition? _rightColumn;
    private GridSplitter? _leftSplitter;
    private GridSplitter? _rightSplitter;
    private double _leftWidthBeforeCollapse = 260;
    private double _rightWidthBeforeCollapse = 320;
    private bool _leftCollapsed;
    private bool _rightCollapsed;
    private const double CollapsedStripWidth = 40;

    // 折叠时需隐藏的内容（只留背景+折叠按钮，左栏额外留导航按钮）
    private Control? _leftSettingsButton;
    private Control? _leftSaveButton;
    private Control? _leftPackageName;
    private Control? _leftTreeScroll;
    private Control? _leftSelectionInfo;
    private Control? _leftCollapsedNavButton;
    private Control? _rightHeaderText;
    private Control? _rightMenuButton;
    private Control? _rightContentScroll;

    public MapEditorView()
    {
        InitializeComponent();
        var centerGrid = this.FindControl<Grid>("CenterColumnGrid");
        _assetLibraryRow = centerGrid is { RowDefinitions.Count: >= 3 } ? centerGrid.RowDefinitions[2] : null;
        _assetSplitter = this.FindControl<GridSplitter>("AssetSplitter");
        _assetContent = this.FindControl<Control>("AssetContent");
        _assetCollapseButton = this.FindControl<Button>("AssetCollapseButton");

        _leftPanel = this.FindControl<Border>("LeftPanel");
        _rightPanel = this.FindControl<Border>("RightPanel");
        _leftCollapseButton = this.FindControl<Button>("LeftCollapseButton");
        _rightCollapseButton = this.FindControl<Button>("RightCollapseButton");
        var rootCols = this.FindControl<Grid>("RootColumnsGrid");
        _leftColumn = rootCols is { ColumnDefinitions.Count: >= 5 } ? rootCols.ColumnDefinitions[0] : null;
        _rightColumn = rootCols is { ColumnDefinitions.Count: >= 5 } ? rootCols.ColumnDefinitions[4] : null;
        _leftSplitter = this.FindControl<GridSplitter>("LeftSplitter");
        _rightSplitter = this.FindControl<GridSplitter>("RightSplitter");
        _leftSettingsButton = this.FindControl<Control>("LeftSettingsButton");
        _leftSaveButton = this.FindControl<Control>("LeftSaveButton");
        _leftPackageName = this.FindControl<Control>("LeftPackageName");
        _leftTreeScroll = this.FindControl<Control>("LeftTreeScroll");
        _leftSelectionInfo = this.FindControl<Control>("LeftSelectionInfo");
        _leftCollapsedNavButton = this.FindControl<Control>("LeftCollapsedNavButton");
        _rightHeaderText = this.FindControl<Control>("RightHeaderText");
        _rightMenuButton = this.FindControl<Control>("RightMenuButton");
        _rightContentScroll = this.FindControl<Control>("RightContentScroll");
        _mapViewportSurface = this.FindControl<Border>("MapViewportSurface");
        _mapSilkCanvas = this.FindControl<SilkMapCanvas>("MapSilkCanvas");
        if (_mapSilkCanvas is not null)
        {
            _mapSilkCanvas.SceneProvider = BuildRenderScene;
            _mapSilkCanvas.RuntimeInfoAvailable += OnRuntimeInfoAvailable;
        }
        _assetItemsListBox = this.FindControl<ListBox>("AssetItemsListBox");
        var mapViewportHost = this.FindControl<Grid>("MapViewportHost");
        _mapDragFinalizeTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(140)
        };
        _mapDragFinalizeTimer.Tick += OnMapDragFinalizeTimerTick;
        _mapViewportSurface?.AddHandler(InputElement.PointerWheelChangedEvent, MapViewportHost_PointerWheelChanged, RoutingStrategies.Tunnel, true);
        _mapViewportSurface?.AddHandler(InputElement.PointerPressedEvent, MapViewportHost_PointerPressed, RoutingStrategies.Tunnel, true);
        _mapViewportSurface?.AddHandler(InputElement.PointerMovedEvent, MapViewportHost_PointerMoved, RoutingStrategies.Tunnel, true);
        _mapViewportSurface?.AddHandler(InputElement.PointerReleasedEvent, MapViewportHost_PointerReleased, RoutingStrategies.Tunnel, true);
        _mapViewportSurface?.AddHandler(InputElement.PointerCaptureLostEvent, MapViewportHost_PointerCaptureLost, RoutingStrategies.Tunnel, true);
        DataContextChanged += OnDataContextChanged;
        AttachedToVisualTree += (_, _) => OnOpened(this, EventArgs.Empty);
        KeyDown += OnKeyDown;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (_viewModel is null) return;

        if (e.KeyModifiers == KeyModifiers.Control)
        {
            switch (e.Key)
            {
                case Key.Z:
                    _viewModel.CommandBus.Undo();
                    e.Handled = true;
                    break;
                case Key.Y:
                    _viewModel.CommandBus.Redo();
                    e.Handled = true;
                    break;
            }
        }
        else if (e.KeyModifiers == KeyModifiers.None)
        {
            switch (e.Key)
            {
                case Key.Delete:
                case Key.Back:
                    if (_viewModel.SelectedHierarchyItem is not null)
                    {
                        _viewModel.DeleteHierarchyItem(_viewModel.SelectedHierarchyItem);
                        e.Handled = true;
                    }
                    break;
            }
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.NavigationRequested -= OnNavigationRequested;
            _viewModel.SettingsRequested -= OnSettingsRequested;
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _mapDragFinalizeTimer.Stop();

        if (_mapSilkCanvas is not null)
        {
            _mapSilkCanvas.RuntimeInfoAvailable -= OnRuntimeInfoAvailable;
        }

        base.OnDetachedFromVisualTree(e);
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.NavigationRequested -= OnNavigationRequested;
            _viewModel.SettingsRequested -= OnSettingsRequested;
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _viewModel = DataContext as MainWindowViewModel;
        if (_viewModel is not null)
        {
            _viewModel.NavigationRequested += OnNavigationRequested;
            _viewModel.SettingsRequested += OnSettingsRequested;
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            _lastZoomScale = _viewModel.ZoomScale <= 0 ? 1.0 : _viewModel.ZoomScale;
            if (_mapSilkCanvas?.RuntimeInfo is GraphicsRuntimeInfo runtimeInfo)
            {
                _viewModel.SetGraphicsRuntimeInfo(runtimeInfo);
            }
            Dispatcher.UIThread.Post(EnsureMapViewportInitialized, DispatcherPriority.Loaded);
        }
    }

    private void OnOpened(object? sender, EventArgs e)
        => Dispatcher.UIThread.Post(EnsureMapViewportInitialized, DispatcherPriority.Loaded);

    private void OnRuntimeInfoAvailable(object? sender, GraphicsRuntimeInfo runtimeInfo)
    {
        Dispatcher.UIThread.Post(() => _viewModel?.SetGraphicsRuntimeInfo(runtimeInfo));
    }

    private async void OnSettingsRequested(object? sender, EventArgs e)
    {
        try
        {
            var dialog = new SettingsDialog();
            var result = await dialog.ShowDialog<GlobalSettings?>(TopLevel.GetTopLevel(this) as Window ?? throw new InvalidOperationException());
            if (result != null && _viewModel != null)
            {
                _viewModel.ApplyGlobalSettings(result);
            }
        }
        catch
        {
        }
    }

    private void OnNavigationRequested(object? sender, SelectionNavigationRequestEventArgs e)
    {
        switch (e.Target)
        {
            case SelectionNavigationTarget.Map when e.Selection is HierarchyItemViewModel mapItem:
                NavigateToMap(mapItem);
                break;
            case SelectionNavigationTarget.AssetLibrary when e.Selection is AssetItemViewModel assetItem:
                NavigateToAsset(assetItem);
                break;
        }
    }

    private void NavigateToMap(HierarchyItemViewModel item)
    {
        if (_viewModel is null)
        {
            return;
        }

        var targetX = MapViewportConstants.WorldOriginContent + item.X;
        var targetY = MapViewportConstants.WorldOriginContent - item.Y;
        SetCameraCenter(new Point(targetX, targetY), _viewModel.ZoomScale <= 0 ? 1.0 : _viewModel.ZoomScale);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_viewModel is null)
        {
            return;
        }

        if (e.PropertyName == nameof(MainWindowViewModel.ZoomScale))
        {
            var newZoomScale = _viewModel.ZoomScale <= 0 ? 1.0 : _viewModel.ZoomScale;
            if (GetViewportSize().Width <= 0 || GetViewportSize().Height <= 0)
            {
                _lastZoomScale = newZoomScale;
                return;
            }

            if (_mapViewportInitialized && !_suppressZoomViewportPreservation)
            {
                PreserveViewportCenterOnZoom(_lastZoomScale, newZoomScale);
            }

            _lastZoomScale = newZoomScale;
            SyncMapViewport();
        }
    }

    private void PreserveViewportCenterOnZoom(double oldZoomScale, double newZoomScale)
    {
        if (oldZoomScale <= 0 || newZoomScale <= 0)
        {
            return;
        }

        _cameraContentCenter = ClampCameraCenter(_cameraContentCenter, newZoomScale);
        ApplyCameraTransform(newZoomScale);
    }

    private void EnsureMapViewportInitialized()
    {
        if (_viewModel is null)
        {
            return;
        }

        var viewportSize = GetViewportSize();
        if (viewportSize.Width <= 0 || viewportSize.Height <= 0)
        {
            return;
        }

        if (!_mapViewportInitialized)
        {
            CenterMapOnOrigin();
        }
        else
        {
            SyncMapViewport();
        }
    }

    private void CenterMapOnOrigin()
    {
        if (_viewModel is null)
        {
            return;
        }

        _cameraContentCenter = ClampCameraCenter(
            new Point(MapViewportConstants.WorldOriginContent, MapViewportConstants.WorldOriginContent),
            _viewModel.ZoomScale <= 0 ? 1.0 : _viewModel.ZoomScale);
        _mapViewportInitialized = true;
        _lastZoomScale = _viewModel.ZoomScale <= 0 ? 1.0 : _viewModel.ZoomScale;
        ApplyCameraTransform(_lastZoomScale);
        SyncMapViewport();
    }

    private void SyncMapViewport()
    {
        if (_viewModel is null)
        {
            return;
        }

        var zoomScale = _viewModel.ZoomScale <= 0 ? 1.0 : _viewModel.ZoomScale;
        var viewportSize = GetViewportSize();
        if (viewportSize.Width <= 0 || viewportSize.Height <= 0)
        {
            return;
        }

        var viewportWidthInContent = viewportSize.Width / zoomScale;
        var viewportHeightInContent = viewportSize.Height / zoomScale;
        _viewModel.UpdateMapViewport(
            _cameraContentCenter.X - (viewportWidthInContent / 2.0),
            _cameraContentCenter.Y - (viewportHeightInContent / 2.0),
            viewportWidthInContent,
            viewportHeightInContent);
        _mapSilkCanvas?.RequestFrame();
    }

    private Size GetViewportSize()
    {
        return _mapViewportSurface?.Bounds.Size
            ?? this.FindControl<Grid>("MapViewportHost")?.Bounds.Size
            ?? default;
    }

    private Point ClampCameraCenter(Point proposedCenter, double zoomScale)
    {
        var viewportSize = GetViewportSize();
        var scale = Math.Max(zoomScale, 0.0001);
        var halfViewportWidthInContent = viewportSize.Width / scale / 2.0;
        var halfViewportHeightInContent = viewportSize.Height / scale / 2.0;
        var minCenterX = halfViewportWidthInContent;
        var maxCenterX = MapViewportConstants.ContentSize - halfViewportWidthInContent;
        var minCenterY = halfViewportHeightInContent;
        var maxCenterY = MapViewportConstants.ContentSize - halfViewportHeightInContent;

        if (minCenterX > maxCenterX)
        {
            minCenterX = maxCenterX = MapViewportConstants.WorldOriginContent;
        }

        if (minCenterY > maxCenterY)
        {
            minCenterY = maxCenterY = MapViewportConstants.WorldOriginContent;
        }

        return new Point(
            Math.Clamp(proposedCenter.X, minCenterX, maxCenterX),
            Math.Clamp(proposedCenter.Y, minCenterY, maxCenterY));
    }

    private void SetCameraCenter(Point targetCenter, double zoomScale)
    {
        _cameraContentCenter = ClampCameraCenter(targetCenter, zoomScale);
        ApplyCameraTransform(zoomScale);
        SyncMapViewport();
    }

    private void ApplyCameraTransform(double zoomScale)
    {
        _ = zoomScale;
        _mapSilkCanvas?.RequestFrame();
    }

    private MapRenderScene? BuildRenderScene()
        => _viewModel is null
            ? null
            : MapSceneBuilder.Build(_viewModel, GetViewportSize(), _cameraContentCenter);

    private void NavigateToAsset(AssetItemViewModel item)
    {
        if (_assetItemsListBox is null)
        {
            return;
        }

        Dispatcher.UIThread.Post(() => _assetItemsListBox.ScrollIntoView(item));
    }

    private void MapViewportHost_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_viewModel is null || sender is not Control control)
        {
            return;
        }

        var point = e.GetCurrentPoint(control);
        var usePanTool = string.Equals(_viewModel.SelectedPrimaryTool?.Key, "pan", StringComparison.OrdinalIgnoreCase);
        var useSelectTool = string.Equals(_viewModel.SelectedPrimaryTool?.Key, "select", StringComparison.OrdinalIgnoreCase);

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

        // 右键点击墙壁控制点 → 删除
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

        if (_isDraggingObject && _viewModel.SelectedHierarchyItem is not null)
        {
            var currentPosition = e.GetPosition(control);
            var delta = currentPosition - _dragStartPointerPosition;
            var zoomScale = _viewModel.ZoomScale <= 0 ? 1.0 : _viewModel.ZoomScale;
            var deltaWorldX = delta.X / zoomScale;
            var deltaWorldY = -(delta.Y / zoomScale);

            var item = _viewModel.SelectedHierarchyItem;
            var newX = _dragStartObjectX + deltaWorldX;
            var newY = _dragStartObjectY + deltaWorldY;
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
        if (_viewModel is null)
        {
            return;
        }

        var stepDirection = Math.Sign(e.Delta.Y);
        if (stepDirection == 0)
        {
            return;
        }

        var oldZoom = _viewModel.ZoomScale <= 0 ? 1.0 : _viewModel.ZoomScale;
        var pointer = sender is Control control ? e.GetPosition(control) : default;
        var viewportSize = GetViewportSize();
        if (viewportSize.Width <= 0 || viewportSize.Height <= 0)
        {
            return;
        }

        var anchorContentX = _cameraContentCenter.X + ((pointer.X - (viewportSize.Width / 2.0)) / oldZoom);
        var anchorContentY = _cameraContentCenter.Y + ((pointer.Y - (viewportSize.Height / 2.0)) / oldZoom);
        _suppressZoomViewportPreservation = true;
        _viewModel.SetZoomFromInteraction(_viewModel.ZoomPercent + (stepDirection * 10));
        _suppressZoomViewportPreservation = false;
        var newZoom = _viewModel.ZoomScale <= 0 ? 1.0 : _viewModel.ZoomScale;
        SetCameraCenter(new Point(
            anchorContentX - ((pointer.X - (viewportSize.Width / 2.0)) / newZoom),
            anchorContentY - ((pointer.Y - (viewportSize.Height / 2.0)) / newZoom)),
            newZoom);
        e.Handled = true;
    }

    /// <summary>折叠/展开素材库：折叠时行高改 Auto（只剩标题栏）+ 隐藏内容 + 禁用 splitter。</summary>
    private void AssetCollapseButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_assetLibraryRow is null) return;

        _assetLibraryCollapsed = !_assetLibraryCollapsed;

        if (_assetLibraryCollapsed)
        {
            // 记住当前高度，改为 Auto（只剩标题栏），隐藏内容+禁用拖拽
            _assetRowHeightBeforeCollapse = _assetLibraryRow.ActualHeight > 40
                ? _assetLibraryRow.ActualHeight : _assetRowHeightBeforeCollapse;
            _assetLibraryRow.MinHeight = 0;
            _assetLibraryRow.Height = GridLength.Auto;
            if (_assetContent is not null) _assetContent.IsVisible = false;
            if (_assetSplitter is not null) _assetSplitter.IsEnabled = false;
            if (_assetCollapseButton is not null) _assetCollapseButton.Content = "▲";
        }
        else
        {
            // 恢复到折叠前的高度
            if (_assetContent is not null) _assetContent.IsVisible = true;
            _assetLibraryRow.MinHeight = 32;
            _assetLibraryRow.Height = new GridLength(_assetRowHeightBeforeCollapse, GridUnitType.Pixel);
            if (_assetSplitter is not null) _assetSplitter.IsEnabled = true;
            if (_assetCollapseButton is not null) _assetCollapseButton.Content = "▼";
        }
    }

    private static void SetVisible(bool visible, params Control?[] controls)
    {
        foreach (var c in controls)
            if (c is not null) c.IsVisible = visible;
    }

    /// <summary>
    /// 平滑动画一个 ColumnDefinition 的像素宽度（GridLength 不能直接过渡，用定时器插值）。
    /// 折叠/展开左右栏时调用。拖动由 GridSplitter 原生处理，与此互不干扰。
    /// </summary>
    private void AnimateColumnWidth(ColumnDefinition col, double from, double to, Action? onDone = null)
    {
        var start = DateTime.UtcNow;
        var duration = TimeSpan.FromMilliseconds(200);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        timer.Tick += (_, _) =>
        {
            var t = (DateTime.UtcNow - start).TotalMilliseconds / duration.TotalMilliseconds;
            if (t >= 1.0)
            {
                col.Width = new GridLength(to, GridUnitType.Pixel);
                timer.Stop();
                onDone?.Invoke();
                return;
            }
            // CubicEaseOut
            var eased = 1 - Math.Pow(1 - t, 3);
            col.Width = new GridLength(from + (to - from) * eased, GridUnitType.Pixel);
        };
        timer.Start();
    }

    /// <summary>折叠/展开左栏：动画列宽到细条(40px)或恢复原宽。GridSplitter 仍可拖。</summary>
    private void LeftCollapseButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_leftColumn is null) return;
        _leftCollapsed = !_leftCollapsed;
        if (_leftCollapsed)
        {
            _leftWidthBeforeCollapse = _leftColumn.ActualWidth > CollapsedStripWidth
                ? _leftColumn.ActualWidth : _leftWidthBeforeCollapse;
            _leftColumn.MinWidth = 0;
            if (_leftSplitter is not null) _leftSplitter.IsEnabled = false;
            SetVisible(false, _leftSettingsButton, _leftSaveButton, _leftPackageName,
                       _leftTreeScroll, _leftSelectionInfo);
            SetVisible(true, _leftCollapsedNavButton);
            if (_leftCollapseButton is not null) _leftCollapseButton.Content = "▶";
            AnimateColumnWidth(_leftColumn, _leftColumn.ActualWidth, CollapsedStripWidth);
        }
        else
        {
            AnimateColumnWidth(_leftColumn, _leftColumn.ActualWidth, _leftWidthBeforeCollapse, () =>
            {
                _leftColumn.MinWidth = 40;
                if (_leftSplitter is not null) _leftSplitter.IsEnabled = true;
            });
            SetVisible(true, _leftSettingsButton, _leftSaveButton, _leftPackageName,
                       _leftTreeScroll, _leftSelectionInfo);
            SetVisible(false, _leftCollapsedNavButton);
            if (_leftCollapseButton is not null) _leftCollapseButton.Content = "◀";
        }
    }

    /// <summary>折叠/展开右栏。</summary>
    private void RightCollapseButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_rightColumn is null) return;
        _rightCollapsed = !_rightCollapsed;
        if (_rightCollapsed)
        {
            _rightWidthBeforeCollapse = _rightColumn.ActualWidth > CollapsedStripWidth
                ? _rightColumn.ActualWidth : _rightWidthBeforeCollapse;
            _rightColumn.MinWidth = 0;
            if (_rightSplitter is not null) _rightSplitter.IsEnabled = false;
            SetVisible(false, _rightHeaderText, _rightMenuButton, _rightContentScroll);
            if (_rightCollapseButton is not null) _rightCollapseButton.Content = "◀";
            AnimateColumnWidth(_rightColumn, _rightColumn.ActualWidth, CollapsedStripWidth);
        }
        else
        {
            AnimateColumnWidth(_rightColumn, _rightColumn.ActualWidth, _rightWidthBeforeCollapse, () =>
            {
                _rightColumn.MinWidth = 40;
                if (_rightSplitter is not null) _rightSplitter.IsEnabled = true;
            });
            SetVisible(true, _rightHeaderText, _rightMenuButton, _rightContentScroll);
            if (_rightCollapseButton is not null) _rightCollapseButton.Content = "▶";
        }
    }

    private void AssetLibrary_Drop(object? sender, DragEventArgs e)
    {
        if (_viewModel is null) return;

        var targetFolder = _viewModel.SelectedAssetFolder?.FullPath;
        if (string.IsNullOrEmpty(targetFolder))
        {
            targetFolder = System.IO.Path.Combine(AppContext.BaseDirectory, "AssetLibrary", "StaticObjects");
        }

        System.IO.Directory.CreateDirectory(targetFolder);

        var files = e.DataTransfer.TryGetFiles();
        if (files is null || files.Length == 0) return;

        foreach (var storageItem in files)
        {
            var sourcePath = storageItem.Path.LocalPath;
            if (string.IsNullOrEmpty(sourcePath) || !System.IO.File.Exists(sourcePath)) continue;

            var destFileName = System.IO.Path.GetFileName(sourcePath);
            var destPath = System.IO.Path.Combine(targetFolder, destFileName);
            if (!System.IO.File.Exists(destPath))
                System.IO.File.Copy(sourcePath, destPath);
        }

        _viewModel.ReloadAssetLibraryPublic();
        e.Handled = true;
    }

    private async void SelectSpriteImage_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel?.SelectedHierarchyItem is null) return;

        var dialog = new FilePickerOpenOptions
        {
            Title = "选择精灵图片",
            AllowMultiple = false,
            FileTypeFilter = [new("图片文件") { Patterns = ["*.png", "*.jpg", "*.jpeg", "*.webp", "*.gif"] }]
        };

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(dialog);
        if (files.Count == 0) return;

        var selectedFile = files[0];
        var sourcePath = selectedFile.Path.LocalPath;
        var item = _viewModel.SelectedHierarchyItem;

        var useCopyMode = _viewModel.SpriteImportModeIndex == 0;

        string finalPath;
        if (useCopyMode)
        {
            var assetFolder = System.IO.Path.Combine(AppContext.BaseDirectory, "AssetLibrary", "StaticObjects");
            System.IO.Directory.CreateDirectory(assetFolder);
            var destFileName = System.IO.Path.GetFileName(sourcePath);
            var destPath = System.IO.Path.Combine(assetFolder, destFileName);
            if (!System.IO.File.Exists(destPath))
                System.IO.File.Copy(sourcePath, destPath);
            finalPath = System.IO.Path.GetRelativePath(AppContext.BaseDirectory, destPath);
        }
        else
        {
            finalPath = sourcePath;
        }

        item.SourceAssetPath = finalPath;
        item.SourceAssetKind = "Image";
        item.SourceAssetName = System.IO.Path.GetFileNameWithoutExtension(sourcePath);
        ApplySpriteAspectRatio(item, finalPath);
    }

    private void PickSpriteFromAsset_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel?.SelectedHierarchyItem is null || _viewModel.SelectedAssetItem is null) return;

        var asset = _viewModel.SelectedAssetItem;
        var item = _viewModel.SelectedHierarchyItem;

        var resolvedPath = MapEngine.Avalonia.Services.MapSpriteAssetResolver.ResolveSpritePath(asset.FullPath, asset.Kind);
        if (string.IsNullOrEmpty(resolvedPath) && System.IO.File.Exists(asset.FullPath))
            resolvedPath = asset.FullPath;

        if (string.IsNullOrEmpty(resolvedPath)) return;

        var relativePath = System.IO.Path.GetRelativePath(AppContext.BaseDirectory, resolvedPath);
        item.SourceAssetPath = relativePath;
        item.SourceAssetKind = "Image";
        item.SourceAssetName = asset.Name;
        ApplySpriteAspectRatio(item, resolvedPath);
    }

    private static void ApplySpriteAspectRatio(HierarchyItemViewModel item, string imagePath)
    {
        try
        {
            var fullPath = System.IO.Path.IsPathRooted(imagePath)
                ? imagePath
                : System.IO.Path.Combine(AppContext.BaseDirectory, imagePath);

            if (!System.IO.File.Exists(fullPath)) return;

            var info = SixLabors.ImageSharp.Image.Identify(fullPath);
            if (info is null || info.Width <= 0 || info.Height <= 0) return;

            var cellSize = MapViewportConstants.CellSize;
            var widthInCells = info.Width / cellSize;
            var heightInCells = info.Height / cellSize;

            if (widthInCells < 1) widthInCells = 1;
            if (heightInCells < 1) heightInCells = 1;

            item.ScaleX = widthInCells;
            item.ScaleY = heightInCells;
        }
        catch { }
    }

    private async void AssetCard_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control control
            || control.DataContext is not AssetItemViewModel asset
            || !e.GetCurrentPoint(control).Properties.IsLeftButtonPressed)
        {
            return;
        }

        _dragPlacementFinalized = false;
        CancelPendingMapFinalize();
        var data = new DataTransfer();
        data.Add(DataTransferItem.Create(AssetDragFormat, asset.FullPath));
        data.Add(DataTransferItem.CreateText(asset.FullPath));
        await DragDrop.DoDragDropAsync(e, data, DragDropEffects.Copy);
        CancelPendingMapFinalize();
        _viewModel?.CancelMapDragPreview();
        _dragPlacementFinalized = false;
    }

    private void HierarchyRoot_DragOver(object? sender, DragEventArgs e)
    {
        CancelPendingMapFinalize();
        UpdateDragEffects(e);
    }

    private async void HierarchyRoot_Drop(object? sender, DragEventArgs e)
    {
        if (_dragPlacementFinalized)
        {
            e.Handled = true;
            return;
        }

        var asset = await TryGetDraggedAssetAsync(e);
        if (_viewModel is null || asset is null)
        {
            return;
        }

        _viewModel.CancelMapDragPreview();
        _viewModel.CreateInstanceFromAssetUnderHierarchy(asset, null);
        _dragPlacementFinalized = true;
        e.Handled = true;
    }

    private void HierarchyItem_DragOver(object? sender, DragEventArgs e)
    {
        CancelPendingMapFinalize();
        UpdateDragEffects(e);
    }

    private async void HierarchyItem_Drop(object? sender, DragEventArgs e)
    {
        if (_dragPlacementFinalized)
        {
            e.Handled = true;
            return;
        }

        var asset = await TryGetDraggedAssetAsync(e);
        if (_viewModel is null
            || sender is not Control control
            || control.DataContext is not HierarchyItemViewModel target
            || asset is null)
        {
            return;
        }

        _viewModel.CancelMapDragPreview();
        _viewModel.CreateInstanceFromAssetUnderHierarchy(asset, target);
        _dragPlacementFinalized = true;
        e.Handled = true;
    }

    private async void MapDropTarget_DragOver(object? sender, DragEventArgs e)
    {
        if (_dragPlacementFinalized)
        {
            e.DragEffects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        CancelPendingMapFinalize();
        var asset = await TryGetDraggedAssetAsync(e);
        if (_viewModel is null || asset is null)
        {
            e.DragEffects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        if (!TryGetMapCanvasPosition(e, out var contentX, out var contentY))
        {
            ScheduleMapFinalize();
            e.DragEffects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        _viewModel.BeginOrUpdateMapDragPreview(asset, ContentToWorldX(contentX), ContentToWorldY(contentY));
        e.DragEffects = DragDropEffects.Copy;
        e.Handled = true;
    }

    private void MapDropTarget_DragLeave(object? sender, RoutedEventArgs e)
        => ScheduleMapFinalize();

    private async void MapDropTarget_Drop(object? sender, DragEventArgs e)
    {
        if (_dragPlacementFinalized)
        {
            e.Handled = true;
            return;
        }

        CancelPendingMapFinalize();
        var asset = await TryGetDraggedAssetAsync(e);
        if (_viewModel is null || asset is null || !TryGetMapCanvasPosition(e, out var contentX, out var contentY))
        {
            FinalizeMapPreviewAtLastValidPosition();
            return;
        }

        _viewModel.BeginOrUpdateMapDragPreview(asset, ContentToWorldX(contentX), ContentToWorldY(contentY));
        _viewModel.CommitMapDragPreview();
        _dragPlacementFinalized = true;
        e.Handled = true;
    }

    private async void AddHierarchyChild_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is null || GetMenuParameter<HierarchyItemViewModel>(sender) is not { } parent)
        {
            return;
        }

        _viewModel.SelectedHierarchyItem = parent;
        var created = _viewModel.AddEmptyObject(parent);
        var name = await PromptForNameAsync("新建空对象", created.Name);
        if (!string.IsNullOrWhiteSpace(name))
        {
            _viewModel.RenameHierarchyItem(created, name);
        }
    }

    private async void AddWallObject_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is null || GetMenuParameter<HierarchyItemViewModel>(sender) is not { } parent)
        {
            return;
        }

        _viewModel.SelectedHierarchyItem = parent;
        var created = _viewModel.CreateWallObject(parent);
        if (created is not null)
        {
            var name = await PromptForNameAsync("新建墙壁", created.Name);
            if (!string.IsNullOrWhiteSpace(name))
            {
                _viewModel.RenameHierarchyItem(created, name);
            }
        }
    }

    private async void AddRootEmptyObject_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is null)
        {
            return;
        }

        var created = _viewModel.AddRootEmptyObject();
        if (created is null)
        {
            return;
        }

        var name = await PromptForNameAsync("新建空对象", created.Name);
        if (!string.IsNullOrWhiteSpace(name))
        {
            _viewModel.RenameHierarchyItem(created, name);
        }
    }

    private async void AddRootWallObject_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is null)
        {
            return;
        }

        var root = _viewModel.HierarchyRoots.FirstOrDefault();
        if (root is null)
        {
            return;
        }

        var created = _viewModel.CreateWallObject(root);
        if (created is null)
        {
            return;
        }

        var name = await PromptForNameAsync("新建墙壁", created.Name);
        if (!string.IsNullOrWhiteSpace(name))
        {
            _viewModel.RenameHierarchyItem(created, name);
        }
    }

    private void CopyHierarchyItem_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is null || GetMenuParameter<HierarchyItemViewModel>(sender) is not { } item)
        {
            return;
        }

        _viewModel.CopyHierarchyItem(item);
    }

    private void PasteHierarchyItem_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is null || GetMenuParameter<HierarchyItemViewModel>(sender) is not { } item)
        {
            return;
        }

        _viewModel.SelectedHierarchyItem = item;
        _viewModel.PasteHierarchyItem(item);
    }

    private void PasteHierarchyToRoot_Click(object? sender, RoutedEventArgs e)
    {
        _viewModel?.PasteHierarchyToRoot();
    }

    private void DuplicateHierarchyItem_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is null || GetMenuParameter<HierarchyItemViewModel>(sender) is not { } item)
        {
            return;
        }

        _viewModel.DuplicateHierarchyItem(item);
    }

    private void MoveHierarchyItemUp_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is null || GetMenuParameter<HierarchyItemViewModel>(sender) is not { } item)
        {
            return;
        }

        _viewModel.MoveItemUp(item);
    }

    private void MoveHierarchyItemDown_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is null || GetMenuParameter<HierarchyItemViewModel>(sender) is not { } item)
        {
            return;
        }

        _viewModel.MoveItemDown(item);
    }

    private void PromoteHierarchyItem_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is null || GetMenuParameter<HierarchyItemViewModel>(sender) is not { } item)
        {
            return;
        }

        _viewModel.PromoteItem(item);
    }

    private void DemoteHierarchyItem_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is null || GetMenuParameter<HierarchyItemViewModel>(sender) is not { } item)
        {
            return;
        }

        _viewModel.DemoteItem(item);
    }

    private async void RenameHierarchyItem_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is null || GetMenuParameter<HierarchyItemViewModel>(sender) is not { } item)
        {
            return;
        }

        _viewModel.SelectedHierarchyItem = item;
        var name = await PromptForNameAsync("重命名层级对象", item.Name);
        if (!string.IsNullOrWhiteSpace(name))
        {
            _viewModel.RenameHierarchyItem(item, name);
        }
    }

    private void DeleteHierarchyItem_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is null || GetMenuParameter<HierarchyItemViewModel>(sender) is not { } item)
        {
            return;
        }

        _viewModel.DeleteHierarchyItem(item);
    }

    private async void AddAssetFolder_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is null || GetMenuParameter<AssetFolderViewModel>(sender) is not { } folder)
        {
            return;
        }

        _viewModel.SelectedAssetFolder = folder;
        var created = _viewModel.AddAssetFolder(folder);
        var name = await PromptForNameAsync("新增素材文件夹", created.Name);
        if (!string.IsNullOrWhiteSpace(name))
        {
            _viewModel.RenameAssetFolder(created, name);
        }
    }

    private void CopyAssetFolder_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is null || GetMenuParameter<AssetFolderViewModel>(sender) is not { } folder)
        {
            return;
        }

        _viewModel.CopyAssetFolder(folder);
    }

    private void PasteAssetFolder_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is null || GetMenuParameter<AssetFolderViewModel>(sender) is not { } folder)
        {
            return;
        }

        _viewModel.PasteAssetFolder(folder);
    }

    private void PasteAssetItemIntoFolder_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is null || GetMenuParameter<AssetFolderViewModel>(sender) is not { } folder)
        {
            return;
        }

        _viewModel.PasteAssetItem(folder);
    }

    private void DuplicateAssetFolder_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is null || GetMenuParameter<AssetFolderViewModel>(sender) is not { } folder)
        {
            return;
        }

        _viewModel.DuplicateAssetFolder(folder);
    }

    private async void AddAssetItem_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is null || GetMenuParameter<AssetFolderViewModel>(sender) is not { } folder)
        {
            return;
        }

        _viewModel.SelectedAssetFolder = folder;
        var created = _viewModel.AddAssetItem(folder);
        var name = await PromptForNameAsync("新增静态对象类", created.Name);
        if (!string.IsNullOrWhiteSpace(name))
        {
            _viewModel.RenameAssetItem(created, name);
        }
    }

    private async void RenameAssetFolder_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is null || GetMenuParameter<AssetFolderViewModel>(sender) is not { } folder)
        {
            return;
        }

        _viewModel.SelectedAssetFolder = folder;
        var name = await PromptForNameAsync("重命名素材文件夹", folder.Name);
        if (!string.IsNullOrWhiteSpace(name))
        {
            _viewModel.RenameAssetFolder(folder, name);
        }
    }

    private void DeleteAssetFolder_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is null || GetMenuParameter<AssetFolderViewModel>(sender) is not { } folder)
        {
            return;
        }

        _viewModel.DeleteAssetFolder(folder);
    }

    private async void RenameAssetItem_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is null || GetMenuParameter<AssetItemViewModel>(sender) is not { } item)
        {
            return;
        }

        _viewModel.SelectedAssetItem = item;
        var name = await PromptForNameAsync("重命名静态对象类", item.Name);
        if (!string.IsNullOrWhiteSpace(name))
        {
            _viewModel.RenameAssetItem(item, name);
        }
    }

    private void CopyAssetItem_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is null || GetMenuParameter<AssetItemViewModel>(sender) is not { } item)
        {
            return;
        }

        _viewModel.CopyAssetItem(item);
    }

    private void DuplicateAssetItem_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is null || GetMenuParameter<AssetItemViewModel>(sender) is not { } item)
        {
            return;
        }

        _viewModel.DuplicateAssetItem(item);
    }

    private void DeleteAssetItem_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is null || GetMenuParameter<AssetItemViewModel>(sender) is not { } item)
        {
            return;
        }

        _viewModel.DeleteAssetItem(item);
    }

    private static T? GetMenuParameter<T>(object? sender) where T : class
        => (sender as MenuItem)?.CommandParameter as T;

    private void UpdateDragEffects(DragEventArgs e)
    {
        e.DragEffects = !_dragPlacementFinalized && (e.DataTransfer.Contains(AssetDragFormat) || e.DataTransfer.Contains(DataFormat.Text))
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private bool TryGetMapCanvasPosition(DragEventArgs e, out double contentX, out double contentY)
    {
        contentX = 0;
        contentY = 0;

        if (_mapViewportSurface is null || _viewModel is null)
        {
            return false;
        }

        var zoomScale = _viewModel.ZoomScale <= 0 ? 1.0 : _viewModel.ZoomScale;
        var position = e.GetPosition(_mapViewportSurface);
        var viewportSize = GetViewportSize();

        if (position.X < 0 || position.Y < 0 || position.X > viewportSize.Width || position.Y > viewportSize.Height)
        {
            return false;
        }

        contentX = _cameraContentCenter.X + ((position.X - (viewportSize.Width / 2.0)) / zoomScale);
        contentY = _cameraContentCenter.Y + ((position.Y - (viewportSize.Height / 2.0)) / zoomScale);

        return contentX >= 0
            && contentY >= 0
            && contentX <= MapViewportConstants.ContentSize
            && contentY <= MapViewportConstants.ContentSize;
    }

    private static double ContentToWorldX(double contentX)
        => contentX - MapViewportConstants.WorldOriginContent;

    private static double ContentToWorldY(double contentY)
        => MapViewportConstants.WorldOriginContent - contentY;

    private void MapViewportHost_SizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (_viewModel is not null)
        {
            _cameraContentCenter = ClampCameraCenter(_cameraContentCenter, _viewModel.ZoomScale <= 0 ? 1.0 : _viewModel.ZoomScale);
            ApplyCameraTransform(_viewModel.ZoomScale <= 0 ? 1.0 : _viewModel.ZoomScale);
        }

        Dispatcher.UIThread.Post(EnsureMapViewportInitialized, DispatcherPriority.Background);
    }

    private void FinalizeMapPreviewAtLastValidPosition()
    {
        if (_dragPlacementFinalized || _viewModel is null || !_viewModel.HasMapDragPreview)
        {
            return;
        }

        CancelPendingMapFinalize();
        _viewModel.CommitMapDragPreview();
        _dragPlacementFinalized = true;
    }

    private void ScheduleMapFinalize()
    {
        if (_dragPlacementFinalized || _viewModel is null || !_viewModel.HasMapDragPreview)
        {
            return;
        }

        _mapDragFinalizeTimer.Stop();
        _mapDragFinalizeTimer.Start();
    }

    private void CancelPendingMapFinalize()
        => _mapDragFinalizeTimer.Stop();

    private void OnMapDragFinalizeTimerTick(object? sender, EventArgs e)
    {
        _mapDragFinalizeTimer.Stop();
        FinalizeMapPreviewAtLastValidPosition();
    }

    private System.Threading.Tasks.Task<AssetItemViewModel?> TryGetDraggedAssetAsync(DragEventArgs e)
    {
        if (_viewModel is null)
        {
            return System.Threading.Tasks.Task.FromResult<AssetItemViewModel?>(null);
        }

        var rawPath = e.DataTransfer.TryGetValue(AssetDragFormat)
            ?? e.DataTransfer.TryGetText();

        if (string.IsNullOrWhiteSpace(rawPath))
        {
            return System.Threading.Tasks.Task.FromResult<AssetItemViewModel?>(null);
        }

        return System.Threading.Tasks.Task.FromResult(_viewModel.GetAssetItemByPath(rawPath));
    }

    private async System.Threading.Tasks.Task<string?> PromptForNameAsync(string title, string initialText)
    {
        var inputBox = new TextBox
        {
            Text = initialText,
            Width = 280
        };

        var okButton = new Button
        {
            Content = "确定",
            MinWidth = 72
        };

        var cancelButton = new Button
        {
            Content = "取消",
            MinWidth = 72
        };

        var dialog = new Window
        {
            Title = title,
            Width = 360,
            Height = 160,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Content = new StackPanel
            {
                Margin = new Thickness(16),
                Spacing = 12,
                Children =
                {
                    new TextBlock { Text = "请输入名称" },
                    inputBox,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 8,
                        Children = { cancelButton, okButton }
                    }
                }
            }
        };

        okButton.Click += (_, _) => dialog.Close(inputBox.Text?.Trim());
        cancelButton.Click += (_, _) => dialog.Close(null);
        inputBox.KeyDown += (_, args) =>
        {
            if (args.Key == Key.Enter)
            {
                dialog.Close(inputBox.Text?.Trim());
            }
        };
        dialog.Opened += (_, _) =>
        {
            inputBox.Focus();
            inputBox.SelectAll();
        };

        return await dialog.ShowDialog<string?>(TopLevel.GetTopLevel(this) as Window ?? throw new InvalidOperationException());
    }

    private readonly Dictionary<TextBox, (string Property, object? OldValue)> _inspectorEditSnapshots = new();

    private void InspectorTextBox_GotFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is not TextBox tb || _viewModel?.SelectedHierarchyItem is null) return;
        if (tb.Tag is not string prop || string.IsNullOrEmpty(prop)) return;
        var item = _viewModel.SelectedHierarchyItem;
        _inspectorEditSnapshots[tb] = (prop, ReadProperty(item, prop));
    }

    private void InspectorTextBox_LostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is not TextBox tb || _viewModel is null) return;
        if (!_inspectorEditSnapshots.TryGetValue(tb, out var snap)) return;
        _inspectorEditSnapshots.Remove(tb);

        var item = _viewModel.SelectedHierarchyItem;
        if (item is null) return;

        var newValue = ReadProperty(item, snap.Property);
        if (Equals(newValue, snap.OldValue)) return;

        WriteProperty(item, snap.Property, snap.OldValue);
        _viewModel.CommandBus.Execute(new VmSetPropertyCommand(_viewModel, item.Id, snap.Property, newValue));
    }

    private static object? ReadProperty(HierarchyItemViewModel item, string prop) => prop switch
    {
        "X" => item.X, "Y" => item.Y, "Z" => item.Z,
        "Rotation" => item.Rotation,
        "ScaleX" => item.ScaleX, "ScaleY" => item.ScaleY,
        "Opacity" => item.Opacity,
        "VisionRadius" => item.VisionRadius,
        "Orientation" => item.Orientation,
        _ => null
    };

    private static void WriteProperty(HierarchyItemViewModel item, string prop, object? value)
    {
        if (value is null) return;
        var d = Convert.ToDouble(value);
        switch (prop)
        {
            case "X": item.X = d; break;
            case "Y": item.Y = d; break;
            case "Z": item.Z = d; break;
            case "Rotation": item.Rotation = d; break;
            case "ScaleX": item.ScaleX = d; break;
            case "ScaleY": item.ScaleY = d; break;
            case "Opacity": item.Opacity = d; break;
            case "VisionRadius": item.VisionRadius = d; break;
            case "Orientation": item.Orientation = d; break;
        }
    }

    private void AddVisionCone_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel?.SelectedHierarchyItem is null) return;
        _viewModel.AddVisionCone(_viewModel.SelectedHierarchyItem);
    }

    private void RemoveVisionCone_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel?.SelectedHierarchyItem is null) return;
        if (sender is Button { CommandParameter: VisionConeViewModel cone })
        {
            _viewModel.RemoveVisionCone(_viewModel.SelectedHierarchyItem, cone);
        }
    }

    private async void VisionConeItem_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (_viewModel?.SelectedHierarchyItem is null) return;
        if ((sender as Control)?.DataContext is not VisionConeViewModel cone) return;

        var dialog = new Window
        {
            Title = "编辑视野锥",
            Width = 400,
            Height = 380,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            CanResize = false,
            Background = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33))
        };

        var nameBox = new TextBox { Text = cone.Name };
        var offsetBox = new TextBox { Text = cone.CenterOffset.ToString() };
        var rangeBox = new TextBox { Text = cone.Range.ToString() };
        var fovBox = new TextBox { Text = cone.FieldOfView.ToString() };
        var enabledCheck = new CheckBox { Content = "启用", IsChecked = cone.IsEnabled, Foreground = Brushes.White };

        var okButton = new Button { Content = "确定", Width = 80 };
        var cancelButton = new Button { Content = "取消", Width = 80, Margin = new Thickness(8, 0, 0, 0) };

        okButton.Click += (_, _) =>
        {
            if (double.TryParse(offsetBox.Text, out var offset) &&
                double.TryParse(rangeBox.Text, out var range) &&
                double.TryParse(fovBox.Text, out var fov))
            {
                cone.Name = nameBox.Text ?? "视野锥";
                cone.CenterOffset = Math.Clamp(offset, -180, 180);
                cone.Range = Math.Max(0, range);
                cone.FieldOfView = Math.Clamp(fov, 0, 360);
                cone.IsEnabled = enabledCheck.IsChecked ?? true;
                dialog.Close(true);
            }
        };
        cancelButton.Click += (_, _) => dialog.Close(false);

        var labelStyle = new Action<TextBlock>(t => { t.Foreground = Brushes.LightGray; t.FontSize = 11; });
        TextBlock Label(string text) { var t = new TextBlock { Text = text }; labelStyle(t); return t; }

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 8,
            Children =
            {
                Label("名称"), nameBox,
                Label("中心偏转（-180 到 +180°）"), offsetBox,
                Label("视距（格子数）"), rangeBox,
                Label("视场角度（0-360°）"), fovBox,
                enabledCheck,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Margin = new Thickness(0, 12, 0, 0),
                    Children = { okButton, cancelButton }
                }
            }
        };

        await dialog.ShowDialog(TopLevel.GetTopLevel(this) as Window ?? throw new InvalidOperationException());
    }

    private void AddComponent_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel?.SelectedHierarchyItem is null) return;

        var item = _viewModel.SelectedHierarchyItem;

        // 显示组件选择菜单
        var menu = new ContextMenu
        {
            Items =
            {
                new MenuItem
                {
                    Header = "👁️ 视野组件",
                    IsEnabled = !item.HasVisionComponent,
                    Command = new CommunityToolkit.Mvvm.Input.RelayCommand(() =>
                    {
                        _viewModel.AddVisionComponent(item);
                    })
                },
                new MenuItem
                {
                    Header = "🧱 墙壁组件",
                    IsEnabled = !item.HasWallComponent,
                    Command = new CommunityToolkit.Mvvm.Input.RelayCommand(() =>
                    {
                        _viewModel.AddWallComponent(item);
                    })
                },
                new MenuItem
                {
                    Header = "🎭 Token 组件",
                    IsEnabled = !item.HasComponent<MapEngine.Core.Components.TokenComponent>(),
                    Command = new CommunityToolkit.Mvvm.Input.RelayCommand(() =>
                    {
                        _viewModel.AddTokenComponent(item);
                    })
                },
                new MenuItem
                {
                    Header = "💡 光源组件（未实现）",
                    IsEnabled = false
                },
                new MenuItem
                {
                    Header = "🔊 音频组件（未实现）",
                    IsEnabled = false
                }
            }
        };

        menu.Open(sender as Button);
    }

    private async void SelectWallTexture_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel?.SelectedHierarchyItem is null) return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择墙壁纹理",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("图片文件")
                {
                    Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.bmp" }
                }
            }
        });

        if (files.Count > 0)
        {
            var path = files[0].Path.LocalPath;
            _viewModel.SelectedHierarchyItem.WallTexturePath = path;
        }
    }
}
