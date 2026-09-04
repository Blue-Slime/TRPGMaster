using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using MapEngine.Avalonia.ViewModels;

namespace MapEngine.Avalonia.Views;

/// <summary>素材库：拖放放置、贴图选择、文件夹/对象类卡片的增删改与剪贴板操作。</summary>
public partial class MapEditorView
{
    private async void AssetLibrary_Drop(object? sender, DragEventArgs e)
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

        var metadataStore = _viewModel.GetAssetMetadataStore();
        var assetLibraryRoot = _viewModel.GetAssetLibraryRootPath();

        foreach (var storageItem in files)
        {
            var sourcePath = storageItem.Path.LocalPath;
            if (string.IsNullOrEmpty(sourcePath) || !System.IO.File.Exists(sourcePath)) continue;

            // 图片走 AssetImporter：按内容哈希命名入库并生成 .asset（哈希相同则复用同一份图片文件）。
            // 这里是素材入库的唯一导入点，之后拖到地图只读 assetRef，不再重复导入。
            var importResult = MapEngine.Core.Assets.AssetImporter.ImportImage(
                sourcePath,
                assetLibraryRoot,
                System.IO.Path.GetRelativePath(assetLibraryRoot, targetFolder),
                metadataStore,
                null);

            if (importResult is null)
            {
                // 非图片（音频等）保持原名直接拷入
                var destPath = System.IO.Path.Combine(targetFolder, System.IO.Path.GetFileName(sourcePath));
                if (!System.IO.File.Exists(destPath))
                    System.IO.File.Copy(sourcePath, destPath);

                // 上传非图片资产到服务器
                await UploadToServerIfInRoomAsync(sourcePath, string.Empty);
                continue;
            }

            // 处理导入结果
            if (importResult.Status == MapEngine.Core.Assets.AssetImporter.ImportStatus.DuplicateContent)
            {
                // 弹窗询问用户是否创建副本
                var topLevel = TopLevel.GetTopLevel(this);
                if (topLevel is null) continue;

                var messageBox = new Window
                {
                    Title = "检测到重复内容",
                    Width = 420,
                    Height = 200,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    CanResize = false
                };

                bool? userChoice = null;
                var content = new StackPanel
                {
                    Margin = new Thickness(20),
                    Spacing = 16
                };

                content.Children.Add(new TextBlock
                {
                    Text = $"图片 {System.IO.Path.GetFileName(sourcePath)} 的内容已存在于素材库中。\n\n是否创建副本引用？",
                    TextWrapping = TextWrapping.Wrap
                });

                var buttonPanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8
                };

                var createButton = new Button { Content = "创建副本", Width = 100 };
                createButton.Click += (_, _) => { userChoice = true; messageBox.Close(); };

                var skipButton = new Button { Content = "跳过", Width = 100 };
                skipButton.Click += (_, _) => { userChoice = false; messageBox.Close(); };

                buttonPanel.Children.Add(skipButton);
                buttonPanel.Children.Add(createButton);
                content.Children.Add(buttonPanel);

                messageBox.Content = content;
                await messageBox.ShowDialog((Window)topLevel);

                if (userChoice != true)
                    continue;

                // 用户选择创建副本：仅创建 .asset 文件引用已存在的图片哈希
                var baseName = System.IO.Path.GetFileNameWithoutExtension(sourcePath);
                var assetPath = GetUniqueAssetPath(targetFolder, baseName);
                var assetContent = new
                {
                    name = System.IO.Path.GetFileNameWithoutExtension(assetPath),
                    type = "StaticObjectClass",
                    assetRef = importResult.ImageHash,
                    components = new object[]
                    {
                        new { type = "Transform", properties = new { x = 0, y = 0, scaleX = 1, scaleY = 1 } },
                        new { type = "SpriteRenderer", properties = new { assetRef = importResult.ImageHash, sourceAssetKind = "Image", opacity = 1.0 } }
                    }
                };

                var json = System.Text.Json.JsonSerializer.Serialize(assetContent, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                System.IO.File.WriteAllText(assetPath, json);

                // 上传图片到服务器（创建副本时也需要上传）
                await UploadToServerIfInRoomAsync(sourcePath, importResult.ImageHash);
            }
            else if (importResult.Status == MapEngine.Core.Assets.AssetImporter.ImportStatus.Success)
            {
                // 导入成功，Register 方法会自动保存元数据索引
                // 上传图片到服务器
                await UploadToServerIfInRoomAsync(sourcePath, importResult.ImageHash);
            }
        }

        _viewModel.ReloadAssetLibraryPublic();
        e.Handled = true;
    }

    private static string GetUniqueAssetPath(string folder, string baseName)
    {
        var candidate = System.IO.Path.Combine(folder, baseName + ".asset");
        if (!System.IO.File.Exists(candidate))
            return candidate;

        for (int i = 2; i < 1000; i++)
        {
            candidate = System.IO.Path.Combine(folder, $"{baseName} {i}.asset");
            if (!System.IO.File.Exists(candidate))
                return candidate;
        }

        return System.IO.Path.Combine(folder, $"{baseName}_{System.Guid.NewGuid():N}.asset");
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

        // 使用 AssetImporter 导入图片并获取哈希值
        var metadataStore = _viewModel.GetAssetMetadataStore();
        var assetLibraryRoot = _viewModel.GetAssetLibraryRootPath();
        var importResult = MapEngine.Core.Assets.AssetImporter.ImportImage(
            sourcePath,
            assetLibraryRoot,
            "StaticObjects",
            metadataStore,
            null);

        if (importResult is null)
        {
            return; // 导入失败
        }

        // Register 方法会自动保存元数据索引

        item.AssetRef = importResult.ImageHash;
        item.SourceAssetKind = "Image";
        item.SourceAssetName = System.IO.Path.GetFileNameWithoutExtension(sourcePath);
        ApplySpriteAspectRatio(item, sourcePath);

        // 上传到服务器
        await UploadToServerIfInRoomAsync(sourcePath, importResult.ImageHash);

        // 通知渲染刷新
        _viewModel.RefreshMapRenderableItemsPublic();
    }

    private void PickSpriteFromAsset_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel?.SelectedHierarchyItem is null || _viewModel.SelectedAssetItem is null) return;

        var asset = _viewModel.SelectedAssetItem;
        var item = _viewModel.SelectedHierarchyItem;

        // 读取 .asset 里已登记的 assetRef（内容哈希），不在此处重新导入
        var assetDoc = TryLoadAssetDocument(asset.FullPath);
        var assetRef = assetDoc?.Components
            .FirstOrDefault(c => c.Type.Equals("SpriteRenderer", StringComparison.OrdinalIgnoreCase))
            ?.Properties.GetValueOrDefault("assetRef");

        if (string.IsNullOrEmpty(assetRef)) return;

        var spritePath = MapEngine.Avalonia.Services.MapSpriteAssetResolver.ResolveSpritePath(assetRef);
        if (string.IsNullOrEmpty(spritePath)) return;

        item.AssetRef = assetRef;
        item.SourceAssetKind = "Image";
        item.SourceAssetName = asset.Name;
        ApplySpriteAspectRatio(item, spritePath);

        // 通知渲染刷新
        _viewModel.RefreshMapRenderableItemsPublic();
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

    /// <summary>
    /// 若当前正看着房间库，把刚导入的素材同步上传到服务端房间共享库。
    /// 本地库导入不上传——本地库是私有的，只有房间库才是共享的。
    /// </summary>
    private async Task UploadToServerIfInRoomAsync(string localFilePath, string hash)
    {
        try
        {
            if (_viewModel is null) return;

            // 只有素材库面板切到"房间"时才上传；本地库导入保持私有
            if (_viewModel.ActiveAssetSource != AssetSource.Room) return;

            var host = _viewModel.Host;
            if (host?.RoomAssetLibraryPath is null) return; // 单机模式

            var assetType = ClassifyAssetType(localFilePath);
            var remoteHash = await host.UploadRoomAssetAsync(localFilePath, assetType);

            if (!string.IsNullOrEmpty(remoteHash))
            {
                host.Logger.Info($"素材已上传房间库：{Path.GetFileName(localFilePath)} → {assetType} (hash={remoteHash})");
            }
        }
        catch (Exception ex)
        {
            // 上传失败不影响本地导入流程
            _viewModel?.Host?.Logger.Warn($"素材上传房间库失败：{ex.Message}");
        }
    }

    /// <summary>按扩展名归入服务端的分类目录（tokens/maps/audio/files）。</summary>
    private static string ClassifyAssetType(string filePath)
        => Path.GetExtension(filePath).ToLowerInvariant() switch
        {
            ".png" or ".jpg" or ".jpeg" or ".webp" or ".gif" or ".bmp" => "token",
            ".mp3" or ".wav" or ".ogg" or ".flac" or ".m4a"            => "audio",
            _                                                          => "file"
        };

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

    private static MapEngine.Avalonia.Services.StaticObjectAssetDocument? TryLoadAssetDocument(string fullPath)
    {
        try
        {
            if (!System.IO.File.Exists(fullPath)) return null;
            var json = System.IO.File.ReadAllText(fullPath);
            return System.Text.Json.JsonSerializer.Deserialize<MapEngine.Avalonia.Services.StaticObjectAssetDocument>(
                json, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch { return null; }
    }
}
