using MapEngine.Avalonia.Services;
using MapEngine.Core.Components;

namespace MapEngine.Avalonia.Services;

public static class LegacyMigrator
{
    public static GameObject FromHierarchyNodeDto(HierarchyNodeDto dto)
    {
        var go = new GameObject
        {
            Id = Guid.TryParse(dto.Id, out var id) ? id : Guid.NewGuid(),
            Name = dto.Name,
            Icon = dto.Icon,
            ObjectType = dto.ObjectType,
            IsActive = dto.IsActive,
            IsLocked = dto.IsLocked,
            SortOrder = dto.SortOrder,
            Tags = [.. dto.Tags]
        };

        go.AddComponent(new TransformComponent
        {
            X = dto.X,
            Y = dto.Y,
            Z = dto.Z,
            Rotation = dto.Rotation,
            ScaleX = dto.ScaleX,
            ScaleY = dto.ScaleY,
            HasMapPosition = dto.HasMapPosition
        });

        if (!string.IsNullOrEmpty(dto.SourceAssetPath) || !string.IsNullOrEmpty(dto.SpriteColor))
        {
            go.AddComponent(new SpriteRendererComponent
            {
                Color = dto.SpriteColor,
                Opacity = dto.Opacity,
                SourceAssetPath = dto.SourceAssetPath,
                SourceAssetKind = dto.SourceAssetKind,
                SourceAssetName = dto.SourceAssetName
            });
        }

        if (dto.VisionEnabled || dto.VisionRadius > 0)
        {
            go.AddComponent(new VisionComponent
            {
                Enabled = dto.VisionEnabled,
                Radius = dto.VisionRadius
            });
        }

        foreach (var childDto in dto.Children)
        {
            var child = FromHierarchyNodeDto(childDto);
            child.Parent = go;
            go.Children.Add(child);
        }

        return go;
    }

    public static HierarchyNodeDto ToHierarchyNodeDto(GameObject go)
    {
        var transform = go.GetComponent<TransformComponent>();
        var sprite = go.GetComponent<SpriteRendererComponent>();
        var vision = go.GetComponent<VisionComponent>();

        return new HierarchyNodeDto
        {
            Id = go.Id.ToString(),
            Name = go.Name,
            Icon = go.Icon,
            ObjectType = go.ObjectType,
            InstanceId = go.Id.ToString("N")[..12].ToUpperInvariant(),
            IsActive = go.IsActive,
            IsLocked = go.IsLocked,
            SortOrder = go.SortOrder,
            X = transform?.X ?? 0,
            Y = transform?.Y ?? 0,
            Z = transform?.Z ?? 0,
            Rotation = transform?.Rotation ?? 0,
            ScaleX = transform?.ScaleX ?? 1,
            ScaleY = transform?.ScaleY ?? 1,
            HasMapPosition = transform?.HasMapPosition ?? false,
            SpriteColor = sprite?.Color ?? "#FF4444",
            Opacity = sprite?.Opacity ?? 1,
            SourceAssetPath = sprite?.SourceAssetPath ?? string.Empty,
            SourceAssetKind = sprite?.SourceAssetKind ?? string.Empty,
            SourceAssetName = sprite?.SourceAssetName ?? string.Empty,
            VisionEnabled = vision?.Enabled ?? false,
            VisionRadius = vision?.Radius ?? 0,
            Tags = [.. go.Tags],
            Children = go.Children.Select(ToHierarchyNodeDto).ToList()
        };
    }
}
