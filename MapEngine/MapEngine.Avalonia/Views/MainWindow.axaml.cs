using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using MapEngine.Core.Components;
using MapEngine.Render;
using MapEngine.Avalonia.Commands;
using MapEngine.Avalonia.Controls;
using MapEngine.Avalonia.Graphics;
using MapEngine.Avalonia.Layout;
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

    // Snap-to-grid 由 ViewModel.IsSnapToGrid 统一管理（已移除本地字段）

    // 旋转 handle 拖拽状态
    private bool _isDraggingRotateHandle;
    private HierarchyItemViewModel? _rotateHandleItem;
    private double _rotateHandleDragStartAngle;
    private double _rotateHandleStartRotation;

    // 缩放 handle 拖拽状态（四角）
    private bool _isDraggingScaleHandle;
    private HierarchyItemViewModel? _scaleHandleItem;
    private int _scaleHandleCorner;               // 0=左上 1=右上 2=右下 3=左下
    private double _scaleHandleStartScaleX;
    private double _scaleHandleStartScaleY;
    private Point _scaleHandleDragStartPos;

    // 地图文本覆盖层管理
    private Canvas? _mapTextOverlay;
    private MapTextManager? _mapTextManager;

    // 动态布局 Behavior（替代硬编码 CSS push/compressed class）
    private MapEngine.Avalonia.Layout.DrawerLayoutBehavior? _drawerLayout;

    // 工具 overlay（Measure / Laser / Shape preview）
    private Canvas? _toolOverlayCanvas;

    public MapEditorView()
    {
        InitializeComponent();

        _mapViewportSurface = this.FindControl<Border>("MapViewportSurface");
        _mapSilkCanvas = this.FindControl<SilkMapCanvas>("MapSilkCanvas");
        if (_mapSilkCanvas is not null)
        {
            _mapSilkCanvas.SceneProvider = BuildRenderScene;
            _mapSilkCanvas.RuntimeInfoAvailable += OnRuntimeInfoAvailable;
        }

        _mapTextOverlay = this.FindControl<Canvas>("MapTextOverlay");
        _toolOverlayCanvas = this.FindControl<Canvas>("ToolOverlayCanvas");
        _assetItemsListBox = null; // 底部抽屉素材卡片用 ItemsControl，不再是 ListBox

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

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (_viewModel == null)
        {
            base.OnKeyDown(e);
            return;
        }

        if (e.Key == Key.PageUp)
        {
            _viewModel.FloorUpCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.PageDown)
        {
            _viewModel.FloorDownCommand.Execute(null);
            e.Handled = true;
        }

        base.OnKeyDown(e);
    }

    /// <summary>
    /// 添加状态按钮点击事件：显示预设状态库的弹出菜单
    /// </summary>
    private void AddCondition_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button) return;
        if (button.DataContext is not TokenComponentEditor editor) return;

        // 创建弹出菜单显示预设状态库
        var flyout = new Flyout
        {
            Placement = PlacementMode.Bottom,
            ShowMode = FlyoutShowMode.Standard
        };

        var scrollViewer = new ScrollViewer
        {
            MaxHeight = 300,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };

        var stackPanel = new StackPanel { Spacing = 2 };

        foreach (var preset in PredefinedConditions.CommonConditions)
        {
            var menuItem = new Button
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(8, 4),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    Children =
                    {
                        new TextBlock { Text = preset.Icon, FontSize = 16 },
                        new TextBlock { Text = preset.Name, FontSize = 12, VerticalAlignment = VerticalAlignment.Center }
                    }
                }
            };

            menuItem.Click += (_, _) =>
            {
                editor.AddConditionCommand.Execute(preset);
                flyout.Hide();
            };

            stackPanel.Children.Add(menuItem);
        }

        scrollViewer.Content = stackPanel;
        flyout.Content = scrollViewer;
        flyout.ShowAt(button);
    }
}
