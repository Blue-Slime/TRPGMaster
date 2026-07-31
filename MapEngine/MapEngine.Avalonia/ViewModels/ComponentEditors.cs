using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MapEngine.Core.Components;

namespace MapEngine.Avalonia.ViewModels;

/// <summary>
/// Inspector 组件头部使用的 SVG 图标路径数据(Material Design Icons,24x24 viewBox)。
/// 与 <c>Styles/OwlbearTheme.axaml</c> 中的 chrome 图标区分:这些是随数据变化的动态图标,
/// 因此以常量形式放在 VM 层,而非 XAML 资源字典。
/// </summary>
internal static class IconPaths
{
    /// <summary>Transform:四向移动箭头。</summary>
    public const string AxisArrow =
        "M12,2L9,5H11V11H5V9L2,12L5,15V13H11V19H9L12,22L15,19H13V13H19V15L22,12L19,9V11H13V5H15L12,2Z";

    /// <summary>Sprite Renderer:图片。</summary>
    public const string Image =
        "M8.5,13.5L11,16.5L14.5,12L19,18H5M21,19V5C21,3.89 20.1,3 19,3H5A2,2 0 0,0 3,5V19A2,2 0 0,0 5,21H19A2,2 0 0,0 21,19Z";

    /// <summary>Vision:眼睛。</summary>
    public const string Eye =
        "M12,9A3,3 0 0,0 9,12A3,3 0 0,0 12,15A3,3 0 0,0 15,12A3,3 0 0,0 12,9M12,17A5,5 0 0,1 7,12A5,5 0 0,1 12,7A5,5 0 0,1 17,12A5,5 0 0,1 12,17M12,4.5C7,4.5 2.73,7.61 1,12C2.73,16.39 7,19.5 12,19.5C17,19.5 21.27,16.39 23,12C21.27,7.61 17,4.5 12,4.5Z";

    /// <summary>Wall:砖墙。</summary>
    public const string Wall =
        "M2,4H22V8H2V4M2,10H10V14H2V10M12,10H22V14H12V10M2,16H6V20H2V16M8,16H16V20H8V16M18,16H22V20H18V16Z";

    /// <summary>Token:剧场面具。</summary>
    public const string Token =
        "M12,2A10,10 0 0,0 2,12A10,10 0 0,0 12,22A10,10 0 0,0 22,12A10,10 0 0,0 12,2M8.5,9A1.5,1.5 0 0,1 10,10.5A1.5,1.5 0 0,1 8.5,12A1.5,1.5 0 0,1 7,10.5A1.5,1.5 0 0,1 8.5,9M15.5,9A1.5,1.5 0 0,1 17,10.5A1.5,1.5 0 0,1 15.5,12A1.5,1.5 0 0,1 14,10.5A1.5,1.5 0 0,1 15.5,9M12,17.5C9.67,17.5 7.69,16.04 6.89,14H17.11C16.31,16.04 14.33,17.5 12,17.5Z";

    /// <summary>Shape:矢量图形(方块+圆)。</summary>
    public const string Shape =
        "M11,13.5V21.5H3V13.5H11M9,15.5H5V19.5H9V15.5M12,2L17.5,11H6.5L12,2M12,5.86L10.08,9H13.92L12,5.86M17.5,13C20,13 22,15 22,17.5C22,20 20,22 17.5,22C15,22 13,20 13,17.5C13,15 15,13 17.5,13M17.5,15A2.5,2.5 0 0,0 15,17.5A2.5,2.5 0 0,0 17.5,20A2.5,2.5 0 0,0 20,17.5A2.5,2.5 0 0,0 17.5,15Z";

    /// <summary>Text:字母 T(format-text)。</summary>
    public const string Text =
        "M18.5,4L19.66,8.35L18.7,8.61C18.25,7.74 17.79,6.87 17.26,6.43C16.73,6 16.11,6 15.5,6H13V16.5C13,17 13,17.5 13.33,17.75C13.67,18 14.33,18 15,18V19H9V18C9.67,18 10.33,18 10.67,17.75C11,17.5 11,17 11,16.5V6H8.5C7.89,6 7.27,6 6.74,6.43C6.21,6.87 5.75,7.74 5.3,8.61L4.34,8.35L5.5,4H18.5Z";

    /// <summary>兜底:拼图块。</summary>
    public const string Puzzle =
        "M20.5,11H19V7A2,2 0 0,0 17,5H13V3.5A2.5,2.5 0 0,0 10.5,1A2.5,2.5 0 0,0 8,3.5V5H4A2,2 0 0,0 2,7V10.8H3.5C5,10.8 6.2,12 6.2,13.5C6.2,15 5,16.2 3.5,16.2H2V20A2,2 0 0,0 4,22H7.8V20.5C7.8,19 9,17.8 10.5,17.8C12,17.8 13.2,19 13.2,20.5V22H17A2,2 0 0,0 19,20V16H20.5A2.5,2.5 0 0,0 23,13.5A2.5,2.5 0 0,0 20.5,11Z";
}

/// <summary>
/// Inspector 里单个组件的编辑器 VM(参考 Unity Inspector 的组件折叠块)。
///
/// 设计:
/// - 每个 Core 组件实例对应一个编辑器 VM,由 <see cref="HierarchyItemViewModel.RebuildComponentEditors"/> 构建。
/// - 编辑器只负责"头部 chrome"(标题/图标/可否移除/折叠状态/移除命令)。
/// - 具体字段编辑走类型化 DataTemplate,直接绑定 <c>Owner.&lt;Prop&gt;</c>(复用既有委托属性和通知),
///   编辑器 VM 保持极薄,不重复实现字段委托。
/// </summary>
public abstract class ComponentEditorViewModel : ViewModelBase
{
    protected ComponentEditorViewModel(HierarchyItemViewModel owner)
    {
        Owner = owner;
        RemoveCommand = new RelayCommand(() => Owner.RemoveComponentEditor(this), () => CanRemove);
        ToggleExpandCommand = new RelayCommand(() => IsExpanded = !IsExpanded);

        // 转发 Owner 的属性变更通知：XAML 绑定路径是 Owner.HPPercent 等，
        // 绑定引擎监听的是本 VM（ComponentEditorViewModel）的 PropertyChanged。
        // 通知 "Owner" 属性变更会让所有 Owner.xxx 子路径绑定重新求值。
        Owner.PropertyChanged += (_, _) => OnPropertyChanged(nameof(Owner));
    }

    /// <summary>所属层级项(承载真正数据的 HierarchyItemViewModel)。字段绑定经此委托。</summary>
    public HierarchyItemViewModel Owner { get; }

    /// <summary>头部显示名(如 "Transform" / "Sprite Renderer")。</summary>
    public abstract string Title { get; }

    /// <summary>头部图标 emoji(SVG 不可用时的兜底)。</summary>
    public abstract string Icon { get; }

    /// <summary>
    /// 头部 SVG 图标路径数据(Material Design Icons,24x24 viewBox)。
    /// Avalonia 内置类型转换器会把该字符串解析为 Geometry。
    /// 派生类未覆盖时返回通用"拼图"图标。
    /// </summary>
    public virtual string IconPath => IconPaths.Puzzle;

    /// <summary>核心组件(Transform/Sprite)不可移除;可选组件(Vision/Wall/...)可移除。</summary>
    public virtual bool CanRemove => true;

    /// <summary>对应的 Core 组件实例(用于移除时定位)。</summary>
    public abstract IComponent Component { get; }

    private bool _isExpanded = true;
    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }

    public IRelayCommand RemoveCommand { get; }
    public IRelayCommand ToggleExpandCommand { get; }
}

/// <summary>Transform 组件编辑器(核心,不可移除)。</summary>
public sealed class TransformComponentEditor(HierarchyItemViewModel owner, TransformComponent component)
    : ComponentEditorViewModel(owner)
{
    public override string Title => "Transform";
    public override string Icon => "✥";
    public override string IconPath => IconPaths.AxisArrow;
    public override bool CanRemove => false;
    public override IComponent Component { get; } = component;
}

/// <summary>SpriteRenderer 组件编辑器(核心,不可移除)。</summary>
public sealed class SpriteRendererComponentEditor(HierarchyItemViewModel owner, SpriteRendererComponent component)
    : ComponentEditorViewModel(owner)
{
    public override string Title => "Sprite Renderer";
    public override string Icon => "🖼";
    public override string IconPath => IconPaths.Image;
    public override bool CanRemove => false;
    public override IComponent Component { get; } = component;
}

/// <summary>Vision 组件编辑器(可选,可移除)。</summary>
public sealed class VisionComponentEditor(HierarchyItemViewModel owner, VisionComponent component)
    : ComponentEditorViewModel(owner)
{
    public override string Title => "Vision";
    public override string Icon => "👁";
    public override string IconPath => IconPaths.Eye;
    public override IComponent Component { get; } = component;
}

/// <summary>Wall 组件编辑器(可选,可移除)。</summary>
public sealed class WallComponentEditor(HierarchyItemViewModel owner, WallComponent component)
    : ComponentEditorViewModel(owner)
{
    public override string Title => "Wall";
    public override string Icon => "🧱";
    public override string IconPath => IconPaths.Wall;
    public override IComponent Component { get; } = component;
}

/// <summary>Token 组件编辑器(可选,可移除)。</summary>
public sealed class TokenComponentEditor : ComponentEditorViewModel
{
    private readonly TokenComponent _component;

    public TokenComponentEditor(HierarchyItemViewModel owner, TokenComponent component)
        : base(owner)
    {
        _component = component;
        Conditions = new ObservableCollection<ConditionEntryViewModel>(
            component.Conditions.Select(c => new ConditionEntryViewModel(c))
        );
        AddConditionCommand = new RelayCommand<ConditionEntry>(AddCondition);
        RemoveConditionCommand = new RelayCommand<ConditionEntryViewModel>(RemoveCondition);
    }

    public override string Title => "Token";
    public override string Icon => "🎭";
    public override string IconPath => IconPaths.Token;
    public override IComponent Component => _component;

    /// <summary>是否显示在先攻追踪器中（双向绑定）。</summary>
    public bool IsInInitiativeTracker
    {
        get => _component.IsInInitiativeTracker;
        set
        {
            if (_component.IsInInitiativeTracker != value)
            {
                _component.IsInInitiativeTracker = value;
                Owner.NotifyTokenComponentChanged();
                OnPropertyChanged();
            }
        }
    }

    public ObservableCollection<ConditionEntryViewModel> Conditions { get; }

    public RelayCommand<ConditionEntry> AddConditionCommand { get; }
    public RelayCommand<ConditionEntryViewModel> RemoveConditionCommand { get; }

    private void AddCondition(ConditionEntry? preset)
    {
        if (preset is null) return;
        var newCondition = MapEngine.Avalonia.Services.PredefinedConditions.Clone(preset);
        _component.Conditions.Add(newCondition);
        Conditions.Add(new ConditionEntryViewModel(newCondition));
    }

    private void RemoveCondition(ConditionEntryViewModel? vm)
    {
        if (vm is null) return;
        _component.Conditions.Remove(vm.Source);
        Conditions.Remove(vm);
    }
}

/// <summary>Shape 组件编辑器(矢量图形样式,可移除)。</summary>
public sealed class ShapeComponentEditor : ComponentEditorViewModel
{
    /// <summary>Owlbear Rodeo 2 风格的 8 色调色板(纯色,填充时自动加 0x40 alpha)。</summary>
    internal static readonly string[] SharedPalette =
    [
        "#845EF7", // 紫(默认)
        "#FF6B6B", // 红
        "#FFA94D", // 橙
        "#FFD43B", // 黄
        "#51CF66", // 绿
        "#22B8CF", // 青
        "#339AF0", // 蓝
        "#F8F9FA", // 白
    ];

    public ShapeComponentEditor(HierarchyItemViewModel owner, ShapeComponent component)
        : base(owner)
    {
        Component = component;
        ApplyStrokeColorCommand = new RelayCommand<string?>(Owner.ApplyShapeStrokeColor);
        ApplyFillColorCommand   = new RelayCommand<string?>(Owner.ApplyShapeFillColor);
    }

    public override string Title => "Shape";
    public override string Icon => "▭";
    public override string IconPath => IconPaths.Shape;
    public override IComponent Component { get; }

    /// <summary>调色板色值(XAML ItemsControl 数据源)。</summary>
    public IReadOnlyList<string> ColorPalette => SharedPalette;

    /// <summary>描边样式下拉选项(索引与 <see cref="StrokeStyle"/> 枚举一致)。</summary>
    public IReadOnlyList<string> StrokeStyleOptions { get; } = ["实线", "虚线", "点线"];

    public IRelayCommand<string?> ApplyStrokeColorCommand { get; }
    public IRelayCommand<string?> ApplyFillColorCommand { get; }
}

/// <summary>Text 组件编辑器(地图文本标注,可移除)。</summary>
public sealed class TextComponentEditor : ComponentEditorViewModel
{
    public TextComponentEditor(HierarchyItemViewModel owner, TextComponent component)
        : base(owner)
    {
        Component = component;
        ApplyTextColorCommand = new RelayCommand<string?>(Owner.ApplyTextColor);
    }

    public override string Title => "Text";
    public override string Icon => "🅣";
    public override string IconPath => IconPaths.Text;
    public override IComponent Component { get; }

    /// <summary>与 Shape 编辑器共用同一套调色板,保持视觉一致。</summary>
    public IReadOnlyList<string> ColorPalette => ShapeComponentEditor.SharedPalette;

    /// <summary>水平对齐下拉选项(索引与 <see cref="TextAlign"/> 枚举一致)。</summary>
    public IReadOnlyList<string> AlignOptions { get; } = ["左对齐", "居中", "右对齐"];

    public IRelayCommand<string?> ApplyTextColorCommand { get; }
}

/// <summary>兜底编辑器:未单独建模的组件,只显示类型名(只读)。</summary>
public sealed class GenericComponentEditor(HierarchyItemViewModel owner, IComponent component)
    : ComponentEditorViewModel(owner)
{
    public override string Title => Component.TypeName;
    public override string Icon => "🧩";
    public override IComponent Component { get; } = component;
}
