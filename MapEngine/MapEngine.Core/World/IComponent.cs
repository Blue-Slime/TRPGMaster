namespace MapEngine.Core.Components;

/// <summary>
/// 组件 = 纯数据。不含任何业务逻辑或生命周期钩子。
///
/// 设计约束(ECS-lite 铁律):
/// - 组件只持有字段/属性,不主动执行行为。
/// - 一切逻辑放进无状态 System(见 MapEngine.Core.Systems.ISystem)。
/// - 挂载/移除时的索引登记由 World 统一负责(见 World.AddObject/RemoveObject),
///   组件不自己登记 —— 这样所有场景变更都过 World 单一入口,dirty 标记绝不会漏。
/// - 空间特征(参与四叉树/索引的组件)通过实现 <see cref="ISpatialComponent"/> 暴露本地包围盒,
///   由 World 读取,而非组件亲自登记。
/// </summary>
public interface IComponent
{
    string TypeName { get; }
    GameObject? Owner { get; set; }
    IComponent Clone();
}

public abstract class ComponentBase : IComponent
{
    public abstract string TypeName { get; }
    public GameObject? Owner { get; set; }
    public abstract IComponent Clone();
}
