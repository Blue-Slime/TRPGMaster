using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.IO;
using Avalonia;
using Avalonia.Media;
using CommunityToolkit.Mvvm.Input;
using MapEngine.Core;
using MapEngine.Core.Commands;
using MapEngine.Core.Data;
using MapEngine.Core.Components;
using MapEngine.Avalonia.Commands;
using MapEngine.Avalonia.Graphics;
using MapEngine.Avalonia.Services;
using MapEngine.Core.Hosting;

namespace MapEngine.Avalonia.ViewModels;

/// <summary>内部工具：索引查找、选中同步、快照克隆、唯一命名、素材库重载。</summary>
public partial class MainWindowViewModel
{
    private HierarchyItemViewModel? FindHierarchy(string? id)
        => id is not null && _hierarchyIndex.TryGetValue(id, out var item) ? item : null;

    private AssetFolderViewModel? FindAssetFolder(string? id)
        => id is not null && _assetFolderIndex.TryGetValue(id, out var item) ? item : null;

    private AssetFolderViewModel? FindAssetFolderByPath(string? path)
        => path is null ? null : FindAssetFolder(NormalizePath(path));

    private AssetItemViewModel? FindAssetItemByPath(string? path)
        => path is null
            ? null
            : _allAssetItems.FirstOrDefault(item => item.Id.Equals(NormalizePath(path), StringComparison.OrdinalIgnoreCase));

    private void UpdateCurrentSelection(IGlobalSelectionItem? selection)
    {
        CurrentSelection = selection;
    }

    private void UpdateHighlightedHierarchyItem(HierarchyItemViewModel? item)
    {
        if (ReferenceEquals(_highlightedHierarchyItem, item))
        {
            return;
        }

        if (_highlightedHierarchyItem is not null)
        {
            _highlightedHierarchyItem.IsSelected = false;
        }

        _highlightedHierarchyItem = item;

        if (_highlightedHierarchyItem is not null)
        {
            _highlightedHierarchyItem.IsSelected = true;
        }
    }

    private HierarchyItemViewModel CreateHierarchyFromSnapshot(HierarchyNodeDto snapshot, HierarchyItemViewModel parent)
    {
        var item = BuildHierarchyItem(snapshot);
        item.Parent = parent;
        parent.Children.Add(item);
        RefreshMapRenderableItems();
        SelectedHierarchyItem = item;
        return item;
    }

    private static HierarchyNodeDto SnapshotHierarchy(HierarchyItemViewModel item)
    {
        var dto = new HierarchyNodeDto
        {
            Id = item.Id,
            Name = item.Name,
            Icon = item.Icon,
            ObjectType = item.ObjectType,
            InstanceId = item.InstanceId,
            IsActive = item.IsActive,
            IsLocked = item.IsLocked,
            SortOrder = item.SortOrder,
            HasMapPosition = item.HasMapPosition,
            SourceAssetPath = item.SourceAssetPath,
            SourceAssetKind = item.SourceAssetKind,
            SourceAssetName = item.SourceAssetName,
            Tags = item.Tags.ToList(),
            Children = item.Children.Select(SnapshotHierarchy).ToList()
        };

        // 从 BackingObject 收集组件到 Components 字典(供持久化兼容)
        dto.Components = new Dictionary<string, object>();
        if (item.BackingObject.GetComponent<WallComponent>() is { } wall)
        {
            dto.Components["WallComponent"] = new Dictionary<string, object>
            {
                ["X1"] = wall.X1,
                ["Y1"] = wall.Y1,
                ["X2"] = wall.X2,
                ["Y2"] = wall.Y2,
                ["Sight"] = (int)wall.Sight,
                ["Move"] = (int)wall.Move,
                ["Sound"] = (int)wall.Sound,
                ["Light"] = (int)wall.Light,
                ["Dir"] = (int)wall.Dir,
                ["Door"] = (int)wall.Door,
                ["State"] = (int)wall.State,
                ["Thickness"] = wall.Thickness,
                ["TileTexturePath"] = wall.TileTexturePath ?? "",
                ["NoCutaway"] = wall.NoCutaway
            };
        }

        // 写入新 V2 结构化字段
        dto.TransformV2 = new MapEngine.Core.Data.TransformData
        {
            X = item.X, Y = item.Y, Z = item.Z,
            Rotation = item.Rotation,
            ScaleX = item.ScaleX, ScaleY = item.ScaleY
        };
        // 同步旧字段
        dto.X = item.X; dto.Y = item.Y; dto.Z = item.Z;
        dto.Rotation = item.Rotation;
        dto.ScaleX = item.ScaleX; dto.ScaleY = item.ScaleY;

        if (!string.IsNullOrEmpty(item.SourceAssetPath))
        {
            dto.SpriteV2 = new MapEngine.Core.Data.SpriteData
            {
                TexturePath = item.SourceAssetPath,
                Opacity = item.Opacity,
                TintColor = item.SpriteColor,
                AlignX = item.SpriteAlignX,
                AlignY = item.SpriteAlignY,
                HitTestEnabled = item.SpriteHitTestEnabled
            };
        }
        // 同步旧字段
        dto.SourceAssetPath = item.SourceAssetPath;
        dto.Opacity = item.Opacity;
        dto.SpriteColor = item.SpriteColor;

        if (item.VisionEnabled || item.VisionCones.Count > 0)
        {
            dto.VisionV2 = new MapEngine.Core.Data.VisionData
            {
                Enabled = item.VisionEnabled,
                Radius = item.VisionRadius,
                Orientation = item.Orientation,
                Cones = item.VisionCones.Select(c => new MapEngine.Core.Data.VisionConeData
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
        // 同步旧字段
        dto.VisionEnabled = item.VisionEnabled;
        dto.VisionRadius = item.VisionRadius;
        dto.Orientation = item.Orientation;
        dto.VisionCones = item.VisionCones.Select(c => c.ToDto()).ToList();

        // Shape / Text 是可选组件，只有挂载了才写入存档
        if (item.BackingObject.GetComponent<ShapeComponent>() is { } shape)
        {
            dto.ShapeV2 = LegacyMigrator.ToShapeData(shape);
        }

        if (item.BackingObject.GetComponent<TextComponent>() is { } text)
        {
            dto.TextV2 = LegacyMigrator.ToTextData(text);
        }

        // Token 组件：只要挂载了 TokenComponent 就写入存档（包括无状态标记的情况）
        if (item.BackingObject.GetComponent<TokenComponent>() is { } token)
        {
            dto.TokenV2 = new TokenData
            {
                InitiativeOrder = token.InitiativeOrder,
                IsPlayerControlled = token.IsPlayerControlled,
                MovementSpeed = token.MovementSpeed,
                CurrentHP = token.CurrentHP,
                MaxHP = token.MaxHP
            };
            dto.ConditionsV2 = token.Conditions.Select(c => new ConditionData
            {
                Id = c.Id,
                Name = c.Name,
                Icon = c.Icon,
                StackCount = c.StackCount,
                RemainingRounds = c.RemainingRounds,
                ColorHex = c.ColorHex
            }).ToList();
        }

        return dto;
    }

    private static HierarchyNodeDto CloneHierarchySnapshot(HierarchyNodeDto source)
    {
        return new HierarchyNodeDto
        {
            Id = CreateId("node"),
            Name = source.Name,
            Icon = source.Icon,
            ObjectType = source.ObjectType,
            InstanceId = CreateId("inst").ToUpperInvariant(),
            IsActive = source.IsActive,
            IsLocked = source.IsLocked,
            SortOrder = source.SortOrder,
            X = source.X,
            Y = source.Y,
            Z = source.Z,
            Rotation = source.Rotation,
            ScaleX = source.ScaleX,
            ScaleY = source.ScaleY,
            SpriteColor = source.SpriteColor,
            Opacity = source.Opacity,
            HasMapPosition = source.HasMapPosition,
            SourceAssetPath = source.SourceAssetPath,
            SourceAssetKind = source.SourceAssetKind,
            SourceAssetName = source.SourceAssetName,
            VisionEnabled = source.VisionEnabled,
            VisionRadius = source.VisionRadius,
            Orientation = source.Orientation,
            VisionCones = source.VisionCones.Select(c => new VisionConeDto
            {
                Id = $"cone-{Guid.NewGuid():N}",
                Name = c.Name,
                CenterOffset = c.CenterOffset,
                Range = c.Range,
                FieldOfView = c.FieldOfView,
                IsEnabled = c.IsEnabled
            }).ToList(),
            Tags = source.Tags.ToList(),
            Components = new Dictionary<string, object>(source.Components),
            // ShapeData/TextData 是值类型，赋值即拷贝；但 Points 是引用列表，需单独深拷
            ShapeV2 = source.ShapeV2 is { } sd
                ? sd with { Points = sd.Points is null ? [] : [.. sd.Points] }
                : null,
            TextV2 = source.TextV2,
            ConditionsV2 = source.ConditionsV2?.Select(c => new ConditionData
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = c.Name,
                Icon = c.Icon,
                StackCount = c.StackCount,
                RemainingRounds = c.RemainingRounds,
                ColorHex = c.ColorHex
            }).ToList(),
            Children = source.Children.Select(CloneHierarchySnapshot).ToList()
        };
    }

    private void RemoveHierarchyIndex(HierarchyItemViewModel item)
    {
        foreach (var child in item.Children.ToList())
        {
            RemoveHierarchyIndex(child);
        }

        _hierarchyIndex.Remove(item.Id);
    }

    private void RemoveAssetFolder(AssetFolderViewModel folder)
    {
        foreach (var child in folder.Children.ToList())
        {
            RemoveAssetFolder(child);
        }

        var folderIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        CollectFolderIds(folder, folderIds);
        _allAssetItems.RemoveAll(item => folderIds.Contains(item.FolderId));
        _assetFolderIndex.Remove(folder.Id);
        RefreshVisibleAssets();
    }

    private void ReloadAssetLibrary(string? selectedFolderPath, string? selectedItemPath)
    {
        var snapshot = AssetLibraryFileSystemService.Load(_assetLibraryRootPath);

        _assetFolderIndex.Clear();
        AssetRoots.Clear();
        foreach (var root in snapshot.RootFolders.Select(folder => BuildAssetFolder(folder, null)))
        {
            AssetRoots.Add(root);
        }

        _allAssetItems.Clear();
        foreach (var item in snapshot.AssetItems)
        {
            _allAssetItems.Add(new AssetItemViewModel(item.Id, item.FolderId, item.FullPath, item.FileName, item.Name, item.Kind, item.Icon, item.Description));
        }

        var folder = FindAssetFolderByPath(selectedFolderPath)
            ?? AssetRoots.FirstOrDefault()?.Children.FirstOrDefault()
            ?? AssetRoots.FirstOrDefault();

        SelectedAssetFolder = folder;

        var selectedItem = FindAssetItemByPath(selectedItemPath);
        SelectedAssetItem = selectedItem is not null && VisibleAssetItems.Contains(selectedItem) ? selectedItem : null;
    }

    private string GetUniqueHierarchyName(HierarchyItemViewModel parent, string baseName)
    {
        return GetUniqueName(parent.Children.Select(child => child.Name), baseName);
    }

    private string GetUniqueFolderName(AssetFolderViewModel parent, string baseName)
    {
        return GetUniqueName(parent.Children.Select(child => child.Name), baseName);
    }

    private string GetUniqueAssetItemName(string folderId, string baseName)
    {
        return GetUniqueName(_allAssetItems.Where(item => item.FolderId == folderId).Select(item => item.Name), baseName);
    }

    private static string GetUniqueName(IEnumerable<string> existingNames, string baseName)
    {
        var nameSet = new HashSet<string>(existingNames, StringComparer.OrdinalIgnoreCase);
        if (!nameSet.Contains(baseName))
        {
            return baseName;
        }

        var index = 2;
        while (nameSet.Contains($"{baseName} {index}"))
        {
            index++;
        }

        return $"{baseName} {index}";
    }

    private static string GetDuplicateName(IEnumerable<string> existingNames, string baseName)
        => GetUniqueName(existingNames, $"{baseName} 副本");

    private HierarchyItemViewModel? ResolveDefaultDropParent()
    {
        if (SelectedHierarchyItem is not null)
            return SelectedHierarchyItem;

        if (HierarchyRoots.Count > 0)
            return HierarchyRoots[0];

        // 空场景时自动建根节点
        return EnsureSceneRoot();
    }

    /// <summary>保证场景至少有一个根节点。返回现有或新建的根节点。</summary>
    private HierarchyItemViewModel EnsureSceneRoot()
    {
        if (HierarchyRoots.Count > 0)
            return HierarchyRoots[0];

        var dto = new HierarchyNodeDto
        {
            Id = CreateId("node"),
            Name = "场景",
            Icon = "🗺️",
            ObjectType = "Scene",
            IsActive = true,
            SortOrder = 0,
            ScaleX = 1,
            ScaleY = 1,
            Opacity = 1,
            VisionEnabled = false,
            VisionRadius = 0,
            Orientation = 0,
            VisionCones = [],
            Tags = []
        };

        var root = BuildHierarchyItem(dto);
        HierarchyRoots.Add(root);
        // 注意:不在这里调 RefreshMapRenderableItems——构造函数末尾会统一刷
        // 若在构造完成后动态调用此方法则需自行刷,见调用方判断
        if (MapRenderableItems is not null)
            RefreshMapRenderableItems();
        return root;
    }

    private HierarchyItemViewModel CreateInstanceFromAsset(
        AssetItemViewModel asset,
        HierarchyItemViewModel parent,
        double? x,
        double? y,
        bool isPreview = false,
        bool updateSelection = true,
        bool updateStatus = true)
    {
        var instanceName = GetUniqueHierarchyName(parent, asset.InstanceBaseName);

        // 读取 .asset 文件里的组件数据
        var assetDoc = TryLoadAssetDocument(asset.FullPath);
        var spriteProps = assetDoc?.Components
            .FirstOrDefault(c => c.Type.Equals("SpriteRenderer", StringComparison.OrdinalIgnoreCase))?.Properties;
        var visionProps = assetDoc?.Components
            .FirstOrDefault(c => c.Type.Equals("Vision", StringComparison.OrdinalIgnoreCase))?.Properties;

        var spriteColor = spriteProps?.GetValueOrDefault("spriteColor") ?? "#FF4444";
        var opacity = double.TryParse(spriteProps?.GetValueOrDefault("opacity"), out var op) ? op : 1.0;
        var visionEnabled = bool.TryParse(visionProps?.GetValueOrDefault("enabled"), out var ve) && ve;
        var visionRadius = double.TryParse(visionProps?.GetValueOrDefault("radius"), out var vr) ? vr : 60.0;

        var dto = new HierarchyNodeDto
        {
            Id = CreateId("node"),
            Name = instanceName,
            Icon = asset.Icon,
            ObjectType = ResolveHierarchyObjectType(asset),
            InstanceId = CreateId("inst").ToUpperInvariant(),
            IsActive = true,
            SortOrder = 0,
            ScaleX = 1,
            ScaleY = 1,
            SpriteColor = spriteColor,
            Opacity = opacity,
            VisionEnabled = visionEnabled,
            VisionRadius = visionRadius,
            Orientation = 0,
            // 视野开启时自动生成一个全向视野锥，否则 BuildVision 空循环不渲染
            VisionCones = visionEnabled
                ? [new VisionConeDto
                    {
                        Id = CreateId("cone"),
                        Name = "默认视野",
                        CenterOffset = 0,
                        // .asset 的 radius 语义是"英尺"(D&D 视距),按每格英尺数换算成格子数;
                        // 渲染端再 Range * CellSize 得像素。除以 CellSize 是把英尺当像素的旧 bug。
                        Range = visionRadius / Math.Max(1, FeetPerCell),
                        FieldOfView = 360,
                        IsEnabled = true
                    }]
                : [],
            X = x ?? 0,
            Y = y ?? 0,
            HasMapPosition = x.HasValue && y.HasValue,
            SourceAssetPath = asset.FullPath,
            SourceAssetKind = asset.Kind,
            SourceAssetName = asset.Name,
            Tags =
            [
                $"AssetKind:{asset.Kind}",
                $"AssetName:{asset.Name}"
            ]
        };

        if (isPreview)
        {
            var instance = new HierarchyItemViewModel(dto) { Parent = parent };
            instance.IsPreviewInstance = true;
            parent.Children.Add(instance);
            _hierarchyIndex[instance.Id] = instance;
            RefreshMapRenderableItems();
            if (updateSelection)
            {
                SelectedAssetItem = asset;
                SelectedHierarchyItem = instance;
            }
            return instance;
        }

        _commandBus.Execute(new MapEngine.Avalonia.Commands.VmCreateInstanceCommand(this, parent.Id, dto));
        var created = FindHierarchyById(dto.Id)!;

        if (updateSelection)
        {
            SelectedAssetItem = asset;
        }

        if (updateStatus)
        {
            StatusMessage = $"已从素材 {asset.Name} 生成实例 {created.Name}";
        }

        return created;
    }

    private static MapEngine.Avalonia.Services.StaticObjectAssetDocument? TryLoadAssetDocument(string fullPath)
    {
        try
        {
            if (!File.Exists(fullPath)) return null;
            var json = File.ReadAllText(fullPath);
            return System.Text.Json.JsonSerializer.Deserialize<MapEngine.Avalonia.Services.StaticObjectAssetDocument>(
                json, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch { return null; }
    }

    private static string ResolveHierarchyObjectType(AssetItemViewModel asset)
        => asset.Kind switch
        {
            "Audio" => "AudioSource",
            "Prefab" => "PrefabInstance",
            "Map" => "Map",
            "StaticObjectClass" => "StaticObject",
            _ => "Prop"
        };
}
