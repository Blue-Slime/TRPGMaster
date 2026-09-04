using System;
using System.Collections.Generic;
using MapEngine.Core;
using MapEngine.Core.Commands;
using MapEngine.Core.Components;
using MapEngine.Avalonia.Services;
using MapEngine.Avalonia.ViewModels;

namespace MapEngine.Avalonia.Commands;

/// <summary>
/// 在层级树中创建新的空对象或 Asset 实例。
/// World 参数在此不使用：HierarchyItemViewModel 是运行时权威。
/// </summary>
public sealed class VmAddEmptyObjectCommand : ILocalOnlyCommand
{
    private readonly MainWindowViewModel _vm;
    private readonly string _parentId;
    private readonly HierarchyNodeDto _dto;
    private string? _createdId;

    public VmAddEmptyObjectCommand(MainWindowViewModel vm, string parentId, HierarchyNodeDto dto)
    {
        _vm = vm;
        _parentId = parentId;
        _dto = dto;
    }

    public string Description => $"添加对象 {_dto.Name}";

    public void Execute(World world)
    {
        var parent = _vm.FindHierarchyById(_parentId);
        if (parent is null) return;

        var child = new HierarchyItemViewModel(_dto) { Parent = parent };
        parent.Children.Add(child);
        _vm.RegisterHierarchyItem(child);
        _vm.RefreshMapRenderableItemsPublic();
        _vm.SelectedHierarchyItem = child;
        _createdId = child.Id;
    }

    public void Undo(World world)
    {
        if (_createdId is null) return;
        var item = _vm.FindHierarchyById(_createdId);
        if (item?.Parent is null) return;

        item.Parent.Children.Remove(item);
        _vm.UnregisterHierarchyItem(item);
        _vm.RefreshMapRenderableItemsPublic();
        _vm.SelectedHierarchyItem = _vm.FindHierarchyById(_parentId);
    }
}

/// <summary>
/// 给节点挂一条拓扑连线（可 Undo）。
///
/// 走 VM 层而非 WorldAddGraphLinkCommand：编辑器新建的对象只进 HierarchyItemViewModel
/// 树、不入 World，走 World 的命令会 FindById 失败而静默无效。
/// </summary>
public sealed class VmAddGraphLinkCommand : ILocalOnlyCommand
{
    private readonly MainWindowViewModel _vm;
    private readonly string _ownerItemId;
    private readonly GraphLinkComponent _link;

    public VmAddGraphLinkCommand(MainWindowViewModel vm, string ownerItemId, GraphLinkComponent link)
    {
        _vm = vm;
        _ownerItemId = ownerItemId;
        _link = link;
    }

    public string Description => $"添加连线 → {_link.TargetNodeId}";

    public void Execute(World world)
    {
        var owner = _vm.FindHierarchyById(_ownerItemId);
        if (owner is null) return;

        owner.BackingObject.AddComponent(_link);
        owner.RebuildComponentEditors();
        _vm.RefreshMapRenderableItemsPublic();
    }

    public void Undo(World world)
    {
        var owner = _vm.FindHierarchyById(_ownerItemId);
        if (owner is null) return;

        // 按实例移除：同一对象可能挂多条 GraphLink，不能按类型删
        owner.BackingObject.RemoveComponent(_link);
        owner.RebuildComponentEditors();
        _vm.RefreshMapRenderableItemsPublic();
    }
}

/// <summary>从层级树删除对象（可 Undo）</summary>
public sealed class VmDeleteHierarchyItemCommand : ILocalOnlyCommand
{
    private readonly MainWindowViewModel _vm;
    private readonly string _itemId;
    private HierarchyNodeDto? _snapshot;
    private string? _parentId;
    private int _insertIndex;

    public VmDeleteHierarchyItemCommand(MainWindowViewModel vm, string itemId)
    {
        _vm = vm;
        _itemId = itemId;
    }

    public string Description => $"删除对象 {_snapshot?.Name ?? _itemId}";

    public void Execute(World world)
    {
        var item = _vm.FindHierarchyById(_itemId);
        if (item?.Parent is null) return;

        _snapshot = _vm.SnapshotHierarchyPublic(item);
        _parentId = item.Parent.Id;
        _insertIndex = item.Parent.Children.IndexOf(item);

        item.Parent.Children.Remove(item);
        _vm.UnregisterHierarchyItemRecursive(item);
        _vm.RefreshMapRenderableItemsPublic();
        _vm.SelectedHierarchyItem = _vm.FindHierarchyById(_parentId);
    }

    public void Undo(World world)
    {
        if (_snapshot is null || _parentId is null) return;
        var parent = _vm.FindHierarchyById(_parentId);
        if (parent is null) return;

        var restored = _vm.BuildHierarchyItemPublic(_snapshot);
        restored.Parent = parent;
        var idx = Math.Min(_insertIndex, parent.Children.Count);
        parent.Children.Insert(idx, restored);
        _vm.RefreshMapRenderableItemsPublic();
        _vm.SelectedHierarchyItem = restored;
    }
}

/// <summary>重命名层级节点</summary>
public sealed class VmRenameCommand : ILocalOnlyCommand
{
    private readonly MainWindowViewModel _vm;
    private readonly string _itemId;
    private readonly string _newName;
    private string? _oldName;

    public VmRenameCommand(MainWindowViewModel vm, string itemId, string newName)
    {
        _vm = vm;
        _itemId = itemId;
        _newName = newName;
    }

    public string Description => $"重命名为 {_newName}";

    public void Execute(World world)
    {
        var item = _vm.FindHierarchyById(_itemId);
        if (item is null) return;
        _oldName = item.Name;
        item.Name = _newName;
    }

    public void Undo(World world)
    {
        if (_oldName is null) return;
        var item = _vm.FindHierarchyById(_itemId);
        if (item is null) return;
        item.Name = _oldName;
    }
}

/// <summary>通用属性赋值（支持 Transform + 外观 + 视野属性）</summary>
public sealed class VmSetPropertyCommand : ILocalOnlyCommand
{
    private readonly MainWindowViewModel _vm;
    private readonly string _itemId;
    private readonly string _property;
    private readonly object? _newValue;
    private object? _oldValue;

    public VmSetPropertyCommand(MainWindowViewModel vm, string itemId, string property, object? newValue)
    {
        _vm = vm;
        _itemId = itemId;
        _property = property;
        _newValue = newValue;
    }

    public string Description => $"设置 {_property}";

    public void Execute(World world)
    {
        var item = _vm.FindHierarchyById(_itemId);
        if (item is null) return;
        _oldValue = GetProp(item);
        SetProp(item, _newValue);
    }

    public void Undo(World world)
    {
        var item = _vm.FindHierarchyById(_itemId);
        if (item is null) return;
        SetProp(item, _oldValue);
    }

    private object? GetProp(HierarchyItemViewModel item) => _property switch
    {
        "X"             => item.X,
        "Y"             => item.Y,
        "Z"             => item.Z,
        "Rotation"      => item.Rotation,
        "ScaleX"        => item.ScaleX,
        "ScaleY"        => item.ScaleY,
        "Opacity"       => item.Opacity,
        "IsActive"      => item.IsActive,
        "IsLocked"      => item.IsLocked,
        "SortOrder"     => item.SortOrder,
        "ObjectType"    => item.ObjectType,
        "SpriteColor"   => item.SpriteColor,
        "VisionEnabled" => item.VisionEnabled,
        "VisionRadius"  => item.VisionRadius,
        "Orientation"   => item.Orientation,
        _               => null
    };

    private void SetProp(HierarchyItemViewModel item, object? value)
    {
        switch (_property)
        {
            case "X":             item.X             = Convert.ToDouble(value);  break;
            case "Y":             item.Y             = Convert.ToDouble(value);  break;
            case "Z":             item.Z             = Convert.ToDouble(value);  break;
            case "Rotation":      item.Rotation      = Convert.ToDouble(value);  break;
            case "ScaleX":        item.ScaleX        = Convert.ToDouble(value);  break;
            case "ScaleY":        item.ScaleY        = Convert.ToDouble(value);  break;
            case "Opacity":       item.Opacity       = Convert.ToDouble(value);  break;
            case "IsActive":      item.IsActive      = Convert.ToBoolean(value); break;
            case "IsLocked":      item.IsLocked      = Convert.ToBoolean(value); break;
            case "SortOrder":     item.SortOrder     = Convert.ToInt32(value);   break;
            case "ObjectType":    item.ObjectType    = (string)(value ?? "Empty"); break;
            case "SpriteColor":   item.SpriteColor   = (string)(value ?? "#FF4444"); break;
            case "VisionEnabled": item.VisionEnabled = Convert.ToBoolean(value); break;
            case "VisionRadius":  item.VisionRadius  = Convert.ToDouble(value);  break;
            case "Orientation":   item.Orientation   = Convert.ToDouble(value);  break;
        }
    }
}

/// <summary>移动对象（及子树）到新位置</summary>
public sealed class VmMoveObjectCommand : ILocalOnlyCommand
{
    private readonly MainWindowViewModel _vm;
    private readonly string _itemId;
    private readonly double _newX, _newY;
    private readonly double _oldX, _oldY;
    private readonly bool _oldHasMapPosition;
    private readonly List<(string Id, double OldX, double OldY, double NewX, double NewY)>? _childMoves;

    public VmMoveObjectCommand(
        MainWindowViewModel vm, string itemId,
        double oldX, double oldY,
        double newX, double newY)
    {
        _vm = vm;
        _itemId = itemId;
        _oldX = oldX;
        _oldY = oldY;
        _newX = newX;
        _newY = newY;
        _oldHasMapPosition = vm.FindHierarchyById(itemId)?.HasMapPosition ?? false;

        var item = vm.FindHierarchyById(itemId);
        if (item is not null)
        {
            var deltaX = newX - oldX;
            var deltaY = newY - oldY;
            _childMoves = [];
            CollectChildMoves(item, deltaX, deltaY, _childMoves);
        }
    }

    public string Description => $"移动到 ({_newX:F0}, {_newY:F0})";

    public void Execute(World world)
    {
        var item = _vm.FindHierarchyById(_itemId);
        if (item is null) return;

        item.X = _newX;
        item.Y = _newY;
        item.HasMapPosition = true;

        if (_childMoves is not null)
        {
            foreach (var (childId, _, _, nx, ny) in _childMoves)
            {
                var child = _vm.FindHierarchyById(childId);
                if (child is not null) { child.X = nx; child.Y = ny; }
            }
        }
    }

    public void Undo(World world)
    {
        var item = _vm.FindHierarchyById(_itemId);
        if (item is null) return;

        item.X = _oldX;
        item.Y = _oldY;
        item.HasMapPosition = _oldHasMapPosition;

        if (_childMoves is not null)
        {
            foreach (var (childId, ox, oy, _, _) in _childMoves)
            {
                var child = _vm.FindHierarchyById(childId);
                if (child is not null) { child.X = ox; child.Y = oy; }
            }
        }
    }

    private static void CollectChildMoves(
        HierarchyItemViewModel parent, double dx, double dy,
        List<(string, double, double, double, double)> list)
    {
        foreach (var child in parent.Children)
        {
            if (child.HasMapPosition)
                list.Add((child.Id, child.X, child.Y, child.X + dx, child.Y + dy));
            CollectChildMoves(child, dx, dy, list);
        }
    }
}

/// <summary>从 Asset 实例化对象到层级树</summary>
public sealed class VmCreateInstanceCommand : ILocalOnlyCommand
{
    private readonly MainWindowViewModel _vm;
    private readonly string _parentId;
    private readonly HierarchyNodeDto _dto;
    private string? _createdId;

    public VmCreateInstanceCommand(MainWindowViewModel vm, string parentId, HierarchyNodeDto dto)
    {
        _vm = vm;
        _parentId = parentId;
        _dto = dto;
    }

    public string Description => $"实例化 {_dto.SourceAssetName ?? _dto.Name}";

    public void Execute(World world)
    {
        var parent = _vm.FindHierarchyById(_parentId);
        if (parent is null) return;

        var instance = new HierarchyItemViewModel(_dto) { Parent = parent };
        parent.Children.Add(instance);
        _vm.RegisterHierarchyItem(instance);
        _vm.RefreshMapRenderableItemsPublic();
        _vm.SelectedHierarchyItem = instance;
        _createdId = instance.Id;
    }

    public void Undo(World world)
    {
        if (_createdId is null) return;
        var item = _vm.FindHierarchyById(_createdId);
        if (item?.Parent is null) return;

        item.Parent.Children.Remove(item);
        _vm.UnregisterHierarchyItem(item);
        _vm.RefreshMapRenderableItemsPublic();
        _vm.SelectedHierarchyItem = _vm.FindHierarchyById(_parentId);
    }
}
