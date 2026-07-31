using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MapEngine.Core.Components;

namespace MapEngine.Avalonia.Services;

public static class SceneFileLoader
{
    public static FakeProjectData? LoadScene(string? path = null)
    {
        var scenePath = path ?? ResolveDefaultScenePath();
        if (scenePath is null || !File.Exists(scenePath))
            return null;

        var json = File.ReadAllText(scenePath);
        var doc = SceneSerializer.Deserialize(json);
        if (doc is null)
            return null;

        var gameObjects = SceneSerializer.FromDocument(doc);
        var hierarchyRoots = gameObjects.Select(GameObjectToDto).ToList();

        var packageName = hierarchyRoots.FirstOrDefault()?.Name ?? "Scene";

        return new FakeProjectData
        {
            PackageName = packageName,
            HierarchyRoots = hierarchyRoots,
            ShowGrid = true,
            ZoomPercent = 100,
            FeetPerCell = 5,
            ActiveTool = "select"
        };
    }

    private static string? ResolveDefaultScenePath()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Data", "demo.scene"),
            Path.Combine(Directory.GetCurrentDirectory(), "Data", "demo.scene")
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    private static HierarchyNodeDto GameObjectToDto(GameObject go)
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
            ShapeV2 = shape is null ? null : LegacyMigrator.ToShapeData(shape),
            TextV2 = text is null ? null : LegacyMigrator.ToTextData(text),
            Tags = [.. go.Tags],
            Children = go.Children.Select(GameObjectToDto).ToList()
        };
    }
}
