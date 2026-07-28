using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using MapEngine.Core.Hosting;
using MapEngine.Avalonia.ViewModels;

namespace MapEngine.Avalonia.Hosting;

/// <summary>
/// 把 ViewModel 当前状态序列化为 AI 友好的 JSON 快照。
/// 不依赖现有 SceneSerializer（那个针对 GameObject 模型），
/// 这里直接读 HierarchyItemViewModel 以反映用户实际看到的状态。
/// </summary>
internal static class SceneSnapshotBuilder
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string Build(MainWindowViewModel vm, SnapshotScope scope)
    {
        var doc = new SnapshotDocument
        {
            Scene = new SceneInfo
            {
                Name = vm.PackageName,
                ZoomPercent = vm.ZoomPercent,
                FeetPerCell = vm.FeetPerCell,
            },
            Selection = vm.SelectedHierarchyItem?.Id,
            Objects = scope switch
            {
                SnapshotScope.Selected => SelectedTrees(vm),
                SnapshotScope.Visible => VisibleTrees(vm),
                _ => vm.HierarchyRoots.Select(BuildNode).ToList(),
            },
        };
        return JsonSerializer.Serialize(doc, JsonOptions);
    }

    private static List<NodeSnapshot> SelectedTrees(MainWindowViewModel vm)
    {
        var sel = vm.SelectedHierarchyItem;
        return sel is null ? new List<NodeSnapshot>() : new List<NodeSnapshot> { BuildNode(sel) };
    }

    private static List<NodeSnapshot> VisibleTrees(MainWindowViewModel vm)
    {
        // MapRenderableItems 是扁平的渲染列表，但快照需要保持层级。
        // 简化策略：返回所有有 MapPosition 的根树（含其后代）。
        return vm.HierarchyRoots
            .Where(HasAnyVisible)
            .Select(BuildNode)
            .ToList();
    }

    private static bool HasAnyVisible(HierarchyItemViewModel item)
    {
        if (item.ShouldRenderOnMap) return true;
        return item.Children.Any(HasAnyVisible);
    }

    private static NodeSnapshot BuildNode(HierarchyItemViewModel item)
    {
        var snap = new NodeSnapshot
        {
            Id = item.Id,
            Name = item.Name,
            Type = item.ObjectType,
            Icon = item.Icon,
            IsActive = item.IsActive,
            IsLocked = item.IsLocked,
            HasMapPosition = item.HasMapPosition,
            Transform = new TransformSnapshot
            {
                X = item.X,
                Y = item.Y,
                Z = item.Z,
                Rotation = item.Rotation,
                ScaleX = item.ScaleX,
                ScaleY = item.ScaleY,
            },
            Sprite = string.IsNullOrEmpty(item.SourceAssetPath) ? null : new SpriteSnapshot
            {
                AssetName = item.SourceAssetName,
                AssetKind = item.SourceAssetKind,
                Color = item.SpriteColor,
                Opacity = item.Opacity,
            },
            Tags = item.Tags.Count > 0 ? item.Tags.ToList() : null,
        };

        if (item.VisionEnabled || item.VisionCones.Count > 0)
        {
            snap.Vision = new VisionSnapshot
            {
                Enabled = item.VisionEnabled,
                Radius = item.VisionRadius,
                Orientation = item.Orientation,
                Cones = item.VisionCones.Select(c => new VisionConeSnapshot
                {
                    Name = c.Name,
                    CenterOffset = c.CenterOffset,
                    Range = c.Range,
                    FieldOfView = c.FieldOfView,
                    IsEnabled = c.IsEnabled,
                }).ToList(),
            };
        }

        if (item.HasWallComponent)
        {
            var wall = item.GetComponent<MapEngine.Core.Components.WallComponent>();
            if (wall is not null)
            {
                snap.Wall = new WallSnapshot
                {
                    WallType = wall.Door != MapEngine.Core.Components.DoorKind.None ? "Door" : "Normal",
                    BlocksVision = wall.Sight != MapEngine.Core.Components.SenseLevel.None,
                    BlocksMovement = wall.Move != MapEngine.Core.Components.SenseLevel.None,
                    Thickness = wall.Thickness,
                    DoorState = wall.Door != MapEngine.Core.Components.DoorKind.None
                        ? wall.State.ToString()
                        : null,
                    ControlPoints = new() {
                        new[] { wall.X1, wall.Y1 },
                        new[] { wall.X2, wall.Y2 }
                    },
                };
            }
        }

        if (item.Children.Count > 0)
            snap.Children = item.Children.Select(BuildNode).ToList();

        return snap;
    }

    private sealed class SnapshotDocument
    {
        public int Version { get; set; } = 1;
        public SceneInfo Scene { get; set; } = new();
        public string? Selection { get; set; }
        public List<NodeSnapshot> Objects { get; set; } = [];
    }

    private sealed class SceneInfo
    {
        public string Name { get; set; } = string.Empty;
        public int ZoomPercent { get; set; }
        public int FeetPerCell { get; set; }
    }

    private sealed class NodeSnapshot
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = "Empty";
        public string Icon { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public bool IsLocked { get; set; }
        public bool HasMapPosition { get; set; }
        public TransformSnapshot Transform { get; set; } = new();
        public SpriteSnapshot? Sprite { get; set; }
        public VisionSnapshot? Vision { get; set; }
        public WallSnapshot? Wall { get; set; }
        public List<string>? Tags { get; set; }
        public List<NodeSnapshot>? Children { get; set; }
    }

    private sealed class TransformSnapshot
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
        public double Rotation { get; set; }
        public double ScaleX { get; set; }
        public double ScaleY { get; set; }
    }

    private sealed class SpriteSnapshot
    {
        public string AssetName { get; set; } = string.Empty;
        public string AssetKind { get; set; } = string.Empty;
        public string Color { get; set; } = string.Empty;
        public double Opacity { get; set; }
    }

    private sealed class VisionSnapshot
    {
        public bool Enabled { get; set; }
        public double Radius { get; set; }
        public double Orientation { get; set; }
        public List<VisionConeSnapshot> Cones { get; set; } = [];
    }

    private sealed class VisionConeSnapshot
    {
        public string Name { get; set; } = string.Empty;
        public double CenterOffset { get; set; }
        public double Range { get; set; }
        public double FieldOfView { get; set; }
        public bool IsEnabled { get; set; }
    }

    private sealed class WallSnapshot
    {
        public string WallType { get; set; } = string.Empty;
        public bool BlocksVision { get; set; }
        public bool BlocksMovement { get; set; }
        public double Thickness { get; set; }
        public string? DoorState { get; set; }
        public List<double[]> ControlPoints { get; set; } = [];
    }
}
