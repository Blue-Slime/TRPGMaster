namespace MapEngine.Core.Systems;

/// <summary>
/// 无状态系统 —— ECS-lite 的逻辑载体。
///
/// 铁律:
/// - System 不持有场景状态,状态全在 World / 组件里。System 只读组件数据、算结果。
/// - System 声明自己关心的脏标记(<see cref="Interest"/>),Scheduler 据此决定是否跳过本轮。
/// - System 之间的执行顺序由 <see cref="Order"/> 决定(小者先跑),用于表达依赖
///   (如 TransformSystem 先于 VisionSystem)。
/// </summary>
public interface ISystem
{
    string Name { get; }

    /// <summary>本 System 关心的脏标记。World.Dirty 与之有交集时才执行。</summary>
    DirtyFlags Interest { get; }

    /// <summary>执行顺序,升序。默认 0。</summary>
    int Order => 0;

    void Execute(SystemContext ctx);
}
