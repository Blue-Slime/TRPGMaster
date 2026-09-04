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
        var token = go.GetComponent<TokenComponent>();
        var graphNode = go.GetComponent<GraphNodeComponent>();
        var graphLinks = go.GetComponents<GraphLinkComponent>().ToList();

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
            AssetRef = sprite?.AssetRef ?? string.Empty,
            SourceAssetKind = sprite?.SourceAssetKind ?? string.Empty,
            SourceAssetName = sprite?.SourceAssetName ?? string.Empty,
            VisionEnabled = vision?.Enabled ?? false,
            VisionRadius = vision?.Radius ?? 0,
            ShapeV2 = shape is null ? null : new MapEngine.Core.Data.ShapeData
            {
                ShapeType = shape.ShapeType,
                Width = shape.Width,
                Height = shape.Height,
                X2 = shape.X2,
                Y2 = shape.Y2,
                Points = shape.Points.Select(p => new MapEngine.Core.Data.PointData { X = p.X, Y = p.Y }).ToList(),
                ConeAngle = shape.ConeAngle,
                ConeRadius = shape.ConeRadius,
                Rotation = shape.Rotation,
                StrokeColor = shape.StrokeColor,
                FillColor = shape.FillColor,
                StrokeWidth = shape.StrokeWidth,
                IsFilled = shape.IsFilled,
                StrokeStyle = (int)shape.StrokeStyle
            },
            TextV2 = text is null ? null : new MapEngine.Core.Data.TextData
            {
                Text = text.Text,
                FontSize = text.FontSize,
                Color = text.Color,
                BackgroundColor = text.BackgroundColor,
                IsBold = text.IsBold,
                IsItalic = text.IsItalic,
                Align = (int)text.Align
            },
            TokenV2 = token is null ? null : new MapEngine.Core.Data.TokenData
            {
                InitiativeOrder = token.InitiativeOrder,
                IsPlayerControlled = token.IsPlayerControlled,
                MovementSpeed = token.MovementSpeed,
                CurrentHP = token.CurrentHP,
                MaxHP = token.MaxHP
            },
            ConditionsV2 = token?.Conditions.Select(c => new MapEngine.Core.Data.ConditionData
            {
                Id = c.Id,
                Name = c.Name,
                Icon = c.Icon,
                StackCount = c.StackCount,
                RemainingRounds = c.RemainingRounds,
                ColorHex = c.ColorHex
            }).ToList(),
            GraphNodeV2 = graphNode is null ? null : new MapEngine.Core.Data.GraphNodeData
            {
                Kind         = (int)graphNode.Kind,
                DisplayName  = graphNode.DisplayName,
                Description  = graphNode.Description,
                Visibility   = (int)graphNode.Visibility,
                RenderMode   = (int)graphNode.RenderMode,
                IconAssetRef = graphNode.IconAssetRef,
                Color        = graphNode.Color,
                Size         = graphNode.Size,
                Shape        = graphNode.Shape
            },
            GraphLinksV2 = graphLinks.Count > 0
                ? graphLinks.Select(l => new MapEngine.Core.Data.GraphLinkData
                {
                    LinkId          = l.LinkId,
                    TargetNodeId    = l.TargetNodeId,
                    Kind            = (int)l.Kind,
                    IsBidirectional = l.IsBidirectional,
                    Label           = l.Label,
                    Visibility      = (int)l.Visibility,
                    IsPassable      = l.IsPassable,
                    Cost            = l.Cost,
                    Color           = l.Color,
                    Width           = l.Width,
                    StrokeStyle     = (int)l.StrokeStyle
                }).ToList()
                : null,
            Tags = [.. go.Tags],
            Children = go.Children.Select(GameObjectToDto).ToList()
        };
    }
}
