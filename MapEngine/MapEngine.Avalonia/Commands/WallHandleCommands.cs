using MapEngine.Core;
using MapEngine.Core.Commands;
using MapEngine.Core.Components;
using MapEngine.Avalonia.ViewModels;

namespace MapEngine.Avalonia.Commands;

/// <summary>
/// 移动墙壁端点。HandleIndex 0 = 起点(X1,Y1)，1 = 终点(X2,Y2)。
/// </summary>
public sealed class VmMoveWallHandleCommand : ILocalOnlyCommand
{
    private readonly MainWindowViewModel _vm;
    private readonly string _itemId;
    private readonly int _handleIndex;
    private readonly (double X, double Y) _oldPos;
    private readonly (double X, double Y) _newPos;

    public VmMoveWallHandleCommand(
        MainWindowViewModel vm, string itemId, int handleIndex,
        (double X, double Y) oldPos, (double X, double Y) newPos)
    {
        _vm = vm;
        _itemId = itemId;
        _handleIndex = handleIndex;
        _oldPos = oldPos;
        _newPos = newPos;
    }

    public string Description => $"移动墙壁端点 #{_handleIndex + 1}";

    public void Execute(World world) => Apply(_newPos);
    public void Undo(World world)    => Apply(_oldPos);

    private void Apply((double X, double Y) pos)
    {
        var item = _vm.FindHierarchyById(_itemId);
        var wall = item?.GetComponent<WallComponent>();
        if (wall is null) return;

        if (_handleIndex == 0) { wall.X1 = pos.X; wall.Y1 = pos.Y; }
        else                   { wall.X2 = pos.X; wall.Y2 = pos.Y; }

        _vm.RefreshMapRenderableItemsPublic();
    }
}

/// <summary>
/// 新模型为单段线（两端点），不支持插入/删除中间点。
/// 保留空实现以避免破坏调用方，执行时静默忽略。
/// </summary>
public sealed class VmInsertWallHandleCommand : ILocalOnlyCommand
{
    public VmInsertWallHandleCommand(MainWindowViewModel _, string __, int ___, (double, double) ____) { }
    public string Description => "插入墙壁控制点（新模型不支持）";
    public void Execute(World world) { }
    public void Undo(World world)    { }
}

public sealed class VmDeleteWallHandleCommand : ILocalOnlyCommand
{
    public VmDeleteWallHandleCommand(MainWindowViewModel _, string __, int ___) { }
    public string Description => "删除墙壁控制点（新模型不支持）";
    public void Execute(World world) { }
    public void Undo(World world)    { }
}
