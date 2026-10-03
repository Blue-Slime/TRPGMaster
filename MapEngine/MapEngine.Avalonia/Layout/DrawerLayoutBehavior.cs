using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace MapEngine.Avalonia.Layout;

/// <summary>
/// 统一管理三个抽屉开关时所有浮动元素的推移。
/// 抽屉使用 RenderTransform 动画（不改变布局 Bounds），推移计算基于 ViewModel bool 属性 + 常量尺寸。
/// </summary>
public sealed class DrawerLayoutBehavior : IDisposable
{
    private readonly UserControl _root;
    private readonly ViewModels.MainWindowViewModel _vm;
    private bool _disposed;
    private bool _syncPending;  // 防止同一帧多次入队

    public DrawerLayoutBehavior(UserControl root, ViewModels.MainWindowViewModel vm)
    {
        _root = root;
        _vm   = vm;
        _vm.PropertyChanged += OnVmPropertyChanged;
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

        bool left   = _vm.IsLeftDrawerOpen;
        bool right  = _vm.IsRightDrawerOpen;
        bool bottom = _vm.IsBottomDrawerOpen;

        // ── 使用常量尺寸（抽屉宽高固定，动画是 RenderTransform 不改变 Bounds）──
        double leftW   = LayoutConstants.LeftDrawerWidth;
        double rightW  = LayoutConstants.RightDrawerWidth;
        double bottomH = LayoutConstants.BottomDrawerHeight;

        // ── 计算底部空间占用 ─────────────────────────────────────────
        double bottomDrawerSpace = bottom ? bottomH + LayoutConstants.EdgeMargin : 0.0;
        double verticalBottom = LayoutConstants.EdgeMargin + bottomDrawerSpace;
        double actionBottom = LayoutConstants.EdgeMargin + bottomDrawerSpace;

        // ActionCapsule 宽度用实际测量（胶囊内容是动态的）
        double actionW = ActualWidth("ActionCapsule", LayoutConstants.ActionCapsuleHeight);
        double floatingBottom = actionBottom;

        // ── open/close class（触发 XAML RenderTransform 动画）───────
        SyncClass("LeftDrawer",   "open", left);
        SyncClass("RightDrawer",  "open", right);
        SyncClass("BottomDrawer", "open", bottom);

        // ── ActionCapsule：随 LeftDrawer 右移 + BottomDrawer 上推 ────
        double actionLeft = LayoutConstants.EdgeMargin + (left ? leftW + LayoutConstants.CapsuleGap : 0);
        SetMargin("ActionCapsule", actionLeft, 0, 0, actionBottom);

        // ── 左右抽屉：底边跟随 BottomDrawer ───────────────────────────
        SetMargin("LeftDrawer",  0, LayoutConstants.EdgeMargin, 0, verticalBottom);
        SetMargin("RightDrawer", 0, LayoutConstants.EdgeMargin, LayoutConstants.EdgeMargin, verticalBottom);

        // ── ToolbarCapsule：随 LeftDrawer 右移 ───────────────────────
        double toolbarLeft = LayoutConstants.EdgeMargin + (left ? leftW + LayoutConstants.CapsuleGap : 0);
        SetMargin("ToolbarCapsule", toolbarLeft, LayoutConstants.EdgeMargin, 0, verticalBottom);

        // ── CharacterSelectorCapsule：紧跟 ToolbarCapsule 右侧（仅游玩模式显示）───
        double charSelectorLeft = toolbarLeft + LayoutConstants.ToolbarCapsuleWidth + LayoutConstants.CapsuleGap;
        SetMargin("CharacterSelectorCapsule", charSelectorLeft, LayoutConstants.EdgeMargin, 0, 0);

        // ── 子工具胶囊：Margin.Left 跟随 ToolbarCapsule 右边缘 ───────
        double subLeft = toolbarLeft + LayoutConstants.ToolbarCapsuleWidth + LayoutConstants.CapsuleGap;
        SetMarginLeftOnly("ShapeSubToolCapsule", subLeft);
        SetMarginLeftOnly("FogSubToolCapsule",   subLeft);

        // ── PanelToggleCapsule + DicePanel：随 RightDrawer 左移 ─────
        double panelRight = LayoutConstants.EdgeMargin + (right ? rightW + LayoutConstants.CapsuleGap : 0);
        SetMargin("PanelToggleCapsule", 0, LayoutConstants.EdgeMargin, panelRight, verticalBottom);
        SetMarginRightOnly("DicePanel", panelRight);

        // ── StatusBarCapsule：ActionCapsule 右侧，底边对齐 ───────────
        double statusLeft = actionLeft + actionW + LayoutConstants.CapsuleGap;
        SetMargin("StatusBarCapsule", statusLeft, 0, 0, floatingBottom);

        // ── ViewportCapsule：右侧，底边对齐 ActionCapsule ────────────
        double vpRight = LayoutConstants.EdgeMargin + (right ? rightW + LayoutConstants.CapsuleGap : 0);
        SetMargin("ViewportCapsule", 0, 0, vpRight, floatingBottom);
    }

    // ── 工具方法 ─────────────────────────────────────────────────────

    private double ActualWidth(string name, double fallback)
    {
        var b = _root.FindControl<Control>(name)?.Bounds.Width ?? 0;
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

    /// <summary>只更新 Margin.Right，保留 Left/Top/Bottom。</summary>
    private void SetMarginRightOnly(string name, double right)
    {
        if (_root.FindControl<Control>(name) is not { } ctrl) return;
        var m = ctrl.Margin;
        if (Math.Abs(m.Right - right) < 0.5) return;
        ctrl.Margin = new Thickness(m.Left, m.Top, right, m.Bottom);
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
    }
}
