using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace MapEngine.Avalonia.Layout;

/// <summary>
/// 统一管理三个抽屉开关时所有浮动元素的推移。
/// 替代 SyncPushClasses 中的硬编码 CSS class + 写死偏移量方案。
///
/// 核心原则：
///   - 所有尺寸从 LayoutConstants 读取（常量）或从控件 Bounds 读取（运行时真实值）
///   - 左右抽屉的底部自适应 BottomDrawer 的顶部位置
///   - 直接写 Margin，配合 XAML 中的 ThicknessTransition 产生平滑动画
///   - XAML 中只保留 open/close 动画 class，不再有 pushed-right / compressed-bottom
/// </summary>
public sealed class DrawerLayoutBehavior : IDisposable
{
    private readonly UserControl _root;
    private readonly ViewModels.MainWindowViewModel _vm;
    private bool _disposed;

    public DrawerLayoutBehavior(UserControl root, ViewModels.MainWindowViewModel vm)
    {
        _root = root;
        _vm   = vm;
        _vm.PropertyChanged += OnVmPropertyChanged;

        // BottomDrawer 尺寸变化时也重新计算（用户可能调整了高度）
        if (_root.FindControl<Border>("BottomDrawer") is { } bd)
            bd.PropertyChanged += OnBottomDrawerBoundsChanged;

        Sync();
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is
            nameof(ViewModels.MainWindowViewModel.IsLeftDrawerOpen) or
            nameof(ViewModels.MainWindowViewModel.IsRightDrawerOpen) or
            nameof(ViewModels.MainWindowViewModel.IsBottomDrawerOpen))
        {
            // 延迟一帧：等抽屉 Bounds 更新后再计算（open class 触发动画，Bounds 稍后变化）
            Dispatcher.UIThread.Post(Sync, DispatcherPriority.Loaded);
        }
    }

    private void OnBottomDrawerBoundsChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Visual.BoundsProperty)
            Dispatcher.UIThread.Post(Sync, DispatcherPriority.Loaded);
    }

    /// <summary>重新计算并应用所有元素的布局。</summary>
    public void Sync()
    {
        if (_disposed) return;

        bool left   = _vm.IsLeftDrawerOpen;
        bool right  = _vm.IsRightDrawerOpen;
        bool bottom = _vm.IsBottomDrawerOpen;

        // ── 读取实际尺寸（Bounds 未测量时回落到 Constants）──────────
        double leftW   = ActualWidth("LeftDrawer",   LayoutConstants.LeftDrawerWidth);
        double rightW  = ActualWidth("RightDrawer",  LayoutConstants.RightDrawerWidth);
        double bottomH = ActualHeight("BottomDrawer", LayoutConstants.BottomDrawerHeight);

        // ── 计算 BottomDrawer 占用的底部空间 ─────────────────────────
        // 自适应关键：底部抽屉打开时，左右抽屉 / 工具条底部 = 抽屉实际高度 + 间距
        double bottomExtra = bottom
            ? bottomH + LayoutConstants.EdgeMargin
            : 0.0;

        // 左右抽屉和竖向工具条的底部：给 ActionCapsule + BottomDrawer 让位
        double verticalBottom = LayoutConstants.BottomSafeMargin + bottomExtra;

        // ── open/close class（保留 XAML 动画）───────────────────────
        SyncClass("LeftDrawer",   "open", left);
        SyncClass("RightDrawer",  "open", right);
        SyncClass("BottomDrawer", "open", bottom);

        // ── 左抽屉 ──────────────────────────────────────────────────
        SetMargin("LeftDrawer",
            left:   0,
            top:    LayoutConstants.EdgeMargin,
            right:  0,
            bottom: verticalBottom);

        // ── 右抽屉 ──────────────────────────────────────────────────
        SetMargin("RightDrawer",
            left:   0,
            top:    LayoutConstants.EdgeMargin,
            right:  LayoutConstants.EdgeMargin,
            bottom: verticalBottom);

        // ── ToolbarCapsule：随 LeftDrawer 右移 ───────────────────────
        double toolbarLeft = LayoutConstants.EdgeMargin + (left ? leftW + LayoutConstants.CapsuleGap : 0);
        SetMargin("ToolbarCapsule",
            left:   toolbarLeft,
            top:    LayoutConstants.EdgeMargin,
            right:  0,
            bottom: verticalBottom);

        // ── 子工具胶囊：Margin.Left 跟随 ToolbarCapsule 右边缘 ───────
        // Margin.Top 由 SubToolLayoutBehavior 单独管理（垂直对齐到触发按钮）
        double subLeft = toolbarLeft + LayoutConstants.ToolbarCapsuleWidth + LayoutConstants.CapsuleGap;
        SetMarginLeftOnly("ShapeSubToolCapsule", subLeft);
        SetMarginLeftOnly("FogSubToolCapsule",   subLeft);

        // ── PanelToggleCapsule：随 RightDrawer 左移 ──────────────────
        double panelRight = LayoutConstants.EdgeMargin + (right ? rightW + LayoutConstants.CapsuleGap : 0);
        SetMargin("PanelToggleCapsule",
            left:   0,
            top:    LayoutConstants.EdgeMargin,
            right:  panelRight,
            bottom: verticalBottom);

        // ── StatusBarCapsule：随 LeftDrawer 右移 + BottomDrawer 上移 ─
        double statusLeft   = LayoutConstants.BottomSafeMargin + (left ? leftW + LayoutConstants.CapsuleGap : 0);
        double statusBottom = LayoutConstants.EdgeMargin + bottomExtra;
        SetMargin("StatusBarCapsule",
            left:   statusLeft,
            top:    0,
            right:  0,
            bottom: statusBottom);

        // ── ViewportCapsule：随 RightDrawer 左移 + BottomDrawer 上移 ─
        double vpRight  = LayoutConstants.EdgeMargin + (right ? rightW + LayoutConstants.CapsuleGap : 0);
        double vpBottom = LayoutConstants.EdgeMargin + bottomExtra;
        SetMargin("ViewportCapsule",
            left:   0,
            top:    0,
            right:  vpRight,
            bottom: vpBottom);
    }

    // ── 工具方法 ─────────────────────────────────────────────────────

    private double ActualWidth(string name, double fallback)
    {
        var b = _root.FindControl<Control>(name)?.Bounds.Width ?? 0;
        return b > 1 ? b : fallback;
    }

    private double ActualHeight(string name, double fallback)
    {
        var b = _root.FindControl<Control>(name)?.Bounds.Height ?? 0;
        return b > 1 ? b : fallback;
    }

    private void SetMargin(string name, double left, double top, double right, double bottom)
    {
        if (_root.FindControl<Control>(name) is { } ctrl)
            ctrl.Margin = new Thickness(left, top, right, bottom);
    }

    /// <summary>只更新 Margin.Left，保留 Top/Right/Bottom（SubToolLayout 管 Top）。</summary>
    private void SetMarginLeftOnly(string name, double left)
    {
        if (_root.FindControl<Control>(name) is { } ctrl)
        {
            var m = ctrl.Margin;
            ctrl.Margin = new Thickness(left, m.Top, m.Right, m.Bottom);
        }
    }

    private void SyncClass(string name, string cls, bool active)
    {
        if (_root.FindControl<Control>(name) is not { } ctrl) return;
        if (active) ctrl.Classes.Add(cls);
        else        ctrl.Classes.Remove(cls);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _vm.PropertyChanged -= OnVmPropertyChanged;
        if (_root.FindControl<Border>("BottomDrawer") is { } bd)
            bd.PropertyChanged -= OnBottomDrawerBoundsChanged;
    }
}
