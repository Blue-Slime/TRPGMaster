namespace MapEngine.Core.Commands;

public sealed class HierarchyNode
{
    public required string Id { get; init; }
    public required string Name { get; set; }
    public string Icon { get; set; } = "📦";
    public string ObjectType { get; set; } = "Empty";
    public string InstanceId { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public bool IsLocked { get; set; }
    public int SortOrder { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }
    public double Rotation { get; set; }
    public double ScaleX { get; set; } = 1;
    public double ScaleY { get; set; } = 1;
    public string SpriteColor { get; set; } = "#FF4444";
    public double Opacity { get; set; } = 1;
    public bool HasMapPosition { get; set; }
    public string AssetRef { get; set; } = string.Empty;
    public string SourceAssetKind { get; set; } = string.Empty;
    public string SourceAssetName { get; set; } = string.Empty;
    public bool VisionEnabled { get; set; }
    public double VisionRadius { get; set; }
    public List<string> Tags { get; set; } = [];
    public string? ParentId { get; set; }
    public List<HierarchyNode> Children { get; set; } = [];
}
