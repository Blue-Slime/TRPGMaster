using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace MapEngine.Avalonia.Layout;

/// <summary>
/// 统一管理三个抽屉开关时所有浮动元素的推移。
///
/// 死循环防护：
///   - _isSyncing 标志：Sync() 执行期间屏蔽所有来自 BoundsProperty 的再入回调
///   - OnBottomDrawerBoundsChanged 只监听 BottomDrawer 的高度真正稳定后的第一次变化，
///     不在 Sync() 内部写 Margin 的过程中重新触发
///   - SetMargin / SetMarginLeftOnly 在值未变时跳过写入，彻底切断写→触发→写的链条
/// </summary>
public sealed class DrawerLayoutBehavior : IDisposable
{
    private readonly UserControl _root;
    private readonly ViewModels.MainWindowViewModel _vm;
    private bool _disposed;
    private bool _isSyncing;           // 防止 Sync() 内部触发的 Bounds 变化再次入队
    private bool _syncPending;         // 防止同一帧多次入队
    private double _lastBottomH = -1;  // 记录上次 BottomDrawer 高度，真正变化时才重算

    public DrawerLayoutBehavior(UserControl root, ViewModels.MainWindowViewModel vm)
    {
        _root = root;
        _vm   = vm;
        _vm.PropertyChanged += OnVmPropertyChanged;

        // 仅监听 BottomDrawer 高度稳定后（动画结束）的尺寸变化
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
            ScheduleSync();
        }
    }

    private void OnBottomDrawerBoundsChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        // 过滤条件：
        // 1. 只关心 BoundsProperty
        // 2. Sync() 正在执行时不重入
        // 3. BottomDrawer 高度没有真正变化时跳过
        if (e.Property != Visual.BoundsProperty) return;
        if (_isSyncing) return;

        var newH = _root.FindControl<Border>("BottomDrawer")?.Bounds.Height ?? 0;
        if (Math.Abs(newH - _lastBottomH) < 0.5) return;  // 高度变化 < 0.5px 忽略

        ScheduleSync();
    }

    /// <summary>防抖：同一帧只入队一次 Sync。</summary>
    private void ScheduleSync()
    {
        if (_syncPending || _disposed) return;
        _syncPending = true;
        Dispatcher.UIThread.Post(() =>
        {
            _syncPending = false;
            Sync();
        }, DispatcherPriority.Loaded);
    }

    /// <summary>重新计算并应用所有元素的布局。</summary>
    public void Sync()
    {
        if (_disposed) return;
        _isSyncing = true;
        try
        {
            bool left   = _vm.IsLeftDrawerOpen;
            bool right  = _vm.IsRightDrawerOpen;
            bool bottom = _vm.IsBottomDrawerOpen;

            // ── 读取实际尺寸（Bounds 未测量时回落到 Constants）──────────
            double leftW   = ActualWidth("LeftDrawer",    LayoutConstants.LeftDrawerWidth);
            double rightW  = ActualWidth("RightDrawer",   LayoutConstants.RightDrawerWidth);
            double bottomH = ActualHeight("BottomDrawer", LayoutConstants.BottomDrawerHeight);
            _lastBottomH   = bottomH;

            // ── 计算底部空间占用 ─────────────────────────────────────────
            // 1. BottomDrawer 打开时占用的空间
            double bottomDrawerSpace = bottom ? bottomH + LayoutConstants.EdgeMargin : 0.0;

            // 2. ActionCapsule 底边距：BottomDrawer 关闭时贴底 EdgeMargin，打开时上移
            double actionBottom = LayoutConstants.EdgeMargin + bottomDrawerSpace;

            // 3. 读取 ActionCapsule 实际高度（动态，万一用户以后改了胶囊大小）
            double actionH = ActualHeight("ActionCapsule", LayoutConstants.ActionCapsuleHeight);

            // 4. 左右抽屉和竖向工具条的底边距：给 ActionCapsule + 边距 + BottomDrawer 让位
            double verticalBottom = LayoutConstants.EdgeMargin + actionH + LayoutConstants.EdgeMargin + bottomDrawerSpace;

            // 5. StatusBar / Viewport 的底边距：紧贴 ActionCapsule 上边缘 + 间距，跟随 ActionCapsule 移动
            double floatingBottom = actionBottom + actionH + LayoutConstants.EdgeMargin;

            // ── open/close class（保留 XAML 动画）───────────────────────
            SyncClass("LeftDrawer",   "open", left);
            SyncClass("RightDrawer",  "open", right);
            SyncClass("BottomDrawer", "open", bottom);

            // ── ActionCapsule：BottomDrawer 打开时向上推 ─────────────────
            SetMargin("ActionCapsule", LayoutConstants.EdgeMargin, 0, 0, actionBottom);

            // ── 左抽屉 ──────────────────────────────────────────────────
            SetMargin("LeftDrawer",  0, LayoutConstants.EdgeMargin, 0, verticalBottom);

            // ── 右抽屉 ──────────────────────────────────────────────────
            SetMargin("RightDrawer", 0, LayoutConstants.EdgeMargin, LayoutConstants.EdgeMargin, verticalBottom);

            // ── ToolbarCapsule：随 LeftDrawer 右移 ───────────────────────
            double toolbarLeft = LayoutConstants.EdgeMargin + (left ? leftW + LayoutConstants.CapsuleGap : 0);
            SetMargin("ToolbarCapsule", toolbarLeft, LayoutConstants.EdgeMargin, 0, verticalBottom);

            // ── 子工具胶囊：Margin.Left 跟随 ToolbarCapsule 右边缘 ───────
            double subLeft = toolbarLeft + LayoutConstants.ToolbarCapsuleWidth + LayoutConstants.CapsuleGap;
            SetMarginLeftOnly("ShapeSubToolCapsule", subLeft);
            SetMarginLeftOnly("FogSubToolCapsule",   subLeft);

            // ── PanelToggleCapsule：随 RightDrawer 左移 ──────────────────
            double panelRight = LayoutConstants.EdgeMargin + (right ? rightW + LayoutConstants.CapsuleGap : 0);
            SetMargin("PanelToggleCapsule", 0, LayoutConstants.EdgeMargin, panelRight, verticalBottom);

            // ── StatusBarCapsule：随 LeftDrawer 右移 + ActionCapsule 上移 ──
            double statusLeft = LayoutConstants.EdgeMargin + actionH + LayoutConstants.EdgeMargin + (left ? leftW + LayoutConstants.CapsuleGap : 0);
            SetMargin("StatusBarCapsule", statusLeft, 0, 0, floatingBottom);

            // ── ViewportCapsule：随 RightDrawer 左移 + ActionCapsule 上移 ──
            double vpRight = LayoutConstants.EdgeMargin + (right ? rightW + LayoutConstants.CapsuleGap : 0);
            SetMargin("ViewportCapsule", 0, 0, vpRight, floatingBottom);
        }
        finally
        {
            _isSyncing = false;
        }
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

    /// <summary>值未变时跳过写入，避免触发不必要的 Bounds/Margin 变化事件。</summary>
    private void SetMargin(string name, double left, double top, double right, double bottom)
    {
        if (_root.FindControl<Control>(name) is not { } ctrl) return;
        var want = new Thickness(left, top, right, bottom);
        if (ctrl.Margin == want) return;
        ctrl.Margin = want;
    }

    /// <summary>只更新 Margin.Left，保留 Top/Right/Bottom（SubToolLayout 管 Top）。</summary>
    private void SetMarginLeftOnly(string name, double left)
    {
        if (_root.FindControl<Control>(name) is not { } ctrl) return;
        var m = ctrl.Margin;
        if (Math.Abs(m.Left - left) < 0.5) return;
        ctrl.Margin = new Thickness(left, m.Top, m.Right, m.Bottom);
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
