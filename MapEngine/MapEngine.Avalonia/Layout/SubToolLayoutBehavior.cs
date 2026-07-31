using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace MapEngine.Avalonia.Layout;

/// <summary>
/// 子工具面板垂直对齐到触发按钮的中心线。
///
/// 使用方式（XAML）：
///   <Border x:Name="ShapeSubToolCapsule"
///           layout:SubToolLayout.TriggerKey="shape"
///           ... />
///
/// 原理：
///   - 在 PrimaryToolsList（ItemsControl）中找到 Key == TriggerKey 的 ToolActionViewModel
///   - TranslatePoint(0, height/2) 计算按钮中心相对于 RootGrid 的 Y 坐标
///   - 更新 Capsule.Margin.Top，使其垂直居中对齐到按钮
/// </summary>
public static class SubToolLayout
{
    public static readonly AttachedProperty<string> TriggerKeyProperty =
        AvaloniaProperty.RegisterAttached<Control, string>("TriggerKey", typeof(SubToolLayout), "");

    public static string GetTriggerKey(Control obj) => obj.GetValue(TriggerKeyProperty);
    public static void SetTriggerKey(Control obj, string value) => obj.SetValue(TriggerKeyProperty, value);

    static SubToolLayout()
    {
        TriggerKeyProperty.Changed.AddClassHandler<Control>(OnTriggerKeyChanged);
    }

    private static void OnTriggerKeyChanged(Control capsule, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.NewValue is not string key || string.IsNullOrEmpty(key))
            return;

        // 延迟一帧：等 Visual Tree 构建完成
        Dispatcher.UIThread.Post(() => AlignToTriggerButton(capsule, key), DispatcherPriority.Loaded);
    }

    /// <summary>
    /// 公开静态方法：外部可手动调用重新对齐（比如 ViewModel.ActivePrimaryTools 变化时）。
    /// </summary>
    public static void AlignToTriggerButton(Control capsule, string triggerKey)
    {
        var root = capsule.FindAncestorOfType<UserControl>();
        if (root is null) return;

        var toolsList = root.FindControl<ItemsControl>("PrimaryToolsList");
        if (toolsList is null) return;

        // 等 ItemsControl 实现出容器
        if (!toolsList.IsArrangeValid)
        {
            Dispatcher.UIThread.Post(() => AlignToTriggerButton(capsule, triggerKey), DispatcherPriority.Loaded);
            return;
        }

        var panel = toolsList.ItemsPanelRoot as Panel;
        if (panel is null || panel.Children.Count == 0)
        {
            // 还没渲染：再试一次
            Dispatcher.UIThread.Post(() => AlignToTriggerButton(capsule, triggerKey), DispatcherPriority.Loaded);
            return;
        }

        // 找到 Key 匹配的容器
        ContentPresenter? targetContainer = null;
        foreach (var child in panel.Children)
        {
            if (child is ContentPresenter cp &&
                cp.Content is ViewModels.ToolActionViewModel vm &&
                vm.Key == triggerKey)
            {
                targetContainer = cp;
                break;
            }
        }

        if (targetContainer?.Bounds is not { Height: > 0 } btnBounds)
            return;

        // 按钮中心点（相对于 root）
        var btnCenterLocal  = new Point(0, btnBounds.Height / 2.0);
        var btnCenterGlobal = ((Visual)targetContainer).TranslatePoint(btnCenterLocal, root);
        if (btnCenterGlobal is not { } center)
            return;

        // 子工具面板当前高度
        var capsuleBounds = capsule.Bounds;
        if (capsuleBounds.Height <= 0)
        {
            // 还没测量：延迟到下一帧
            Dispatcher.UIThread.Post(() => AlignToTriggerButton(capsule, triggerKey), DispatcherPriority.Loaded);
            return;
        }

        // 让 capsule 垂直居中对齐到按钮中心
        var newTop = center.Y - capsuleBounds.Height / 2.0;

        // 只更新 Margin.Top，保留 Left（由 DrawerLayoutBehavior 管理）
        var m = capsule.Margin;
        capsule.Margin = new Thickness(m.Left, newTop, m.Right, m.Bottom);
    }
}
