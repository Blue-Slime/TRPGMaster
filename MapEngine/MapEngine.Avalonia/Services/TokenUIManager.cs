using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using MapEngine.Core.Components;
using MapEngine.Avalonia.ViewModels;

namespace MapEngine.Avalonia.Services;

/// <summary>
/// Token UI 覆盖层管理器（GL 层之上的 Avalonia UI）
///
/// 为何直接用 Border 作为 Canvas 子元素：
/// - Canvas.Left/Top 只对直接子元素生效
/// - ContentControl 的 ClipToBounds 会裁掉 TranslateTransform 偏移后的内容
/// 偏移量折算进 Canvas.SetLeft/Top，彻底避免裁剪。
/// </summary>
public sealed class TokenUIManager
{
    public delegate (Point Center, double Zoom) CameraProvider();

    private sealed record Entry(Border Host, TokenUIViewModel Vm);

    // 标签框宽 96px → 居中偏移 48px；上移量使标签贴在 Token 正上方
    private const double HalfWidth  = 48;
    private const double AboveToken = 56;

    private readonly Canvas _overlay;
    private readonly MainWindowViewModel _viewModel;
    private readonly CameraProvider _cameraProvider;

    private readonly Dictionary<string, Entry> _entries = new();
    private readonly List<HierarchyItemViewModel> _watched = new();
    private readonly HashSet<string> _aliveScratch = new();

    public TokenUIManager(
        Canvas overlay,
        MainWindowViewModel viewModel,
        CameraProvider cameraProvider)
    {
        _overlay       = overlay       ?? throw new ArgumentNullException(nameof(overlay));
        _viewModel     = viewModel     ?? throw new ArgumentNullException(nameof(viewModel));
        _cameraProvider = cameraProvider ?? throw new ArgumentNullException(nameof(cameraProvider));

        _overlay.PropertyChanged += (_, e) =>
        {
            if (e.Property == Visual.BoundsProperty) Sync();
        };

        _viewModel.MapRenderableItems.CollectionChanged += OnMapRenderableItemsChanged;
        RebuildWatchList();
    }

    private void OnMapRenderableItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RebuildWatchList();
        Sync();
    }

    private void RebuildWatchList()
    {
        foreach (var item in _watched)
            item.PropertyChanged -= OnWatchedItemPropertyChanged;
        _watched.Clear();

        foreach (var item in _viewModel.MapRenderableItems)
        {
            item.PropertyChanged += OnWatchedItemPropertyChanged;
            _watched.Add(item);
        }
    }

    private void OnWatchedItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(HierarchyItemViewModel.X):
            case nameof(HierarchyItemViewModel.Y):
            case nameof(HierarchyItemViewModel.MapLeft):
            case nameof(HierarchyItemViewModel.MapTop):
            case nameof(HierarchyItemViewModel.Name):
            case nameof(HierarchyItemViewModel.ScaleX):
            case nameof(HierarchyItemViewModel.ScaleY):
            case nameof(HierarchyItemViewModel.IsActive):
            case nameof(HierarchyItemViewModel.ShouldRenderOnMap):
                Sync();
                break;
        }
    }

    public void Sync()
    {
        var (center, zoom) = _cameraProvider();
        SyncFromViewModel(center, zoom);
    }

    public void SyncFromViewModel(Point cameraCenter, double zoom)
    {
        var viewportSize = new Size(_overlay.Bounds.Width, _overlay.Bounds.Height);
        if (viewportSize.Width <= 0 || viewportSize.Height <= 0)
            return;

        if (zoom <= 0) zoom = 1.0;

        _aliveScratch.Clear();

        foreach (var item in _viewModel.MapRenderableItems)
        {
            if (!item.ShouldRenderOnMap || string.IsNullOrWhiteSpace(item.Name))
                continue;

            // 矢量图形/文本不是 Token，不该挂名字标签和血条
            if (item.ObjectType is "Shape" or "Text")
                continue;

            // 精灵中心（content 坐标）
            var contentX = item.MapLeft + item.SpriteWidth  / 2.0;
            var contentY = item.MapTop  + item.SpriteHeight / 2.0;

            var screenPos = WorldToScreen(contentX, contentY, cameraCenter, zoom, viewportSize);

            if (!IsOnScreen(screenPos, viewportSize))
                continue;

            _aliveScratch.Add(item.Id);

            if (!_entries.TryGetValue(item.Id, out var entry))
            {
                var vm   = new TokenUIViewModel { Id = item.Id };
                var host = BuildTokenHost(vm);
                entry = new Entry(host, vm);
                _entries[item.Id] = entry;
                _overlay.Children.Add(host);
            }

            var tokenComp = item.GetComponent<TokenComponent>();
            UpdateEntry(entry, item.Name,
                tokenComp?.CurrentHP  ?? 100,
                tokenComp?.MaxHP      ?? 100,
                tokenComp?.Conditions ?? []);

            Canvas.SetLeft(entry.Host, screenPos.X - HalfWidth);
            Canvas.SetTop (entry.Host, screenPos.Y - AboveToken);
        }

        // 移除本帧不再存活的条目
        if (_entries.Count != _aliveScratch.Count)
        {
            var stale = new List<string>();
            foreach (var kv in _entries)
                if (!_aliveScratch.Contains(kv.Key)) stale.Add(kv.Key);
            foreach (var id in stale)
            {
                _overlay.Children.Remove(_entries[id].Host);
                _entries.Remove(id);
            }
        }
    }

    private static Point WorldToScreen(
        double contentX, double contentY,
        Point cameraCenter, double zoom,
        Size viewportSize)
    {
        return new Point(
            (contentX - cameraCenter.X) * zoom + viewportSize.Width  / 2,
            (contentY - cameraCenter.Y) * zoom + viewportSize.Height / 2);
    }

    private static bool IsOnScreen(Point screenPos, Size viewportSize)
    {
        const double margin = 100;
        return screenPos.X >= -margin && screenPos.X <= viewportSize.Width  + margin
            && screenPos.Y >= -margin && screenPos.Y <= viewportSize.Height + margin;
    }

    /// <summary>
    /// 直接构建 Border 作为 Canvas 子元素，不使用 ContentControl 包裹。
    /// Border.Tag 存可更新的子控件引用，供 UpdateEntry 就地修改数值。
    /// </summary>
    private static Border BuildTokenHost(TokenUIViewModel vm)
    {
        const double barWidth = 80;

        var hpBarBg = new Border
        {
            Height       = 6,
            CornerRadius = new CornerRadius(3),
            Background   = new SolidColorBrush(Color.FromArgb(128, 80, 0, 0)),
        };

        var hpBarFg = new Border
        {
            Height              = 6,
            CornerRadius        = new CornerRadius(3),
            HorizontalAlignment = HorizontalAlignment.Left,
            Width               = Math.Max(0, Math.Min(barWidth, vm.HPPercent * barWidth)),
            Background          = HpBrush(vm.HPPercent),
        };

        var hpGrid = new Grid { Width = barWidth, Height = 6 };
        hpGrid.Children.Add(hpBarBg);
        hpGrid.Children.Add(hpBarFg);

        var nameText = new TextBlock
        {
            Text                = vm.Name,
            Foreground          = Brushes.White,
            FontSize            = 12,
            FontWeight          = FontWeight.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
        };

        var hpText = new TextBlock
        {
            Text                = $"{vm.CurrentHP}/{vm.MaxHP}",
            Foreground          = new SolidColorBrush(Color.Parse("#B0B0B0")),
            FontSize            = 10,
            HorizontalAlignment = HorizontalAlignment.Center,
        };

        // 状态徽章栏（最多显示 5 个，超出显示 +N）
        var badgePanel = new WrapPanel
        {
            Orientation         = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
        };

        var stack = new StackPanel { Spacing = 2 };
        stack.Children.Add(nameText);
        stack.Children.Add(hpGrid);
        stack.Children.Add(hpText);
        stack.Children.Add(badgePanel);

        return new Border
        {
            Width            = 96,
            Background       = new SolidColorBrush(Color.FromArgb(224, 0, 0, 0)),
            CornerRadius     = new CornerRadius(4),
            Padding          = new Thickness(8, 4),
            IsHitTestVisible = false,
            Child            = stack,
            Tag              = (nameText, hpBarFg, hpText, badgePanel),
        };
    }

    private static void UpdateEntry(Entry entry, string name, int currentHp, int maxHp,
                                    List<ConditionEntry> conditions)
    {
        const double barWidth = 80;
        if (entry.Host.Tag is not (TextBlock nameText, Border hpBarFg, TextBlock hpText,
                                   WrapPanel badgePanel))
            return;

        var pct = maxHp > 0 ? (double)currentHp / maxHp : 0;
        nameText.Text      = name;
        hpBarFg.Width      = Math.Max(0, Math.Min(barWidth, pct * barWidth));
        hpBarFg.Background = HpBrush(pct);
        hpText.Text        = $"{currentHp}/{maxHp}";

        // 同步状态徽章（最多 5 个 + 溢出标签）
        SyncBadges(badgePanel, conditions);
    }

    private static void SyncBadges(WrapPanel panel, List<ConditionEntry> conditions)
    {
        panel.Children.Clear();
        if (conditions.Count == 0) return;

        const int maxBadges = 5;
        var shown = conditions.Count <= maxBadges ? conditions.Count : maxBadges;

        for (var i = 0; i < shown; i++)
        {
            var c = conditions[i];
            // 解析 ColorHex → 背景色
            if (!Color.TryParse(c.ColorHex, out var bg))
                bg = Color.Parse("#10B981");

            var badge = new Border
            {
                Margin           = new Thickness(1),
                Padding          = new Thickness(3, 1),
                CornerRadius     = new CornerRadius(8),
                Background       = new SolidColorBrush(Color.FromArgb(200, bg.R, bg.G, bg.B)),
                IsHitTestVisible = false,
                Child            = new TextBlock
                {
                    Text     = c.StackCount > 1 ? $"{c.Icon}{c.StackCount}" : c.Icon,
                    FontSize = 10,
                },
            };
            panel.Children.Add(badge);
        }

        // 溢出标签：+N
        if (conditions.Count > maxBadges)
        {
            var overflow = new Border
            {
                Margin           = new Thickness(1),
                Padding          = new Thickness(3, 1),
                CornerRadius     = new CornerRadius(8),
                Background       = new SolidColorBrush(Color.FromArgb(160, 80, 80, 80)),
                IsHitTestVisible = false,
                Child            = new TextBlock
                {
                    Text       = $"+{conditions.Count - maxBadges}",
                    FontSize   = 9,
                    Foreground = Brushes.White,
                },
            };
            panel.Children.Add(overflow);
        }
    }

    private static IBrush HpBrush(double pct) => pct switch
    {
        > 0.5  => new SolidColorBrush(Color.Parse("#44DD44")),
        > 0.25 => new SolidColorBrush(Color.Parse("#DDAA00")),
        _      => new SolidColorBrush(Color.Parse("#DD3333")),
    };
}
