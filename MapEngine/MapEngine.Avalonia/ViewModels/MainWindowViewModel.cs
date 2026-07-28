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

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly World _world = new();
    private readonly CommandBus _commandBus;

    /// <summary>
    /// 外部宿主引用，用于上报流式实时数据（拖拽预览、光标等）。
    /// 单机模式下为 null 或 StandaloneMapEditorHost；联机时由 ChatRoomWindow 注入。
    /// </summary>
    public IMapEditorHost? Host { get; set; }
    private ViewModelSceneState _sceneState = null!;

    private readonly List<AssetItemViewModel> _allAssetItems;
    private readonly Dictionary<string, AssetFolderViewModel> _assetFolderIndex;
    // _hierarchyIndex 保留作为 VM 层快速查找,不再是唯一索引(World.FindById 才是)
    private readonly Dictionary<string, HierarchyItemViewModel> _hierarchyIndex;
    private HierarchyNodeDto? _hierarchyClipboard;
    private string? _assetFolderClipboardPath;
    private string? _assetItemClipboardPath;
    private AssetClipboardKind _assetClipboardKind;
    private readonly string _assetLibraryRootPath;
    private HierarchyItemViewModel? _mapDragPreviewItem;
    private HierarchyItemViewModel? _highlightedHierarchyItem;

    private IGlobalSelectionItem? _currentSelection;
    private HierarchyItemViewModel? _selectedHierarchyItem;
    private AssetFolderViewModel? _selectedAssetFolder;
    private AssetItemViewModel? _selectedAssetItem;
    private ToolActionViewModel? _selectedPrimaryTool;
    private ToolActionViewModel? _selectedQuickAction;
    private string _assetSearchText = string.Empty;
    private double _assetThumbnailSize = 64;
    private bool _showGrid;
    private int _zoomPercent = 100;
    private int _feetPerCell = 5;
    private string _packageName = "默认地图包";
    private string _statusMessage = "界面已就绪";
    private string _graphicsRuntimeSummary = "渲染环境检测中";
    private string _graphicsRuntimeHint = "启动后将显示当前图形后端。";
    private double _mapViewportCenterX;
    private double _mapViewportCenterY;
    private double _lastMapOffsetX;
    private double _lastMapOffsetY;
    private double _lastMapViewportWidth;
    private double _lastMapViewportHeight;

    public MainWindowViewModel()
    {
        _commandBus = new CommandBus(_world);

        ObjectTypes = ["Empty", "Folder", "Map", "Layer", "Prop", "Token", "Marker", "AudioSource", "PrefabInstance", "StaticObject"];

        SelectToolCommand = new RelayCommand<string?>(SelectTool);
        ActivateQuickActionCommand = new RelayCommand<string?>(ActivateQuickAction);
        ToggleGridCommand = new RelayCommand(ToggleGrid);
        ZoomInCommand = new RelayCommand(() => SetZoom(ZoomPercent + 10));
        ZoomOutCommand = new RelayCommand(() => SetZoom(ZoomPercent - 10));
        ResetZoomCommand = new RelayCommand(() => SetZoom(100));
        CycleScaleCommand = new RelayCommand(CycleScale);
        AddTagCommand = new RelayCommand(AddTag);
        RemoveTagCommand = new RelayCommand<string?>(RemoveTag);
        NavigateToSelectionCommand = new RelayCommand(NavigateToSelection, () => CurrentSelection is not null);
        ShowInspectorMenuCommand = new RelayCommand(() => StatusMessage = "Inspector 更多选项暂以本地假数据模式运行");
        SaveSceneCommand = new RelayCommand(SaveScene);
        OpenSettingsCommand = new RelayCommand(OpenSettings);

        PrimaryTools = new ObservableCollection<ToolActionViewModel>
        {
            new("select", "👆", "选择", "移动/选择 (V)"),
            new("pan", "✋", "拖拽", "平移视口 (Middle / H)"),
            new("draw", "🖌️", "画笔", "画笔工具 (D)"),
            new("text", "T", "文本", "文本工具 (T)"),
            new("shape", "🔲", "形状", "形状工具 (S)"),
            new("measure", "📏", "测量", "测量工具 (M)"),
            new("laser", "🎯", "激光", "激光笔 (L)"),
            new("fog", "☁️", "迷雾", "迷雾工具 (F)"),
            new("attach", "📎", "附件", "附件/标记 (A)")
        };

        QuickActions = new ObservableCollection<ToolActionViewModel>
        {
            new("initiative", "⚔️", "先攻", "活跃棋子/先攻列表 (I)"),
            new("tray", "📥", "棋篓", "棋篓 (Tray)"),
            new("dice", "🎲", "骰子", "骰子工具 (Dice)"),
            new("extensions", "🧩", "扩展", "扩展插件 (Extensions)")
        };

        var data = FakeProjectDataLoader.Load();
        PackageName = data.PackageName;

        _hierarchyIndex = [];
        HierarchyRoots = new ObservableCollection<HierarchyItemViewModel>(
            data.HierarchyRoots.Select(BuildHierarchyItem));

        // 场景根保底:空场景时自动建一个根节点,确保拖放/创建有落点
        if (HierarchyRoots.Count == 0)
            EnsureSceneRoot();

        var assetLibrary = AssetLibraryFileSystemService.Load(data.AssetLibraryRootFolder);
        _assetLibraryRootPath = assetLibrary.RootPath;
        _assetFolderIndex = [];
        AssetRoots = new ObservableCollection<AssetFolderViewModel>(
            assetLibrary.RootFolders.Select(folder => BuildAssetFolder(folder, null)));

        _allAssetItems = assetLibrary.AssetItems
            .Select(item => new AssetItemViewModel(item.Id, item.FolderId, item.FullPath, item.FileName, item.Name, item.Kind, item.Icon, item.Description))
            .ToList();

        VisibleAssetItems = [];
        MapRenderableItems = [];
        MapPreloadedTiles = [];
        MapGridLines = [];

        ShowGrid = data.ShowGrid;
        SetZoom(data.ZoomPercent);
        FeetPerCell = data.FeetPerCell;

        SelectTool(data.ActiveTool);

        SelectedHierarchyItem = FindHierarchy(data.SelectedHierarchyId) ?? HierarchyRoots.FirstOrDefault();
        SelectedAssetFolder = FindAssetFolder(data.SelectedAssetFolderId)
            ?? AssetRoots.FirstOrDefault()?.Children.FirstOrDefault()
            ?? AssetRoots.FirstOrDefault();

        RefreshVisibleAssets();
        RefreshMapRenderableItems();
        RefreshMapViewportDecorations();

        // S2: 把所有 GameObject 加入 World,让 World 成为运行时权威
        SyncGameObjectsToWorld();

        _sceneState = new ViewModelSceneState(this);
        _commandBus.SetLegacyState(_sceneState);
        _commandBus.CommandExecuted += (_, e) =>
            StatusMessage = $"[{e.Action}] {e.Command.Description}";
    }

    public CommandBus CommandBus => _commandBus;

    public ISceneState SceneState => _sceneState;

    public World World => _world;

    public void RefreshMapRenderableItemsPublic() => RefreshMapRenderableItems();

    public void ReloadAssetLibraryPublic()
    {
        var folderPath = SelectedAssetFolder?.FullPath;
        ReloadAssetLibrary(folderPath, null);
    }

    public HierarchyItemViewModel? FindHierarchyById(string? id)
        => id is not null && _hierarchyIndex.TryGetValue(id, out var item) ? item : null;

    public void RegisterHierarchyItem(HierarchyItemViewModel item)
    {
        _hierarchyIndex[item.Id] = item;
        foreach (var child in item.Children)
            RegisterHierarchyItem(child);
    }

    public void UnregisterHierarchyItem(HierarchyItemViewModel item)
        => _hierarchyIndex.Remove(item.Id);

    public void UnregisterHierarchyItemRecursive(HierarchyItemViewModel item)
    {
        foreach (var child in item.Children)
            UnregisterHierarchyItemRecursive(child);
        _hierarchyIndex.Remove(item.Id);
    }

    public HierarchyNodeDto SnapshotHierarchyPublic(HierarchyItemViewModel item)
        => SnapshotHierarchy(item);

    public HierarchyItemViewModel BuildHierarchyItemPublic(HierarchyNodeDto dto)
        => BuildHierarchyItem(dto);

    public ObservableCollection<HierarchyItemViewModel> HierarchyRoots { get; }

    public ObservableCollection<AssetFolderViewModel> AssetRoots { get; }

    public ObservableCollection<AssetItemViewModel> VisibleAssetItems { get; }

    public ObservableCollection<HierarchyItemViewModel> MapRenderableItems { get; }

    public ObservableCollection<MapTileViewModel> MapPreloadedTiles { get; }

    public ObservableCollection<MapGuideLineViewModel> MapGridLines { get; }

    public ObservableCollection<ToolActionViewModel> PrimaryTools { get; }

    public ObservableCollection<ToolActionViewModel> QuickActions { get; }

    public IReadOnlyList<string> ObjectTypes { get; }

    private int _spriteImportModeIndex;
    /// <summary>Sprite Renderer 里"选择图片"的导入方式:0=复制到素材库, 1=引用原路径。</summary>
    public int SpriteImportModeIndex
    {
        get => _spriteImportModeIndex;
        set => SetProperty(ref _spriteImportModeIndex, value);
    }

    public string PackageName
    {
        get => _packageName;
        private set => SetProperty(ref _packageName, value);
    }

    public RelayCommand<string?> SelectToolCommand { get; }

    public RelayCommand<string?> ActivateQuickActionCommand { get; }

    public RelayCommand ToggleGridCommand { get; }

    public RelayCommand ZoomInCommand { get; }

    public RelayCommand ZoomOutCommand { get; }

    public RelayCommand ResetZoomCommand { get; }

    public RelayCommand CycleScaleCommand { get; }

    public RelayCommand AddTagCommand { get; }

    public RelayCommand<string?> RemoveTagCommand { get; }

    public RelayCommand NavigateToSelectionCommand { get; }

    public RelayCommand ShowInspectorMenuCommand { get; }

    public RelayCommand SaveSceneCommand { get; }

    public RelayCommand OpenSettingsCommand { get; }

    public event EventHandler? SettingsRequested;

    private void SaveScene()
    {
        try
        {
            var doc = new ScenePersistDocument
            {
                PackageName = PackageName,
                ShowGrid = ShowGrid,
                ZoomPercent = ZoomPercent,
                FeetPerCell = FeetPerCell,
                HierarchyRoots = HierarchyRoots.Select(SnapshotHierarchyPublic).ToList()
            };
            var path = SceneFileSaver.ResolveDefaultScenePath(AppContext.BaseDirectory);
            SceneFileSaver.SaveScene(path, doc);
            StatusMessage = $"场景已保存：{path}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"场景保存失败：{ex.Message}";
        }
    }

    private void OpenSettings()
    {
        SettingsRequested?.Invoke(this, EventArgs.Empty);
    }

    public void ApplyGlobalSettings(GlobalSettings settings)
    {
        PackageName = settings.PackageName;
        FeetPerCell = settings.FeetPerCell;
        SetZoom(settings.DefaultZoomPercent);
        ShowGrid = settings.ShowGrid;
        StatusMessage = $"全局设置已更新（端口 {settings.AgentTcpPort}）";
    }

    public event EventHandler<SelectionNavigationRequestEventArgs>? NavigationRequested;

    public HierarchyItemViewModel? SelectedHierarchyItem
    {
        get => _selectedHierarchyItem;
        set
        {
            if (SetProperty(ref _selectedHierarchyItem, value))
            {
                UpdateCurrentSelection((IGlobalSelectionItem?)value ?? SelectedAssetItem);
                StatusMessage = value is null
                    ? "未选择任何层级对象"
                    : $"已选中 {value.DisplayName}";

                // 重建右侧面板组件编辑器列表
                value?.RebuildComponentEditors();
            }
        }
    }

    public AssetFolderViewModel? SelectedAssetFolder
    {
        get => _selectedAssetFolder;
        set
        {
            if (SetProperty(ref _selectedAssetFolder, value))
            {
                OnPropertyChanged(nameof(AssetBreadcrumb));
                RefreshVisibleAssets();

                if (value is not null)
                {
                    StatusMessage = $"素材目录切换到 {value.PathDisplay}";
                }
            }
        }
    }

    public AssetItemViewModel? SelectedAssetItem
    {
        get => _selectedAssetItem;
        set
        {
            if (SetProperty(ref _selectedAssetItem, value) && value is not null)
            {
                UpdateCurrentSelection(value);
                StatusMessage = $"已选中素材 {value.Name} ({value.Kind})";
            }
            else if (value is null)
            {
                UpdateCurrentSelection(SelectedHierarchyItem);
            }
        }
    }

    public ToolActionViewModel? SelectedPrimaryTool
    {
        get => _selectedPrimaryTool;
        set
        {
            if (SetProperty(ref _selectedPrimaryTool, value) && value is not null)
            {
                SelectTool(value.Key);
            }
        }
    }

    public ToolActionViewModel? SelectedQuickAction
    {
        get => _selectedQuickAction;
        set
        {
            if (SetProperty(ref _selectedQuickAction, value) && value is not null)
            {
                ActivateQuickAction(value.Key);
            }
        }
    }

    public string AssetSearchText
    {
        get => _assetSearchText;
        set
        {
            if (SetProperty(ref _assetSearchText, value))
            {
                RefreshVisibleAssets();
            }
        }
    }

    public double AssetThumbnailSize
    {
        get => _assetThumbnailSize;
        set
        {
            if (SetProperty(ref _assetThumbnailSize, value))
            {
                OnPropertyChanged(nameof(AssetTileWidth));
            }
        }
    }

    public bool ShowGrid
    {
        get => _showGrid;
        set
        {
            if (SetProperty(ref _showGrid, value))
            {
                RefreshMapViewportDecorations();
            }
        }
    }

    public int ZoomPercent
    {
        get => _zoomPercent;
        private set
        {
            if (SetProperty(ref _zoomPercent, value))
            {
                OnPropertyChanged(nameof(ZoomText));
                OnPropertyChanged(nameof(ZoomScale));
                RefreshMapViewportDecorations();
            }
        }
    }

    public int FeetPerCell
    {
        get => _feetPerCell;
        private set
        {
            if (SetProperty(ref _feetPerCell, value))
            {
                OnPropertyChanged(nameof(ScaleText));
            }
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public string GraphicsRuntimeSummary
    {
        get => _graphicsRuntimeSummary;
        private set => SetProperty(ref _graphicsRuntimeSummary, value);
    }

    public string GraphicsRuntimeHint
    {
        get => _graphicsRuntimeHint;
        private set => SetProperty(ref _graphicsRuntimeHint, value);
    }

    public IGlobalSelectionItem? CurrentSelection
    {
        get => _currentSelection;
        private set
        {
            if (SetProperty(ref _currentSelection, value))
            {
                UpdateHighlightedHierarchyItem(value as HierarchyItemViewModel);
                OnPropertyChanged(nameof(HasSelection));
                OnPropertyChanged(nameof(CurrentSelectionDisplayName));
                OnPropertyChanged(nameof(CurrentSelectionType));
                OnPropertyChanged(nameof(CurrentSelectionHint));
                NavigateToSelectionCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool HasSelection => CurrentSelection is not null;

    public string CurrentSelectionDisplayName
        => CurrentSelection?.DisplayName ?? "未选择";

    public string CurrentSelectionType
        => CurrentSelection?.SelectionType ?? "无";

    public string CurrentSelectionHint
        => CurrentSelection switch
        {
            AssetItemViewModel asset when asset.Kind == "StaticObjectClass" => "当前选中的是静态对象类，导航会定位到素材库中的定义位置。",
            AssetItemViewModel => "当前选中的是素材资源，导航会定位到素材库中的定义位置。",
            HierarchyItemViewModel item when item.CanNavigateToMap => "当前选中的是地图中的对象实例，导航会定位到地图上的实例位置。",
            HierarchyItemViewModel => "当前选中的是地图对象实例，但缺少可定位坐标。",
            _ => "请选择一个对象类或对象实例。"
        };

    public string AssetBreadcrumb => SelectedAssetFolder?.PathDisplay ?? "Assets";

    public string ZoomText => $"{ZoomPercent}%";

    public double ZoomScale => ZoomPercent / 100.0;

    public string ScaleText => $"{FeetPerCell} ft";

    public double AssetTileWidth => AssetThumbnailSize + 22;

    public double MapContentSize => MapViewportConstants.ContentSize;

    public string MapViewportSummary
        => $"中心 {FormatWorldCoordinate(_mapViewportCenterX)}, {FormatWorldCoordinate(_mapViewportCenterY)} | 已预加载 {MapPreloadedTiles.Count} 个地图块";

    public void SetGraphicsRuntimeInfo(GraphicsRuntimeInfo info)
    {
        GraphicsRuntimeSummary = $"渲染后端: {info.BackendSummary}";
        GraphicsRuntimeHint = info.RuntimeHint;
    }

    public HierarchyItemViewModel AddEmptyObject(HierarchyItemViewModel parent)
    {
        var dto = new HierarchyNodeDto
        {
            Id = CreateId("node"),
            Name = GetUniqueHierarchyName(parent, "空对象"),
            Icon = "📦",
            ObjectType = "Empty",
            InstanceId = CreateId("inst").ToUpperInvariant(),
            IsActive = true,
            ScaleX = 1,
            ScaleY = 1,
            Opacity = 1,
            VisionEnabled = false,
            VisionRadius = 0
        };

        _commandBus.Execute(new MapEngine.Avalonia.Commands.VmAddEmptyObjectCommand(this, parent.Id, dto));
        return FindHierarchyById(dto.Id) ?? throw new InvalidOperationException("AddEmptyObject failed");
    }

    public HierarchyItemViewModel? AddRootEmptyObject()
    {
        var root = HierarchyRoots.Count > 0 ? HierarchyRoots[0] : EnsureSceneRoot();
        return AddEmptyObject(root);
    }

    public void RenameHierarchyItem(HierarchyItemViewModel item, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        _commandBus.Execute(new MapEngine.Avalonia.Commands.VmRenameCommand(this, item.Id, name.Trim()));
    }

    public bool DeleteHierarchyItem(HierarchyItemViewModel item)
    {
        if (item.Parent is null)
        {
            StatusMessage = "地图包根节点不允许删除";
            return false;
        }

        _commandBus.Execute(new MapEngine.Avalonia.Commands.VmDeleteHierarchyItemCommand(this, item.Id));
        return true;
    }

    public void MoveItemUp(HierarchyItemViewModel item)
    {
        if (item.Parent is null) return;
        var siblings = item.Parent.Children;
        var index = siblings.IndexOf(item);
        if (index <= 0)
        {
            StatusMessage = "已是第一个，无法上移";
            return;
        }
        _commandBus.Execute(new MapEngine.Avalonia.Commands.VmMoveItemUpCommand(this, item.Id));
    }

    public void MoveItemDown(HierarchyItemViewModel item)
    {
        if (item.Parent is null) return;
        var siblings = item.Parent.Children;
        var index = siblings.IndexOf(item);
        if (index < 0 || index >= siblings.Count - 1)
        {
            StatusMessage = "已是最后一个，无法下移";
            return;
        }
        _commandBus.Execute(new MapEngine.Avalonia.Commands.VmMoveItemDownCommand(this, item.Id));
    }

    public void PromoteItem(HierarchyItemViewModel item)
    {
        if (item.Parent?.Parent is null)
        {
            StatusMessage = "已是顶层，无法提升";
            return;
        }
        _commandBus.Execute(new MapEngine.Avalonia.Commands.VmPromoteItemCommand(this, item.Id));
    }

    public void DemoteItem(HierarchyItemViewModel item)
    {
        if (item.Parent is null) return;
        var siblings = item.Parent.Children;
        var index = siblings.IndexOf(item);
        if (index <= 0)
        {
            StatusMessage = "前面没有对象，无法降低层级";
            return;
        }
        _commandBus.Execute(new MapEngine.Avalonia.Commands.VmDemoteItemCommand(this, item.Id));
    }

    public void AddVisionCone(HierarchyItemViewModel item)
    {
        var cone = new VisionConeViewModel();
        item.VisionCones.Add(cone);
        StatusMessage = $"已添加视野锥 {cone.Name}";
    }

    public void RemoveVisionCone(HierarchyItemViewModel item, VisionConeViewModel cone)
    {
        item.VisionCones.Remove(cone);
        StatusMessage = $"已删除视野锥 {cone.Name}";
    }

    public void AddVisionComponent(HierarchyItemViewModel item)
    {
        if (item.HasComponent<MapEngine.Core.Components.VisionComponent>())
        {
            StatusMessage = "该对象已有视野组件";
            return;
        }

        var vision = new MapEngine.Core.Components.VisionComponent { Enabled = true, Radius = 60 };
        item.AddComponent(vision);
        item.NotifyVisionComponentChanged();
        StatusMessage = $"已为 {item.Name} 添加视野组件";
    }

    public void RemoveVisionComponent(HierarchyItemViewModel item)
    {
        if (!item.HasComponent<MapEngine.Core.Components.VisionComponent>())
        {
            StatusMessage = "该对象没有视野组件";
            return;
        }

        item.RemoveComponent<MapEngine.Core.Components.VisionComponent>();
        item.VisionCones.Clear();
        item.NotifyVisionComponentChanged();
        StatusMessage = $"已移除 {item.Name} 的视野组件";
    }

    public void AddWallComponent(HierarchyItemViewModel item)
    {
        if (item.HasComponent<MapEngine.Core.Components.WallComponent>())
        {
            StatusMessage = "该对象已有墙壁组件";
            return;
        }

        var wall = WallPresets.Normal();
        wall.X1 = item.X; wall.Y1 = item.Y;
        wall.X2 = item.X + 100; wall.Y2 = item.Y;

        item.AddComponent(wall);
        StatusMessage = $"已为 {item.Name} 添加墙壁组件";
    }

    public HierarchyItemViewModel? CreateWallObject(HierarchyItemViewModel parent)
    {
        var dto = new HierarchyNodeDto
        {
            Id = CreateId("node"),
            Name = GetUniqueHierarchyName(parent, "墙壁"),
            Icon = "🧱",
            ObjectType = "Empty",
            InstanceId = CreateId("inst").ToUpperInvariant(),
            IsActive = true,
            SortOrder = 0,
            X = 0,
            Y = 0,
            HasMapPosition = false
        };

        // 添加墙壁组件
        var wall = WallPresets.Normal();
        wall.X1 = 0; wall.Y1 = 0; wall.X2 = 100; wall.Y2 = 0;
        dto.Components["WallComponent"] = wall;

        _commandBus.Execute(new MapEngine.Avalonia.Commands.VmAddEmptyObjectCommand(this, parent.Id, dto));
        var created = FindHierarchyById(dto.Id);
        StatusMessage = $"已创建墙壁对象 {dto.Name}";
        return created;
    }

    public void RemoveWallComponent(HierarchyItemViewModel item)
    {
        if (!item.HasComponent<MapEngine.Core.Components.WallComponent>())
        {
            StatusMessage = "该对象没有墙壁组件";
            return;
        }

        item.RemoveComponent<MapEngine.Core.Components.WallComponent>();
        StatusMessage = $"已移除 {item.Name} 的墙壁组件";
    }

    public void AddTokenComponent(HierarchyItemViewModel item)
    {
        if (item.HasComponent<MapEngine.Core.Components.TokenComponent>())
        {
            StatusMessage = "该对象已有 Token 组件";
            return;
        }

        var token = new MapEngine.Core.Components.TokenComponent
        {
            TokenName = item.Name,
            InitiativeOrder = 0,
            IsPlayerControlled = false,
            MovementSpeed = 30
        };

        item.AddComponent(token);
        StatusMessage = $"已为 {item.Name} 添加 Token 组件";
    }

    public void RemoveTokenComponent(HierarchyItemViewModel item)
    {
        if (!item.HasComponent<MapEngine.Core.Components.TokenComponent>())
        {
            StatusMessage = "该对象没有 Token 组件";
            return;
        }

        item.RemoveComponent<MapEngine.Core.Components.TokenComponent>();
        StatusMessage = $"已移除 {item.Name} 的 Token 组件";
    }

    public void CopyHierarchyItem(HierarchyItemViewModel item)
    {
        _hierarchyClipboard = SnapshotHierarchy(item);
        StatusMessage = $"已复制层级对象 {item.DisplayName}";
    }

    public HierarchyItemViewModel? PasteHierarchyItem(HierarchyItemViewModel parent)
    {
        if (_hierarchyClipboard is null)
        {
            StatusMessage = "当前没有可粘贴的层级对象";
            return null;
        }

        var snapshot = CloneHierarchySnapshot(_hierarchyClipboard);
        snapshot.Name = GetUniqueHierarchyName(parent, snapshot.Name);
        _commandBus.Execute(new MapEngine.Avalonia.Commands.VmAddEmptyObjectCommand(this, parent.Id, snapshot));
        return FindHierarchyById(snapshot.Id);
    }

    public HierarchyItemViewModel? PasteHierarchyToRoot()
    {
        // 粘贴到当前选中对象的父级（同级粘贴）。
        // 若没有选中，或选中的是包根节点，则粘贴到第一个根节点下。
        var targetParent = SelectedHierarchyItem?.Parent ?? HierarchyRoots.FirstOrDefault();
        return targetParent is null ? null : PasteHierarchyItem(targetParent);
    }

    public HierarchyItemViewModel? DuplicateHierarchyItem(HierarchyItemViewModel item)
    {
        if (item.Parent is null)
        {
            StatusMessage = "地图包根节点不支持复制副本";
            return null;
        }

        var snapshot = CloneHierarchySnapshot(SnapshotHierarchy(item));
        snapshot.Name = GetDuplicateName(item.Parent.Children.Select(child => child.Name), item.Name);
        _commandBus.Execute(new MapEngine.Avalonia.Commands.VmAddEmptyObjectCommand(this, item.Parent.Id, snapshot));
        return FindHierarchyById(snapshot.Id);
    }

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

        var targetPath = AssetLibraryFileSystemService.RenameFolder(folder.FullPath, name.Trim());
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

        var folderPath = AssetLibraryFileSystemService.CopyFolderToParent(_assetFolderClipboardPath, parent.FullPath);
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

        var folderPath = AssetLibraryFileSystemService.CopyFolderToParent(folder.FullPath, folder.Parent.FullPath);
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

        var targetPath = AssetLibraryFileSystemService.RenameFile(item.FullPath, name.Trim());
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

        var filePath = AssetLibraryFileSystemService.CopyFileToFolder(_assetItemClipboardPath, folder.FullPath);
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

        var filePath = AssetLibraryFileSystemService.CopyFileToFolder(item.FullPath, folder.FullPath);
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
            && !_mapDragPreviewItem.SourceAssetPath.Equals(asset.FullPath, StringComparison.OrdinalIgnoreCase))
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
            SelectedAssetItem = GetAssetItemByPath(committed.SourceAssetPath);
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

    private void SelectTool(string? key)
    {
        key ??= "select";

        foreach (var tool in PrimaryTools)
        {
            tool.IsSelected = tool.Key.Equals(key, StringComparison.OrdinalIgnoreCase);
        }

        var active = PrimaryTools.FirstOrDefault(tool => tool.IsSelected);
        if (active is not null)
        {
            if (!ReferenceEquals(SelectedPrimaryTool, active))
            {
                SetProperty(ref _selectedPrimaryTool, active, nameof(SelectedPrimaryTool));
            }

            StatusMessage = $"当前工具: {active.Label}";
        }
    }

    private void ActivateQuickAction(string? key)
    {
        foreach (var action in QuickActions)
        {
            action.IsSelected = action.Key.Equals(key, StringComparison.OrdinalIgnoreCase);
        }

        var active = QuickActions.FirstOrDefault(action => action.IsSelected);
        if (active is not null)
        {
            if (!ReferenceEquals(SelectedQuickAction, active))
            {
                SetProperty(ref _selectedQuickAction, active, nameof(SelectedQuickAction));
            }

            StatusMessage = $"已打开 {active.Label} 面板入口";
        }
    }

    private void ToggleGrid()
    {
        ShowGrid = !ShowGrid;
        StatusMessage = ShowGrid ? "已显示网格参考线" : "已隐藏网格参考线";
    }

    private void SetZoom(int zoom)
    {
        ZoomPercent = Math.Clamp(zoom, 25, 300);
    }

    public void SetZoomFromInteraction(int zoom)
        => SetZoom(zoom);

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
            5 => 10,
            10 => 15,
            _ => 5
        };

        StatusMessage = $"地图比例尺已切换为 1 格 = {ScaleText}";
    }

    private void AddTag()
    {
        if (SelectedHierarchyItem is null)
        {
            return;
        }

        var nextTag = $"NewTag{SelectedHierarchyItem.Tags.Count + 1}";
        SelectedHierarchyItem.Tags.Add(nextTag);
        StatusMessage = $"已为 {SelectedHierarchyItem.Name} 添加标签 {nextTag}";
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
        var folderIds = GetSelectedFolderIds();
        var search = AssetSearchText.Trim();

        var items = _allAssetItems
            .Where(item => folderIds.Count == 0 || folderIds.Contains(item.FolderId))
            .Where(item =>
                string.IsNullOrWhiteSpace(search)
                || item.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
                || item.Description.Contains(search, StringComparison.OrdinalIgnoreCase)
                || item.Kind.Contains(search, StringComparison.OrdinalIgnoreCase))
            .OrderBy(item => item.Kind)
            .ThenBy(item => item.Name)
            .ToList();

        VisibleAssetItems.Clear();
        foreach (var item in items)
        {
            VisibleAssetItems.Add(item);
        }

        if (SelectedAssetItem is not null && !VisibleAssetItems.Contains(SelectedAssetItem))
        {
            SelectedAssetItem = null;
        }
    }

    private HashSet<string> GetSelectedFolderIds()
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (SelectedAssetFolder is null)
        {
            return result;
        }

        CollectFolderIds(SelectedAssetFolder, result);
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

    private HierarchyItemViewModel? FindHierarchy(string? id)
        => id is not null && _hierarchyIndex.TryGetValue(id, out var item) ? item : null;

    private AssetFolderViewModel? FindAssetFolder(string? id)
        => id is not null && _assetFolderIndex.TryGetValue(id, out var item) ? item : null;

    private AssetFolderViewModel? FindAssetFolderByPath(string? path)
        => path is null ? null : FindAssetFolder(NormalizePath(path));

    private AssetItemViewModel? FindAssetItemByPath(string? path)
        => path is null
            ? null
            : _allAssetItems.FirstOrDefault(item => item.Id.Equals(NormalizePath(path), StringComparison.OrdinalIgnoreCase));

    private void UpdateCurrentSelection(IGlobalSelectionItem? selection)
    {
        CurrentSelection = selection;
    }

    private void UpdateHighlightedHierarchyItem(HierarchyItemViewModel? item)
    {
        if (ReferenceEquals(_highlightedHierarchyItem, item))
        {
            return;
        }

        if (_highlightedHierarchyItem is not null)
        {
            _highlightedHierarchyItem.IsSelected = false;
        }

        _highlightedHierarchyItem = item;

        if (_highlightedHierarchyItem is not null)
        {
            _highlightedHierarchyItem.IsSelected = true;
        }
    }

    private HierarchyItemViewModel CreateHierarchyFromSnapshot(HierarchyNodeDto snapshot, HierarchyItemViewModel parent)
    {
        var item = BuildHierarchyItem(snapshot);
        item.Parent = parent;
        parent.Children.Add(item);
        RefreshMapRenderableItems();
        SelectedHierarchyItem = item;
        return item;
    }

    private static HierarchyNodeDto SnapshotHierarchy(HierarchyItemViewModel item)
    {
        var dto = new HierarchyNodeDto
        {
            Id = item.Id,
            Name = item.Name,
            Icon = item.Icon,
            ObjectType = item.ObjectType,
            InstanceId = item.InstanceId,
            IsActive = item.IsActive,
            IsLocked = item.IsLocked,
            SortOrder = item.SortOrder,
            HasMapPosition = item.HasMapPosition,
            SourceAssetPath = item.SourceAssetPath,
            SourceAssetKind = item.SourceAssetKind,
            SourceAssetName = item.SourceAssetName,
            Tags = item.Tags.ToList(),
            Children = item.Children.Select(SnapshotHierarchy).ToList()
        };

        // 从 BackingObject 收集组件到 Components 字典(供持久化兼容)
        dto.Components = new Dictionary<string, object>();
        if (item.BackingObject.GetComponent<WallComponent>() is { } wall)
        {
            dto.Components["WallComponent"] = new Dictionary<string, object>
            {
                ["X1"] = wall.X1,
                ["Y1"] = wall.Y1,
                ["X2"] = wall.X2,
                ["Y2"] = wall.Y2,
                ["Sight"] = (int)wall.Sight,
                ["Move"] = (int)wall.Move,
                ["Sound"] = (int)wall.Sound,
                ["Light"] = (int)wall.Light,
                ["Dir"] = (int)wall.Dir,
                ["Door"] = (int)wall.Door,
                ["State"] = (int)wall.State,
                ["Thickness"] = wall.Thickness,
                ["TileTexturePath"] = wall.TileTexturePath ?? "",
                ["NoCutaway"] = wall.NoCutaway
            };
        }

        // 写入新 V2 结构化字段
        dto.TransformV2 = new MapEngine.Core.Data.TransformData
        {
            X = item.X, Y = item.Y, Z = item.Z,
            Rotation = item.Rotation,
            ScaleX = item.ScaleX, ScaleY = item.ScaleY
        };
        // 同步旧字段
        dto.X = item.X; dto.Y = item.Y; dto.Z = item.Z;
        dto.Rotation = item.Rotation;
        dto.ScaleX = item.ScaleX; dto.ScaleY = item.ScaleY;

        if (!string.IsNullOrEmpty(item.SourceAssetPath))
        {
            dto.SpriteV2 = new MapEngine.Core.Data.SpriteData
            {
                TexturePath = item.SourceAssetPath,
                Opacity = item.Opacity,
                TintColor = item.SpriteColor,
                AlignX = item.SpriteAlignX,
                AlignY = item.SpriteAlignY,
                HitTestEnabled = item.SpriteHitTestEnabled
            };
        }
        // 同步旧字段
        dto.SourceAssetPath = item.SourceAssetPath;
        dto.Opacity = item.Opacity;
        dto.SpriteColor = item.SpriteColor;

        if (item.VisionEnabled || item.VisionCones.Count > 0)
        {
            dto.VisionV2 = new MapEngine.Core.Data.VisionData
            {
                Enabled = item.VisionEnabled,
                Radius = item.VisionRadius,
                Orientation = item.Orientation,
                Cones = item.VisionCones.Select(c => new MapEngine.Core.Data.VisionConeData
                {
                    Id = c.Id,
                    Name = c.Name,
                    CenterOffset = c.CenterOffset,
                    Range = c.Range,
                    FieldOfView = c.FieldOfView,
                    IsEnabled = c.IsEnabled
                }).ToList()
            };
        }
        // 同步旧字段
        dto.VisionEnabled = item.VisionEnabled;
        dto.VisionRadius = item.VisionRadius;
        dto.Orientation = item.Orientation;
        dto.VisionCones = item.VisionCones.Select(c => c.ToDto()).ToList();

        return dto;
    }

    private static HierarchyNodeDto CloneHierarchySnapshot(HierarchyNodeDto source)
    {
        return new HierarchyNodeDto
        {
            Id = CreateId("node"),
            Name = source.Name,
            Icon = source.Icon,
            ObjectType = source.ObjectType,
            InstanceId = CreateId("inst").ToUpperInvariant(),
            IsActive = source.IsActive,
            IsLocked = source.IsLocked,
            SortOrder = source.SortOrder,
            X = source.X,
            Y = source.Y,
            Z = source.Z,
            Rotation = source.Rotation,
            ScaleX = source.ScaleX,
            ScaleY = source.ScaleY,
            SpriteColor = source.SpriteColor,
            Opacity = source.Opacity,
            HasMapPosition = source.HasMapPosition,
            SourceAssetPath = source.SourceAssetPath,
            SourceAssetKind = source.SourceAssetKind,
            SourceAssetName = source.SourceAssetName,
            VisionEnabled = source.VisionEnabled,
            VisionRadius = source.VisionRadius,
            Orientation = source.Orientation,
            VisionCones = source.VisionCones.Select(c => new VisionConeDto
            {
                Id = $"cone-{Guid.NewGuid():N}",
                Name = c.Name,
                CenterOffset = c.CenterOffset,
                Range = c.Range,
                FieldOfView = c.FieldOfView,
                IsEnabled = c.IsEnabled
            }).ToList(),
            Tags = source.Tags.ToList(),
            Components = new Dictionary<string, object>(source.Components),
            Children = source.Children.Select(CloneHierarchySnapshot).ToList()
        };
    }

    private void RemoveHierarchyIndex(HierarchyItemViewModel item)
    {
        foreach (var child in item.Children.ToList())
        {
            RemoveHierarchyIndex(child);
        }

        _hierarchyIndex.Remove(item.Id);
    }

    private void RemoveAssetFolder(AssetFolderViewModel folder)
    {
        foreach (var child in folder.Children.ToList())
        {
            RemoveAssetFolder(child);
        }

        var folderIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        CollectFolderIds(folder, folderIds);
        _allAssetItems.RemoveAll(item => folderIds.Contains(item.FolderId));
        _assetFolderIndex.Remove(folder.Id);
        RefreshVisibleAssets();
    }

    private void ReloadAssetLibrary(string? selectedFolderPath, string? selectedItemPath)
    {
        var snapshot = AssetLibraryFileSystemService.Load(_assetLibraryRootPath);

        _assetFolderIndex.Clear();
        AssetRoots.Clear();
        foreach (var root in snapshot.RootFolders.Select(folder => BuildAssetFolder(folder, null)))
        {
            AssetRoots.Add(root);
        }

        _allAssetItems.Clear();
        foreach (var item in snapshot.AssetItems)
        {
            _allAssetItems.Add(new AssetItemViewModel(item.Id, item.FolderId, item.FullPath, item.FileName, item.Name, item.Kind, item.Icon, item.Description));
        }

        var folder = FindAssetFolderByPath(selectedFolderPath)
            ?? AssetRoots.FirstOrDefault()?.Children.FirstOrDefault()
            ?? AssetRoots.FirstOrDefault();

        SelectedAssetFolder = folder;

        var selectedItem = FindAssetItemByPath(selectedItemPath);
        SelectedAssetItem = selectedItem is not null && VisibleAssetItems.Contains(selectedItem) ? selectedItem : null;
    }

    private string GetUniqueHierarchyName(HierarchyItemViewModel parent, string baseName)
    {
        return GetUniqueName(parent.Children.Select(child => child.Name), baseName);
    }

    private string GetUniqueFolderName(AssetFolderViewModel parent, string baseName)
    {
        return GetUniqueName(parent.Children.Select(child => child.Name), baseName);
    }

    private string GetUniqueAssetItemName(string folderId, string baseName)
    {
        return GetUniqueName(_allAssetItems.Where(item => item.FolderId == folderId).Select(item => item.Name), baseName);
    }

    private static string GetUniqueName(IEnumerable<string> existingNames, string baseName)
    {
        var nameSet = new HashSet<string>(existingNames, StringComparer.OrdinalIgnoreCase);
        if (!nameSet.Contains(baseName))
        {
            return baseName;
        }

        var index = 2;
        while (nameSet.Contains($"{baseName} {index}"))
        {
            index++;
        }

        return $"{baseName} {index}";
    }

    private static string GetDuplicateName(IEnumerable<string> existingNames, string baseName)
        => GetUniqueName(existingNames, $"{baseName} 副本");

    private HierarchyItemViewModel? ResolveDefaultDropParent()
    {
        if (SelectedHierarchyItem is not null)
            return SelectedHierarchyItem;

        if (HierarchyRoots.Count > 0)
            return HierarchyRoots[0];

        // 空场景时自动建根节点
        return EnsureSceneRoot();
    }

    /// <summary>保证场景至少有一个根节点。返回现有或新建的根节点。</summary>
    private HierarchyItemViewModel EnsureSceneRoot()
    {
        if (HierarchyRoots.Count > 0)
            return HierarchyRoots[0];

        var dto = new HierarchyNodeDto
        {
            Id = CreateId("node"),
            Name = "场景",
            Icon = "🗺️",
            ObjectType = "Scene",
            IsActive = true,
            SortOrder = 0,
            ScaleX = 1,
            ScaleY = 1,
            Opacity = 1,
            VisionEnabled = false,
            VisionRadius = 0,
            Orientation = 0,
            VisionCones = [],
            Tags = []
        };

        var root = BuildHierarchyItem(dto);
        HierarchyRoots.Add(root);
        // 注意:不在这里调 RefreshMapRenderableItems——构造函数末尾会统一刷
        // 若在构造完成后动态调用此方法则需自行刷,见调用方判断
        if (MapRenderableItems is not null)
            RefreshMapRenderableItems();
        return root;
    }

    private HierarchyItemViewModel CreateInstanceFromAsset(
        AssetItemViewModel asset,
        HierarchyItemViewModel parent,
        double? x,
        double? y,
        bool isPreview = false,
        bool updateSelection = true,
        bool updateStatus = true)
    {
        var instanceName = GetUniqueHierarchyName(parent, asset.InstanceBaseName);

        // 读取 .asset 文件里的组件数据
        var assetDoc = TryLoadAssetDocument(asset.FullPath);
        var spriteProps = assetDoc?.Components
            .FirstOrDefault(c => c.Type.Equals("SpriteRenderer", StringComparison.OrdinalIgnoreCase))?.Properties;
        var visionProps = assetDoc?.Components
            .FirstOrDefault(c => c.Type.Equals("Vision", StringComparison.OrdinalIgnoreCase))?.Properties;

        var spriteColor = spriteProps?.GetValueOrDefault("spriteColor") ?? "#FF4444";
        var opacity = double.TryParse(spriteProps?.GetValueOrDefault("opacity"), out var op) ? op : 1.0;
        var visionEnabled = bool.TryParse(visionProps?.GetValueOrDefault("enabled"), out var ve) && ve;
        var visionRadius = double.TryParse(visionProps?.GetValueOrDefault("radius"), out var vr) ? vr : 60.0;

        var dto = new HierarchyNodeDto
        {
            Id = CreateId("node"),
            Name = instanceName,
            Icon = asset.Icon,
            ObjectType = ResolveHierarchyObjectType(asset),
            InstanceId = CreateId("inst").ToUpperInvariant(),
            IsActive = true,
            SortOrder = 0,
            ScaleX = 1,
            ScaleY = 1,
            SpriteColor = spriteColor,
            Opacity = opacity,
            VisionEnabled = visionEnabled,
            VisionRadius = visionRadius,
            Orientation = 0,
            // 视野开启时自动生成一个全向视野锥，否则 BuildVision 空循环不渲染
            VisionCones = visionEnabled
                ? [new VisionConeDto
                    {
                        Id = CreateId("cone"),
                        Name = "默认视野",
                        CenterOffset = 0,
                        // .asset 的 radius 语义是"英尺"(D&D 视距),按每格英尺数换算成格子数;
                        // 渲染端再 Range * CellSize 得像素。除以 CellSize 是把英尺当像素的旧 bug。
                        Range = visionRadius / Math.Max(1, FeetPerCell),
                        FieldOfView = 360,
                        IsEnabled = true
                    }]
                : [],
            X = x ?? 0,
            Y = y ?? 0,
            HasMapPosition = x.HasValue && y.HasValue,
            SourceAssetPath = asset.FullPath,
            SourceAssetKind = asset.Kind,
            SourceAssetName = asset.Name,
            Tags =
            [
                $"AssetKind:{asset.Kind}",
                $"AssetName:{asset.Name}"
            ]
        };

        if (isPreview)
        {
            var instance = new HierarchyItemViewModel(dto) { Parent = parent };
            instance.IsPreviewInstance = true;
            parent.Children.Add(instance);
            _hierarchyIndex[instance.Id] = instance;
            RefreshMapRenderableItems();
            if (updateSelection)
            {
                SelectedAssetItem = asset;
                SelectedHierarchyItem = instance;
            }
            return instance;
        }

        _commandBus.Execute(new MapEngine.Avalonia.Commands.VmCreateInstanceCommand(this, parent.Id, dto));
        var created = FindHierarchyById(dto.Id)!;

        if (updateSelection)
        {
            SelectedAssetItem = asset;
        }

        if (updateStatus)
        {
            StatusMessage = $"已从素材 {asset.Name} 生成实例 {created.Name}";
        }

        return created;
    }

    private static MapEngine.Avalonia.Services.StaticObjectAssetDocument? TryLoadAssetDocument(string fullPath)
    {
        try
        {
            if (!File.Exists(fullPath)) return null;
            var json = File.ReadAllText(fullPath);
            return System.Text.Json.JsonSerializer.Deserialize<MapEngine.Avalonia.Services.StaticObjectAssetDocument>(
                json, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch { return null; }
    }

    private static string ResolveHierarchyObjectType(AssetItemViewModel asset)
        => asset.Kind switch
        {
            "Audio" => "AudioSource",
            "Prefab" => "PrefabInstance",
            "Map" => "Map",
            "StaticObjectClass" => "StaticObject",
            _ => "Prop"
        };

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
        var preloadMargin = MapViewportConstants.TileSize;
        var startX = (int)Math.Floor((contentLeft - preloadMargin) / MapViewportConstants.TileSize);
        var endX = (int)Math.Ceiling((contentRight + preloadMargin) / MapViewportConstants.TileSize);
        var startY = (int)Math.Floor((contentTop - preloadMargin) / MapViewportConstants.TileSize);
        var endY = (int)Math.Ceiling((contentBottom + preloadMargin) / MapViewportConstants.TileSize);
        var originTileIndex = (int)(MapViewportConstants.WorldOriginContent / MapViewportConstants.TileSize);

        MapPreloadedTiles.Clear();
        for (var tileY = startY; tileY <= endY; tileY++)
        {
            for (var tileX = startX; tileX <= endX; tileX++)
            {
                var worldTileX = tileX - originTileIndex;
                var worldTileY = originTileIndex - tileY - 1;
                var isAxisTile = worldTileX == 0 || worldTileY == 0;
                var isEven = ((worldTileX + worldTileY) & 1) == 0;

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
        if (!ShowGrid)
        {
            return;
        }

        var preloadMargin = MapViewportConstants.CellSize * 2;
        var expandedLeft = Math.Max(0, contentLeft - preloadMargin);
        var expandedTop = Math.Max(0, contentTop - preloadMargin);
        var expandedRight = Math.Min(MapContentSize, contentRight + preloadMargin);
        var expandedBottom = Math.Min(MapContentSize, contentBottom + preloadMargin);
        var startColumn = (int)Math.Floor(expandedLeft / MapViewportConstants.CellSize);
        var endColumn = (int)Math.Ceiling(expandedRight / MapViewportConstants.CellSize);
        var startRow = (int)Math.Floor(expandedTop / MapViewportConstants.CellSize);
        var endRow = (int)Math.Ceiling(expandedBottom / MapViewportConstants.CellSize);
        var originCellIndex = (int)(MapViewportConstants.WorldOriginContent / MapViewportConstants.CellSize);

        for (var column = startColumn; column <= endColumn; column++)
        {
            var x = column * MapViewportConstants.CellSize;
            var isAxis = column == originCellIndex;
            var isMajor = !isAxis && Math.Abs(column - originCellIndex) % 5 == 0;
            MapGridLines.Add(new MapGuideLineViewModel(
                x,
                expandedTop,
                isAxis ? 2 : 1,
                expandedBottom - expandedTop,
                isAxis ? "#7087A6" : isMajor ? "#3D4B61" : "#293140"));
        }

        for (var row = startRow; row <= endRow; row++)
        {
            var y = row * MapViewportConstants.CellSize;
            var isAxis = row == originCellIndex;
            var isMajor = !isAxis && Math.Abs(row - originCellIndex) % 5 == 0;
            MapGridLines.Add(new MapGuideLineViewModel(
                expandedLeft,
                y,
                expandedRight - expandedLeft,
                isAxis ? 2 : 1,
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

public interface IGlobalSelectionItem
{
    string DisplayName { get; }

    string SelectionType { get; }
}

public enum SelectionNavigationTarget
{
    Map,
    AssetLibrary
}

public sealed class SelectionNavigationRequestEventArgs : EventArgs
{
    public SelectionNavigationRequestEventArgs(SelectionNavigationTarget target, IGlobalSelectionItem selection)
    {
        Target = target;
        Selection = selection;
    }

    public SelectionNavigationTarget Target { get; }

    public IGlobalSelectionItem Selection { get; }
}

public sealed class HierarchyItemViewModel : ViewModelBase, IGlobalSelectionItem
{
    private static readonly IBrush DefaultMapBackgroundBrush = new SolidColorBrush(Color.Parse("#26262ACC"));
    private static readonly IBrush SelectedMapBackgroundBrush = new SolidColorBrush(Color.Parse("#2A6FB0CC"));

    // UI 专用字段(不在 Core GameObject 里)
    private readonly string _id;  // 原始 dto.Id 字符串,命令/索引层用
    private string _instanceId;
    private bool _isPreviewInstance;
    private bool _isSelected;

    /// <summary>底层 Core GameObject,持有真正的数据和组件。</summary>
    public GameObject BackingObject { get; }

    public HierarchyItemViewModel(HierarchyNodeDto dto)
    {
        // VM 层 Id 保留原始 dto.Id 字符串(命令/索引层用)
        // BackingObject.Id 是 Core World 用的 Guid,两者独立
        _id = dto.Id;

        // 创建底层 GameObject
        BackingObject = new GameObject
        {
            Id = Guid.TryParse(dto.Id, out var gid) ? gid : Guid.NewGuid(),
            Name = dto.Name,
            Icon = dto.Icon,
            ObjectType = dto.ObjectType,
            IsActive = dto.IsActive,
            IsLocked = dto.IsLocked,
            SortOrder = dto.SortOrder,
            Tags = [.. dto.Tags]
        };

        // 挂载 TransformComponent(永远存在)
        var transform = new TransformComponent();
        if (dto.TransformV2.HasValue)
        {
            var t = dto.TransformV2.Value;
            transform.X = t.X;
            transform.Y = t.Y;
            transform.Z = t.Z;
            transform.Rotation = t.Rotation;
            transform.ScaleX = t.ScaleX;
            transform.ScaleY = t.ScaleY;
        }
        else
        {
            transform.X = dto.X;
            transform.Y = dto.Y;
            transform.Z = dto.Z;
            transform.Rotation = dto.Rotation;
            transform.ScaleX = dto.ScaleX;
            transform.ScaleY = dto.ScaleY;
        }
        transform.HasMapPosition = dto.HasMapPosition;
        BackingObject.AddComponent(transform);

        // 挂载 SpriteRendererComponent(永远存在)
        var sprite = new SpriteRendererComponent();
        if (dto.SpriteV2.HasValue)
        {
            var s = dto.SpriteV2.Value;
            sprite.SourceAssetPath = s.TexturePath;
            sprite.Opacity = s.Opacity;
            sprite.Color = s.TintColor;
            sprite.AlignX = s.AlignX;
            sprite.AlignY = s.AlignY;
            sprite.HitTestEnabled = s.HitTestEnabled;
        }
        else
        {
            sprite.SourceAssetPath = dto.SourceAssetPath;
            sprite.Opacity = dto.Opacity;
            sprite.Color = dto.SpriteColor;
            // V1 没有 AlignX/AlignY,用默认值 1
        }
        sprite.SourceAssetKind = dto.SourceAssetKind;
        sprite.SourceAssetName = dto.SourceAssetName;
        BackingObject.AddComponent(sprite);

        // 挂载 VisionComponent(可选组件):仅当 dto 实际携带视野数据时挂载。
        // 新建对象默认不挂 Vision(dto.VisionEnabled 默认 false),需要时经"Add Component"菜单添加。
        var visionSource = dto.VisionV2;
        var hasVisionData = visionSource.HasValue || dto.VisionEnabled || dto.VisionCones.Count > 0;
        if (hasVisionData)
        {
            var vision = new VisionComponent();
            if (visionSource.HasValue)
            {
                var v = visionSource.Value;
                vision.Enabled = v.Enabled;
                vision.Radius = v.Radius;
                vision.Orientation = v.Orientation;
                vision.VisionCones = [.. v.Cones];
            }
            else
            {
                vision.Enabled = dto.VisionEnabled;
                vision.Radius = dto.VisionRadius;
                vision.Orientation = dto.Orientation;
                vision.VisionCones = dto.VisionCones.Select(ToConeData).ToList();
            }
            BackingObject.AddComponent(vision);

            // bug#3 修复:把视野锥填充进 VM 集合(渲染读的是 VM 集合,构造期不填充则加载后不渲染)
            foreach (var cone in vision.VisionCones)
            {
                VisionCones.Add(new VisionConeViewModel(new VisionConeDto
                {
                    Id = cone.Id,
                    Name = cone.Name,
                    CenterOffset = cone.CenterOffset,
                    Range = cone.Range,
                    FieldOfView = cone.FieldOfView,
                    IsEnabled = cone.IsEnabled
                }));
            }
        }

        // VM 集合任何增删都同步回 Core VisionComponent
        VisionCones.CollectionChanged += (_, _) => SyncVisionConesToCore();

        // 挂载 WallComponent(如果 dto.Components 里有)
        if (dto.Components.TryGetValue("WallComponent", out var wallData)
            && wallData is Dictionary<string, object> wallDict)
        {
            var wall = new WallComponent();
            if (wallDict.TryGetValue("X1", out var x1)) wall.X1 = Convert.ToDouble(x1);
            if (wallDict.TryGetValue("Y1", out var y1)) wall.Y1 = Convert.ToDouble(y1);
            if (wallDict.TryGetValue("X2", out var x2)) wall.X2 = Convert.ToDouble(x2);
            if (wallDict.TryGetValue("Y2", out var y2)) wall.Y2 = Convert.ToDouble(y2);
            if (wallDict.TryGetValue("Sight", out var sv))
                wall.Sight = (SenseLevel)(int)sv;
            if (wallDict.TryGetValue("Move", out var mv))
                wall.Move = (SenseLevel)(int)mv;
            if (wallDict.TryGetValue("Sound", out var snd))
                wall.Sound = (SenseLevel)(int)snd;
            if (wallDict.TryGetValue("Light", out var lgt))
                wall.Light = (SenseLevel)(int)lgt;
            if (wallDict.TryGetValue("Dir", out var dir))
                wall.Dir = (WallDir)(int)dir;
            if (wallDict.TryGetValue("Door", out var door))
                wall.Door = (DoorKind)(int)door;
            if (wallDict.TryGetValue("State", out var state))
                wall.State = (DoorState)(int)state;
            if (wallDict.TryGetValue("Thickness", out var thick))
                wall.Thickness = Convert.ToDouble(thick);
            if (wallDict.TryGetValue("TileTexturePath", out var tex))
                wall.TileTexturePath = (string)tex;
            if (wallDict.TryGetValue("NoCutaway", out var noCut))
                wall.NoCutaway = Convert.ToBoolean(noCut);
            BackingObject.AddComponent(wall);
        }

        // UI 专用字段
        _instanceId = dto.InstanceId;

        // Children/Tags
        Children = [];
        Tags = new ObservableCollection<string>(dto.Tags);

        // 组件编辑器列表(Inspector 动态渲染的数据源),依据 BackingObject 实际组件构建
        RebuildComponentEditors();
    }

    public string Id => _id;

    // ── 组件编辑器(Inspector 数据驱动)────────────────────────

    /// <summary>Inspector 里的组件折叠块列表,反映 BackingObject 上真实挂载的组件。</summary>
    public ObservableCollection<ComponentEditorViewModel> ComponentEditors { get; } = [];

    /// <summary>依据 BackingObject.Components 重建编辑器列表。任何组件增删后调用。</summary>
    public void RebuildComponentEditors()
    {
        ComponentEditors.Clear();
        foreach (var component in BackingObject.Components)
        {
            ComponentEditors.Add(component switch
            {
                TransformComponent t => new TransformComponentEditor(this, t),
                SpriteRendererComponent s => new SpriteRendererComponentEditor(this, s),
                VisionComponent v => new VisionComponentEditor(this, v),
                WallComponent w => new WallComponentEditor(this, w),
                TokenComponent tk => new TokenComponentEditor(this, tk),
                _ => new GenericComponentEditor(this, component)
            });
        }
    }

    /// <summary>从对象上移除某个组件编辑器对应的组件,并做类型相关清理 + 重建列表。</summary>
    public void RemoveComponentEditor(ComponentEditorViewModel editor)
    {
        if (!editor.CanRemove) return;

        switch (editor)
        {
            case VisionComponentEditor:
                BackingObject.RemoveComponent<VisionComponent>();
                VisionCones.Clear();
                NotifyVisionComponentChanged();
                break;
            case WallComponentEditor:
                BackingObject.RemoveComponent<WallComponent>();
                NotifyWallComponentChanged();
                break;
            default:
                BackingObject.RemoveComponent(editor.Component);
                break;
        }

        RebuildComponentEditors();
    }

    public HierarchyItemViewModel? Parent { get; set; }

    public ObservableCollection<HierarchyItemViewModel> Children { get; }

    public ObservableCollection<string> Tags { get; }

    // ── 基本属性委托 ──────────────────────────────────────

    public string Name
    {
        get => BackingObject.Name;
        set
        {
            if (BackingObject.Name != value)
            {
                BackingObject.Name = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DisplayName));
            }
        }
    }

    public string Icon
    {
        get => BackingObject.Icon;
        set
        {
            if (BackingObject.Icon != value)
            {
                BackingObject.Icon = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DisplayName));
                OnPropertyChanged(nameof(ShouldRenderOnMap));
            }
        }
    }

    public string ObjectType
    {
        get => BackingObject.ObjectType;
        set
        {
            if (BackingObject.ObjectType != value)
            {
                BackingObject.ObjectType = value;
                OnPropertyChanged();
            }
        }
    }

    public string InstanceId
    {
        get => _instanceId;
        set => SetProperty(ref _instanceId, value);
    }

    public bool IsActive
    {
        get => BackingObject.IsActive;
        set
        {
            if (BackingObject.IsActive != value)
            {
                BackingObject.IsActive = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ShouldRenderOnMap));
            }
        }
    }

    public bool IsLocked
    {
        get => BackingObject.IsLocked;
        set
        {
            if (BackingObject.IsLocked != value)
            {
                BackingObject.IsLocked = value;
                OnPropertyChanged();
            }
        }
    }

    public int SortOrder
    {
        get => BackingObject.SortOrder;
        set
        {
            if (BackingObject.SortOrder != value)
            {
                BackingObject.SortOrder = value;
                OnPropertyChanged();
            }
        }
    }

    // ── 组件系统(委托到 BackingObject)──────────────────────

    public T? GetComponent<T>() where T : class, IComponent
        => BackingObject.GetComponent<T>();

    public void AddComponent<T>(T component) where T : class, IComponent
    {
        BackingObject.AddComponent(component);
        OnPropertyChanged(nameof(HasWallComponent));
        RebuildComponentEditors();
    }

    public bool RemoveComponent<T>() where T : class, IComponent
    {
        var removed = BackingObject.RemoveComponent<T>();
        if (removed)
        {
            OnPropertyChanged(nameof(HasWallComponent));
            RebuildComponentEditors();
        }
        return removed;
    }

    public bool HasComponent<T>() where T : class, IComponent
        => BackingObject.GetComponent<T>() is not null;

    // ── Transform 属性委托 ──────────────────────────────────

    private TransformComponent Tf => BackingObject.GetComponent<TransformComponent>()!;

    public double X
    {
        get => Tf.X;
        set
        {
            if (Tf.X != value)
            {
                Tf.X = value;
                if (!HasMapPosition && (Math.Abs(value) > 0.001 || Math.Abs(Tf.Y) > 0.001))
                    HasMapPosition = true;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanNavigateToMap));
                OnPropertyChanged(nameof(ShouldRenderOnMap));
                OnPropertyChanged(nameof(MapLeft));
            }
        }
    }

    public double Y
    {
        get => Tf.Y;
        set
        {
            if (Tf.Y != value)
            {
                Tf.Y = value;
                if (!HasMapPosition && (Math.Abs(Tf.X) > 0.001 || Math.Abs(value) > 0.001))
                    HasMapPosition = true;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanNavigateToMap));
                OnPropertyChanged(nameof(ShouldRenderOnMap));
                OnPropertyChanged(nameof(MapTop));
            }
        }
    }

    public double Z
    {
        get => Tf.Z;
        set { if (Tf.Z != value) { Tf.Z = value; OnPropertyChanged(); } }
    }

    public double Rotation
    {
        get => Tf.Rotation;
        set { if (Tf.Rotation != value) { Tf.Rotation = value; OnPropertyChanged(); } }
    }

    public double ScaleX
    {
        get => Tf.ScaleX;
        set { if (Tf.ScaleX != value) { Tf.ScaleX = value; OnPropertyChanged(); } }
    }

    public double ScaleY
    {
        get => Tf.ScaleY;
        set { if (Tf.ScaleY != value) { Tf.ScaleY = value; OnPropertyChanged(); } }
    }

    public bool HasMapPosition
    {
        get => Tf.HasMapPosition;
        set
        {
            if (Tf.HasMapPosition != value)
            {
                Tf.HasMapPosition = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanNavigateToMap));
                OnPropertyChanged(nameof(ShouldRenderOnMap));
            }
        }
    }

    // ── Sprite 属性委托 ─────────────────────────────────────

    private SpriteRendererComponent Sp => BackingObject.GetComponent<SpriteRendererComponent>()!;

    public string SpriteColor
    {
        get => Sp.Color;
        set
        {
            if (Sp.Color != value)
            {
                Sp.Color = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SpriteBrush));
            }
        }
    }

    public double Opacity
    {
        get => Sp.Opacity;
        set { if (Sp.Opacity != value) { Sp.Opacity = value; OnPropertyChanged(); } }
    }

    public string SourceAssetPath
    {
        get => Sp.SourceAssetPath;
        set { if (Sp.SourceAssetPath != value) { Sp.SourceAssetPath = value; OnPropertyChanged(); } }
    }

    public string SourceAssetKind
    {
        get => Sp.SourceAssetKind;
        set { if (Sp.SourceAssetKind != value) { Sp.SourceAssetKind = value; OnPropertyChanged(); } }
    }

    public string SourceAssetName
    {
        get => Sp.SourceAssetName;
        set { if (Sp.SourceAssetName != value) { Sp.SourceAssetName = value; OnPropertyChanged(); } }
    }

    public int SpriteAlignX
    {
        get => Sp.AlignX;
        set
        {
            if (Sp.AlignX != value)
            {
                Sp.AlignX = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(MapLeft));
            }
        }
    }

    public int SpriteAlignY
    {
        get => Sp.AlignY;
        set
        {
            if (Sp.AlignY != value)
            {
                Sp.AlignY = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(MapTop));
            }
        }
    }

    public bool SpriteHitTestEnabled
    {
        get => Sp.HitTestEnabled;
        set { if (Sp.HitTestEnabled != value) { Sp.HitTestEnabled = value; OnPropertyChanged(); } }
    }

    // ── Vision 属性委托(可选组件,可能为 null)──────────────

    /// <summary>视野组件,可能未挂载(返回 null)。</summary>
    private VisionComponent? Vis => BackingObject.GetComponent<VisionComponent>();

    /// <summary>是否挂载了视野组件(可增删组件门控)。</summary>
    public bool HasVisionComponent => Vis is not null;

    public bool VisionEnabled
    {
        get => Vis?.Enabled ?? false;
        set { if (Vis is { } v && v.Enabled != value) { v.Enabled = value; OnPropertyChanged(); } }
    }

    public double VisionRadius
    {
        get => Vis?.Radius ?? 0;
        set { if (Vis is { } v && v.Radius != value) { v.Radius = value; OnPropertyChanged(); } }
    }

    public double Orientation
    {
        get => Vis?.Orientation ?? 0;
        set { if (Vis is { } v && v.Orientation != value) { v.Orientation = value; OnPropertyChanged(); } }
    }

    /// <summary>
    /// VisionCones VM 集合。供 XAML/树绑定用。
    /// 集合变更自动经 CollectionChanged 同步回 VisionComponent。
    /// </summary>
    public ObservableCollection<VisionConeViewModel> VisionCones { get; }
        = [];

    private static VisionConeData ToConeData(VisionConeDto c) => new()
    {
        Id = c.Id,
        Name = c.Name,
        CenterOffset = c.CenterOffset,
        Range = c.Range,
        FieldOfView = c.FieldOfView,
        IsEnabled = c.IsEnabled
    };

    /// <summary>视野组件增删后刷新所有相关绑定(面板门控+属性值)。</summary>
    internal void NotifyVisionComponentChanged()
    {
        OnPropertyChanged(nameof(HasVisionComponent));
        OnPropertyChanged(nameof(VisionEnabled));
        OnPropertyChanged(nameof(VisionRadius));
        OnPropertyChanged(nameof(Orientation));
    }

    internal void NotifyWallComponentChanged()
    {
        OnPropertyChanged(nameof(HasWallComponent));
        OnPropertyChanged(nameof(WallType));
        OnPropertyChanged(nameof(IsDoor));
        OnPropertyChanged(nameof(IsDoorOpen));
        OnPropertyChanged(nameof(WallBlocksVision));
        OnPropertyChanged(nameof(WallBlocksMovement));
        OnPropertyChanged(nameof(WallTexturePath));
        OnPropertyChanged(nameof(WallTextureName));
        OnPropertyChanged(nameof(WallTileWidth));
        OnPropertyChanged(nameof(WallTileHeight));
        OnPropertyChanged(nameof(WallThickness));
        OnPropertyChanged(nameof(WallShowHandles));
    }

    /// <summary>把 VisionCones VM 集合同步回 Core VisionComponent(无组件则忽略)。</summary>
    internal void SyncVisionConesToCore()
    {
        if (Vis is not { } vis) return;
        vis.VisionCones = VisionCones.Select(c => new VisionConeData
        {
            Id = c.Id,
            Name = c.Name,
            CenterOffset = c.CenterOffset,
            Range = c.Range,
            FieldOfView = c.FieldOfView,
            IsEnabled = c.IsEnabled
        }).ToList();
    }

    // ── UI 专用属性 ─────────────────────────────────────────

    public bool IsPreviewInstance
    {
        get => _isPreviewInstance;
        set
        {
            if (SetProperty(ref _isPreviewInstance, value))
            {
                OnPropertyChanged(nameof(MapStroke));
                OnPropertyChanged(nameof(MapStrokeThickness));
            }
        }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (SetProperty(ref _isSelected, value))
            {
                OnPropertyChanged(nameof(MapStroke));
                OnPropertyChanged(nameof(MapStrokeThickness));
                OnPropertyChanged(nameof(MapBackground));
            }
        }
    }

    // ── 墙壁组件属性(委托到 WallComponent)──────────────────

    public bool HasWallComponent => BackingObject.GetComponent<WallComponent>() is not null;

    /// <summary>
    /// 墙壁类型下拉索引:0=实心墙 1=门 2=窗户 3=隐形墙 4=地形。
    /// getter 从组件当前感知状态推断,setter 套用对应预设(保留几何)。
    /// </summary>
    public int WallType
    {
        get
        {
            var wall = GetComponent<MapEngine.Core.Components.WallComponent>();
            if (wall is null) return 0;
            if (wall.Door != MapEngine.Core.Components.DoorKind.None) return 1; // 门
            if (wall.Sight == MapEngine.Core.Components.SenseLevel.None
                && wall.Move != MapEngine.Core.Components.SenseLevel.None) return 3; // 隐形墙
            if (wall.Sight == MapEngine.Core.Components.SenseLevel.Limited) return 4; // 地形
            if (wall.Sound == MapEngine.Core.Components.SenseLevel.None
                && wall.Sight == MapEngine.Core.Components.SenseLevel.Normal) return 2; // 窗户
            return 0; // 实心墙
        }
        set
        {
            var wall = GetComponent<MapEngine.Core.Components.WallComponent>();
            if (wall is null) return;

            var preset = value switch
            {
                1 => MapEngine.Core.Components.WallPresets.Normal(),   // 门:实心墙感知 + Door
                2 => MapEngine.Core.Components.WallPresets.Window(),
                3 => MapEngine.Core.Components.WallPresets.Invisible(),
                4 => MapEngine.Core.Components.WallPresets.Terrain(),
                _ => MapEngine.Core.Components.WallPresets.Normal(),
            };
            wall.Sight = preset.Sight;
            wall.Move = preset.Move;
            wall.Sound = preset.Sound;
            wall.Light = preset.Light;
            wall.Door = value == 1
                ? MapEngine.Core.Components.DoorKind.Door
                : MapEngine.Core.Components.DoorKind.None;

            OnPropertyChanged();
            OnPropertyChanged(nameof(IsDoor));
            OnPropertyChanged(nameof(WallBlocksVision));
            OnPropertyChanged(nameof(WallBlocksMovement));
        }
    }

    /// <summary>是否为门（DoorKind != None）</summary>
    public bool IsDoor => GetComponent<MapEngine.Core.Components.WallComponent>()?.Door != MapEngine.Core.Components.DoorKind.None;

    public bool IsDoorOpen
    {
        get => GetComponent<MapEngine.Core.Components.WallComponent>()?.State == MapEngine.Core.Components.DoorState.Open;
        set
        {
            if (GetComponent<MapEngine.Core.Components.WallComponent>() is { } wall)
            {
                wall.State = value
                    ? MapEngine.Core.Components.DoorState.Open
                    : MapEngine.Core.Components.DoorState.Closed;
                OnPropertyChanged();
            }
        }
    }

    public bool WallBlocksVision
    {
        get => GetComponent<MapEngine.Core.Components.WallComponent>()?.Sight != MapEngine.Core.Components.SenseLevel.None;
        set
        {
            if (GetComponent<MapEngine.Core.Components.WallComponent>() is { } wall)
            {
                wall.Sight = value ? MapEngine.Core.Components.SenseLevel.Normal : MapEngine.Core.Components.SenseLevel.None;
                OnPropertyChanged();
            }
        }
    }

    public bool WallBlocksMovement
    {
        get => GetComponent<MapEngine.Core.Components.WallComponent>()?.Move != MapEngine.Core.Components.SenseLevel.None;
        set
        {
            if (GetComponent<MapEngine.Core.Components.WallComponent>() is { } wall)
            {
                wall.Move = value ? MapEngine.Core.Components.SenseLevel.Normal : MapEngine.Core.Components.SenseLevel.None;
                OnPropertyChanged();
            }
        }
    }

    public string WallTexturePath
    {
        get => GetComponent<MapEngine.Core.Components.WallComponent>()?.TileTexturePath ?? string.Empty;
        set
        {
            if (GetComponent<MapEngine.Core.Components.WallComponent>() is { } wall)
            {
                wall.TileTexturePath = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(WallTextureName));
            }
        }
    }

    public string WallTextureName => string.IsNullOrEmpty(WallTexturePath)
        ? "(无纹理)"
        : System.IO.Path.GetFileName(WallTexturePath);

    public double WallTileWidth
    {
        get => GetComponent<MapEngine.Core.Components.WallComponent>()?.Thickness ?? 5;
        set
        {
            if (GetComponent<MapEngine.Core.Components.WallComponent>() is { } wall)
            {
                wall.Thickness = value;
                OnPropertyChanged();
            }
        }
    }

    public double WallTileHeight
    {
        get => GetComponent<MapEngine.Core.Components.WallComponent>()?.Thickness ?? 5;
        set { /* 新模型统一用 Thickness，保留属性避免 XAML 绑定报错 */ }
    }

    public double WallThickness
    {
        get => GetComponent<MapEngine.Core.Components.WallComponent>()?.Thickness ?? 5;
        set
        {
            if (GetComponent<MapEngine.Core.Components.WallComponent>() is { } wall)
            {
                wall.Thickness = value;
                OnPropertyChanged();
            }
        }
    }

    public bool WallShowHandles
    {
        get => false; // 新模型无此字段，编辑把手由工具层控制
        set { }
    }

    // ═══════════════════════════════════════════════════════════════════
    // Token 组件属性
    // ═══════════════════════════════════════════════════════════════════

    public string TokenName
    {
        get => GetComponent<MapEngine.Core.Components.TokenComponent>()?.TokenName ?? string.Empty;
        set
        {
            if (GetComponent<MapEngine.Core.Components.TokenComponent>() is { } token)
            {
                token.TokenName = value;
                OnPropertyChanged();
            }
        }
    }

    public int InitiativeOrder
    {
        get => GetComponent<MapEngine.Core.Components.TokenComponent>()?.InitiativeOrder ?? 0;
        set
        {
            if (GetComponent<MapEngine.Core.Components.TokenComponent>() is { } token)
            {
                token.InitiativeOrder = value;
                OnPropertyChanged();
            }
        }
    }

    public bool IsPlayerControlled
    {
        get => GetComponent<MapEngine.Core.Components.TokenComponent>()?.IsPlayerControlled ?? false;
        set
        {
            if (GetComponent<MapEngine.Core.Components.TokenComponent>() is { } token)
            {
                token.IsPlayerControlled = value;
                OnPropertyChanged();
            }
        }
    }

    public double MovementSpeed
    {
        get => GetComponent<MapEngine.Core.Components.TokenComponent>()?.MovementSpeed ?? 30;
        set
        {
            if (GetComponent<MapEngine.Core.Components.TokenComponent>() is { } token)
            {
                token.MovementSpeed = value;
                OnPropertyChanged();
            }
        }
    }

    public string DisplayName => $"{Icon} {Name}";

    public string SelectionType => $"地图对象 / {ObjectType}";

    public bool CanNavigateToMap => HasMapPosition;

    public bool ShouldRenderOnMap
        => IsActive && (CanNavigateToMap || IsPreviewInstance) && !string.IsNullOrWhiteSpace(Icon);

    public double SpriteWidth => MapViewportConstants.CellSize * ScaleX;
    public double SpriteHeight => MapViewportConstants.CellSize * ScaleY;

    public double MapLeft
    {
        get
        {
            var baseX = MapViewportConstants.WorldOriginContent + X;
            return SpriteAlignX switch
            {
                0 => baseX,
                2 => baseX - SpriteWidth,
                _ => baseX - (SpriteWidth / 2.0)
            };
        }
    }

    public double MapTop
    {
        get
        {
            var baseY = MapViewportConstants.WorldOriginContent - Y;
            return SpriteAlignY switch
            {
                0 => baseY,
                2 => baseY - SpriteHeight,
                _ => baseY - (SpriteHeight / 2.0)
            };
        }
    }

    public string MapLabel => Name;

    public IBrush MapStroke => IsSelected ? Brushes.DeepSkyBlue : IsPreviewInstance ? Brushes.Gold : Brushes.Transparent;

    public Thickness MapStrokeThickness => (IsSelected || IsPreviewInstance)
        ? new Thickness(2)
        : new Thickness(0);

    public IBrush MapBackground => IsSelected ? SelectedMapBackgroundBrush : DefaultMapBackgroundBrush;

    public SolidColorBrush SpriteBrush => new(Color.Parse(SpriteColor));
}

public static class MapViewportConstants
{
    public const double ContentSize = 204800;
    public const double WorldOriginContent = ContentSize / 2.0;
    public const double TileSize = 512;
    public const double CellSize = 50;
    public const double MapObjectSize = 36;
}

public sealed class MapTileViewModel
{
    public MapTileViewModel(double canvasLeft, double canvasTop, double size, string background, string borderBrush, string label)
    {
        CanvasLeft = canvasLeft;
        CanvasTop = canvasTop;
        Size = size;
        Background = background;
        BorderBrush = borderBrush;
        Label = label;
    }

    public double CanvasLeft { get; }

    public double CanvasTop { get; }

    public double Size { get; }

    public string Background { get; }

    public string BorderBrush { get; }

    public string Label { get; }
}

public sealed class MapGuideLineViewModel
{
    public MapGuideLineViewModel(double canvasLeft, double canvasTop, double width, double height, string stroke)
    {
        CanvasLeft = canvasLeft;
        CanvasTop = canvasTop;
        Width = width;
        Height = height;
        Stroke = stroke;
    }

    public double CanvasLeft { get; }

    public double CanvasTop { get; }

    public double Width { get; }

    public double Height { get; }

    public string Stroke { get; }
}

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

    private static void NotifyPathChangedRecursive(AssetFolderViewModel folder)
    {
        folder.OnPropertyChanged(nameof(PathDisplay));
        foreach (var child in folder.Children)
        {
            NotifyPathChangedRecursive(child);
        }
    }
}

public sealed class AssetItemViewModel : ViewModelBase, IGlobalSelectionItem
{
    private string _name;

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

public sealed class ToolActionViewModel : ViewModelBase
{
    private bool _isSelected;

    public ToolActionViewModel(string key, string icon, string label, string toolTip)
    {
        Key = key;
        Icon = icon;
        Label = label;
        ToolTip = toolTip;
    }

    public string Key { get; }

    public string Icon { get; }

    public string Label { get; }

    public string ToolTip { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (SetProperty(ref _isSelected, value))
            {
                OnPropertyChanged(nameof(Background));
                OnPropertyChanged(nameof(Foreground));
            }
        }
    }

    public string Background => IsSelected ? "#555555" : "Transparent";

    public string Foreground => IsSelected ? "#FFFFFF" : "#CCCCCC";
}
