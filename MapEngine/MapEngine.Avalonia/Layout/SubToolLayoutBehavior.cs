using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace MapEngine.Avalonia.Layout;

/// <summary>
/// 子工具面板垂直对齐到触发按钮的中心线。
///
/// 死循环防护：
///   - 胶囊不可见（IsVisible=false）时直接跳出，不重试
///   - ItemsControl 未就绪时最多重试 MaxRetries 次
///   - 只在 Margin.Top 实际需要变化时才写入
/// </summary>
public static class SubToolLayout
{
    private const int MaxRetries = 8;

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

        Dispatcher.UIThread.Post(
            () => AlignToTriggerButton(capsule, key, 0),
            DispatcherPriority.Loaded);
    }

    /// <summary>
    /// 公开入口：外部可手动触发重新对齐（ViewModel.ActivePrimaryTools 变化时）。
    /// </summary>
    public static void AlignToTriggerButton(Control capsule, string triggerKey)
        => AlignToTriggerButton(capsule, triggerKey, 0);

    private static void AlignToTriggerButton(Control capsule, string triggerKey, int attempt)
    {
        // ── 防护1：胶囊不可见时不操作，不重试 ──────────────────────────
        if (!capsule.IsVisible) return;

        // ── 防护2：重试超限时放弃 ────────────────────────────────────────
        if (attempt >= MaxRetries) return;

        // ── 找根 UserControl ─────────────────────────────────────────────
        var root = capsule.FindAncestorOfType<UserControl>();
        if (root is null) return;

        var toolsList = root.FindControl<ItemsControl>("PrimaryToolsList");
        if (toolsList is null) return;

        // ── ItemsControl 未完成 Arrange：延迟重试 ─────────────────────────
        if (!toolsList.IsArrangeValid || toolsList.ItemsPanelRoot is not Panel panel || panel.Children.Count == 0)
        {
            Retry(capsule, triggerKey, attempt);
            return;
        }

        // ── 找 Key 匹配的容器 ─────────────────────────────────────────────
        Control? targetContainer = null;
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

        if (targetContainer is null || targetContainer.Bounds.Height <= 0)
        {
            Retry(capsule, triggerKey, attempt);
            return;
        }

        // ── 胶囊高度还未测量：延迟重试 ──────────────────────────────────
        if (capsule.Bounds.Height <= 0)
        {
            Retry(capsule, triggerKey, attempt);
            return;
        }

        // ── 计算目标 Margin.Top ──────────────────────────────────────────
        var btnCenterLocal  = new Point(0, targetContainer.Bounds.Height / 2.0);
        var btnCenterGlobal = ((Visual)targetContainer).TranslatePoint(btnCenterLocal, root);
        if (btnCenterGlobal is not { } center) return;

        var newTop = center.Y - capsule.Bounds.Height / 2.0;

        // ── 只在值真正变化时写入，避免触发多余的布局事件 ───────────────
        var m = capsule.Margin;
        if (Math.Abs(m.Top - newTop) < 0.5) return;
        capsule.Margin = new Thickness(m.Left, newTop, m.Right, m.Bottom);
    }

    private static void Retry(Control capsule, string key, int attempt)
    {
        Dispatcher.UIThread.Post(
            () => AlignToTriggerButton(capsule, key, attempt + 1),
            DispatcherPriority.Loaded);
    }
}
