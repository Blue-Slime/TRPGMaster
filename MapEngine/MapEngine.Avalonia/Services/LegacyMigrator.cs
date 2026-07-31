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

        if (dto.ShapeV2 is { } sd)
        {
            go.AddComponent(new ShapeComponent
            {
                ShapeType = sd.ShapeType,
                Width = sd.Width,
                Height = sd.Height,
                X2 = sd.X2,
                Y2 = sd.Y2,
                Points = sd.Points is null ? [] : sd.Points.Select(p => (p.X, p.Y)).ToList(),
                ConeAngle = sd.ConeAngle,
                ConeRadius = sd.ConeRadius,
                Rotation = sd.Rotation,
                StrokeColor = sd.StrokeColor,
                FillColor = sd.FillColor,
                StrokeWidth = sd.StrokeWidth,
                IsFilled = sd.IsFilled,
                StrokeStyle = (StrokeStyle)sd.StrokeStyle
            });
        }

        if (dto.TextV2 is { } td)
        {
            go.AddComponent(new TextComponent
            {
                Text = td.Text ?? string.Empty,
                FontSize = td.FontSize,
                Color = td.Color,
                BackgroundColor = td.BackgroundColor,
                IsBold = td.IsBold,
                IsItalic = td.IsItalic,
                Align = (TextAlign)td.Align
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
        var shape = go.GetComponent<ShapeComponent>();
        var text = go.GetComponent<TextComponent>();

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
            ShapeV2 = shape is null ? null : ToShapeData(shape),
            TextV2 = text is null ? null : ToTextData(text),
            Tags = [.. go.Tags],
            Children = go.Children.Select(ToHierarchyNodeDto).ToList()
        };
    }

    internal static MapEngine.Core.Data.ShapeData ToShapeData(ShapeComponent shape) => new()
    {
        ShapeType = shape.ShapeType,
        Width = shape.Width,
        Height = shape.Height,
        X2 = shape.X2,
        Y2 = shape.Y2,
        Points = shape.Points
            .Select(p => new MapEngine.Core.Data.PointData { X = p.X, Y = p.Y })
            .ToList(),
        ConeAngle = shape.ConeAngle,
        ConeRadius = shape.ConeRadius,
        Rotation = shape.Rotation,
        StrokeColor = shape.StrokeColor,
        FillColor = shape.FillColor,
        StrokeWidth = shape.StrokeWidth,
        IsFilled = shape.IsFilled,
        StrokeStyle = (int)shape.StrokeStyle
    };

    internal static MapEngine.Core.Data.TextData ToTextData(TextComponent text) => new()
    {
        Text = text.Text,
        FontSize = text.FontSize,
        Color = text.Color,
        BackgroundColor = text.BackgroundColor,
        IsBold = text.IsBold,
        IsItalic = text.IsItalic,
        Align = (int)text.Align
    };
}
