using System;
using System.Collections.Generic;
using System.Linq;
using MapEngine.Core.Commands;
using MapEngine.Avalonia.ViewModels;

namespace MapEngine.Avalonia.Commands;

public sealed class ViewModelSceneState : ISceneState
{
    private readonly MainWindowViewModel _viewModel;
    private readonly ViewModelHierarchyState _hierarchy;

    public ViewModelSceneState(MainWindowViewModel viewModel)
    {
        _viewModel = viewModel;
        _hierarchy = new ViewModelHierarchyState(viewModel);
    }

    public IHierarchyState Hierarchy => _hierarchy;
    public IAssetState Assets => new ViewModelAssetState();
    public IViewportState Viewport => new ViewModelViewportState(_viewModel);
}

internal sealed class ViewModelHierarchyState : IHierarchyState
{
    private readonly MainWindowViewModel _vm;

    public ViewModelHierarchyState(MainWindowViewModel vm) => _vm = vm;

    public IReadOnlyList<HierarchyNode> Roots =>
        _vm.HierarchyRoots.Select(ToNode).ToList();

    public HierarchyNode? FindById(string id)
    {
        var item = FindViewModelById(id);
        return item is null ? null : ToNode(item);
    }

    public void AddChild(string parentId, HierarchyNode node)
    {
        var parent = FindViewModelById(parentId);
        if (parent is null) return;

        var dto = ToDto(node);
        var child = new HierarchyItemViewModel(dto) { Parent = parent };
        parent.Children.Add(child);
        _vm.RefreshMapRenderableItemsPublic();
    }

    public void RemoveNode(string nodeId)
    {
        var item = FindViewModelById(nodeId);
        if (item?.Parent is null) return;

        item.Parent.Children.Remove(item);
        _vm.RefreshMapRenderableItemsPublic();
    }

    public void SetProperty(string nodeId, string propertyName, object? value)
    {
        var item = FindViewModelById(nodeId);
        if (item is null) return;

        switch (propertyName)
        {
            case nameof(HierarchyNode.Name): item.Name = (string)(value ?? ""); break;
            case nameof(HierarchyNode.X): item.X = Convert.ToDouble(value); break;
            case nameof(HierarchyNode.Y): item.Y = Convert.ToDouble(value); break;
            case nameof(HierarchyNode.Z): item.Z = Convert.ToDouble(value); break;
            case nameof(HierarchyNode.Rotation): item.Rotation = Convert.ToDouble(value); break;
            case nameof(HierarchyNode.ScaleX): item.ScaleX = Convert.ToDouble(value); break;
            case nameof(HierarchyNode.ScaleY): item.ScaleY = Convert.ToDouble(value); break;
            case nameof(HierarchyNode.Opacity): item.Opacity = Convert.ToDouble(value); break;
            case nameof(HierarchyNode.IsActive): item.IsActive = Convert.ToBoolean(value); break;
            case nameof(HierarchyNode.IsLocked): item.IsLocked = Convert.ToBoolean(value); break;
            case nameof(HierarchyNode.SortOrder): item.SortOrder = Convert.ToInt32(value); break;
            case nameof(HierarchyNode.ObjectType): item.ObjectType = (string)(value ?? "Empty"); break;
            case nameof(HierarchyNode.Icon): item.Icon = (string)(value ?? "📦"); break;
            case nameof(HierarchyNode.SpriteColor): item.SpriteColor = (string)(value ?? "#FF4444"); break;
            case nameof(HierarchyNode.VisionEnabled): item.VisionEnabled = Convert.ToBoolean(value); break;
            case nameof(HierarchyNode.VisionRadius): item.VisionRadius = Convert.ToDouble(value); break;
            case nameof(HierarchyNode.HasMapPosition): item.HasMapPosition = Convert.ToBoolean(value); break;
        }
    }

    private HierarchyItemViewModel? FindViewModelById(string id)
    {
        foreach (var root in _vm.HierarchyRoots)
        {
            var found = FindRecursive(root, id);
            if (found is not null) return found;
        }
        return null;
    }

    private static HierarchyItemViewModel? FindRecursive(HierarchyItemViewModel item, string id)
    {
        if (item.Id == id) return item;
        foreach (var child in item.Children)
        {
            var found = FindRecursive(child, id);
            if (found is not null) return found;
        }
        return null;
    }

    private static HierarchyNode ToNode(HierarchyItemViewModel item) => new()
    {
        Id = item.Id,
        Name = item.Name,
        Icon = item.Icon,
        ObjectType = item.ObjectType,
        InstanceId = item.InstanceId,
        IsActive = item.IsActive,
        IsLocked = item.IsLocked,
        SortOrder = item.SortOrder,
        X = item.X,
        Y = item.Y,
        Z = item.Z,
        Rotation = item.Rotation,
        ScaleX = item.ScaleX,
        ScaleY = item.ScaleY,
        SpriteColor = item.SpriteColor,
        Opacity = item.Opacity,
        HasMapPosition = item.HasMapPosition,
        SourceAssetPath = item.SourceAssetPath,
        SourceAssetKind = item.SourceAssetKind,
        SourceAssetName = item.SourceAssetName,
        VisionEnabled = item.VisionEnabled,
        VisionRadius = item.VisionRadius,
        Tags = [.. item.Tags],
        ParentId = item.Parent?.Id,
        Children = item.Children.Select(ToNode).ToList()
    };

    private static MapEngine.Avalonia.Services.HierarchyNodeDto ToDto(HierarchyNode node) => new()
    {
        Id = node.Id,
        Name = node.Name,
        Icon = node.Icon,
        ObjectType = node.ObjectType,
        InstanceId = node.InstanceId,
        IsActive = node.IsActive,
        IsLocked = node.IsLocked,
        SortOrder = node.SortOrder,
        X = node.X,
        Y = node.Y,
        Z = node.Z,
        Rotation = node.Rotation,
        ScaleX = node.ScaleX,
        ScaleY = node.ScaleY,
        SpriteColor = node.SpriteColor,
        Opacity = node.Opacity,
        HasMapPosition = node.HasMapPosition,
        SourceAssetPath = node.SourceAssetPath,
        SourceAssetKind = node.SourceAssetKind,
        SourceAssetName = node.SourceAssetName,
        VisionEnabled = node.VisionEnabled,
        VisionRadius = node.VisionRadius,
        Tags = [.. node.Tags],
        Children = node.Children.Select(ToDto).ToList()
    };
}

internal sealed class ViewModelAssetState : IAssetState
{
    public string RootPath => "AssetLibrary";
}

internal sealed class ViewModelViewportState : IViewportState
{
    private readonly MainWindowViewModel _vm;
    public ViewModelViewportState(MainWindowViewModel vm) => _vm = vm;

    public double CenterX { get; set; }
    public double CenterY { get; set; }
    public double Zoom
    {
        get => _vm.ZoomScale;
        set => _vm.SetZoomFromInteraction((int)(value * 100));
    }
    public bool ShowGrid
    {
        get => _vm.ShowGrid;
        set => _vm.ShowGrid = value;
    }
}
