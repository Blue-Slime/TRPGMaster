namespace MapEngine.Core.Commands;

public interface ISceneState
{
    IHierarchyState Hierarchy { get; }
    IAssetState Assets { get; }
    IViewportState Viewport { get; }
}

public interface IHierarchyState
{
    IReadOnlyList<HierarchyNode> Roots { get; }
    HierarchyNode? FindById(string id);
    void AddChild(string parentId, HierarchyNode node);
    void RemoveNode(string nodeId);
    void SetProperty(string nodeId, string propertyName, object? value);
}

public interface IAssetState
{
    string RootPath { get; }
}

public interface IViewportState
{
    double CenterX { get; set; }
    double CenterY { get; set; }
    double Zoom { get; set; }
    bool ShowGrid { get; set; }
}
