using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
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

public sealed class AssetFolderViewModel : ViewModelBase
{
    private string _name;

    public AssetFolderViewModel(string id, string name, string fullPath, AssetFolderViewModel? parent)
    {
        Id = id;
        _name = name;
        FullPath = fullPath;
        Parent = parent;
        Children = [];
    }

    public string Id { get; }

    public string Name
    {
        get => _name;
        set
        {
            if (SetProperty(ref _name, value))
            {
                OnPropertyChanged(nameof(DisplayName));
                NotifyPathChangedRecursive(this);
            }
        }
    }

    public string FullPath { get; }

    public AssetFolderViewModel? Parent { get; }

    public ObservableCollection<AssetFolderViewModel> Children { get; }

    public string DisplayName => Name;

    public string PathDisplay => Parent is null ? Name : $"{Parent.PathDisplay} > {Name}";

    /// <summary>卡片副标题：子文件夹数 + 父目录标记，用于素材区文件夹卡片显示。</summary>
    public string ChildCountLabel =>
        Children.Count == 0 ? "文件夹" : $"{Children.Count} 个子目录";

    private static void NotifyPathChangedRecursive(AssetFolderViewModel folder)
    {
        folder.OnPropertyChanged(nameof(PathDisplay));
        folder.OnPropertyChanged(nameof(ChildCountLabel));
        foreach (var child in folder.Children)
        {
            NotifyPathChangedRecursive(child);
        }
    }
}

public sealed class AssetItemViewModel : ViewModelBase, IGlobalSelectionItem
{
    private string _name;
    private Bitmap? _thumbnail;
    private bool _thumbnailRequested;

    public AssetItemViewModel(string id, string folderId, string fullPath, string fileName, string name, string kind, string icon, string description)
    {
        Id = id;
        FolderId = folderId;
        FullPath = fullPath;
        FileName = fileName;
        _name = name;
        Kind = kind;
        Icon = icon;
        Description = description;
    }

    public string Id { get; }

    public string FolderId { get; }

    public string FullPath { get; }

    public string FileName { get; }

    public string FolderPath => Path.GetDirectoryName(FullPath) ?? string.Empty;

    public string Name
    {
        get => _name;
        set
        {
            if (SetProperty(ref _name, value))
            {
                OnPropertyChanged(nameof(DisplayName));
            }
        }
    }

    public string Kind { get; }

    public string Icon { get; }

    public string Description { get; }

    public string InstanceBaseName
        => Kind == "StaticObjectClass"
            ? Name
            : Path.GetFileNameWithoutExtension(FileName);

    public string DisplayName => $"{Icon} {Name}";

    public string SelectionType => $"素材 / {Kind}";

    // ── 缩略图（懒加载）────────────────────────────────────────
    // 图片素材和 .asset（其 SpriteRenderer 引用的图）都能出图；
    // 首次读取 Thumbnail 时才发起解码，滚动列表外的素材不占内存。

    /// <summary>
    /// 卡片缩略图。首次访问触发后台解码，解完再 OnPropertyChanged 让 UI 换上。
    /// 解不出来（非图片素材 / 文件损坏）保持 null，模板回退到 emoji 图标。
    /// </summary>
    public Bitmap? Thumbnail
    {
        get
        {
            EnsureThumbnailRequested();
            return _thumbnail;
        }
    }

    /// <summary>缩略图是否已就绪，模板用它切换 Image / emoji 显示。</summary>
    public bool HasThumbnail => Thumbnail is not null;

    private void EnsureThumbnailRequested()
    {
        if (_thumbnailRequested) return;
        _thumbnailRequested = true;

        var imagePath = MapSpriteAssetResolver.ResolveAssetFileSprite(FullPath);
        if (string.IsNullOrWhiteSpace(imagePath)) return;

        _ = LoadThumbnailAsync(imagePath);
    }

    private async Task LoadThumbnailAsync(string imagePath)
    {
        var bitmap = await AssetThumbnailCache.GetAsync(imagePath).ConfigureAwait(true);
        if (bitmap is null) return;

        // 回到 UI 线程再改属性：解码 Task 可能在线程池上完成
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            _thumbnail = bitmap;
            OnPropertyChanged(nameof(Thumbnail));
            OnPropertyChanged(nameof(HasThumbnail));
        });
    }
}

public enum AssetClipboardKind
{
    None,
    Folder,
    Item
}

public sealed class AssetFolderSnapshot
{
    public string Name { get; set; } = string.Empty;

    public List<AssetFolderSnapshot> Children { get; set; } = [];

    public List<AssetItemSnapshot> Items { get; set; } = [];
}

public sealed class AssetItemSnapshot
{
    public string Name { get; set; } = string.Empty;

    public string Kind { get; set; } = "ObjectClass";

    public string Icon { get; set; } = "📦";

    public string Description { get; set; } = string.Empty;
}
