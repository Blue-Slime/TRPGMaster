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
/// 地图文本覆盖层管理器（GL 层之上的 Avalonia 文本）。
///
/// 为什么不在 GL 层画字：Silk 渲染管线没有字体栈（无 glyph atlas / shaping），
/// 自己实现代价远高于收益。文本量小、不需要逐帧几何变换，
/// 用 Avalonia TextBlock 贴在 Canvas 上即可，缩放时按 zoom 折算字号。
///
/// 字典 diff 复用控件，只增删变化的条目。
/// </summary>
public sealed class MapTextManager
{
    public delegate (Point Center, double Zoom) CameraProvider();

    private sealed record Entry(Border Host, TextBlock Label);

    private readonly Canvas _overlay;
    private readonly MainWindowViewModel _viewModel;
    private readonly CameraProvider _cameraProvider;

    private readonly Dictionary<string, Entry> _entries = new();
    private readonly List<HierarchyItemViewModel> _watched = new();
    private readonly HashSet<string> _aliveScratch = new();

    public MapTextManager(Canvas overlay, MainWindowViewModel viewModel, CameraProvider cameraProvider)
    {
        _overlay        = overlay        ?? throw new ArgumentNullException(nameof(overlay));
        _viewModel      = viewModel      ?? throw new ArgumentNullException(nameof(viewModel));
        _cameraProvider = cameraProvider ?? throw new ArgumentNullException(nameof(cameraProvider));

        _overlay.PropertyChanged += (_, e) =>
        {
            if (e.Property == Visual.BoundsProperty) Sync();
        };

        _viewModel.MapRenderableItems.CollectionChanged += OnItemsChanged;
        RebuildWatchList();
    }

    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
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
            case nameof(HierarchyItemViewModel.IsActive):
            case nameof(HierarchyItemViewModel.IsSelected):
            case nameof(HierarchyItemViewModel.ShouldRenderOnMap):
            case nameof(HierarchyItemViewModel.TextContent):
            case nameof(HierarchyItemViewModel.TextFontSize):
            case nameof(HierarchyItemViewModel.TextColor):
            case nameof(HierarchyItemViewModel.TextIsBold):
            case nameof(HierarchyItemViewModel.TextIsItalic):
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
        if (viewportSize.Width <= 0 || viewportSize.Height <= 0) return;
        if (zoom <= 0) zoom = 1.0;

        _aliveScratch.Clear();

        foreach (var item in _viewModel.MapRenderableItems)
        {
            if (item.ObjectType != "Text" || !item.IsActive) continue;

            var comp = item.GetComponent<TextComponent>();
            if (comp is null || string.IsNullOrEmpty(comp.Text)) continue;

            // 世界坐标 → content 坐标 → 屏幕坐标
            var contentX = MapViewportConstants.WorldOriginContent + item.X;
            var contentY = MapViewportConstants.WorldOriginContent - item.Y;
            var screenPos = new Point(
                (contentX - cameraCenter.X) * zoom + viewportSize.Width  / 2,
                (contentY - cameraCenter.Y) * zoom + viewportSize.Height / 2);

            if (!IsOnScreen(screenPos, viewportSize)) continue;

            _aliveScratch.Add(item.Id);

            if (!_entries.TryGetValue(item.Id, out var entry))
            {
                entry = BuildHost();
                _entries[item.Id] = entry;
                _overlay.Children.Add(entry.Host);
            }

            UpdateEntry(entry, comp, item.IsSelected, zoom);

            // 锚点居中：控件尺寸由内容决定，用 DesiredSize 折算居中偏移。
            entry.Host.Measure(Size.Infinity);
            var half = entry.Host.DesiredSize;
            Canvas.SetLeft(entry.Host, screenPos.X - half.Width  / 2.0);
            Canvas.SetTop (entry.Host, screenPos.Y - half.Height / 2.0);
        }

        PruneStale();
    }

    private void PruneStale()
    {
        if (_entries.Count == _aliveScratch.Count) return;

        var stale = new List<string>();
        foreach (var kv in _entries)
            if (!_aliveScratch.Contains(kv.Key)) stale.Add(kv.Key);
        foreach (var id in stale)
        {
            _overlay.Children.Remove(_entries[id].Host);
            _entries.Remove(id);
        }
    }

    private static bool IsOnScreen(Point p, Size viewport)
    {
        const double margin = 200;
        return p.X >= -margin && p.X <= viewport.Width  + margin
            && p.Y >= -margin && p.Y <= viewport.Height + margin;
    }

    private static Entry BuildHost()
    {
        var label = new TextBlock
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment   = VerticalAlignment.Center,
            TextWrapping        = TextWrapping.NoWrap,
        };

        var host = new Border
        {
            CornerRadius     = new CornerRadius(4),
            Padding          = new Thickness(6, 3),
            IsHitTestVisible = false,
            Child            = label,
        };

        return new Entry(host, label);
    }

    private static void UpdateEntry(Entry entry, TextComponent comp, bool isSelected, double zoom)
    {
        entry.Label.Text       = comp.Text;
        entry.Label.FontSize   = Math.Max(6, comp.FontSize * zoom);
        entry.Label.FontWeight = comp.IsBold ? FontWeight.Bold : FontWeight.Normal;
        entry.Label.FontStyle  = comp.IsItalic ? FontStyle.Italic : FontStyle.Normal;
        entry.Label.Foreground = SafeBrush(comp.Color, Brushes.White);
        entry.Label.TextAlignment = comp.Align switch
        {
            TextAlign.Left  => TextAlignment.Left,
            TextAlign.Right => TextAlignment.Right,
            _               => TextAlignment.Center,
        };

        entry.Host.Background = SafeBrush(comp.BackgroundColor, Brushes.Transparent);

        // 选中时描一圈蓝边，和 GL 层的选中反馈对齐
        entry.Host.BorderThickness = new Thickness(isSelected ? 2 : 0);
        entry.Host.BorderBrush     = isSelected
            ? new SolidColorBrush(Color.Parse("#1A8CE6"))
            : Brushes.Transparent;
    }

    private static IBrush SafeBrush(string hex, IBrush fallback)
    {
        if (string.IsNullOrWhiteSpace(hex)) return fallback;
        try { return new SolidColorBrush(Color.Parse(hex)); }
        catch (FormatException) { return fallback; }
    }
}
