using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MapEngine.Core.Components;

namespace MapEngine.Avalonia.ViewModels;

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
    }

    /// <summary>所属层级项(承载真正数据的 HierarchyItemViewModel)。字段绑定经此委托。</summary>
    public HierarchyItemViewModel Owner { get; }

    /// <summary>头部显示名(如 "Transform" / "Sprite Renderer")。</summary>
    public abstract string Title { get; }

    /// <summary>头部图标 emoji。</summary>
    public abstract string Icon { get; }

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
    public override bool CanRemove => false;
    public override IComponent Component { get; } = component;
}

/// <summary>SpriteRenderer 组件编辑器(核心,不可移除)。</summary>
public sealed class SpriteRendererComponentEditor(HierarchyItemViewModel owner, SpriteRendererComponent component)
    : ComponentEditorViewModel(owner)
{
    public override string Title => "Sprite Renderer";
    public override string Icon => "🖼";
    public override bool CanRemove => false;
    public override IComponent Component { get; } = component;
}

/// <summary>Vision 组件编辑器(可选,可移除)。</summary>
public sealed class VisionComponentEditor(HierarchyItemViewModel owner, VisionComponent component)
    : ComponentEditorViewModel(owner)
{
    public override string Title => "Vision";
    public override string Icon => "👁";
    public override IComponent Component { get; } = component;
}

/// <summary>Wall 组件编辑器(可选,可移除)。</summary>
public sealed class WallComponentEditor(HierarchyItemViewModel owner, WallComponent component)
    : ComponentEditorViewModel(owner)
{
    public override string Title => "Wall";
    public override string Icon => "🧱";
    public override IComponent Component { get; } = component;
}

/// <summary>Token 组件编辑器(可选,可移除)。</summary>
public sealed class TokenComponentEditor(HierarchyItemViewModel owner, TokenComponent component)
    : ComponentEditorViewModel(owner)
{
    public override string Title => "Token";
    public override string Icon => "🎭";
    public override IComponent Component { get; } = component;
}

/// <summary>兜底编辑器:未单独建模的组件,只显示类型名(只读)。</summary>
public sealed class GenericComponentEditor(HierarchyItemViewModel owner, IComponent component)
    : ComponentEditorViewModel(owner)
{
    public override string Title => Component.TypeName;
    public override string Icon => "🧩";
    public override IComponent Component { get; } = component;
}
