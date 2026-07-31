using System;
using System.Collections.Generic;
using MapEngine.Core;
using MapEngine.Core.Commands;
using MapEngine.Core.Components;
using MapEngine.Avalonia.Services;
using MapEngine.Avalonia.ViewModels;

namespace MapEngine.Avalonia.Commands;

/// <summary>
/// 在场景中创建形状对象（矩形/椭圆/线段）。
/// Shape 以 ShapeComponent 存储几何信息，TransformComponent 存储世界坐标中心。
/// </summary>
public sealed class VmCreateShapeCommand : ILocalOnlyCommand
{
    private readonly MainWindowViewModel _vm;
    private readonly string _shapeType;   // rect | ellipse | circle | line | cone | wedge
    private readonly double _cx, _cy;     // 世界坐标中心（cone/wedge 为顶点）
    private readonly double _ww, _wh;     // 世界尺寸（矩形/椭圆用；cone 用 _ww 存半径）
    private readonly double _lx2, _ly2;   // line 终点（相对中心）
    private readonly double _dirDeg;      // cone/wedge 朝向（度）
    private readonly double _halfAngle;   // cone/wedge 半张角（度）
    private string? _createdId;

    public VmCreateShapeCommand(
        MainWindowViewModel vm, string shapeType,
        double cx, double cy, double ww, double wh,
        double wx1, double wy1, double wx2, double wy2,
        double dirDeg = 0, double halfAngle = 30)
    {
        _vm = vm;
        _shapeType = shapeType;
        _cx = cx; _cy = cy;
        _ww = ww; _wh = wh;
        // 线段：终点相对于中心（中心 = 起点，终点为 wx2-wx1, wy2-wy1）
        _lx2 = wx2 - wx1;
        _ly2 = wy2 - wy1;
        _dirDeg = dirDeg;
        _halfAngle = halfAngle;
    }

    public string Description => $"创建{_shapeType switch
    {
        "ellipse" => "椭圆", "circle" => "圆形", "line" => "线段",
        "cone" => "锥形", "wedge" => "扇形", _ => "矩形",
    }}";

    public void Execute(World world)
    {
        var icon = _shapeType switch
        {
            "ellipse" => "⭕", "circle" => "⬤", "line" => "📏",
            "cone" => "🔺", "wedge" => "🍕", _ => "▭",
        };
        var dto  = new HierarchyNodeDto
        {
            Id         = Guid.NewGuid().ToString(),
            Name       = Description,
            Icon       = icon,
            ObjectType = "Shape",
            HasMapPosition = true,
            X = _cx,
            Y = _cy,
        };

        // 找根层添加（Shape 不归属某个具体父节点，直接加到根）
        var root = _vm.GetOrCreateShapeRoot();
        var item = new HierarchyItemViewModel(dto) { Parent = root };

        // 附加 ShapeComponent
        var isCone = _shapeType is "cone" or "wedge";
        var shape = new ShapeComponent
        {
            ShapeType   = _shapeType,
            Width       = _ww,
            Height      = _wh,
            X2          = _lx2,
            Y2          = _ly2,
            ConeRadius  = isCone ? _ww : 120,
            ConeAngle   = isCone ? _halfAngle : 30,
            Rotation    = isCone ? _dirDeg : 0,
            StrokeColor = "#845EF7",
            FillColor   = "#40845EF7",
            StrokeWidth = 2,
            IsFilled    = _shapeType != "line" && _vm.ShapeDefaultFilled,
            StrokeStyle = _vm.ShapeDefaultStrokeStyle,
        };
        item.AddComponent(shape);

        root.Children.Add(item);
        _vm.RegisterHierarchyItem(item);
        _vm.RefreshMapRenderableItemsPublic();
        _vm.SelectedHierarchyItem = item;
        _createdId = item.Id;
    }

    public void Undo(World world)
    {
        if (_createdId is null) return;
        var item = _vm.FindHierarchyById(_createdId);
        if (item?.Parent is null) return;
        item.Parent.Children.Remove(item);
        _vm.UnregisterHierarchyItem(item);
        _vm.RefreshMapRenderableItemsPublic();
        _createdId = null;
    }
}

/// <summary>
/// 在场景中创建锚点多边形（闭合矢量图形）。
/// 顶点存储在 ShapeComponent.Points（相对于对象中心），ShapeType="polygon"。
/// </summary>
public sealed class VmCreatePolygonCommand : ILocalOnlyCommand
{
    private readonly MainWindowViewModel _vm;
    private readonly double _cx, _cy;
    private readonly List<(double X, double Y)> _relPts;
    private string? _createdId;

    public VmCreatePolygonCommand(
        MainWindowViewModel vm,
        double cx, double cy,
        List<(double X, double Y)> relPts)
    {
        _vm = vm;
        _cx = cx; _cy = cy;
        _relPts = relPts;
    }

    public string Description => "创建多边形";

    public void Execute(World world)
    {
        var dto = new HierarchyNodeDto
        {
            Id         = Guid.NewGuid().ToString(),
            Name       = "多边形",
            Icon       = "⬟",
            ObjectType = "Shape",
            HasMapPosition = true,
            X = _cx,
            Y = _cy,
        };

        var root = _vm.GetOrCreateShapeRoot();
        var item = new HierarchyItemViewModel(dto) { Parent = root };

        item.AddComponent(new ShapeComponent
        {
            ShapeType   = "polygon",
            Points      = new List<(double X, double Y)>(_relPts),
            StrokeColor = "#845EF7",
            FillColor   = "#40845EF7",
            StrokeWidth = 2,
            IsFilled    = _vm.ShapeDefaultFilled,
            StrokeStyle = _vm.ShapeDefaultStrokeStyle,
        });

        root.Children.Add(item);
        _vm.RegisterHierarchyItem(item);
        _vm.RefreshMapRenderableItemsPublic();
        _vm.SelectedHierarchyItem = item;
        _createdId = item.Id;
    }

    public void Undo(World world)
    {
        if (_createdId is null) return;
        var item = _vm.FindHierarchyById(_createdId);
        if (item?.Parent is null) return;
        item.Parent.Children.Remove(item);
        _vm.UnregisterHierarchyItem(item);
        _vm.RefreshMapRenderableItemsPublic();
        _createdId = null;
    }
}

/// <summary>
/// 在场景中创建自由绘制笔触对象（折线）。
/// 顶点存储在 ShapeComponent.Points（相对于对象中心），ShapeType="freehand"。
/// </summary>
public sealed class VmCreateDrawingCommand : ILocalOnlyCommand
{
    private readonly MainWindowViewModel _vm;
    private readonly double _cx, _cy;
    private readonly List<(double X, double Y)> _relPts;
    private string? _createdId;

    public VmCreateDrawingCommand(
        MainWindowViewModel vm,
        double cx, double cy,
        List<(double X, double Y)> relPts)
    {
        _vm = vm;
        _cx = cx; _cy = cy;
        _relPts = relPts;
    }

    public string Description => "自由绘制";

    public void Execute(World world)
    {
        var dto = new HierarchyNodeDto
        {
            Id         = Guid.NewGuid().ToString(),
            Name       = "绘制笔触",
            Icon       = "✏️",
            ObjectType = "Shape",
            HasMapPosition = true,
            X = _cx,
            Y = _cy,
        };

        var root = _vm.GetOrCreateShapeRoot();
        var item = new HierarchyItemViewModel(dto) { Parent = root };

        var shape = new ShapeComponent
        {
            ShapeType   = "freehand",
            Points      = new List<(double X, double Y)>(_relPts),
            StrokeColor = "#F59E0B",
            FillColor   = "#00000000",
            StrokeWidth = 3,
            IsFilled    = false,
        };
        item.AddComponent(shape);

        root.Children.Add(item);
        _vm.RegisterHierarchyItem(item);
        _vm.RefreshMapRenderableItemsPublic();
        _vm.SelectedHierarchyItem = item;
        _createdId = item.Id;
    }

    public void Undo(World world)
    {
        if (_createdId is null) return;
        var item = _vm.FindHierarchyById(_createdId);
        if (item?.Parent is null) return;
        item.Parent.Children.Remove(item);
        _vm.UnregisterHierarchyItem(item);
        _vm.RefreshMapRenderableItemsPublic();
        _createdId = null;
    }
}
