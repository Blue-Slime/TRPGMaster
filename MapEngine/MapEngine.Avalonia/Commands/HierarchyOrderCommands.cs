using MapEngine.Core.Commands;
using MapEngine.Avalonia.ViewModels;

namespace MapEngine.Avalonia.Commands;

public sealed class VmMoveItemUpCommand : ICommand
{
    private readonly MainWindowViewModel _vm;
    private readonly string _itemId;

    public VmMoveItemUpCommand(MainWindowViewModel vm, string itemId)
    {
        _vm = vm;
        _itemId = itemId;
    }

    public string Description => "上移对象";

    public void Execute(ISceneState state)
    {
        var item = _vm.FindHierarchyById(_itemId);
        if (item?.Parent is null) return;

        var siblings = item.Parent.Children;
        var index = siblings.IndexOf(item);
        if (index <= 0) return;

        siblings.Move(index, index - 1);
        _vm.RefreshMapRenderableItemsPublic();
    }

    public void Undo(ISceneState state)
    {
        var item = _vm.FindHierarchyById(_itemId);
        if (item?.Parent is null) return;

        var siblings = item.Parent.Children;
        var index = siblings.IndexOf(item);
        if (index >= siblings.Count - 1) return;

        siblings.Move(index, index + 1);
        _vm.RefreshMapRenderableItemsPublic();
    }
}

public sealed class VmMoveItemDownCommand : ICommand
{
    private readonly MainWindowViewModel _vm;
    private readonly string _itemId;

    public VmMoveItemDownCommand(MainWindowViewModel vm, string itemId)
    {
        _vm = vm;
        _itemId = itemId;
    }

    public string Description => "下移对象";

    public void Execute(ISceneState state)
    {
        var item = _vm.FindHierarchyById(_itemId);
        if (item?.Parent is null) return;

        var siblings = item.Parent.Children;
        var index = siblings.IndexOf(item);
        if (index < 0 || index >= siblings.Count - 1) return;

        siblings.Move(index, index + 1);
        _vm.RefreshMapRenderableItemsPublic();
    }

    public void Undo(ISceneState state)
    {
        var item = _vm.FindHierarchyById(_itemId);
        if (item?.Parent is null) return;

        var siblings = item.Parent.Children;
        var index = siblings.IndexOf(item);
        if (index <= 0) return;

        siblings.Move(index, index - 1);
        _vm.RefreshMapRenderableItemsPublic();
    }
}

public sealed class VmPromoteItemCommand : ICommand
{
    private readonly MainWindowViewModel _vm;
    private readonly string _itemId;
    private string? _oldParentId;
    private int _oldIndex;

    public VmPromoteItemCommand(MainWindowViewModel vm, string itemId)
    {
        _vm = vm;
        _itemId = itemId;
    }

    public string Description => "提升层级";

    public void Execute(ISceneState state)
    {
        var item = _vm.FindHierarchyById(_itemId);
        if (item?.Parent?.Parent is null) return;

        var oldParent = item.Parent;
        var newParent = oldParent.Parent;
        _oldParentId = oldParent.Id;
        _oldIndex = oldParent.Children.IndexOf(item);

        oldParent.Children.Remove(item);
        var insertIndex = newParent.Children.IndexOf(oldParent) + 1;
        newParent.Children.Insert(insertIndex, item);
        item.Parent = newParent;
        _vm.RefreshMapRenderableItemsPublic();
    }

    public void Undo(ISceneState state)
    {
        if (_oldParentId is null) return;
        var item = _vm.FindHierarchyById(_itemId);
        var oldParent = _vm.FindHierarchyById(_oldParentId);
        if (item?.Parent is null || oldParent is null) return;

        item.Parent.Children.Remove(item);
        oldParent.Children.Insert(_oldIndex, item);
        item.Parent = oldParent;
        _vm.RefreshMapRenderableItemsPublic();
    }
}

public sealed class VmDemoteItemCommand : ICommand
{
    private readonly MainWindowViewModel _vm;
    private readonly string _itemId;
    private string? _oldParentId;
    private int _oldIndex;

    public VmDemoteItemCommand(MainWindowViewModel vm, string itemId)
    {
        _vm = vm;
        _itemId = itemId;
    }

    public string Description => "降低层级";

    public void Execute(ISceneState state)
    {
        var item = _vm.FindHierarchyById(_itemId);
        if (item?.Parent is null) return;

        var siblings = item.Parent.Children;
        var index = siblings.IndexOf(item);
        if (index <= 0) return;

        var newParent = siblings[index - 1];
        _oldParentId = item.Parent.Id;
        _oldIndex = index;

        siblings.Remove(item);
        newParent.Children.Add(item);
        item.Parent = newParent;
        _vm.RefreshMapRenderableItemsPublic();
    }

    public void Undo(ISceneState state)
    {
        if (_oldParentId is null) return;
        var item = _vm.FindHierarchyById(_itemId);
        var oldParent = _vm.FindHierarchyById(_oldParentId);
        if (item?.Parent is null || oldParent is null) return;

        item.Parent.Children.Remove(item);
        oldParent.Children.Insert(_oldIndex, item);
        item.Parent = oldParent;
        _vm.RefreshMapRenderableItemsPublic();
    }
}
