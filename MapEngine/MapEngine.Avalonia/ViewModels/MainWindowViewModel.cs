using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.IO;
using System.Text.Json;
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

    private IMapEditorHost? _host;

    /// <summary>
    /// 外部宿主引用，用于上报流式实时数据（拖拽预览、光标等）。
    /// 单机模式下为 null 或 StandaloneMapEditorHost；联机时由 ChatRoomWindow 注入。
    /// </summary>
    /// <remarks>
    /// 必须走属性通知：房间库路径只有注入 Host 之后才知道，
    /// 素材库面板"房间"单选框的 IsEnabled 绑的是算自本属性的 IsRoomAssetLibraryAvailable。
    /// 不通知的话绑定会停在构造时求得的 false，按钮一直是禁用态、点不动。
    /// </remarks>
    public IMapEditorHost? Host
    {
        get => _host;
        set
        {
            if (ReferenceEquals(_host, value))
                return;

            _host = value;
            OnPropertyChanged();

            // 渲染端解析 assetRef 要靠房间库根路径；客户端不走 MapEditorEntry.Mount，
            // 所以这里补上注入，否则房间库的精灵图永远解析不到。
            MapSpriteAssetResolver.RoomAssetRoot = value?.RoomAssetLibraryPath;
            MapSpriteAssetResolver.Initialize();

            OnPropertyChanged(nameof(IsRoomAssetLibraryAvailable));
        }
    }

    private readonly List<AssetItemViewModel> _allAssetItems;
    private readonly Dictionary<string, AssetFolderViewModel> _assetFolderIndex;
    // _hierarchyIndex 保留作为 VM 层快速查找,不再是唯一索引(World.FindById 才是)
    private readonly Dictionary<string, HierarchyItemViewModel> _hierarchyIndex;
    private HierarchyNodeDto? _hierarchyClipboard;
    private string? _assetFolderClipboardPath;
    private string? _assetItemClipboardPath;
    private AssetClipboardKind _assetClipboardKind;
    /// <summary>当前素材库根路径。随 ActiveAssetSource 切换（本地库 / 房间库）。</summary>
    private string _assetLibraryRootPath;
    /// <summary>本地素材库根路径，切回本地时复位用。</summary>
    private readonly string _localAssetLibraryRootPath;
    /// <summary>当前素材库的元数据索引（哈希→路径映射）。随 ActiveAssetSource 切换。</summary>
    private MapEngine.Core.Assets.AssetMetadataStore? _assetMetadataStore;
    private HierarchyItemViewModel? _mapDragPreviewItem;
    private HierarchyItemViewModel? _highlightedHierarchyItem;

    private IGlobalSelectionItem? _currentSelection;
    private HierarchyItemViewModel? _selectedHierarchyItem;
    private AssetFolderViewModel? _selectedAssetFolder;
    private AssetItemViewModel? _selectedAssetItem;
    private ToolActionViewModel? _selectedPrimaryTool;
    private string _assetSearchText = string.Empty;

    // ── 编辑器模式 ──────────────────────────────────────────────────────────
    /// <summary>设计模式（GM编辑）vs 游玩模式（战斗运行时）。</summary>
    public enum MapEditorMode { Design, Play }

    private MapEditorMode _editorMode = MapEditorMode.Design;
    public MapEditorMode EditorMode
    {
        get => _editorMode;
        set
        {
            if (SetProperty(ref _editorMode, value))
            {
                OnPropertyChanged(nameof(ActivePrimaryTools));
                OnPropertyChanged(nameof(ActiveQuickActions));
                OnPropertyChanged(nameof(IsDesignMode));
                OnPropertyChanged(nameof(IsPlayMode));
                OnPropertyChanged(nameof(ModeSwitchIcon));
                OnPropertyChanged(nameof(ModeSwitchTip));
                // 游玩模式自动关闭层级/素材库抽屉（玩家不需要编辑器面板）
                if (value == MapEditorMode.Play)
                {
                    IsLeftDrawerOpen = false;
                    IsBottomDrawerOpen = false;
                }
            }
        }
    }
    public bool IsDesignMode => _editorMode == MapEditorMode.Design;
    public bool IsPlayMode   => _editorMode == MapEditorMode.Play;
    /// <summary>
    /// 模式切换图标（Material Design Icons 路径数据，24x24 viewBox）。
    /// 设计模式下显示"播放"图标（点击进入游玩），游玩模式下显示"铅笔"图标（点击回到设计）。
    /// </summary>
    public string ModeSwitchIcon => _editorMode == MapEditorMode.Design
        ? "M8,5.14V19.14L19,12.14L8,5.14Z"
        : "M20.71,7.04C21.1,6.65 21.1,6 20.71,5.63L18.37,3.29C18,2.9 17.35,2.9 16.96,3.29L15.12,5.12L18.87,8.87M3,17.25V21H6.75L17.81,9.93L14.06,6.18L3,17.25Z";
    public string ModeSwitchTip  => _editorMode == MapEditorMode.Design ? "切换为游玩模式" : "切换为设计模式";

    // ── 抽屉状态 ────────────────────────────────────────────────────────────
    private bool _isLeftDrawerOpen = false;
    private bool _isRightDrawerOpen;
    private bool _isBottomDrawerOpen;
    private bool _isTopDrawerOpen;
    private bool _isDicePanelOpen;

    public bool IsLeftDrawerOpen
    {
        get => _isLeftDrawerOpen;
        set => SetProperty(ref _isLeftDrawerOpen, value);
    }
    public bool IsRightDrawerOpen
    {
        get => _isRightDrawerOpen;
        set => SetProperty(ref _isRightDrawerOpen, value);
    }
    public bool IsBottomDrawerOpen
    {
        get => _isBottomDrawerOpen;
        set => SetProperty(ref _isBottomDrawerOpen, value);
    }
    public bool IsTopDrawerOpen
    {
        get => _isTopDrawerOpen;
        set => SetProperty(ref _isTopDrawerOpen, value);
    }
    public bool IsDicePanelOpen
    {
        get => _isDicePanelOpen;
        set => SetProperty(ref _isDicePanelOpen, value);
    }
    private double _assetThumbnailSize = 64;
    private bool _showGrid;
    private bool _isSnapToGrid = true;
    private double _zoomScale = 1.0;   // 权威值，范围 [0.02, 64.0]（2% ~ 6400%）
    private int _feetPerCell = 5;
    private string _packageName = "默认地图包";
    private string _statusMessage = "界面已就绪";
    private string _newTagText = string.Empty;
    private string _graphicsRuntimeSummary = "渲染环境检测中";
    private string _graphicsRuntimeHint = "";
    private double _mapViewportCenterX;
    private double _mapViewportCenterY;
    private double _lastMapOffsetX;
    private double _lastMapOffsetY;
    private double _lastMapViewportWidth;
    private double _lastMapViewportHeight;

    // 工具集（按模式分组）
    private readonly ObservableCollection<ToolActionViewModel> _designTools;
    private readonly ObservableCollection<ToolActionViewModel> _playTools;
    private readonly ObservableCollection<ToolActionViewModel> _designQuickActions;
    private readonly ObservableCollection<ToolActionViewModel> _playQuickActions;

    public MainWindowViewModel()
    {
        _commandBus = new CommandBus(_world);

        ObjectTypes = ["Empty", "Folder", "Map", "Layer", "Prop", "Token", "Marker", "AudioSource", "PrefabInstance", "StaticObject"];

        SelectToolCommand = new RelayCommand<string?>(SelectTool);
        SelectShapeSubToolCommand = new RelayCommand<string?>(SelectShapeSubTool);
        SelectGraphSubToolCommand = new RelayCommand<string?>(key =>
        {
            if (string.IsNullOrWhiteSpace(key)) return;
            GraphSubTool = key;
            if (!IsGraphToolActive) SelectTool("graph");
        });
        PruneDanglingGraphLinksCommand = new RelayCommand(() => PruneDanglingGraphLinks());
        ToggleShapeFillCommand = new RelayCommand(ToggleShapeFill);
        CycleStrokeStyleCommand = new RelayCommand(CycleStrokeStyle);
        ActivateQuickActionCommand = new RelayCommand<string?>(ActivateQuickAction);
        ToggleGridCommand = new RelayCommand(ToggleGrid);
        ToggleSnapCommand = new RelayCommand(ToggleSnap);
        ZoomInCommand  = new RelayCommand(() => SetZoomScale(_zoomScale * ZoomStepFactor));
        ZoomOutCommand = new RelayCommand(() => SetZoomScale(_zoomScale / ZoomStepFactor));
        ResetZoomCommand = new RelayCommand(() => SetZoomScale(1.0));
        SetZoomPresetCommand  = new RelayCommand<object>(p => {
            if (p is double d) SetZoomScale(d);
            else if (p is string s && double.TryParse(s, System.Globalization.NumberStyles.Any,
                     System.Globalization.CultureInfo.InvariantCulture, out var v)) SetZoomScale(v);
        });
        SetScalePresetCommand = new RelayCommand<object>(p => {
            if (p is int i) FeetPerCell = i;
            else if (p is string s && int.TryParse(s, out var v)) FeetPerCell = v;
        });
        CycleScaleCommand = new RelayCommand(CycleScale);
        ResetViewCommand = new RelayCommand(RequestViewReset);
        AddTagCommand = new RelayCommand(AddTag);
        RemoveTagCommand = new RelayCommand<string?>(RemoveTag);
        AddVisionConeCommand    = new RelayCommand<HierarchyItemViewModel?>(item => { if (item is not null) AddVisionCone(item); });
        RemoveVisionConeCommand = new RelayCommand<VisionConeViewModel?>(cone => {
            if (SelectedHierarchyItem is not null && cone is not null) RemoveVisionCone(SelectedHierarchyItem, cone);
        });
        PickTokenPortraitCommand = new RelayCommand<HierarchyItemViewModel?>(item => {
            // 实际文件选择由 code-behind PickTokenPortrait_Click 处理；
            // 此命令作为 XAML 中 Button.Command 的绑定目标，触发事件路由。
            // 当前为占位实现，code-behind 通过事件处理文件选择并直接写 PortraitPath。
        });
        NavigateToSelectionCommand = new RelayCommand(NavigateToSelection, () => CurrentSelection is not null);
        ShowInspectorMenuCommand = new RelayCommand(() => StatusMessage = "Inspector 更多选项暂以本地假数据模式运行");
        SaveSceneCommand = new RelayCommand(SaveScene);
        LoadSceneCommand = new RelayCommand(() => { /* 实际处理由 code-behind OnLoadSceneClicked 完成 */ });
        OpenSettingsCommand = new RelayCommand(OpenSettings);
        ToggleModeCommand = new RelayCommand(() =>
            EditorMode = EditorMode == MapEditorMode.Design ? MapEditorMode.Play : MapEditorMode.Design);
        ToggleLeftDrawerCommand   = new RelayCommand(() => IsLeftDrawerOpen   = !IsLeftDrawerOpen);
        ToggleRightDrawerCommand  = new RelayCommand(() => IsRightDrawerOpen  = !IsRightDrawerOpen);
        ToggleBottomDrawerCommand = new RelayCommand(() => IsBottomDrawerOpen = !IsBottomDrawerOpen);
        ToggleTopDrawerCommand    = new RelayCommand(() => IsTopDrawerOpen    = !IsTopDrawerOpen);
        ToggleDicePanelCommand    = new RelayCommand(() => IsDicePanelOpen    = !IsDicePanelOpen);

        ToggleFogCommand      = new RelayCommand(() => IsFogEnabled = !IsFogEnabled);
        SetFogSubModeCommand  = new RelayCommand<string>(mode => { if (mode is not null) FogSubMode = mode; });
        FogRevealAllCommand   = new RelayCommand(FogRevealAll);
        FogClearAllCommand    = new RelayCommand(FogClearAll);

        // 设计模式：GM 全工具集
        _designTools = new ObservableCollection<ToolActionViewModel>
        {
            new("select",  "👆",  "选择", "移动/选择 (V)"),
            new("pan",     "✋",  "拖拽", "平移视口 (Middle / H)"),
            new("draw",    "🖌️", "画笔", "画笔工具 (D)"),
            new("text",    "T",   "文本", "文本工具 (T)"),
            new("shape",   "🔲",  "形状", "形状工具 (S)"),
            new("wall",    "🧱",  "墙体", "墙体工具 (W)"),
            new("measure", "📏",  "测量", "测量工具 (M)"),
            new("laser",   "🎯",  "激光", "激光笔 (L)", isToggle: true),
            new("fog",     "☁️", "迷雾", "迷雾工具 (F)"),
            new("attach",  "📎",  "附件", "附件/标记 (A)"),
            new("graph",   "🕸",  "拓扑", "拓扑节点/连线 (G)")
        };

        // 游玩模式：玩家可用工具（无编辑/迷雾/附件）
        _playTools = new ObservableCollection<ToolActionViewModel>
        {
            new("select",  "👆",  "选择", "移动 token (V)"),
            new("pan",     "✋",  "拖拽", "平移视口 (Middle / H)"),
            new("measure", "📏",  "测量", "测量距离 (M)"),
            new("laser",   "🎯",  "激光", "激光笔 (L)", isToggle: true)
        };

        // 设计模式快捷操作
        _designQuickActions = new ObservableCollection<ToolActionViewModel>
        {
            new("initiative", "⚔️", "先攻", "活跃棋子/先攻列表 (I)"),
            new("tray",       "📥", "棋篓", "棋篓 (Tray)"),
            new("dice",       "🎲", "骰子", "骰子工具 (Dice)"),
            new("extensions", "🧩", "扩展", "扩展插件 (Extensions)")
        };

        // 游玩模式快捷操作（无棋篓/扩展）
        _playQuickActions = new ObservableCollection<ToolActionViewModel>
        {
            new("initiative", "⚔️", "先攻", "先攻列表 (I)"),
            new("dice",       "🎲", "骰子", "骰子 (Dice)")
        };

        // 兼容字段（axaml 绑定改用 ActivePrimaryTools / ActiveQuickActions）
        PrimaryTools = _designTools;
        QuickActions  = _designQuickActions;

        var data = FakeProjectDataLoader.Load();
        PackageName = data.PackageName;

        _hierarchyIndex = [];
        HierarchyRoots = new ObservableCollection<HierarchyItemViewModel>(
            data.HierarchyRoots.Select(BuildHierarchyItem));

        // 场景根保底:空场景时自动建一个根节点,确保拖放/创建有落点
        if (HierarchyRoots.Count == 0)
            EnsureSceneRoot();

        // 初始化先攻追踪器（必须在 HierarchyRoots 赋值之后）
        InitiativeTracker = new InitiativeTrackerViewModel(this);
        Dice              = new DicePanelViewModel();

        var assetLibrary = AssetLibraryFileSystemService.Load(data.AssetLibraryRootFolder);
        _assetLibraryRootPath = assetLibrary.RootPath;
        _localAssetLibraryRootPath = assetLibrary.RootPath;
        _assetMetadataStore = LoadOrCreateMetadataStore(_assetLibraryRootPath);
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
        SetZoomScale(data.ZoomPercent / 100.0);
        FeetPerCell = data.FeetPerCell;

        SelectTool(data.ActiveTool);

        SelectedHierarchyItem = FindHierarchy(data.SelectedHierarchyId) ?? HierarchyRoots.FirstOrDefault();
        // 默认打开素材库根目录（AssetRoots 第一个文件夹）
        SelectedAssetFolder = FindAssetFolder(data.SelectedAssetFolderId)
            ?? AssetRoots.FirstOrDefault();

        RefreshVisibleAssets();
        RefreshMapRenderableItems();
        RefreshMapViewportDecorations();

        // S2: 把所有 GameObject 加入 World,让 World 成为运行时权威
        SyncGameObjectsToWorld();

        _commandBus.CommandExecuted += (_, e) =>
            StatusMessage = $"[{e.Action}] {e.Command.Description}";
    }

    public CommandBus CommandBus => _commandBus;

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

    /// <summary>当前模式下激活的主工具集（axaml 绑定此属性）。</summary>
    public ObservableCollection<ToolActionViewModel> ActivePrimaryTools =>
        _editorMode == MapEditorMode.Design ? _designTools : _playTools;

    /// <summary>当前模式下激活的快捷操作集（axaml 绑定此属性）。</summary>
    public ObservableCollection<ToolActionViewModel> ActiveQuickActions =>
        _editorMode == MapEditorMode.Design ? _designQuickActions : _playQuickActions;

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

    // ── Shape/Draw 子工具 ────────────────────────────────────────────────────
    private string _shapeSubTool = "rect";
    /// <summary>Shape 工具当前子类型：line | rect | circle | ellipse | cone | wedge | polygon。</summary>
    public string ShapeSubTool
    {
        get => _shapeSubTool;
        set => SetProperty(ref _shapeSubTool, value);
    }

    private bool _shapeDefaultFilled = true;
    /// <summary>新建矢量形状默认是否填充。</summary>
    public bool ShapeDefaultFilled
    {
        get => _shapeDefaultFilled;
        set => SetProperty(ref _shapeDefaultFilled, value);
    }

    private Core.Components.StrokeStyle _shapeDefaultStrokeStyle = Core.Components.StrokeStyle.Solid;
    /// <summary>新建矢量形状默认线型。</summary>
    public Core.Components.StrokeStyle ShapeDefaultStrokeStyle
    {
        get => _shapeDefaultStrokeStyle;
        set => SetProperty(ref _shapeDefaultStrokeStyle, value);
    }

    private double _textDefaultFontSize = 16;
    /// <summary>新建文本默认字号（世界单位）。</summary>
    public double TextDefaultFontSize
    {
        get => _textDefaultFontSize;
        set => SetProperty(ref _textDefaultFontSize, value);
    }

    private string _textDefaultColor = "#F8F9FA";
    /// <summary>新建文本默认颜色。</summary>
    public string TextDefaultColor
    {
        get => _textDefaultColor;
        set => SetProperty(ref _textDefaultColor, value);
    }

    /// <summary>获取或创建"形状"根节点（lazy，挂在 HierarchyRoots 末尾）。</summary>
    public HierarchyItemViewModel GetOrCreateShapeRoot()
        => GetOrCreateSystemRoot("形状", "ShapeRoot");

    /// <summary>获取或创建"绘制"根节点（lazy，挂在 HierarchyRoots 末尾）。</summary>
    public HierarchyItemViewModel GetOrCreateDrawingRoot()
        => GetOrCreateSystemRoot("绘制", "DrawingRoot");

    /// <summary>获取或创建"文本"根节点（lazy，挂在 HierarchyRoots 末尾）。</summary>
    public HierarchyItemViewModel GetOrCreateTextRoot()
        => GetOrCreateSystemRoot("文本", "TextRoot");

    private HierarchyItemViewModel GetOrCreateSystemRoot(string displayName, string fixedId)
    {
        if (_hierarchyIndex.TryGetValue(fixedId, out var existing))
            return existing;

        var dto = new Services.HierarchyNodeDto
        {
            Id         = fixedId,
            Name       = displayName,
            Icon       = "📁",
            ObjectType = "Group",
            IsActive   = true,
            HasMapPosition = false,
        };
        var root = BuildHierarchyItemPublic(dto);
        HierarchyRoots.Add(root);
        RegisterHierarchyItem(root);
        return root;
    }

    public RelayCommand<string?> SelectToolCommand { get; }

    public RelayCommand<string?> SelectShapeSubToolCommand { get; }

    /// <summary>切换拓扑子工具（node / link）。</summary>
    public RelayCommand<string?> SelectGraphSubToolCommand { get; }

    /// <summary>清理所有指向已删节点的悬空连线。</summary>
    public RelayCommand PruneDanglingGraphLinksCommand { get; }

    public RelayCommand ToggleShapeFillCommand { get; }

    public RelayCommand CycleStrokeStyleCommand { get; }

    public RelayCommand<string?> ActivateQuickActionCommand { get; }

    public RelayCommand ToggleGridCommand { get; }

    public RelayCommand ToggleSnapCommand { get; }

    public RelayCommand ZoomInCommand { get; }

    public RelayCommand ZoomOutCommand { get; }

    public RelayCommand ResetZoomCommand { get; }

    public RelayCommand<object> SetZoomPresetCommand { get; }

    public RelayCommand<object> SetScalePresetCommand { get; }

    public RelayCommand CycleScaleCommand { get; }

    public RelayCommand ResetViewCommand { get; }

    public RelayCommand AddTagCommand { get; }

    public RelayCommand<string?> RemoveTagCommand { get; }

    public RelayCommand<HierarchyItemViewModel?> AddVisionConeCommand { get; }
    public RelayCommand<VisionConeViewModel?> RemoveVisionConeCommand { get; }
    public RelayCommand<HierarchyItemViewModel?> PickTokenPortraitCommand { get; }

    public RelayCommand NavigateToSelectionCommand { get; }

    public RelayCommand ShowInspectorMenuCommand { get; }

    public RelayCommand SaveSceneCommand { get; }

    public RelayCommand LoadSceneCommand { get; }

    public RelayCommand OpenSettingsCommand { get; }

    public RelayCommand ToggleModeCommand { get; }
    public RelayCommand ToggleLeftDrawerCommand { get; }
    public RelayCommand ToggleRightDrawerCommand { get; }
    public RelayCommand ToggleBottomDrawerCommand { get; }
    public RelayCommand ToggleTopDrawerCommand { get; }
    public RelayCommand ToggleDicePanelCommand { get; }

    public RelayCommand ToggleFogCommand { get; }
    public RelayCommand<string> SetFogSubModeCommand { get; }
    public RelayCommand FogRevealAllCommand { get; }
    public RelayCommand FogClearAllCommand { get; }

    /// <summary>先攻追踪器 ViewModel。</summary>
    public InitiativeTrackerViewModel InitiativeTracker { get; }

    /// <summary>骰子面板 ViewModel。</summary>
    public DicePanelViewModel Dice { get; }

    /// <summary>请求 code-behind 将相机回归到原点并重置缩放。</summary>
    public event EventHandler? ViewResetRequested;

    public event EventHandler? SettingsRequested;

    /// <summary>请求打开保存场景对话框（传出当前场景数据，传入保存路径）。</summary>
    public event EventHandler<SaveSceneRequestEventArgs>? SaveSceneRequested;

    /// <summary>请求打开加载场景对话框（传入加载路径）。</summary>
    public event EventHandler<LoadSceneRequestEventArgs>? LoadSceneRequested;

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

            // 触发事件让 View 打开保存对话框
            var eventArgs = new SaveSceneRequestEventArgs(doc);
            SaveSceneRequested?.Invoke(this, eventArgs);

            if (!string.IsNullOrEmpty(eventArgs.SavedPath))
            {
                StatusMessage = $"场景已保存：{eventArgs.SavedPath}";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"场景保存失败：{ex.Message}";
        }
    }

    private void LoadScene() { /* 空占位，实际由 code-behind 调用 LoadSceneFromDocument */ }

    /// <summary>由 View 层文件对话框完成后直接调用，传入已反序列化的文档。</summary>
    public void LoadSceneFromDocument(ScenePersistDocument doc, string loadedPath)
    {
        try
        {
            // 清空当前场景
            HierarchyRoots.Clear();
            _allAssetItems.Clear();
            VisibleAssetItems.Clear();
            SelectedHierarchyItem = null;

            // 加载场景数据
            PackageName = doc.PackageName;
            ShowGrid = doc.ShowGrid;
            SetZoomScale(doc.ZoomPercent / 100.0);
            FeetPerCell = doc.FeetPerCell;

            // 重建层级树（用 BuildHierarchyItemPublic 递归构建，含子节点和父子关系）
            foreach (var rootDto in doc.HierarchyRoots)
            {
                var root = BuildHierarchyItemPublic(rootDto);
                HierarchyRoots.Add(root);
            }

            // 刷新先攻追踪器
            InitiativeTracker.RefreshEntries();

            // 刷新地图渲染
            RefreshMapRenderableItemsPublic();

            StatusMessage = $"场景已加载：{loadedPath}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"场景加载失败：{ex.Message}";
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
        SetZoomScale(settings.DefaultZoomPercent / 100.0);
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

    public bool IsSnapToGrid
    {
        get => _isSnapToGrid;
        set => SetProperty(ref _isSnapToGrid, value);
    }

    /// <summary>滚轮每档几何倍率（≈12%，28档从2%到6400%）。</summary>
    public const double ZoomStepFactor = 1.122; // 2^(1/6) ≈ 1.122，6档一倍

    /// <summary>缩放权威值（double），范围 [0.02, 64.0]。</summary>
    public double ZoomScale
    {
        get => _zoomScale;
        private set
        {
            if (SetProperty(ref _zoomScale, value))
            {
                OnPropertyChanged(nameof(ZoomPercent));
                OnPropertyChanged(nameof(ZoomText));
                RefreshMapViewportDecorations();
            }
        }
    }

    /// <summary>兼容存档/快照的整数百分比（派生，只读）。</summary>
    public int ZoomPercent => (int)Math.Round(_zoomScale * 100.0);

    public int FeetPerCell
    {
        get => _feetPerCell;
        set
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
        internal set => SetProperty(ref _statusMessage, value);
    }

    /// <summary>Tag 输入框的临时文本，绑定到标签编辑器的 TextBox。</summary>
    public string NewTagText
    {
        get => _newTagText;
        set => SetProperty(ref _newTagText, value);
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

    /// <summary>智能缩放显示文本：低于10%显示一位小数，高于1000%省略小数。</summary>
    public string ZoomText => _zoomScale switch
    {
        < 0.10  => $"{_zoomScale * 100.0:F1}%",
        >= 10.0 => $"{(int)(_zoomScale * 100.0)}%",
        _       => $"{(int)Math.Round(_zoomScale * 100.0)}%"
    };

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

    /// <summary>获取当前素材库的元数据索引（供拖放导入使用）。</summary>
    public MapEngine.Core.Assets.AssetMetadataStore? GetAssetMetadataStore() => _assetMetadataStore;

    /// <summary>获取当前素材库根路径（供拖放导入使用）。</summary>
    public string GetAssetLibraryRootPath() => _assetLibraryRootPath;

    private static MapEngine.Core.Assets.AssetMetadataStore LoadOrCreateMetadataStore(string assetLibraryRoot)
    {
        try
        {
            return new MapEngine.Core.Assets.AssetMetadataStore(assetLibraryRoot);
        }
        catch
        {
            // 损坏时重建
            return new MapEngine.Core.Assets.AssetMetadataStore(assetLibraryRoot);
        }
    }
}

/// <summary>保存场景请求事件参数。</summary>
public sealed class SaveSceneRequestEventArgs : EventArgs
{
    public ScenePersistDocument Document { get; }
    public string? SavedPath { get; set; }

    public SaveSceneRequestEventArgs(ScenePersistDocument document)
    {
        Document = document;
    }
}

/// <summary>加载场景请求事件参数。</summary>
public sealed class LoadSceneRequestEventArgs : EventArgs
{
    public ScenePersistDocument? Document { get; set; }
    public string? LoadedPath { get; set; }
}
