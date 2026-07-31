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

/// <summary>地图视口渲染：装饰层、瓦片、网格线、可渲染对象收集、快照与 ID 工具。</summary>
public partial class MainWindowViewModel
{
    private void RefreshMapViewportDecorations()
    {
        var viewportWidthInContent = _lastMapViewportWidth > 0 ? _lastMapViewportWidth : 1600;
        var viewportHeightInContent = _lastMapViewportHeight > 0 ? _lastMapViewportHeight : 900;
        var contentLeft = _lastMapOffsetX;
        var contentTop = _lastMapOffsetY;
        var contentRight = contentLeft + viewportWidthInContent;
        var contentBottom = contentTop + viewportHeightInContent;

        _mapViewportCenterX = ContentToWorldX((contentLeft + contentRight) / 2.0);
        _mapViewportCenterY = ContentToWorldY((contentTop + contentBottom) / 2.0);
        OnPropertyChanged(nameof(MapViewportSummary));

        RefreshMapTiles(contentLeft, contentTop, contentRight, contentBottom);
        RefreshMapGridLines(contentLeft, contentTop, contentRight, contentBottom);
    }

    private void RefreshMapTiles(double contentLeft, double contentTop, double contentRight, double contentBottom)
    {
        var zoom = ZoomScale <= 0 ? 1.0 : ZoomScale;
        var tilePx = MapViewportConstants.TileSize * zoom;

        // 缩放极小时（每个 tile < 24px）背景棋盘格意义不大，跳过节省性能
        if (tilePx < 24.0)
        {
            MapPreloadedTiles.Clear();
            return;
        }

        var preloadMargin = MapViewportConstants.TileSize;
        var startX = (int)Math.Floor((contentLeft - preloadMargin) / MapViewportConstants.TileSize);
        var endX   = (int)Math.Ceiling((contentRight  + preloadMargin) / MapViewportConstants.TileSize);
        var startY = (int)Math.Floor((contentTop  - preloadMargin) / MapViewportConstants.TileSize);
        var endY   = (int)Math.Ceiling((contentBottom + preloadMargin) / MapViewportConstants.TileSize);
        var originTileIndex = (int)(MapViewportConstants.WorldOriginContent / MapViewportConstants.TileSize);

        // 硬上限：最多 900 个 tile（30×30），防止极大缩放时内存爆炸
        const int maxTilesPerAxis = 30;
        if ((endX - startX) > maxTilesPerAxis) endX = startX + maxTilesPerAxis;
        if ((endY - startY) > maxTilesPerAxis) endY = startY + maxTilesPerAxis;

        MapPreloadedTiles.Clear();
        for (var tileY = startY; tileY <= endY; tileY++)
        {
            for (var tileX = startX; tileX <= endX; tileX++)
            {
                var worldTileX = tileX - originTileIndex;
                var worldTileY = originTileIndex - tileY - 1;
                var isAxisTile = worldTileX == 0 || worldTileY == 0;
                var isEven     = ((worldTileX + worldTileY) & 1) == 0;

                MapPreloadedTiles.Add(new MapTileViewModel(
                    tileX * MapViewportConstants.TileSize,
                    tileY * MapViewportConstants.TileSize,
                    MapViewportConstants.TileSize,
                    isAxisTile ? "#21262E" : isEven ? "#1B1D22" : "#181A1F",
                    isAxisTile ? "#384656" : "#232833",
                    $"{worldTileX:+#;-#;0}, {worldTileY:+#;-#;0}"));
            }
        }
    }

    private void RefreshMapGridLines(double contentLeft, double contentTop, double contentRight, double contentBottom)
    {
        MapGridLines.Clear();
        if (!ShowGrid) return;

        var zoom = ZoomScale <= 0 ? 1.0 : ZoomScale;
        var cellPx = MapViewportConstants.CellSize * zoom; // 一个格在屏幕上的像素数

        // LOD：选择最小的 step 使 step 格的屏幕宽度 >= 8px，避免线密到不可辨
        var step = 1;
        foreach (var candidate in new[] { 1, 2, 5, 10, 25, 50, 100, 250, 500, 1000 })
        {
            if (candidate * cellPx >= 8.0) { step = candidate; break; }
        }
        // 低密度时（一格>512px）回到每格画一条
        if (cellPx >= 512) step = 1;

        // 线宽补偿：屏幕上恒为 1px/2px（内容坐标系里的等效宽度）
        var thinW  = 1.0 / zoom;
        var thickW = 2.0 / zoom;

        var margin = MapViewportConstants.CellSize * step * 2;
        var expandedLeft   = Math.Max(0, contentLeft  - margin);
        var expandedTop    = Math.Max(0, contentTop   - margin);
        var expandedRight  = Math.Min(MapContentSize, contentRight  + margin);
        var expandedBottom = Math.Min(MapContentSize, contentBottom + margin);

        var originIdx = (int)(MapViewportConstants.WorldOriginContent / MapViewportConstants.CellSize);
        var startCol  = (int)Math.Floor(expandedLeft  / MapViewportConstants.CellSize / step) * step;
        var endCol    = (int)Math.Ceiling(expandedRight / MapViewportConstants.CellSize / step) * step;
        var startRow  = (int)Math.Floor(expandedTop    / MapViewportConstants.CellSize / step) * step;
        var endRow    = (int)Math.Ceiling(expandedBottom / MapViewportConstants.CellSize / step) * step;

        // 硬上限：每轴最多 600 条，防止极端缩放时卡顿
        const int maxLines = 600;
        var colStep = Math.Max(step, (endCol - startCol) / maxLines / step * step);
        if (colStep < step) colStep = step;
        var rowStep = Math.Max(step, (endRow - startRow) / maxLines / step * step);
        if (rowStep < step) rowStep = step;

        for (var col = startCol; col <= endCol; col += colStep)
        {
            var x      = col * MapViewportConstants.CellSize;
            var isAxis = col == originIdx;
            var isMajor = !isAxis && step <= 5 && Math.Abs(col - originIdx) % (step * 5) == 0;
            MapGridLines.Add(new MapGuideLineViewModel(
                x, expandedTop,
                isAxis ? thickW : thinW,
                expandedBottom - expandedTop,
                isAxis ? "#7087A6" : isMajor ? "#3D4B61" : "#293140"));
        }

        for (var row = startRow; row <= endRow; row += rowStep)
        {
            var y      = row * MapViewportConstants.CellSize;
            var isAxis = row == originIdx;
            var isMajor = !isAxis && step <= 5 && Math.Abs(row - originIdx) % (step * 5) == 0;
            MapGridLines.Add(new MapGuideLineViewModel(
                expandedLeft, y,
                expandedRight - expandedLeft,
                isAxis ? thickW : thinW,
                isAxis ? "#7087A6" : isMajor ? "#3D4B61" : "#293140"));
        }
    }

    private static double ContentToWorldX(double contentX)
        => contentX - MapViewportConstants.WorldOriginContent;

    private static double ContentToWorldY(double contentY)
        => MapViewportConstants.WorldOriginContent - contentY;

    private static string FormatWorldCoordinate(double value)
        => $"{Math.Round(value):+#;-#;0}";

    private void RefreshMapRenderableItems()
    {
        MapRenderableItems.Clear();
        foreach (var root in HierarchyRoots)
        {
            AddMapRenderableItems(root);
        }
    }

    private void AddMapRenderableItems(HierarchyItemViewModel item)
    {
        MapRenderableItems.Add(item);

        for (var i = item.Children.Count - 1; i >= 0; i--)
        {
            AddMapRenderableItems(item.Children[i]);
        }
    }

    private void RemoveHierarchyItem(HierarchyItemViewModel item, HierarchyItemViewModel? parent, bool updateSelection)
    {
        if (parent is null)
        {
            return;
        }

        RemoveHierarchyIndex(item);
        parent.Children.Remove(item);

        if (updateSelection && SelectedHierarchyItem is not null && IsHierarchySelfOrDescendant(item, SelectedHierarchyItem))
        {
            SelectedHierarchyItem = parent;
        }
    }

    private static bool IsHierarchySelfOrDescendant(HierarchyItemViewModel ancestor, HierarchyItemViewModel candidate)
    {
        var current = candidate;
        while (current is not null)
        {
            if (ReferenceEquals(current, ancestor))
            {
                return true;
            }

            current = current.Parent!;
        }

        return false;
    }

    private AssetFolderSnapshot SnapshotAssetFolder(AssetFolderViewModel folder)
    {
        return new AssetFolderSnapshot
        {
            Name = folder.Name,
            Children = folder.Children.Select(SnapshotAssetFolder).ToList(),
            Items = _allAssetItems
                .Where(item => item.FolderId == folder.Id)
                .Select(SnapshotAssetItem)
                .ToList()
        };
    }

    private static AssetItemSnapshot SnapshotAssetItem(AssetItemViewModel item)
    {
        return new AssetItemSnapshot
        {
            Name = item.Name,
            Kind = item.Kind,
            Icon = item.Icon,
            Description = item.Description
        };
    }

    private AssetFolderViewModel CreateFolderFromSnapshot(AssetFolderSnapshot snapshot, AssetFolderViewModel parent, bool useUniqueName)
    {
        var folderName = useUniqueName ? GetUniqueFolderName(parent, snapshot.Name) : snapshot.Name;
        var folderPath = Path.Combine(parent.FullPath, folderName);
        var folder = new AssetFolderViewModel(
            CreateId("folder"),
            folderName,
            folderPath,
            parent);

        parent.Children.Add(folder);
        _assetFolderIndex[folder.Id] = folder;

        foreach (var itemSnapshot in snapshot.Items)
        {
            CreateItemFromSnapshot(itemSnapshot, folder, true);
        }

        foreach (var childSnapshot in snapshot.Children)
        {
            CreateFolderFromSnapshot(childSnapshot, folder, true);
        }

        RefreshVisibleAssets();
        return folder;
    }

    private AssetItemViewModel CreateItemFromSnapshot(AssetItemSnapshot snapshot, AssetFolderViewModel folder, bool useUniqueName)
    {
        var fileName = useUniqueName ? GetUniqueAssetItemName(folder.Id, snapshot.Name) : snapshot.Name;
        var item = new AssetItemViewModel(
            CreateId("asset"),
            folder.Id,
            Path.Combine(folder.FullPath, fileName),
            fileName,
            fileName,
            snapshot.Kind,
            snapshot.Icon,
            snapshot.Description);

        _allAssetItems.Add(item);
        return item;
    }

    private static string CreateId(string prefix)
        => $"{prefix}-{Guid.NewGuid():N}";

    private static string NormalizePath(string path)
        => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar).ToUpperInvariant();
}
