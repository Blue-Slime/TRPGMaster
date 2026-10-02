using System;
using System.Collections.Generic;
using MapEngine.Core;
using MapEngine.Core.Commands;
using MapEngine.Core.Components;
using MapEngine.Avalonia.ViewModels;

namespace MapEngine.Avalonia.Commands;

/// <summary>
/// 在墙体线段上添加门窗（自动插入两个锚点定义门窗区间）。
/// 流程：
/// 1. 在点击位置线段上插入两个新锚点（间距 = 门宽度）
/// 2. 创建 DoorSegment 指向这两个锚点
/// 3. 更新后续门窗的锚点索引
/// </summary>
public sealed class VmAddDoorCommand : ILocalOnlyCommand
{
    private readonly MainWindowViewModel _vm;
    private readonly string _wallId;
    private readonly int _segmentIndex;         // 目标线段索引（在 Points[i] → Points[i+1] 之间）
    private readonly double _insertParam;       // 线段参数 t ∈ [0,1]，表示插入位置
    private readonly double _doorWidth;         // 门宽度（世界单位）
    private readonly DoorKind _doorKind;        // 门类型（Door/Window/Archway）

    private int _insertedStartIndex = -1;       // 插入的起始锚点索引（用于 Undo）
    private DoorSegment? _createdDoor;          // 创建的门窗对象（用于 Undo）

    public VmAddDoorCommand(
        MainWindowViewModel vm,
        string wallId,
        int segmentIndex,
        double insertParam,
        double doorWidth,
        DoorKind doorKind)
    {
        _vm = vm;
        _wallId = wallId;
        _segmentIndex = segmentIndex;
        _insertParam = Math.Clamp(insertParam, 0, 1);
        _doorWidth = doorWidth;
        _doorKind = doorKind;
    }

    public string Description => _doorKind switch
    {
        DoorKind.Window => "添加窗户",
        DoorKind.Archway => "添加拱门",
        DoorKind.Secret => "添加密门",
        _ => "添加门"
    };

    public void Execute(World world)
    {
        var item = _vm.FindHierarchyById(_wallId);
        var wall = item?.GetComponent<WallPathComponent>();
        if (wall is null || _segmentIndex < 0 || _segmentIndex >= wall.Points.Count - 1)
            return;

        var p1 = wall.Points[_segmentIndex];
        var p2 = wall.Points[_segmentIndex + 1];

        // 计算线段向量和长度
        var dx = p2.X - p1.X;
        var dy = p2.Y - p1.Y;
        var segmentLength = Math.Sqrt(dx * dx + dy * dy);

        if (segmentLength < _doorWidth + 2) // 线段太短，无法容纳门
        {
            _vm.StatusMessage = $"线段太短，无法添加 {_doorWidth:F0} 单位的门窗";
            return;
        }

        // 计算门的中心点（基于 _insertParam）
        var centerX = p1.X + dx * _insertParam;
        var centerY = p1.Y + dy * _insertParam;

        // 计算门的起止点（沿线段方向偏移 ±doorWidth/2）
        var halfWidth = _doorWidth / 2.0;
        var dirX = dx / segmentLength;
        var dirY = dy / segmentLength;

        var doorStartX = centerX - dirX * halfWidth;
        var doorStartY = centerY - dirY * halfWidth;
        var doorEndX = centerX + dirX * halfWidth;
        var doorEndY = centerY + dirY * halfWidth;

        // 插入两个新锚点：doorStart 和 doorEnd
        _insertedStartIndex = _segmentIndex + 1;
        wall.Points.Insert(_insertedStartIndex, (doorStartX, doorStartY));
        wall.Points.Insert(_insertedStartIndex + 1, (doorEndX, doorEndY));

        // 更新已有门窗的锚点索引（插入点之后的索引全部 +2）
        foreach (var door in wall.Doors)
        {
            if (door.StartAnchorIndex >= _insertedStartIndex)
                door.StartAnchorIndex += 2;
            if (door.EndAnchorIndex >= _insertedStartIndex)
                door.EndAnchorIndex += 2;
        }

        // 创建新的 DoorSegment
        _createdDoor = new DoorSegment
        {
            Id = Guid.NewGuid().ToString("N"),
            StartAnchorIndex = _insertedStartIndex,
            EndAnchorIndex = _insertedStartIndex + 1,
            Kind = _doorKind,
            State = _doorKind == DoorKind.Archway ? DoorState.Open : DoorState.Closed,
            Swing = DoorSwing.None
        };

        wall.Doors.Add(_createdDoor);
        _vm.RefreshMapRenderableItemsPublic();
        _vm.StatusMessage = $"{Description} 成功（拖拽锚点可调整位置）";
    }

    public void Undo(World world)
    {
        if (_insertedStartIndex < 0 || _createdDoor is null)
            return;

        var item = _vm.FindHierarchyById(_wallId);
        var wall = item?.GetComponent<WallPathComponent>();
        if (wall is null)
            return;

        // 移除创建的门窗
        wall.Doors.RemoveAll(d => d.Id == _createdDoor.Id);

        // 移除插入的两个锚点（倒序删除，避免索引错位）
        if (_insertedStartIndex + 1 < wall.Points.Count)
            wall.Points.RemoveAt(_insertedStartIndex + 1);
        if (_insertedStartIndex < wall.Points.Count)
            wall.Points.RemoveAt(_insertedStartIndex);

        // 恢复其他门窗的锚点索引（插入点之后的索引全部 -2）
        foreach (var door in wall.Doors)
        {
            if (door.StartAnchorIndex > _insertedStartIndex)
                door.StartAnchorIndex -= 2;
            if (door.EndAnchorIndex > _insertedStartIndex)
                door.EndAnchorIndex -= 2;
        }

        _vm.RefreshMapRenderableItemsPublic();
    }
}

/// <summary>
/// 删除墙体上的门窗（同时移除对应的锚点区间）。
/// </summary>
public sealed class VmDeleteDoorCommand : ILocalOnlyCommand
{
    private readonly MainWindowViewModel _vm;
    private readonly string _wallId;
    private readonly string _doorId;

    private DoorSegment? _deletedDoor;
    private List<(double X, double Y)>? _deletedAnchors;
    private int _startIndex;

    public VmDeleteDoorCommand(MainWindowViewModel vm, string wallId, string doorId)
    {
        _vm = vm;
        _wallId = wallId;
        _doorId = doorId;
    }

    public string Description => "删除门窗";

    public void Execute(World world)
    {
        var item = _vm.FindHierarchyById(_wallId);
        var wall = item?.GetComponent<WallPathComponent>();
        if (wall is null)
            return;

        var door = wall.Doors.Find(d => d.Id == _doorId);
        if (door is null)
            return;

        _deletedDoor = door.Clone();
        _startIndex = door.StartAnchorIndex;

        // 保存要删除的锚点（门窗区间内的所有锚点）
        _deletedAnchors = [];
        for (int i = door.StartAnchorIndex; i <= door.EndAnchorIndex && i < wall.Points.Count; i++)
        {
            _deletedAnchors.Add(wall.Points[i]);
        }

        // 移除门窗记录
        wall.Doors.Remove(door);

        // 移除锚点（倒序删除）
        int anchorCount = door.EndAnchorIndex - door.StartAnchorIndex + 1;
        for (int i = door.EndAnchorIndex; i >= door.StartAnchorIndex; i--)
        {
            if (i < wall.Points.Count)
                wall.Points.RemoveAt(i);
        }

        // 更新其他门窗的锚点索引
        foreach (var otherDoor in wall.Doors)
        {
            if (otherDoor.StartAnchorIndex > door.EndAnchorIndex)
                otherDoor.StartAnchorIndex -= anchorCount;
            if (otherDoor.EndAnchorIndex > door.EndAnchorIndex)
                otherDoor.EndAnchorIndex -= anchorCount;
        }

        _vm.RefreshMapRenderableItemsPublic();
    }

    public void Undo(World world)
    {
        if (_deletedDoor is null || _deletedAnchors is null)
            return;

        var item = _vm.FindHierarchyById(_wallId);
        var wall = item?.GetComponent<WallPathComponent>();
        if (wall is null)
            return;

        // 恢复锚点
        for (int i = 0; i < _deletedAnchors.Count; i++)
        {
            wall.Points.Insert(_startIndex + i, _deletedAnchors[i]);
        }

        // 更新其他门窗的锚点索引
        int anchorCount = _deletedAnchors.Count;
        foreach (var door in wall.Doors)
        {
            if (door.StartAnchorIndex >= _startIndex)
                door.StartAnchorIndex += anchorCount;
            if (door.EndAnchorIndex >= _startIndex)
                door.EndAnchorIndex += anchorCount;
        }

        // 恢复门窗记录
        wall.Doors.Add(_deletedDoor.Clone());

        _vm.RefreshMapRenderableItemsPublic();
    }
}

/// <summary>
/// 切换门窗状态（Closed → Open → Locked → Closed）。
/// </summary>
public sealed class VmToggleDoorStateCommand : ILocalOnlyCommand
{
    private readonly MainWindowViewModel _vm;
    private readonly string _wallId;
    private readonly string _doorId;

    private DoorState _oldState;
    private DoorState _newState;

    public VmToggleDoorStateCommand(MainWindowViewModel vm, string wallId, string doorId)
    {
        _vm = vm;
        _wallId = wallId;
        _doorId = doorId;
    }

    public string Description => "切换门窗状态";

    public void Execute(World world)
    {
        var item = _vm.FindHierarchyById(_wallId);
        var wall = item?.GetComponent<WallPathComponent>();
        if (wall is null)
            return;

        var door = wall.Doors.Find(d => d.Id == _doorId);
        if (door is null)
            return;

        _oldState = door.State;
        _newState = door.State switch
        {
            DoorState.Closed => DoorState.Open,
            DoorState.Open => DoorState.Locked,
            DoorState.Locked => DoorState.Closed,
            _ => DoorState.Closed
        };

        door.State = _newState;
        _vm.RefreshMapRenderableItemsPublic();

        var stateText = _newState switch
        {
            DoorState.Open => "开启",
            DoorState.Locked => "锁定",
            _ => "关闭"
        };
        _vm.StatusMessage = $"门窗状态：{stateText}";
    }

    public void Undo(World world)
    {
        var item = _vm.FindHierarchyById(_wallId);
        var wall = item?.GetComponent<WallPathComponent>();
        if (wall is null)
            return;

        var door = wall.Doors.Find(d => d.Id == _doorId);
        if (door is not null)
        {
            door.State = _oldState;
            _vm.RefreshMapRenderableItemsPublic();
        }
    }
}
