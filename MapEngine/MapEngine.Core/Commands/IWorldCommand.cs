namespace MapEngine.Core.Commands;

/// <summary>
/// 新一代命令接口 —— 操作 World 而非 ViewModel,可序列化以支持联机。
///
/// 区别于旧 ICommand(接收 ISceneState,实际绑定 MainWindowViewModel):
/// - Execute/Undo 只接收 World,不依赖 UI 层
/// - 实现类必须可 JSON 序列化(所有字段都是值类型/string/Guid/数组)
/// - 对端收到序列化载荷后可在干净 World 上重放,得到相同结果
///
/// 迁移路径(S3):
/// 1. 定义此接口
/// 2. 实现核心命令(AddObject/DeleteObject/MoveObject/SetProperty/Rename)
/// 3. CommandBus 改为泛型 Execute&lt;T&gt; where T : IWorldCommand
/// 4. MainWindowViewModel 持有 World,订阅 World.Changed 同步到 HierarchyItemViewModel
/// 5. 废弃所有 Vm*Command
/// </summary>
public interface IWorldCommand
{
    string Description { get; }

    /// <summary>执行命令,修改 World 状态。</summary>
    void Execute(World world);

    /// <summary>撤销命令,恢复 World 到执行前状态。</summary>
    void Undo(World world);
}
