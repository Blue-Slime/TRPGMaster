using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using MapEngine.Core.Data;

namespace MapEngine.Avalonia.Services;

public static class FakeProjectDataLoader
{
    public static FakeProjectData Load()
    {
        var sceneData = SceneFileLoader.LoadScene();
        if (sceneData is not null)
            return sceneData;

        try
        {
            var dataPath = ResolveDataPath();
            var json = File.ReadAllText(dataPath);
            var data = JsonSerializer.Deserialize<FakeProjectData>(json, SerializerOptions);
            return data ?? new FakeProjectData();
        }
        catch
        {
            return new FakeProjectData();
        }
    }

    private static string ResolveDataPath()
    {
        var outputPath = Path.Combine(AppContext.BaseDirectory, "Data", "ui-state.json");
        if (File.Exists(outputPath))
        {
            return outputPath;
        }

        return Path.Combine(Directory.GetCurrentDirectory(), "Data", "ui-state.json");
    }

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };
}

public sealed class FakeProjectData
{
    public string PackageName { get; set; } = "默认地图包";

    public string AssetLibraryRootFolder { get; set; } = AssetLibraryFileSystemService.DefaultRootFolderName;

    public string? SelectedHierarchyId { get; set; }

    public string? SelectedAssetFolderId { get; set; }

    public string ActiveTool { get; set; } = "select";

    public bool ShowGrid { get; set; } = true;

    public int ZoomPercent { get; set; } = 100;

    public int FeetPerCell { get; set; } = 5;

    public List<HierarchyNodeDto> HierarchyRoots { get; set; } = [];

    public List<AssetFolderDto> AssetFolders { get; set; } = [];

    public List<AssetItemDto> AssetItems { get; set; } = [];
}

public sealed class HierarchyNodeDto
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

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

    public bool VisionEnabled { get; set; } = false;

    public double VisionRadius { get; set; } = 60;

    /// <summary>
    /// 朝向角度（0-360°，0=正右，90=正上，逆时针）
    /// </summary>
    public double Orientation { get; set; } = 0;

    /// <summary>
    /// 视野锥列表
    /// </summary>
    public List<VisionConeDto> VisionCones { get; set; } = [];

    /// <summary>
    /// 可选组件数据（JSON 序列化）
    /// </summary>
    public Dictionary<string, object> Components { get; set; } = new();

    // ===== 新架构：分层组件（方案 C） =====
    // 保留旧字段以保持向后兼容，逐步迁移

    /// <summary>
    /// Transform 数据（新架构）
    /// 访问优先级：Transform > (X,Y,Z,Rotation,ScaleX,ScaleY)
    /// </summary>
    public TransformData? TransformV2 { get; set; }

    /// <summary>
    /// Sprite 数据（新架构）
    /// 访问优先级：SpriteV2 > (AssetRef, Opacity, SpriteColor)
    /// </summary>
    public SpriteData? SpriteV2 { get; set; }

    /// <summary>
    /// Vision 数据（新架构）
    /// 访问优先级：VisionV2 > (VisionEnabled, VisionRadius, Orientation, VisionCones)
    /// </summary>
    public VisionData? VisionV2 { get; set; }

    /// <summary>
    /// 矢量形状数据（ObjectType == "Shape" 时存在）
    /// </summary>
    public ShapeData? ShapeV2 { get; set; }

    /// <summary>
    /// 文本标注数据（ObjectType == "Text" 时存在）
    /// </summary>
    public TextData? TextV2 { get; set; }

    /// <summary>
    /// Token 状态列表（ObjectType == "Token" 时存在）
    /// </summary>
    public List<ConditionData>? ConditionsV2 { get; set; }

    /// <summary>
    /// Token 核心数据（InitiativeOrder/HP 等，ObjectType == "Token" 时存在）
    /// </summary>
    public TokenData? TokenV2 { get; set; }

    /// <summary>
    /// 拓扑节点数据（挂了 GraphNodeComponent 时存在）
    /// </summary>
    public GraphNodeData? GraphNodeV2 { get; set; }

    /// <summary>
    /// 拓扑出边列表（挂了 GraphLinkComponent 时存在，一个对象可有多条）
    /// </summary>
    public List<GraphLinkData>? GraphLinksV2 { get; set; }

    /// <summary>
    /// 墙体路径数据（ObjectType == "Wall" 时存在）
    /// </summary>
    public WallPathData? WallPathV2 { get; set; }

    public List<string> Tags { get; set; } = [];

    public List<HierarchyNodeDto> Children { get; set; } = [];

    // ===== 迁移辅助方法 =====

    /// <summary>
    /// 从旧字段迁移到新架构
    /// </summary>
    public void MigrateToV2()
    {
        // Transform 迁移
        if (TransformV2 == null)
        {
            TransformV2 = new TransformData
            {
                X = X,
                Y = Y,
                Z = Z,
                Rotation = Rotation,
                ScaleX = ScaleX,
                ScaleY = ScaleY
            };
        }

        // Sprite 迁移（如果有素材引用）
        if (SpriteV2 == null && !string.IsNullOrEmpty(AssetRef))
        {
            SpriteV2 = new SpriteData
            {
                TexturePath = AssetRef,
                Opacity = Opacity,
                TintColor = SpriteColor,
                AlignX = 1,
                AlignY = 1,
                HitTestEnabled = true
            };
        }

        // Vision 迁移（如果启用）
        if (VisionV2 == null && VisionEnabled)
        {
            VisionV2 = new VisionData
            {
                Enabled = VisionEnabled,
                Radius = VisionRadius,
                Orientation = Orientation,
                Cones = VisionCones.Select(c => new VisionConeData
                {
                    Id = c.Id,
                    Name = c.Name,
                    CenterOffset = c.CenterOffset,
                    Range = c.Range,
                    FieldOfView = c.FieldOfView,
                    IsEnabled = c.IsEnabled
                }).ToList()
            };
        }

        // 递归迁移子节点
        foreach (var child in Children)
        {
            child.MigrateToV2();
        }
    }

    /// <summary>
    /// 同步新字段到旧字段（向后兼容）
    /// </summary>
    public void SyncV2ToLegacy()
    {
        if (TransformV2.HasValue)
        {
            var t = TransformV2.Value;
            X = t.X;
            Y = t.Y;
            Z = t.Z;
            Rotation = t.Rotation;
            ScaleX = t.ScaleX;
            ScaleY = t.ScaleY;
        }

        if (SpriteV2.HasValue)
        {
            var s = SpriteV2.Value;
            AssetRef = s.TexturePath;
            Opacity = s.Opacity;
            SpriteColor = s.TintColor;
        }

        if (VisionV2.HasValue)
        {
            var v = VisionV2.Value;
            VisionEnabled = v.Enabled;
            VisionRadius = v.Radius;
            Orientation = v.Orientation;
            VisionCones = v.Cones.Select(c => new VisionConeDto
            {
                Id = c.Id,
                Name = c.Name,
                CenterOffset = c.CenterOffset,
                Range = c.Range,
                FieldOfView = c.FieldOfView,
                IsEnabled = c.IsEnabled
            }).ToList();
        }
    }
}

public sealed class AssetFolderDto
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string FullPath { get; set; } = string.Empty;

    public List<AssetFolderDto> Children { get; set; } = [];
}

public sealed class AssetItemDto
{
    public string Id { get; set; } = string.Empty;

    public string FolderId { get; set; } = string.Empty;

    public string FullPath { get; set; } = string.Empty;

    public string FileName { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Kind { get; set; } = "Image";

    public string Icon { get; set; } = "🖼️";

    public string Description { get; set; } = string.Empty;
}
