using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.IO;
using Avalonia;
using Avalonia.Media;
using CommunityToolkit.Mvvm.Input;
using MapEngine.Core;
using MapEngine.Core.Commands;
using MapEngine.Core.Data;
using MapEngine.Core.Components;
using MapEngine.Avalonia.Commands;
using MapEngine.Avalonia.Graphics;
using MapEngine.Avalonia.Services;
using MapEngine.Core.Hosting;

namespace MapEngine.Avalonia.ViewModels;

/// <summary>工具栏与视口交互：工具切换、快捷操作、网格、缩放、标签、导航、素材过滤。</summary>
public partial class MainWindowViewModel
{
    private void SelectTool(string? key)
    {
        key ??= "select";

        var target = PrimaryTools.FirstOrDefault(t =>
            t.Key.Equals(key, StringComparison.OrdinalIgnoreCase));

        if (target is null) return;

        if (target.IsToggle)
        {
            // 开关工具：再次点击取消自身，不影响其他工具的选中状态
            target.IsSelected = !target.IsSelected;
            var state = target.IsSelected ? "已开启" : "已关闭";
            StatusMessage = $"{target.Label}{state}";
        }
        else
        {
            // 单选互斥工具：取消所有其他单选工具，激活自身
            foreach (var tool in PrimaryTools)
            {
                if (!tool.IsToggle)
                    tool.IsSelected = tool.Key.Equals(key, StringComparison.OrdinalIgnoreCase);
            }

            if (!ReferenceEquals(SelectedPrimaryTool, target))
                SetProperty(ref _selectedPrimaryTool, target, nameof(SelectedPrimaryTool));

            StatusMessage = $"当前工具: {target.Label}";
        }

        OnPropertyChanged(nameof(IsShapeToolActive));
        OnPropertyChanged(nameof(IsFogToolActive));
    }

    // ─────────────────────────────────────────────────────────────────────
    // Shape 子工具（与枭熊2对齐：直线 / 常用形状 / 桌游形状 / 锚点多边形）
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>形状工具是否为当前主工具（控制子工具面板显隐）。</summary>
    public bool IsShapeToolActive =>
        string.Equals(SelectedPrimaryTool?.Key, "shape", StringComparison.OrdinalIgnoreCase);

    /// <summary>子工具选项，供工具栏子菜单绑定。</summary>
    public ObservableCollection<ShapeSubToolViewModel> ShapeSubTools { get; } =
    [
        new("line",    "直线",     "📏", "直线（拖拽绘制）"),
        new("rect",    "矩形",     "▭",  "矩形（拖拽绘制）"),
        new("circle",  "圆形",     "⬤",  "圆形（拖拽绘制，等宽高）"),
        new("ellipse", "椭圆",     "⭕", "椭圆（拖拽绘制）"),
        new("cone",    "锥形",     "🔺", "锥形 AOE（从顶点向外拖拽）"),
        new("wedge",   "扇形",     "🍕", "扇形 AOE（从圆心向外拖拽）"),
        new("polygon", "多边形",   "⬟",  "锚点多边形（单击加点，双击闭合，Esc 取消）"),
    ];

    private void SelectShapeSubTool(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return;

        ShapeSubTool = key;
        foreach (var sub in ShapeSubTools)
            sub.IsSelected = sub.Key.Equals(key, StringComparison.OrdinalIgnoreCase);

        var label = ShapeSubTools.FirstOrDefault(s => s.IsSelected)?.Label ?? key;
        StatusMessage = $"形状子工具: {label}";

        // 选中子工具时自动激活形状主工具
        if (!IsShapeToolActive) SelectTool("shape");
    }

    /// <summary>切换当前矢量形状的填充开关（作用于选中对象；无选中时改默认值）。</summary>
    private void ToggleShapeFill()
    {
        var comp = SelectedHierarchyItem?.GetComponent<ShapeComponent>();
        if (comp is null)
        {
            ShapeDefaultFilled = !ShapeDefaultFilled;
            StatusMessage = ShapeDefaultFilled ? "新建形状：填充" : "新建形状：仅描边";
            return;
        }

        comp.IsFilled = !comp.IsFilled;
        RefreshMapRenderableItemsPublic();
        StatusMessage = comp.IsFilled ? "已开启填充" : "已关闭填充";
    }

    /// <summary>循环切换当前矢量形状的线型：实线 → 虚线 → 点线。</summary>
    private void CycleStrokeStyle()
    {
        var comp = SelectedHierarchyItem?.GetComponent<ShapeComponent>();
        if (comp is null)
        {
            ShapeDefaultStrokeStyle = ShapeDefaultStrokeStyle switch
            {
                StrokeStyle.Solid  => StrokeStyle.Dashed,
                StrokeStyle.Dashed => StrokeStyle.Dotted,
                _                  => StrokeStyle.Solid,
            };
            StatusMessage = $"新建形状线型：{StrokeStyleLabel(ShapeDefaultStrokeStyle)}";
            return;
        }

        comp.StrokeStyle = comp.StrokeStyle switch
        {
            StrokeStyle.Solid  => StrokeStyle.Dashed,
            StrokeStyle.Dashed => StrokeStyle.Dotted,
            _                  => StrokeStyle.Solid,
        };
        RefreshMapRenderableItemsPublic();
        StatusMessage = $"线型：{StrokeStyleLabel(comp.StrokeStyle)}";
    }

    private static string StrokeStyleLabel(StrokeStyle style) => style switch
    {
        StrokeStyle.Dashed => "虚线",
        StrokeStyle.Dotted => "点线",
        _                  => "实线",
    };

    private void ActivateQuickAction(string? key)
    {
        foreach (var action in QuickActions)
        {
            action.IsSelected = action.Key.Equals(key, StringComparison.OrdinalIgnoreCase);
        }

        var active = QuickActions.FirstOrDefault(action => action.IsSelected);
        if (active is not null)
        {
            StatusMessage = $"已激活 {active.Label}";
        }
    }

    private void ToggleGrid()
    {
        ShowGrid = !ShowGrid;
        StatusMessage = ShowGrid ? "已显示网格参考线" : "已隐藏网格参考线";
    }

    private void ToggleSnap()
    {
        IsSnapToGrid = !IsSnapToGrid;
        StatusMessage = IsSnapToGrid ? "格子吸附已开启" : "格子吸附已关闭";
    }

    /// <summary>几何缩放，范围钳制在 [0.02, 64.0]（2% ~ 6400%）。</summary>
    public void SetZoomScale(double scale)
    {
        ZoomScale = Math.Clamp(scale, 0.02, 64.0);
    }

    /// <summary>滚轮/键盘触发的缩放（保持视口中心锚点，由 code-behind 在 PreserveViewportCenterOnZoom 里处理）。</summary>
    public void SetZoomFromInteraction(double scale)
        => SetZoomScale(scale);

    /// <summary>兼容旧存档：从整数百分比设置缩放。</summary>
    public void SetZoomFromPercent(int percent)
        => SetZoomScale(percent / 100.0);

    private void RequestViewReset()
    {
        SetZoomScale(1.0);
        ViewResetRequested?.Invoke(this, EventArgs.Empty);
    }

    public void UpdateMapViewport(double horizontalOffset, double verticalOffset, double viewportWidth, double viewportHeight)
    {
        _lastMapOffsetX = horizontalOffset;
        _lastMapOffsetY = verticalOffset;
        _lastMapViewportWidth = viewportWidth;
        _lastMapViewportHeight = viewportHeight;
        RefreshMapViewportDecorations();
    }

    private void CycleScale()
    {
        FeetPerCell = FeetPerCell switch
        {
            5  => 10,
            10 => 15,
            15 => 20,
            20 => 30,
            30 => 5,
            _  => 5
        };
        StatusMessage = $"地图比例尺已切换为 1 格 = {ScaleText}";
    }

    private void AddTag()
    {
        if (SelectedHierarchyItem is null) return;

        var tag = NewTagText.Trim();
        if (string.IsNullOrWhiteSpace(tag))
            tag = $"NewTag{SelectedHierarchyItem.Tags.Count + 1}";

        if (!SelectedHierarchyItem.Tags.Contains(tag))
        {
            SelectedHierarchyItem.Tags.Add(tag);
            StatusMessage = $"已为 {SelectedHierarchyItem.Name} 添加标签 {tag}";
        }
        NewTagText = string.Empty;
    }

    private void RemoveTag(string? tag)
    {
        if (SelectedHierarchyItem is null || string.IsNullOrWhiteSpace(tag))
        {
            return;
        }

        if (SelectedHierarchyItem.Tags.Remove(tag))
        {
            StatusMessage = $"已移除标签 {tag}";
        }
    }

    private void NavigateToSelection()
    {
        switch (CurrentSelection)
        {
            case null:
                StatusMessage = "当前没有可导航的选中对象";
                break;
            case AssetItemViewModel asset:
                NavigationRequested?.Invoke(this, new SelectionNavigationRequestEventArgs(SelectionNavigationTarget.AssetLibrary, asset));
                StatusMessage = $"正在导航到素材 {asset.Name}";
                break;
            case HierarchyItemViewModel item when item.CanNavigateToMap:
                NavigationRequested?.Invoke(this, new SelectionNavigationRequestEventArgs(SelectionNavigationTarget.Map, item));
                StatusMessage = $"正在导航到地图对象 {item.DisplayName}";
                break;
            case HierarchyItemViewModel item:
                StatusMessage = $"当前地图对象 {item.DisplayName} 没有可定位坐标";
                break;
        }
    }

    private void RefreshVisibleAssets()
    {
        var search = AssetSearchText.Trim();
        var hasSearch = !string.IsNullOrWhiteSpace(search);

        IEnumerable<AssetItemViewModel> items;
        if (hasSearch)
        {
            // 搜索模式：跨所有层级匹配，与 Windows 搜索框行为一致
            var scopeIds = GetSelectedFolderIds(recursive: true);
            items = _allAssetItems
                .Where(item => scopeIds.Count == 0 || scopeIds.Contains(item.FolderId))
                .Where(item =>
                    item.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
                    || item.Description.Contains(search, StringComparison.OrdinalIgnoreCase)
                    || item.Kind.Contains(search, StringComparison.OrdinalIgnoreCase));
        }
        else
        {
            // 浏览模式：只显示当前文件夹的直属对象（Windows 资源管理器习惯）
            var currentFolderId = SelectedAssetFolder?.Id;
            items = currentFolderId is null
                ? _allAssetItems.Where(item =>
                    AssetRoots.Any(r => string.Equals(r.Id, item.FolderId, StringComparison.OrdinalIgnoreCase)))
                : _allAssetItems.Where(item => string.Equals(item.FolderId, currentFolderId,
                    StringComparison.OrdinalIgnoreCase));
        }

        var sorted = items
            .OrderBy(item => item.Kind)
            .ThenBy(item => item.Name)
            .ToList();

        VisibleAssetItems.Clear();
        foreach (var item in sorted)
        {
            VisibleAssetItems.Add(item);
        }

        if (SelectedAssetItem is not null && !VisibleAssetItems.Contains(SelectedAssetItem))
        {
            SelectedAssetItem = null;
        }
    }

    private HashSet<string> GetSelectedFolderIds(bool recursive = false)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (SelectedAssetFolder is null)
        {
            return result;
        }

        if (recursive)
            CollectFolderIds(SelectedAssetFolder, result);
        else
            result.Add(SelectedAssetFolder.Id);

        return result;
    }

    private static void CollectFolderIds(AssetFolderViewModel folder, HashSet<string> result)
    {
        result.Add(folder.Id);
        foreach (var child in folder.Children)
        {
            CollectFolderIds(child, result);
        }
    }

}
