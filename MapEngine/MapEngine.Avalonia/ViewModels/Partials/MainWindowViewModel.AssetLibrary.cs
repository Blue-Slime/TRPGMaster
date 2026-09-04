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

/// <summary>素材库数据源。</summary>
public enum AssetSource
{
    /// <summary>本地素材库（跨房间复用，仅本机可见）</summary>
    Local = 0,
    /// <summary>房间素材库（房间成员共享，联机时可用）</summary>
    Room = 1,
}

/// <summary>素材库：文件夹/对象类 CRUD、剪贴板、拖放预览与实例化。</summary>
public partial class MainWindowViewModel
{
    private AssetSource _activeAssetSource = AssetSource.Local;

    /// <summary>房间库是否可用（联机且宿主提供了房间素材库路径）。</summary>
    public bool IsRoomAssetLibraryAvailable
        => !string.IsNullOrWhiteSpace(Host?.RoomAssetLibraryPath);

    /// <summary>当前素材库数据源。切换时重载素材库树。</summary>
    public AssetSource ActiveAssetSource
    {
        get => _activeAssetSource;
        set
        {
            if (_activeAssetSource == value)
                return;

            if (value == AssetSource.Room && !IsRoomAssetLibraryAvailable)
            {
                StatusMessage = "当前为单机模式，没有房间素材库";
                // 单选框此刻已被点亮，但状态没变。推回通知让 UI 复位到"本地"，
                // 否则界面显示房间库、实际读的还是本地库。
                OnPropertyChanged(nameof(IsLocalAssetSourceActive));
                OnPropertyChanged(nameof(IsRoomAssetSourceActive));
                return;
            }

            if (!SetProperty(ref _activeAssetSource, value))
                return;

            _assetLibraryRootPath = value == AssetSource.Room
                ? Host!.RoomAssetLibraryPath!
                : _localAssetLibraryRootPath;

            // 元数据索引是按根路径建的，换根后必须重建，
            // 否则导入/去重会继续写进上一个库的 asset-index.json。
            _assetMetadataStore = LoadOrCreateMetadataStore(_assetLibraryRootPath);

            OnPropertyChanged(nameof(IsLocalAssetSourceActive));
            OnPropertyChanged(nameof(IsRoomAssetSourceActive));

            // 素材路径整体换根，缓存的 hash→path 全部失效
            MapSpriteAssetResolver.ClearCache();
            ReloadAssetLibrary(null, null);

            StatusMessage = value == AssetSource.Room ? "已切换到房间素材库" : "已切换到本地素材库";
        }
    }

    /// <summary>供 UI 单选绑定</summary>
    public bool IsLocalAssetSourceActive
    {
        get => _activeAssetSource == AssetSource.Local;
        set { if (value) ActiveAssetSource = AssetSource.Local; }
    }

    /// <summary>供 UI 单选绑定</summary>
    public bool IsRoomAssetSourceActive
    {
        get => _activeAssetSource == AssetSource.Room;
        set { if (value) ActiveAssetSource = AssetSource.Room; }
    }

    [RelayCommand]
    private void UseLocalAssetSource() => ActiveAssetSource = AssetSource.Local;

    [RelayCommand]
    private void UseRoomAssetSource() => ActiveAssetSource = AssetSource.Room;

    public AssetFolderViewModel AddAssetFolder(AssetFolderViewModel parent)
    {
        var folderPath = AssetLibraryFileSystemService.CreateFolder(parent.FullPath, "新建文件夹");
        ReloadAssetLibrary(folderPath, null);
        var folder = FindAssetFolderByPath(folderPath)
            ?? throw new InvalidOperationException("新建素材文件夹后未能重新定位该目录。");

        StatusMessage = $"已新增素材文件夹 {folder.Name}";
        return folder;
    }

    public void RenameAssetFolder(AssetFolderViewModel folder, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var targetPath = AssetLibraryFileSystemService.RenameFolder(folder.FullPath, name.Trim(), _assetLibraryRootPath);
        ReloadAssetLibrary(targetPath, null);

        var renamed = FindAssetFolderByPath(targetPath);
        StatusMessage = $"已重命名素材文件夹为 {renamed?.Name ?? Path.GetFileName(targetPath)}";
    }

    public bool DeleteAssetFolder(AssetFolderViewModel folder)
    {
        if (folder.Parent is null)
        {
            StatusMessage = "素材库根目录不允许删除";
            return false;
        }

        var parentPath = folder.Parent.FullPath;
        AssetLibraryFileSystemService.DeleteFolder(folder.FullPath);
        ReloadAssetLibrary(parentPath, null);

        StatusMessage = $"已删除素材文件夹 {folder.Name}";
        return true;
    }

    public void CopyAssetFolder(AssetFolderViewModel folder)
    {
        _assetFolderClipboardPath = folder.FullPath;
        _assetItemClipboardPath = null;
        _assetClipboardKind = AssetClipboardKind.Folder;
        StatusMessage = $"已复制素材文件夹 {folder.Name}";
    }

    public AssetFolderViewModel? PasteAssetFolder(AssetFolderViewModel parent)
    {
        if (_assetClipboardKind != AssetClipboardKind.Folder || string.IsNullOrWhiteSpace(_assetFolderClipboardPath))
        {
            StatusMessage = "当前没有可粘贴的素材文件夹";
            return null;
        }

        var folderPath = AssetLibraryFileSystemService.CopyFolderToParent(_assetFolderClipboardPath, parent.FullPath, _assetLibraryRootPath);
        ReloadAssetLibrary(folderPath, null);
        var folder = FindAssetFolderByPath(folderPath);
        if (folder is null)
        {
            return null;
        }

        StatusMessage = $"已粘贴素材文件夹 {folder.Name}";
        return folder;
    }

    public AssetFolderViewModel? DuplicateAssetFolder(AssetFolderViewModel folder)
    {
        if (folder.Parent is null)
        {
            StatusMessage = "素材库根目录不支持复制副本";
            return null;
        }

        var folderPath = AssetLibraryFileSystemService.CopyFolderToParent(folder.FullPath, folder.Parent.FullPath, _assetLibraryRootPath);
        ReloadAssetLibrary(folderPath, null);
        var duplicated = FindAssetFolderByPath(folderPath);
        if (duplicated is null)
        {
            return null;
        }

        StatusMessage = $"已创建素材文件夹副本 {duplicated.Name}";
        return duplicated;
    }

    public AssetItemViewModel AddAssetItem(AssetFolderViewModel folder)
    {
        var filePath = AssetLibraryFileSystemService.CreateStaticObjectFile(folder.FullPath, "新建静态对象类");
        ReloadAssetLibrary(folder.FullPath, filePath);
        var item = FindAssetItemByPath(filePath)
            ?? throw new InvalidOperationException("新建静态对象类后未能重新定位该文件。");

        StatusMessage = $"已新增素材对象类 {item.Name}";
        return item;
    }

    public void RenameAssetItem(AssetItemViewModel item, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var targetPath = AssetLibraryFileSystemService.RenameFile(item.FullPath, name.Trim(), _assetLibraryRootPath);
        ReloadAssetLibrary(Path.GetDirectoryName(targetPath), targetPath);

        var renamed = FindAssetItemByPath(targetPath);
        StatusMessage = $"已重命名素材为 {renamed?.Name ?? Path.GetFileName(targetPath)}";
    }

    public bool DeleteAssetItem(AssetItemViewModel item)
    {
        AssetLibraryFileSystemService.DeleteFile(item.FullPath);
        ReloadAssetLibrary(item.FolderPath, null);
        StatusMessage = $"已删除素材 {item.Name}";
        return true;
    }

    public void CopyAssetItem(AssetItemViewModel item)
    {
        _assetItemClipboardPath = item.FullPath;
        _assetFolderClipboardPath = null;
        _assetClipboardKind = AssetClipboardKind.Item;
        StatusMessage = $"已复制素材 {item.Name}";
    }

    public AssetItemViewModel? PasteAssetItem(AssetFolderViewModel folder)
    {
        if (_assetClipboardKind != AssetClipboardKind.Item || string.IsNullOrWhiteSpace(_assetItemClipboardPath))
        {
            StatusMessage = "当前没有可粘贴的素材对象类";
            return null;
        }

        var filePath = AssetLibraryFileSystemService.CopyFileToFolder(_assetItemClipboardPath, folder.FullPath, _assetLibraryRootPath);
        ReloadAssetLibrary(folder.FullPath, filePath);
        var item = FindAssetItemByPath(filePath);
        if (item is null)
        {
            return null;
        }

        StatusMessage = $"已粘贴素材 {item.Name}";
        return item;
    }

    public AssetItemViewModel? DuplicateAssetItem(AssetItemViewModel item)
    {
        var folder = FindAssetFolderByPath(item.FolderPath);
        if (folder is null)
        {
            return null;
        }

        var filePath = AssetLibraryFileSystemService.CopyFileToFolder(item.FullPath, folder.FullPath, _assetLibraryRootPath);
        ReloadAssetLibrary(folder.FullPath, filePath);
        var duplicated = FindAssetItemByPath(filePath);
        if (duplicated is null)
        {
            return null;
        }

        StatusMessage = $"已创建素材副本 {duplicated.Name}";
        return duplicated;
    }

    public AssetItemViewModel? GetAssetItemByPath(string? path)
        => FindAssetItemByPath(path);

    public bool HasMapDragPreview => _mapDragPreviewItem is not null;

    public HierarchyItemViewModel? CreateInstanceFromAssetAtMap(AssetItemViewModel asset, double x, double y)
    {
        // 地图拖放固定挂到场景根第一层,不受当前选中节点影响
        var parent = HierarchyRoots.Count > 0 ? HierarchyRoots[0] : EnsureSceneRoot();
        return CreateInstanceFromAsset(asset, parent, x, y);
    }

    public void BeginOrUpdateMapDragPreview(AssetItemViewModel asset, double x, double y)
    {
        if (_mapDragPreviewItem is not null
            && !_mapDragPreviewItem.AssetRef.Equals(asset.Id, StringComparison.OrdinalIgnoreCase))
        {
            CancelMapDragPreview();
        }

        if (_mapDragPreviewItem is null)
        {
            // 地图拖放固定挂到场景根第一层,不受当前选中节点影响
            var parent = HierarchyRoots.Count > 0 ? HierarchyRoots[0] : EnsureSceneRoot();
            if (parent is null)
            {
                return;
            }

            _mapDragPreviewItem = CreateInstanceFromAsset(asset, parent, x, y, isPreview: true, updateSelection: true, updateStatus: false);
            return;
        }

        _mapDragPreviewItem.HasMapPosition = true;
        _mapDragPreviewItem.X = x;
        _mapDragPreviewItem.Y = y;
    }

    public void CommitMapDragPreview()
    {
        if (_mapDragPreviewItem is null)
        {
            return;
        }

        var preview = _mapDragPreviewItem;
        var parentId = preview.Parent?.Id;
        _mapDragPreviewItem = null;

        if (parentId is null)
        {
            return;
        }

        var snapshot = SnapshotHierarchyPublic(preview);
        RemoveHierarchyItem(preview, preview.Parent, updateSelection: false);
        _commandBus.Execute(new MapEngine.Avalonia.Commands.VmAddEmptyObjectCommand(this, parentId, snapshot));
        var committed = FindHierarchyById(snapshot.Id);
        if (committed is not null)
        {
            // SelectedAssetItem 在拖拽时已设置，此处只更新选中的层级节点
            SelectedHierarchyItem = committed;
            StatusMessage = $"已从素材 {committed.SourceAssetName} 生成实例 {committed.Name}";
        }
        RefreshMapRenderableItems();
    }

    public void CancelMapDragPreview()
    {
        if (_mapDragPreviewItem is null)
        {
            return;
        }

        var preview = _mapDragPreviewItem;
        _mapDragPreviewItem = null;
        RemoveHierarchyItem(preview, preview.Parent, updateSelection: true);
        RefreshMapRenderableItems();
    }

    public HierarchyItemViewModel? CreateInstanceFromAssetUnderHierarchy(AssetItemViewModel asset, HierarchyItemViewModel? parent)
    {
        CancelMapDragPreview();

        var target = parent ?? ResolveDefaultDropParent();
        if (target is null)
        {
            StatusMessage = "当前没有可用的层级父对象";
            return null;
        }

        return CreateInstanceFromAsset(asset, target, null, null);
    }

    private HierarchyItemViewModel BuildHierarchyItem(HierarchyNodeDto dto)
    {
        var item = new HierarchyItemViewModel(dto);
        _hierarchyIndex[item.Id] = item;

        // 递归构建子节点
        foreach (var childDto in dto.Children)
        {
            var child = BuildHierarchyItem(childDto);
            child.Parent = item;
            item.Children.Add(child);
            // 子的 GameObject 父子关系在下面统一建立
            child.BackingObject.Parent = item.BackingObject;
            item.BackingObject.Children.Add(child.BackingObject);
        }

        return item;
    }

    /// <summary>把 HierarchyRoots 的所有 GameObject 加入 World(在初始化完成后调用一次)。</summary>
    private void SyncGameObjectsToWorld()
    {
        // 递归添加所有 GameObject 到 World
        foreach (var root in HierarchyRoots)
        {
            AddObjectAndChildrenToWorld(root.BackingObject);
        }
    }

    private void AddObjectAndChildrenToWorld(GameObject obj)
    {
        _world.AddObject(obj);
        foreach (var child in obj.Children)
        {
            AddObjectAndChildrenToWorld(child);
        }
    }

    private AssetFolderViewModel BuildAssetFolder(AssetFolderDto dto, AssetFolderViewModel? parent)
    {
        var item = new AssetFolderViewModel(dto.Id, dto.Name, dto.FullPath, parent);
        _assetFolderIndex[item.Id] = item;

        foreach (var child in dto.Children.Select(child => BuildAssetFolder(child, item)))
        {
            item.Children.Add(child);
        }

        return item;
    }
}
