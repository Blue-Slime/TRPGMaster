using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using MapEngine.Avalonia.ViewModels;

namespace MapEngine.Avalonia.Views;

/// <summary>素材库：拖放放置、贴图选择、文件夹/对象类卡片的增删改与剪贴板操作。</summary>
public partial class MapEditorView
{
    private void AssetLibrary_Drop(object? sender, DragEventArgs e)
    {
        if (_viewModel is null) return;

        var targetFolder = _viewModel.SelectedAssetFolder?.FullPath;
        if (string.IsNullOrEmpty(targetFolder))
        {
            targetFolder = System.IO.Path.Combine(AppContext.BaseDirectory, "AssetLibrary", "StaticObjects");
        }

        System.IO.Directory.CreateDirectory(targetFolder);

        var files = e.DataTransfer.TryGetFiles();
        if (files is null || files.Length == 0) return;

        foreach (var storageItem in files)
        {
            var sourcePath = storageItem.Path.LocalPath;
            if (string.IsNullOrEmpty(sourcePath) || !System.IO.File.Exists(sourcePath)) continue;

            var destFileName = System.IO.Path.GetFileName(sourcePath);
            var destPath = System.IO.Path.Combine(targetFolder, destFileName);
            if (!System.IO.File.Exists(destPath))
                System.IO.File.Copy(sourcePath, destPath);
        }

        _viewModel.ReloadAssetLibraryPublic();
        e.Handled = true;
    }

    private async void SelectSpriteImage_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel?.SelectedHierarchyItem is null) return;

        var dialog = new FilePickerOpenOptions
        {
            Title = "选择精灵图片",
            AllowMultiple = false,
            FileTypeFilter = [new("图片文件") { Patterns = ["*.png", "*.jpg", "*.jpeg", "*.webp", "*.gif"] }]
        };

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(dialog);
        if (files.Count == 0) return;

        var selectedFile = files[0];
        var sourcePath = selectedFile.Path.LocalPath;
        var item = _viewModel.SelectedHierarchyItem;

        var useCopyMode = _viewModel.SpriteImportModeIndex == 0;

        string finalPath;
        if (useCopyMode)
        {
            var assetFolder = System.IO.Path.Combine(AppContext.BaseDirectory, "AssetLibrary", "StaticObjects");
            System.IO.Directory.CreateDirectory(assetFolder);
            var destFileName = System.IO.Path.GetFileName(sourcePath);
            var destPath = System.IO.Path.Combine(assetFolder, destFileName);
            if (!System.IO.File.Exists(destPath))
                System.IO.File.Copy(sourcePath, destPath);
            finalPath = System.IO.Path.GetRelativePath(AppContext.BaseDirectory, destPath);
        }
        else
        {
            finalPath = sourcePath;
        }

        item.SourceAssetPath = finalPath;
        item.SourceAssetKind = "Image";
        item.SourceAssetName = System.IO.Path.GetFileNameWithoutExtension(sourcePath);
        ApplySpriteAspectRatio(item, finalPath);
    }

    private void PickSpriteFromAsset_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel?.SelectedHierarchyItem is null || _viewModel.SelectedAssetItem is null) return;

        var asset = _viewModel.SelectedAssetItem;
        var item = _viewModel.SelectedHierarchyItem;

        var resolvedPath = MapEngine.Avalonia.Services.MapSpriteAssetResolver.ResolveSpritePath(asset.FullPath, asset.Kind);
        if (string.IsNullOrEmpty(resolvedPath) && System.IO.File.Exists(asset.FullPath))
            resolvedPath = asset.FullPath;

        if (string.IsNullOrEmpty(resolvedPath)) return;

        var relativePath = System.IO.Path.GetRelativePath(AppContext.BaseDirectory, resolvedPath);
        item.SourceAssetPath = relativePath;
        item.SourceAssetKind = "Image";
        item.SourceAssetName = asset.Name;
        ApplySpriteAspectRatio(item, resolvedPath);
    }

    private static void ApplySpriteAspectRatio(HierarchyItemViewModel item, string imagePath)
    {
        try
        {
            var fullPath = System.IO.Path.IsPathRooted(imagePath)
                ? imagePath
                : System.IO.Path.Combine(AppContext.BaseDirectory, imagePath);

            if (!System.IO.File.Exists(fullPath)) return;

            var info = SixLabors.ImageSharp.Image.Identify(fullPath);
            if (info is null || info.Width <= 0 || info.Height <= 0) return;

            var cellSize = MapViewportConstants.CellSize;
            var widthInCells = info.Width / cellSize;
            var heightInCells = info.Height / cellSize;

            if (widthInCells < 1) widthInCells = 1;
            if (heightInCells < 1) heightInCells = 1;

            item.ScaleX = widthInCells;
            item.ScaleY = heightInCells;
        }
        catch { }
    }

    private async void AssetCard_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control control
            || control.DataContext is not AssetItemViewModel asset
            || !e.GetCurrentPoint(control).Properties.IsLeftButtonPressed)
        {
            return;
        }

        _dragPlacementFinalized = false;
        CancelPendingMapFinalize();
        var data = new DataTransfer();
        data.Add(DataTransferItem.Create(AssetDragFormat, asset.FullPath));
        data.Add(DataTransferItem.CreateText(asset.FullPath));
        await DragDrop.DoDragDropAsync(e, data, DragDropEffects.Copy);
        CancelPendingMapFinalize();
        _viewModel?.CancelMapDragPreview();
        _dragPlacementFinalized = false;
    }

    // ── 素材文件卡片事件 ─────────────────────────────────────────────────

    /// <summary>
    /// 素材文件卡片点击：单击选中，拖拽放置到地图。
    /// </summary>
    private async void AssetItemCard_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_viewModel is null
            || sender is not Control control
            || control.DataContext is not AssetItemViewModel item)
        {
            return;
        }

        if (!e.GetCurrentPoint(control).Properties.IsLeftButtonPressed)
            return;

        // 单击：选中素材（高亮）
        _viewModel.SelectedAssetItem = item;

        // 拖拽：放置到地图
        _dragPlacementFinalized = false;
        CancelPendingMapFinalize();
        var data = new DataTransfer();
        data.Add(DataTransferItem.Create(AssetDragFormat, item.FullPath));
        data.Add(DataTransferItem.CreateText(item.FullPath));
        await DragDrop.DoDragDropAsync(e, data, DragDropEffects.Copy);
        CancelPendingMapFinalize();
        _viewModel?.CancelMapDragPreview();
        _dragPlacementFinalized = false;
    }

    private void PasteAssetItem_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is null || GetMenuParameter<AssetItemViewModel>(sender) is not { } item)
        {
            return;
        }

        // 粘贴到当前选中文件夹
        if (_viewModel.SelectedAssetFolder is not null)
            _viewModel.PasteAssetItem(_viewModel.SelectedAssetFolder);
    }

    // ── 素材库文件夹卡片事件 ─────────────────────────────────────────────────

    /// <summary>
    /// 文件夹卡片点击：单击选中，双击进入（切换 SelectedAssetFolder）。
    /// </summary>
    private void AssetFolderCard_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_viewModel is null
            || sender is not Control control
            || control.DataContext is not AssetFolderViewModel folder)
        {
            return;
        }

        if (!e.GetCurrentPoint(control).Properties.IsLeftButtonPressed)
            return;

        if (e.ClickCount == 2)
        {
            // 双击：进入子目录
            _viewModel.SelectedAssetFolder = folder;
            e.Handled = true;
        }
        else
        {
            // 单击：仅选中（高亮），不切换目录
            _viewModel.SelectedAssetFolder = folder;
        }
    }

    /// <summary>右键菜单"打开文件夹"——等效于双击进入。</summary>
    private void OpenAssetFolderCard_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is null || GetMenuParameter<AssetFolderViewModel>(sender) is not { } folder)
            return;
        _viewModel.SelectedAssetFolder = folder;
    }

    private async void AddAssetFolder_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is null || GetMenuParameter<AssetFolderViewModel>(sender) is not { } folder)
        {
            return;
        }

        _viewModel.SelectedAssetFolder = folder;
        var created = _viewModel.AddAssetFolder(folder);
        var name = await PromptForNameAsync("新增素材文件夹", created.Name);
        if (!string.IsNullOrWhiteSpace(name))
        {
            _viewModel.RenameAssetFolder(created, name);
        }
    }

    private void CopyAssetFolder_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is null || GetMenuParameter<AssetFolderViewModel>(sender) is not { } folder)
        {
            return;
        }

        _viewModel.CopyAssetFolder(folder);
    }

    private void PasteAssetFolder_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is null || GetMenuParameter<AssetFolderViewModel>(sender) is not { } folder)
        {
            return;
        }

        _viewModel.PasteAssetFolder(folder);
    }

    private void PasteAssetItemIntoFolder_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is null || GetMenuParameter<AssetFolderViewModel>(sender) is not { } folder)
        {
            return;
        }

        _viewModel.PasteAssetItem(folder);
    }

    private void DuplicateAssetFolder_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is null || GetMenuParameter<AssetFolderViewModel>(sender) is not { } folder)
        {
            return;
        }

        _viewModel.DuplicateAssetFolder(folder);
    }

    private async void AddAssetItem_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is null || GetMenuParameter<AssetFolderViewModel>(sender) is not { } folder)
        {
            return;
        }

        _viewModel.SelectedAssetFolder = folder;
        var created = _viewModel.AddAssetItem(folder);
        var name = await PromptForNameAsync("新增静态对象类", created.Name);
        if (!string.IsNullOrWhiteSpace(name))
        {
            _viewModel.RenameAssetItem(created, name);
        }
    }

    private async void RenameAssetFolder_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is null || GetMenuParameter<AssetFolderViewModel>(sender) is not { } folder)
        {
            return;
        }

        _viewModel.SelectedAssetFolder = folder;
        var name = await PromptForNameAsync("重命名素材文件夹", folder.Name);
        if (!string.IsNullOrWhiteSpace(name))
        {
            _viewModel.RenameAssetFolder(folder, name);
        }
    }

    private void DeleteAssetFolder_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is null || GetMenuParameter<AssetFolderViewModel>(sender) is not { } folder)
        {
            return;
        }

        _viewModel.DeleteAssetFolder(folder);
    }

    private async void RenameAssetItem_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is null || GetMenuParameter<AssetItemViewModel>(sender) is not { } item)
        {
            return;
        }

        _viewModel.SelectedAssetItem = item;
        var name = await PromptForNameAsync("重命名静态对象类", item.Name);
        if (!string.IsNullOrWhiteSpace(name))
        {
            _viewModel.RenameAssetItem(item, name);
        }
    }

    private void CopyAssetItem_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is null || GetMenuParameter<AssetItemViewModel>(sender) is not { } item)
        {
            return;
        }

        _viewModel.CopyAssetItem(item);
    }

    private void DuplicateAssetItem_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is null || GetMenuParameter<AssetItemViewModel>(sender) is not { } item)
        {
            return;
        }

        _viewModel.DuplicateAssetItem(item);
    }

    private void DeleteAssetItem_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is null || GetMenuParameter<AssetItemViewModel>(sender) is not { } item)
        {
            return;
        }

        _viewModel.DeleteAssetItem(item);
    }


    private void HierarchyRoot_DragOver(object? sender, DragEventArgs e)
    {
        CancelPendingMapFinalize();
        UpdateDragEffects(e);
    }

    private async void HierarchyRoot_Drop(object? sender, DragEventArgs e)
    {
        if (_dragPlacementFinalized)
        {
            e.Handled = true;
            return;
        }

        var asset = await TryGetDraggedAssetAsync(e);
        if (_viewModel is null || asset is null)
        {
            return;
        }

        _viewModel.CancelMapDragPreview();
        _viewModel.CreateInstanceFromAssetUnderHierarchy(asset, null);
        _dragPlacementFinalized = true;
        e.Handled = true;
    }

    private void HierarchyItem_DragOver(object? sender, DragEventArgs e)
    {
        CancelPendingMapFinalize();
        UpdateDragEffects(e);
    }

    private async void HierarchyItem_Drop(object? sender, DragEventArgs e)
    {
        if (_dragPlacementFinalized)
        {
            e.Handled = true;
            return;
        }

        var asset = await TryGetDraggedAssetAsync(e);
        if (_viewModel is null
            || sender is not Control control
            || control.DataContext is not HierarchyItemViewModel target
            || asset is null)
        {
            return;
        }

        _viewModel.CancelMapDragPreview();
        _viewModel.CreateInstanceFromAssetUnderHierarchy(asset, target);
        _dragPlacementFinalized = true;
        e.Handled = true;
    }

    private async void MapDropTarget_DragOver(object? sender, DragEventArgs e)
    {
        if (_dragPlacementFinalized)
        {
            e.DragEffects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        CancelPendingMapFinalize();
        var asset = await TryGetDraggedAssetAsync(e);
        if (_viewModel is null || asset is null)
        {
            e.DragEffects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        if (!TryGetMapCanvasPosition(e, out var contentX, out var contentY))
        {
            ScheduleMapFinalize();
            e.DragEffects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        _viewModel.BeginOrUpdateMapDragPreview(asset, ContentToWorldX(contentX), ContentToWorldY(contentY));
        e.DragEffects = DragDropEffects.Copy;
        e.Handled = true;
    }

    private void MapDropTarget_DragLeave(object? sender, RoutedEventArgs e)
        => ScheduleMapFinalize();

    private async void MapDropTarget_Drop(object? sender, DragEventArgs e)
    {
        if (_dragPlacementFinalized)
        {
            e.Handled = true;
            return;
        }

        CancelPendingMapFinalize();
        var asset = await TryGetDraggedAssetAsync(e);
        if (_viewModel is null || asset is null || !TryGetMapCanvasPosition(e, out var contentX, out var contentY))
        {
            FinalizeMapPreviewAtLastValidPosition();
            return;
        }

        _viewModel.BeginOrUpdateMapDragPreview(asset, ContentToWorldX(contentX), ContentToWorldY(contentY));
        _viewModel.CommitMapDragPreview();
        _dragPlacementFinalized = true;
        e.Handled = true;
    }

}
