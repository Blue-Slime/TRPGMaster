using System;
using System.Collections.Generic;
using MapEngine.Core;
using MapEngine.Core.Commands;
using MapEngine.Core.Components;
using MapEngine.Avalonia.ViewModels;

namespace MapEngine.Avalonia.Commands;

/// <summary>
/// 更新墙体路径的单个锚点位置（可撤销）。
/// </summary>
public sealed class VmUpdateWallAnchorCommand : ILocalOnlyCommand
{
    private readonly MainWindowViewModel _vm;
    private readonly string _objectId;
    private readonly int _anchorIndex;
    private readonly (double X, double Y) _newPosition;
    private (double X, double Y) _oldPosition;

    public VmUpdateWallAnchorCommand(
        MainWindowViewModel vm,
        string objectId,
        int anchorIndex,
        (double X, double Y) newPosition)
    {
        _vm = vm;
        _objectId = objectId;
        _anchorIndex = anchorIndex;
        _newPosition = newPosition;
    }

    public string Description => "移动墙体锚点";

    public void Execute(World world)
    {
        var item = _vm.FindHierarchyById(_objectId);
        var wall = item?.GetComponent<WallPathComponent>();
        if (wall is null || _anchorIndex < 0 || _anchorIndex >= wall.Points.Count)
            return;

        _oldPosition = wall.Points[_anchorIndex];
        wall.Points[_anchorIndex] = _newPosition;
        _vm.RefreshMapRenderableItemsPublic();
    }

    public void Undo(World world)
    {
        var item = _vm.FindHierarchyById(_objectId);
        var wall = item?.GetComponent<WallPathComponent>();
        if (wall is null || _anchorIndex < 0 || _anchorIndex >= wall.Points.Count)
            return;

        wall.Points[_anchorIndex] = _oldPosition;
        _vm.RefreshMapRenderableItemsPublic();
    }
}

/// <summary>
/// 在墙体路径指定位置插入新锚点（Shift+单击线段）。
/// </summary>
public sealed class VmInsertWallAnchorCommand : ILocalOnlyCommand
{
    private readonly MainWindowViewModel _vm;
    private readonly string _objectId;
    private readonly int _insertIndex;
    private readonly (double X, double Y) _position;

    public VmInsertWallAnchorCommand(
        MainWindowViewModel vm,
        string objectId,
        int insertIndex,
        (double X, double Y) position)
    {
        _vm = vm;
        _objectId = objectId;
        _insertIndex = insertIndex;
        _position = position;
    }

    public string Description => "插入墙体锚点";

    public void Execute(World world)
    {
        var item = _vm.FindHierarchyById(_objectId);
        var wall = item?.GetComponent<WallPathComponent>();
        if (wall is null || _insertIndex < 0 || _insertIndex > wall.Points.Count)
            return;

        wall.Points.Insert(_insertIndex, _position);

        // 更新所有门窗的锚点索引（插入点之后的索引全部 +1）
        foreach (var door in wall.Doors)
        {
            if (door.StartAnchorIndex >= _insertIndex)
                door.StartAnchorIndex++;
            if (door.EndAnchorIndex >= _insertIndex)
                door.EndAnchorIndex++;
        }

        _vm.RefreshMapRenderableItemsPublic();
    }

    public void Undo(World world)
    {
        var item = _vm.FindHierarchyById(_objectId);
        var wall = item?.GetComponent<WallPathComponent>();
        if (wall is null || _insertIndex < 0 || _insertIndex >= wall.Points.Count)
            return;

        wall.Points.RemoveAt(_insertIndex);

        // 恢复门窗锚点索引
        foreach (var door in wall.Doors)
        {
            if (door.StartAnchorIndex > _insertIndex)
                door.StartAnchorIndex--;
            if (door.EndAnchorIndex > _insertIndex)
                door.EndAnchorIndex--;
        }

        _vm.RefreshMapRenderableItemsPublic();
    }
}

/// <summary>
/// 删除墙体路径的锚点（至少保留 2 个）。
/// </summary>
public sealed class VmDeleteWallAnchorCommand : ILocalOnlyCommand
{
    private readonly MainWindowViewModel _vm;
    private readonly string _objectId;
    private readonly int _anchorIndex;
    private (double X, double Y) _deletedPosition;
    private List<DoorSegment>? _affectedDoors;

    public VmDeleteWallAnchorCommand(
        MainWindowViewModel vm,
        string objectId,
        int anchorIndex)
    {
        _vm = vm;
        _objectId = objectId;
        _anchorIndex = anchorIndex;
    }

    public string Description => "删除墙体锚点";

    public void Execute(World world)
    {
        var item = _vm.FindHierarchyById(_objectId);
        var wall = item?.GetComponent<WallPathComponent>();
        if (wall is null || wall.Points.Count <= 2 || _anchorIndex < 0 || _anchorIndex >= wall.Points.Count)
            return;

        _deletedPosition = wall.Points[_anchorIndex];
        wall.Points.RemoveAt(_anchorIndex);

        // 移除涉及此锚点的门窗，保存备份用于 Undo
        _affectedDoors = [];
        for (int i = wall.Doors.Count - 1; i >= 0; i--)
        {
            var door = wall.Doors[i];
            if (door.StartAnchorIndex == _anchorIndex || door.EndAnchorIndex == _anchorIndex)
            {
                _affectedDoors.Add(door.Clone());
                wall.Doors.RemoveAt(i);
            }
            else
            {
                // 更新后续锚点索引
                if (door.StartAnchorIndex > _anchorIndex)
                    door.StartAnchorIndex--;
                if (door.EndAnchorIndex > _anchorIndex)
                    door.EndAnchorIndex--;
            }
        }

        _vm.RefreshMapRenderableItemsPublic();
    }

    public void Undo(World world)
    {
        var item = _vm.FindHierarchyById(_objectId);
        var wall = item?.GetComponent<WallPathComponent>();
        if (wall is null)
            return;

        wall.Points.Insert(_anchorIndex, _deletedPosition);

        // 恢复门窗
        if (_affectedDoors is not null)
        {
            foreach (var door in _affectedDoors)
            {
                wall.Doors.Add(door.Clone());
            }
        }

        // 恢复其他门窗的索引
        foreach (var door in wall.Doors)
        {
            if (_affectedDoors?.Exists(d => d.Id == door.Id) == true)
                continue; // 跳过刚恢复的门窗

            if (door.StartAnchorIndex > _anchorIndex)
                door.StartAnchorIndex++;
            if (door.EndAnchorIndex > _anchorIndex)
                door.EndAnchorIndex++;
        }

        _vm.RefreshMapRenderableItemsPublic();
    }
}
