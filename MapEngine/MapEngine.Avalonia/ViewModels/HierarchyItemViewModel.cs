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
            sprite.AssetRef = s.TexturePath;
            sprite.Opacity = s.Opacity;
            sprite.Color = s.TintColor;
            sprite.AlignX = s.AlignX;
            sprite.AlignY = s.AlignY;
            sprite.HitTestEnabled = s.HitTestEnabled;
        }
        else
        {
            sprite.AssetRef = dto.AssetRef;
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

        // 挂载 ShapeComponent(可选):矢量形状/自由笔触
        if (dto.ShapeV2 is { } sd)
        {
            BackingObject.AddComponent(new ShapeComponent
            {
                ShapeType = sd.ShapeType,
                Width = sd.Width,
                Height = sd.Height,
                X2 = sd.X2,
                Y2 = sd.Y2,
                Points = sd.Points is null
                    ? []
                    : sd.Points.Select(p => (p.X, p.Y)).ToList(),
                ConeAngle = sd.ConeAngle,
                ConeRadius = sd.ConeRadius,
                Rotation = sd.Rotation,
                StrokeColor = sd.StrokeColor,
                FillColor = sd.FillColor,
                StrokeWidth = sd.StrokeWidth,
                IsFilled = sd.IsFilled,
                StrokeStyle = (StrokeStyle)sd.StrokeStyle
            });
        }

        // 挂载 TextComponent(可选):地图文本标注
        if (dto.TextV2 is { } td)
        {
            BackingObject.AddComponent(new TextComponent
            {
                Text = td.Text ?? string.Empty,
                FontSize = td.FontSize,
                Color = td.Color,
                BackgroundColor = td.BackgroundColor,
                IsBold = td.IsBold,
                IsItalic = td.IsItalic,
                Align = (TextAlign)td.Align
            });
        }

        // 挂载 TokenComponent：ObjectType == "Token" 或有 TokenV2/ConditionsV2 数据时
        if (dto.ObjectType == "Token" || dto.TokenV2.HasValue || dto.ConditionsV2 is not null)
        {
            var token = new TokenComponent
            {
                TokenName = dto.Name,
            };
            if (dto.TokenV2.HasValue)
            {
                var tv = dto.TokenV2.Value;
                token.InitiativeOrder = tv.InitiativeOrder;
                token.IsPlayerControlled = tv.IsPlayerControlled;
                token.MovementSpeed = tv.MovementSpeed;
                token.CurrentHP = tv.CurrentHP;
                token.MaxHP = tv.MaxHP;
                token.Shape = tv.Shape;
            }
            if (dto.ConditionsV2 is not null)
            {
                token.Conditions = dto.ConditionsV2.Select(c => new ConditionEntry
                {
                    Id = c.Id,
                    Name = c.Name,
                    Icon = c.Icon,
                    StackCount = c.StackCount,
                    RemainingRounds = c.RemainingRounds,
                    ColorHex = c.ColorHex
                }).ToList();
            }
            BackingObject.AddComponent(token);
        }

        // 挂载 GraphNodeComponent(可选):把本对象标记成拓扑节点
        if (dto.GraphNodeV2 is { } gnd)
        {
            BackingObject.AddComponent(new GraphNodeComponent
            {
                Kind         = (GraphNodeKind)gnd.Kind,
                DisplayName  = gnd.DisplayName ?? string.Empty,
                Description  = gnd.Description ?? string.Empty,
                Visibility   = (GraphVisibility)gnd.Visibility,
                RenderMode   = (GraphNodeRenderMode)gnd.RenderMode,
                IconAssetRef = gnd.IconAssetRef ?? string.Empty,
                Color        = gnd.Color,
                Size         = gnd.Size,
                Shape        = gnd.Shape,
            });
        }

        // 挂载 GraphLinkComponent(可选,可多条):本节点的出边
        if (dto.GraphLinksV2 is not null)
        {
            foreach (var gld in dto.GraphLinksV2)
            {
                BackingObject.AddComponent(new GraphLinkComponent
                {
                    LinkId          = string.IsNullOrEmpty(gld.LinkId)
                        ? Guid.NewGuid().ToString("N") : gld.LinkId,
                    TargetNodeId    = gld.TargetNodeId ?? string.Empty,
                    Kind            = (GraphLinkKind)gld.Kind,
                    IsBidirectional = gld.IsBidirectional,
                    Label           = gld.Label ?? string.Empty,
                    Visibility      = (GraphVisibility)gld.Visibility,
                    IsPassable      = gld.IsPassable,
                    Cost            = gld.Cost,
                    Color           = gld.Color,
                    Width           = gld.Width,
                    StrokeStyle     = (StrokeStyle)gld.StrokeStyle,
                });
            }
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
                ShapeComponent sh => new ShapeComponentEditor(this, sh),
                TextComponent tx => new TextComponentEditor(this, tx),
                GraphNodeComponent gn => new GraphNodeComponentEditor(this, gn),
                GraphLinkComponent gl => new GraphLinkComponentEditor(this, gl),
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

    /// <summary>层级树和状态栏显示名称（图标 + 名称）。</summary>
    public string DisplayName => string.IsNullOrWhiteSpace(Icon)
        ? Name
        : $"{Icon} {Name}";

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
        // Shape/Text 的包围盒读自组件，挂载后必须重算（原先是 CellSize 兜底值）
        NotifyBoundsChanged();
        RebuildComponentEditors();
    }

    public bool RemoveComponent<T>() where T : class, IComponent
    {
        var removed = BackingObject.RemoveComponent<T>();
        if (removed)
        {
            OnPropertyChanged(nameof(HasWallComponent));
            NotifyBoundsChanged();
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

    public string AssetRef
    {
        get => Sp.AssetRef;
        set { if (Sp.AssetRef != value) { Sp.AssetRef = value; OnPropertyChanged(); } }
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

    /// <summary>Token 组件属性变更后刷新相关绑定（先攻追踪器等）。</summary>
    internal void NotifyTokenComponentChanged()
    {
        OnPropertyChanged(nameof(InitiativeOrder));
        OnPropertyChanged("Components");
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

    public int CurrentHP
    {
        get => GetComponent<MapEngine.Core.Components.TokenComponent>()?.CurrentHP ?? 0;
        set
        {
            if (GetComponent<MapEngine.Core.Components.TokenComponent>() is { } token)
            {
                token.CurrentHP = Math.Clamp(value, 0, token.MaxHP);
                OnPropertyChanged();
                OnPropertyChanged(nameof(HPPercent));
                OnPropertyChanged(nameof(HPBarColor));
            }
        }
    }

    public int MaxHP
    {
        get => GetComponent<MapEngine.Core.Components.TokenComponent>()?.MaxHP ?? 100;
        set
        {
            if (GetComponent<MapEngine.Core.Components.TokenComponent>() is { } token)
            {
                token.MaxHP = Math.Max(1, value);
                // 同步 CurrentHP 不超上限
                if (token.CurrentHP > token.MaxHP) token.CurrentHP = token.MaxHP;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CurrentHP));
                OnPropertyChanged(nameof(HPPercent));
                OnPropertyChanged(nameof(HPBarColor));
            }
        }
    }

    /// <summary>HP 百分比 [0,100]，用于 PercentToWidthConverter。</summary>
    public double HPPercent
    {
        get
        {
            var token = GetComponent<MapEngine.Core.Components.TokenComponent>();
            if (token is null || token.MaxHP <= 0) return 0;
            return Math.Clamp(token.CurrentHP * 100.0 / token.MaxHP, 0, 100);
        }
    }

    /// <summary>HP 颜色：绿 > 50%，黄 > 25%，红 ≤ 25%。</summary>
    public string HPBarColor => HPPercent switch
    {
        > 50 => "#4CAF50",
        > 25 => "#FF9800",
        _    => "#F44336"
    };

    public string HPBarBackground => "#33FFFFFF";

    private string _portraitPath = string.Empty;
    /// <summary>Token 肖像图片路径（本地文件路径或资源 URI）。</summary>
    public string PortraitPath
    {
        get => _portraitPath;
        set => SetProperty(ref _portraitPath, value);
    }

    /// <summary>是否已设置肖像（用于 XAML 条件显示）。</summary>
    public bool HasPortrait => !string.IsNullOrWhiteSpace(_portraitPath);

    public string SelectionType => $"地图对象 / {ObjectType}";

    public bool CanNavigateToMap => HasMapPosition;

    public bool ShouldRenderOnMap
        => IsActive && (CanNavigateToMap || IsPreviewInstance) && !string.IsNullOrWhiteSpace(Icon);

    // Shape/Text 不是精灵，没有格子尺寸的概念。若沿用 CellSize，
    // 一个 600 单位长的锥形只会得到 50×50 的选中框和命中区，选不中也看不出范围。
    // 这里改成读实际几何：返回一个以对象中心为心、能完整包住图形的对称盒。
    // 对 line/cone 这类"中心即起点/顶点"的非对称图形，对称盒会比紧包围盒略大，
    // 但换来的是 MapLeft/MapTop/命中测试/handle 全部无需改动。
    public double SpriteWidth => ObjectType switch
    {
        "Shape" => ShapeHalfExtentX * 2.0,
        "Text" => TextHalfExtentX * 2.0,
        _ => MapViewportConstants.CellSize * ScaleX
    };

    public double SpriteHeight => ObjectType switch
    {
        "Shape" => ShapeHalfExtentY * 2.0,
        "Text" => TextHalfExtentY * 2.0,
        _ => MapViewportConstants.CellSize * ScaleY
    };

    private const double MinPickExtent = 12.0;   // 极小图形也留出可点击的半径

    private double ShapeHalfExtentX => ShapeHalfExtents.X;
    private double ShapeHalfExtentY => ShapeHalfExtents.Y;

    /// <summary>按 ShapeType 求"以中心为心的对称半extent"（内容像素）。</summary>
    private (double X, double Y) ShapeHalfExtents
    {
        get
        {
            if (Shp is not { } s) return (MinPickExtent, MinPickExtent);
            // 描边向外扩张一半线宽
            var pad = s.StrokeWidth / 2.0 + 2.0;

            double hx, hy;
            switch (s.ShapeType)
            {
                case "line":
                    hx = Math.Abs(s.X2);
                    hy = Math.Abs(s.Y2);
                    break;

                case "cone":
                case "wedge":
                    // 顶点在中心，最远端为半径；对称盒取半径本身
                    hx = hy = s.ConeRadius;
                    break;

                case "polygon":
                case "freehand":
                    hx = hy = 0;
                    foreach (var (px, py) in s.Points)
                    {
                        hx = Math.Max(hx, Math.Abs(px));
                        hy = Math.Max(hy, Math.Abs(py));
                    }
                    break;

                default:   // rect / ellipse / circle
                    hx = s.Width / 2.0;
                    hy = s.Height / 2.0;
                    break;
            }

            return (Math.Max(MinPickExtent, hx + pad), Math.Max(MinPickExtent, hy + pad));
        }
    }

    private double TextHalfExtentX => TextHalfExtents.X;
    private double TextHalfExtentY => TextHalfExtents.Y;

    /// <summary>
    /// 文本的估算半extent。真实排版在 Avalonia 覆盖层（MapTextManager），
    /// 这里只需要一个够用的命中/选中盒，所以按字符宽度粗估：
    /// CJK 及全角约 1em，其余约 0.55em。
    /// </summary>
    private (double X, double Y) TextHalfExtents
    {
        get
        {
            if (Txt is not { } t) return (MinPickExtent, MinPickExtent);

            var lines = (t.Text ?? string.Empty).Split('\n');
            var widestEm = 0.0;
            foreach (var line in lines)
            {
                var em = 0.0;
                foreach (var ch in line)
                {
                    em += ch >= 0x2E80 ? 1.0 : 0.55;
                }
                widestEm = Math.Max(widestEm, em);
            }

            var width = Math.Max(1.0, widestEm) * t.FontSize;
            var height = Math.Max(1, lines.Length) * t.FontSize * 1.3;
            // 覆盖层的 Border 有内边距，横向多留一点
            return (Math.Max(MinPickExtent, width / 2.0 + 6.0),
                    Math.Max(MinPickExtent, height / 2.0 + 4.0));
        }
    }

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

    // ── Shape 组件属性委托（矢量图形，可选组件）───────────────────
    // 渲染层是 60fps 持续重绘（SilkMapCanvas 的 DispatcherTimer），
    // 因此改这些值不需要手动触发场景重建，下一帧 MapSceneBuilder 会读到新值。

    /// <summary>矢量形状组件，可能未挂载（返回 null）。</summary>
    private MapEngine.Core.Components.ShapeComponent? Shp
        => BackingObject.GetComponent<MapEngine.Core.Components.ShapeComponent>();

    /// <summary>是否挂载了形状组件（Inspector 面板门控）。</summary>
    public bool HasShapeComponent => Shp is not null;

    /// <summary>形状类型（line/rect/ellipse/cone/wedge/polygon/freehand），只读展示。</summary>
    public string ShapeTypeLabel => Shp?.ShapeType switch
    {
        "line"     => "直线",
        "rect"     => "矩形",
        "ellipse"  => "椭圆",
        "circle"   => "圆形",
        "cone"     => "锥形",
        "wedge"    => "扇形",
        "polygon"  => "多边形",
        "freehand" => "自由笔触",
        _          => "未知",
    };

    /// <summary>描边颜色 #AARRGGBB / #RRGGBB。</summary>
    public string ShapeStrokeColor
    {
        get => Shp?.StrokeColor ?? "#845EF7";
        set
        {
            if (Shp is { } s && s.StrokeColor != value)
            {
                s.StrokeColor = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>填充颜色 #AARRGGBB（含 alpha，默认 0x40 半透明）。</summary>
    public string ShapeFillColor
    {
        get => Shp?.FillColor ?? "#40845EF7";
        set
        {
            if (Shp is { } s && s.FillColor != value)
            {
                s.FillColor = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>描边宽度（像素，1-20）。</summary>
    public double ShapeStrokeWidth
    {
        get => Shp?.StrokeWidth ?? 2;
        set
        {
            var clamped = Math.Clamp(value, 1, 20);
            if (Shp is { } s && Math.Abs(s.StrokeWidth - clamped) > 0.001)
            {
                s.StrokeWidth = clamped;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>是否填充（line/freehand 恒为 false 更合理，但不强制）。</summary>
    public bool ShapeIsFilled
    {
        get => Shp?.IsFilled ?? false;
        set
        {
            if (Shp is { } s && s.IsFilled != value)
            {
                s.IsFilled = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>描边样式下拉索引：0=实线 1=虚线 2=点线。</summary>
    public int ShapeStrokeStyleIndex
    {
        get => (int)(Shp?.StrokeStyle ?? MapEngine.Core.Components.StrokeStyle.Solid);
        set
        {
            if (Shp is { } s && (int)s.StrokeStyle != value)
            {
                s.StrokeStyle = (MapEngine.Core.Components.StrokeStyle)value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>形状宽度（rect/ellipse 有效）。</summary>
    public double ShapeWidth
    {
        get => Shp?.Width ?? 0;
        set
        {
            if (Shp is { } s && Math.Abs(s.Width - value) > 0.001)
            {
                s.Width = Math.Max(1, value);
                OnPropertyChanged();
                NotifyBoundsChanged();
            }
        }
    }

    /// <summary>形状高度（rect/ellipse 有效）。</summary>
    public double ShapeHeight
    {
        get => Shp?.Height ?? 0;
        set
        {
            if (Shp is { } s && Math.Abs(s.Height - value) > 0.001)
            {
                s.Height = Math.Max(1, value);
                OnPropertyChanged();
                NotifyBoundsChanged();
            }
        }
    }

    /// <summary>rect/ellipse 才显示宽高编辑框。</summary>
    public bool ShapeHasSize => Shp?.ShapeType is "rect" or "ellipse" or "circle";

    /// <summary>cone/wedge 才显示半径/张角/朝向编辑框。</summary>
    public bool ShapeHasCone => Shp?.ShapeType is "cone" or "wedge";

    /// <summary>锥形半径。</summary>
    public double ShapeConeRadius
    {
        get => Shp?.ConeRadius ?? 0;
        set
        {
            if (Shp is { } s && Math.Abs(s.ConeRadius - value) > 0.001)
            {
                s.ConeRadius = Math.Max(1, value);
                OnPropertyChanged();
                NotifyBoundsChanged();
            }
        }
    }

    /// <summary>锥形半张角（度，1-180）。</summary>
    public double ShapeConeAngle
    {
        get => Shp?.ConeAngle ?? 0;
        set
        {
            var clamped = Math.Clamp(value, 1, 180);
            if (Shp is { } s && Math.Abs(s.ConeAngle - clamped) > 0.001)
            {
                s.ConeAngle = clamped;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>锥形朝向（度，0 = +X，逆时针为正）。</summary>
    public double ShapeRotation
    {
        get => Shp?.Rotation ?? 0;
        set
        {
            if (Shp is { } s && Math.Abs(s.Rotation - value) > 0.001)
            {
                s.Rotation = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>把调色板色值套到描边（保留原 alpha 通道语义：描边取纯色）。</summary>
    public void ApplyShapeStrokeColor(string? hex)
    {
        if (!string.IsNullOrWhiteSpace(hex)) ShapeStrokeColor = hex;
    }

    /// <summary>把调色板色值套到填充（自动加 0x40 alpha 前缀，形状填充默认半透明）。</summary>
    public void ApplyShapeFillColor(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return;
        // "#RRGGBB" → "#40RRGGBB"；已带 alpha 的原样使用
        ShapeFillColor = hex.Length == 7 ? $"#40{hex[1..]}" : hex;
    }

    // ── Text 组件属性委托（地图文本标注）─────────────────────────

    /// <summary>文本组件，可能未挂载（返回 null）。</summary>
    private MapEngine.Core.Components.TextComponent? Txt
        => BackingObject.GetComponent<MapEngine.Core.Components.TextComponent>();

    /// <summary>是否挂载了文本组件（Inspector 面板门控）。</summary>
    public bool HasTextComponent => Txt is not null;

    public string TextContent
    {
        get => Txt?.Text ?? string.Empty;
        set
        {
            if (Txt is { } t && t.Text != value)
            {
                t.Text = value;
                OnPropertyChanged();
                NotifyBoundsChanged();
                // 层级树显示名跟随内容（截断）
                Name = value.Length > 12 ? value[..12] + "…" : value;
            }
        }
    }

    public double TextFontSize
    {
        get => Txt?.FontSize ?? 16;
        set
        {
            var clamped = Math.Clamp(value, 4, 400);
            if (Txt is { } t && Math.Abs(t.FontSize - clamped) > 0.001)
            {
                t.FontSize = clamped;
                OnPropertyChanged();
                NotifyBoundsChanged();
            }
        }
    }

    public string TextColor
    {
        get => Txt?.Color ?? "#F8F9FA";
        set
        {
            if (Txt is { } t && t.Color != value)
            {
                t.Color = value;
                OnPropertyChanged();
            }
        }
    }

    public string TextBackgroundColor
    {
        get => Txt?.BackgroundColor ?? "#A0000000";
        set
        {
            if (Txt is { } t && t.BackgroundColor != value)
            {
                t.BackgroundColor = value;
                OnPropertyChanged();
            }
        }
    }

    public bool TextIsBold
    {
        get => Txt?.IsBold ?? false;
        set
        {
            if (Txt is { } t && t.IsBold != value)
            {
                t.IsBold = value;
                OnPropertyChanged();
            }
        }
    }

    public bool TextIsItalic
    {
        get => Txt?.IsItalic ?? false;
        set
        {
            if (Txt is { } t && t.IsItalic != value)
            {
                t.IsItalic = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>对齐下拉索引：0=左 1=中 2=右。</summary>
    public int TextAlignIndex
    {
        get => (int)(Txt?.Align ?? MapEngine.Core.Components.TextAlign.Center);
        set
        {
            if (Txt is { } t && (int)t.Align != value)
            {
                t.Align = (MapEngine.Core.Components.TextAlign)value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>把调色板色值套到文字颜色。</summary>
    public void ApplyTextColor(string? hex)
    {
        if (!string.IsNullOrWhiteSpace(hex)) TextColor = hex;
    }

    // ── 拓扑节点（GraphNodeComponent）─────────────────────────────────────

    /// <summary>拓扑节点组件，可能未挂载。</summary>
    private MapEngine.Core.Components.GraphNodeComponent? GNode
        => BackingObject.GetComponent<MapEngine.Core.Components.GraphNodeComponent>();

    /// <summary>是否是拓扑节点（Inspector 面板门控）。</summary>
    public bool HasGraphNodeComponent => GNode is not null;

    public int GraphNodeKindIndex
    {
        get => (int)(GNode?.Kind ?? MapEngine.Core.Components.GraphNodeKind.Location);
        set
        {
            if (GNode is { } g && (int)g.Kind != value)
            {
                g.Kind = (MapEngine.Core.Components.GraphNodeKind)value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>节点显示名。留空时回落到对象名，所以 getter 不补默认值。</summary>
    public string GraphNodeDisplayName
    {
        get => GNode?.DisplayName ?? string.Empty;
        set
        {
            if (GNode is { } g && g.DisplayName != value)
            {
                g.DisplayName = value ?? string.Empty;
                OnPropertyChanged();
            }
        }
    }

    public string GraphNodeDescription
    {
        get => GNode?.Description ?? string.Empty;
        set
        {
            if (GNode is { } g && g.Description != value)
            {
                g.Description = value ?? string.Empty;
                OnPropertyChanged();
            }
        }
    }

    public int GraphNodeVisibilityIndex
    {
        get => (int)(GNode?.Visibility ?? MapEngine.Core.Components.GraphVisibility.Hidden);
        set
        {
            if (GNode is { } g && (int)g.Visibility != value)
            {
                g.Visibility = (MapEngine.Core.Components.GraphVisibility)value;
                OnPropertyChanged();
                NotifyBoundsChanged();  // 可见性影响渲染
            }
        }
    }

    public int GraphNodeRenderModeIndex
    {
        get => (int)(GNode?.RenderMode ?? MapEngine.Core.Components.GraphNodeRenderMode.Icon);
        set
        {
            if (GNode is { } g && (int)g.RenderMode != value)
            {
                g.RenderMode = (MapEngine.Core.Components.GraphNodeRenderMode)value;
                OnPropertyChanged();
                NotifyBoundsChanged();
            }
        }
    }

    public string GraphNodeColor
    {
        get => GNode?.Color ?? "#4A90E2";
        set
        {
            if (GNode is { } g && g.Color != value)
            {
                g.Color = value ?? "#4A90E2";
                OnPropertyChanged();
            }
        }
    }

    public double GraphNodeSize
    {
        get => GNode?.Size ?? 48;
        set
        {
            var clamped = Math.Clamp(value, 8, 512);
            if (GNode is { } g && Math.Abs(g.Size - clamped) > 0.001)
            {
                g.Size = clamped;
                OnPropertyChanged();
                NotifyBoundsChanged();
            }
        }
    }

    public string GraphNodeShape
    {
        get => GNode?.Shape ?? "circle";
        set
        {
            if (GNode is { } g && g.Shape != value)
            {
                g.Shape = value ?? "circle";
                OnPropertyChanged();
            }
        }
    }

    /// <summary>把调色板色值套到节点颜色。</summary>
    public void ApplyGraphNodeColor(string? hex)
    {
        if (!string.IsNullOrWhiteSpace(hex)) GraphNodeColor = hex;
    }

    /// <summary>拓扑节点组件增删后刷新绑定。</summary>
    internal void NotifyGraphNodeComponentChanged()
    {
        OnPropertyChanged(nameof(HasGraphNodeComponent));
        OnPropertyChanged(nameof(GraphNodeKindIndex));
        OnPropertyChanged(nameof(GraphNodeDisplayName));
        OnPropertyChanged(nameof(GraphNodeDescription));
        OnPropertyChanged(nameof(GraphNodeVisibilityIndex));
        OnPropertyChanged(nameof(GraphNodeRenderModeIndex));
        OnPropertyChanged(nameof(GraphNodeColor));
        OnPropertyChanged(nameof(GraphNodeSize));
        OnPropertyChanged(nameof(GraphNodeShape));
        NotifyBoundsChanged();
    }

    /// <summary>形状组件增删后刷新所有相关绑定。</summary>
    internal void NotifyShapeComponentChanged()
    {
        OnPropertyChanged(nameof(HasShapeComponent));
        OnPropertyChanged(nameof(ShapeTypeLabel));
        OnPropertyChanged(nameof(ShapeStrokeColor));
        OnPropertyChanged(nameof(ShapeFillColor));
        OnPropertyChanged(nameof(ShapeStrokeWidth));
        OnPropertyChanged(nameof(ShapeIsFilled));
        OnPropertyChanged(nameof(ShapeStrokeStyleIndex));
        OnPropertyChanged(nameof(ShapeWidth));
        OnPropertyChanged(nameof(ShapeHeight));
        OnPropertyChanged(nameof(ShapeHasSize));
        OnPropertyChanged(nameof(ShapeHasCone));
        OnPropertyChanged(nameof(ShapeConeRadius));
        OnPropertyChanged(nameof(ShapeConeAngle));
        OnPropertyChanged(nameof(ShapeRotation));
        NotifyBoundsChanged();
    }

    /// <summary>
    /// 几何尺寸变化后刷新派生的包围盒属性。
    /// Shape/Text 的 SpriteWidth/Height 读自组件，改宽高/字号/文本内容都要通知，
    /// 否则选中框和命中区还停在旧尺寸上。
    /// </summary>
    internal void NotifyBoundsChanged()
    {
        OnPropertyChanged(nameof(SpriteWidth));
        OnPropertyChanged(nameof(SpriteHeight));
        OnPropertyChanged(nameof(MapLeft));
        OnPropertyChanged(nameof(MapTop));
    }
}

public static class MapViewportConstants
{
    public const double ContentSize = 204800;
    public const double WorldOriginContent = ContentSize / 2.0;
    public const double TileSize = 512;
    public const double CellSize = 50;
    public const double MapObjectSize = 36;
}
