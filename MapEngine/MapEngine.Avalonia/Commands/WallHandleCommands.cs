using MapEngine.Core.Commands;
using MapEngine.Avalonia.ViewModels;

namespace MapEngine.Avalonia.Commands;

/// <summary>
/// 移动墙壁端点。HandleIndex 0 = 起点 (X1,Y1)，1 = 终点 (X2,Y2)。
/// </summary>
public sealed class VmMoveWallHandleCommand : ICommand
{
    private readonly MainWindowViewModel _viewModel;
    private readonly string _itemId;
    private readonly int _handleIndex;
    private readonly (double X, double Y) _oldPos;
    private readonly (double X, double Y) _newPos;

    public VmMoveWallHandleCommand(
        MainWindowViewModel viewModel,
        string itemId,
        int handleIndex,
        (double X, double Y) oldPos,
        (double X, double Y) newPos)
    {
        _viewModel = viewModel;
        _itemId = itemId;
        _handleIndex = handleIndex;
        _oldPos = oldPos;
        _newPos = newPos;
    }

    public string Description => $"移动墙壁端点 #{_handleIndex + 1}";

    public void Execute(ISceneState state) => Apply(_newPos);
    public void Undo(ISceneState state)    => Apply(_oldPos);

    private void Apply((double X, double Y) pos)
    {
        var item = _viewModel.FindHierarchyById(_itemId);
        var wall  = item?.GetComponent<MapEngine.Core.Components.WallComponent>();
        if (wall is null) return;

        if (_handleIndex == 0) { wall.X1 = pos.X; wall.Y1 = pos.Y; }
        else                   { wall.X2 = pos.X; wall.Y2 = pos.Y; }

        _viewModel.RefreshMapRenderableItemsPublic();
    }
}

/// <summary>
/// 新模型为单段线（两端点），不支持插入/删除中间点。
/// 保留空实现以避免破坏调用方，执行时静默忽略。
/// </summary>
public sealed class VmInsertWallHandleCommand : ICommand
{
    public VmInsertWallHandleCommand(MainWindowViewModel _, string __, int ___, (double, double) ____) { }
    public string Description => "插入墙壁控制点（新模型不支持）";
    public void Execute(ISceneState state) { }
    public void Undo(ISceneState state)    { }
}

public sealed class VmDeleteWallHandleCommand : ICommand
{
    public VmDeleteWallHandleCommand(MainWindowViewModel _, string __, int ___) { }
    public string Description => "删除墙壁控制点（新模型不支持）";
    public void Execute(ISceneState state) { }
    public void Undo(ISceneState state)    { }
}
