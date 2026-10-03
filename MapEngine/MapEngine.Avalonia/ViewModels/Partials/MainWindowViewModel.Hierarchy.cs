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

/// <summary>场景层级：增删改、上下移动、层级升降、组件挂载、复制粘贴。</summary>
public partial class MainWindowViewModel
{
    public HierarchyItemViewModel AddEmptyObject(HierarchyItemViewModel parent)
    {
        var dto = new HierarchyNodeDto
        {
            Id = CreateId("node"),
            Name = GetUniqueHierarchyName(parent, "空对象"),
            Icon = "📦",
            ObjectType = "Empty",
            InstanceId = CreateId("inst").ToUpperInvariant(),
            IsActive = true,
            ScaleX = 1,
            ScaleY = 1,
            Opacity = 1,
            VisionEnabled = false,
            VisionRadius = 0
        };

        _commandBus.Execute(new MapEngine.Avalonia.Commands.VmAddEmptyObjectCommand(this, parent.Id, dto));
        return FindHierarchyById(dto.Id) ?? throw new InvalidOperationException("AddEmptyObject failed");
    }

    public HierarchyItemViewModel? AddRootEmptyObject()
    {
        var root = HierarchyRoots.Count > 0 ? HierarchyRoots[0] : EnsureSceneRoot();
        return AddEmptyObject(root);
    }

    public void RenameHierarchyItem(HierarchyItemViewModel item, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        _commandBus.Execute(new MapEngine.Avalonia.Commands.VmRenameCommand(this, item.Id, name.Trim()));
    }

    public bool DeleteHierarchyItem(HierarchyItemViewModel item)
    {
        if (item.Parent is null)
        {
            StatusMessage = "地图包根节点不允许删除";
            return false;
        }

        _commandBus.Execute(new MapEngine.Avalonia.Commands.VmDeleteHierarchyItemCommand(this, item.Id));

        // 删除后刷新角色列表
        RefreshPlayableCharacters();

        return true;
    }

    public void MoveItemUp(HierarchyItemViewModel item)
    {
        if (item.Parent is null) return;
        var siblings = item.Parent.Children;
        var index = siblings.IndexOf(item);
        if (index <= 0)
        {
            StatusMessage = "已是第一个，无法上移";
            return;
        }
        _commandBus.Execute(new MapEngine.Avalonia.Commands.VmMoveItemUpCommand(this, item.Id));
    }

    public void MoveItemDown(HierarchyItemViewModel item)
    {
        if (item.Parent is null) return;
        var siblings = item.Parent.Children;
        var index = siblings.IndexOf(item);
        if (index < 0 || index >= siblings.Count - 1)
        {
            StatusMessage = "已是最后一个，无法下移";
            return;
        }
        _commandBus.Execute(new MapEngine.Avalonia.Commands.VmMoveItemDownCommand(this, item.Id));
    }

    public void PromoteItem(HierarchyItemViewModel item)
    {
        if (item.Parent?.Parent is null)
        {
            StatusMessage = "已是顶层，无法提升";
            return;
        }
        _commandBus.Execute(new MapEngine.Avalonia.Commands.VmPromoteItemCommand(this, item.Id));
    }

    public void DemoteItem(HierarchyItemViewModel item)
    {
        if (item.Parent is null) return;
        var siblings = item.Parent.Children;
        var index = siblings.IndexOf(item);
        if (index <= 0)
        {
            StatusMessage = "前面没有对象，无法降低层级";
            return;
        }
        _commandBus.Execute(new MapEngine.Avalonia.Commands.VmDemoteItemCommand(this, item.Id));
    }

    public void AddVisionCone(HierarchyItemViewModel item)
    {
        var cone = new VisionConeViewModel();
        item.VisionCones.Add(cone);
        StatusMessage = $"已添加视野锥 {cone.Name}";
    }

    public void RemoveVisionCone(HierarchyItemViewModel item, VisionConeViewModel cone)
    {
        item.VisionCones.Remove(cone);
        StatusMessage = $"已删除视野锥 {cone.Name}";
    }

    public void AddVisionComponent(HierarchyItemViewModel item)
    {
        if (item.HasComponent<MapEngine.Core.Components.VisionComponent>())
        {
            StatusMessage = "该对象已有视野组件";
            return;
        }

        var vision = new MapEngine.Core.Components.VisionComponent { Enabled = true, Radius = 60 };
        item.AddComponent(vision);
        item.NotifyVisionComponentChanged();
        StatusMessage = $"已为 {item.Name} 添加视野组件";
    }

    public void RemoveVisionComponent(HierarchyItemViewModel item)
    {
        if (!item.HasComponent<MapEngine.Core.Components.VisionComponent>())
        {
            StatusMessage = "该对象没有视野组件";
            return;
        }

        item.RemoveComponent<MapEngine.Core.Components.VisionComponent>();
        item.VisionCones.Clear();
        item.NotifyVisionComponentChanged();
        StatusMessage = $"已移除 {item.Name} 的视野组件";
    }

    public void AddWallComponent(HierarchyItemViewModel item)
    {
        if (item.HasComponent<MapEngine.Core.Components.WallComponent>())
        {
            StatusMessage = "该对象已有墙壁组件";
            return;
        }

        var wall = WallPresets.Normal();
        wall.X1 = item.X; wall.Y1 = item.Y;
        wall.X2 = item.X + 100; wall.Y2 = item.Y;

        item.AddComponent(wall);
        StatusMessage = $"已为 {item.Name} 添加墙壁组件";
    }

    public HierarchyItemViewModel? CreateWallObject(HierarchyItemViewModel parent)
    {
        var dto = new HierarchyNodeDto
        {
            Id = CreateId("node"),
            Name = GetUniqueHierarchyName(parent, "墙壁"),
            Icon = "🧱",
            ObjectType = "Empty",
            InstanceId = CreateId("inst").ToUpperInvariant(),
            IsActive = true,
            SortOrder = 0,
            X = 0,
            Y = 0,
            HasMapPosition = false
        };

        // 添加墙壁组件
        var wall = WallPresets.Normal();
        wall.X1 = 0; wall.Y1 = 0; wall.X2 = 100; wall.Y2 = 0;
        dto.Components["WallComponent"] = wall;

        _commandBus.Execute(new MapEngine.Avalonia.Commands.VmAddEmptyObjectCommand(this, parent.Id, dto));
        var created = FindHierarchyById(dto.Id);
        StatusMessage = $"已创建墙壁对象 {dto.Name}";
        return created;
    }

    public void RemoveWallComponent(HierarchyItemViewModel item)
    {
        if (!item.HasComponent<MapEngine.Core.Components.WallComponent>())
        {
            StatusMessage = "该对象没有墙壁组件";
            return;
        }

        item.RemoveComponent<MapEngine.Core.Components.WallComponent>();
        StatusMessage = $"已移除 {item.Name} 的墙壁组件";
    }

    public void AddTokenComponent(HierarchyItemViewModel item)
    {
        if (item.HasComponent<MapEngine.Core.Components.TokenComponent>())
        {
            StatusMessage = "该对象已有 Token 组件";
            return;
        }

        var token = new MapEngine.Core.Components.TokenComponent
        {
            TokenName = item.Name,
            InitiativeOrder = 0,
            IsPlayerControlled = false,
            MovementSpeed = 30
        };

        item.AddComponent(token);

        // 修改 ObjectType 为 "Token"，触发先攻追踪器刷新
        item.ObjectType = "Token";

        System.Diagnostics.Debug.WriteLine($"[AddTokenComponent] 已设置 ObjectType='{item.ObjectType}' for '{item.Name}'");

        // 添加 Token 后刷新角色列表
        RefreshPlayableCharacters();

        StatusMessage = $"已为 {item.Name} 添加 Token 组件";
    }

    public void RemoveTokenComponent(HierarchyItemViewModel item)
    {
        if (!item.HasComponent<MapEngine.Core.Components.TokenComponent>())
        {
            StatusMessage = "该对象没有 Token 组件";
            return;
        }

        item.RemoveComponent<MapEngine.Core.Components.TokenComponent>();

        // 移除 Token 后刷新角色列表
        RefreshPlayableCharacters();

        StatusMessage = $"已移除 {item.Name} 的 Token 组件";
    }

    public void AddToInitiativeTracker(HierarchyItemViewModel item)
    {
        var token = item.GetComponent<MapEngine.Core.Components.TokenComponent>();
        if (token == null)
        {
            StatusMessage = "该对象没有 Token 组件，无法加入先攻表";
            return;
        }

        if (token.IsInInitiativeTracker)
        {
            StatusMessage = $"{item.Name} 已在先攻表中";
            return;
        }

        token.IsInInitiativeTracker = true;
        item.NotifyTokenComponentChanged();
        StatusMessage = $"已将 {item.Name} 加入先攻表";
    }

    public void RemoveFromInitiativeTracker(HierarchyItemViewModel item)
    {
        var token = item.GetComponent<MapEngine.Core.Components.TokenComponent>();
        if (token == null)
        {
            StatusMessage = "该对象没有 Token 组件";
            return;
        }

        if (!token.IsInInitiativeTracker)
        {
            StatusMessage = $"{item.Name} 不在先攻表中";
            return;
        }

        token.IsInInitiativeTracker = false;
        item.NotifyTokenComponentChanged();
        StatusMessage = $"已将 {item.Name} 移出先攻表";
    }

    public void CopyHierarchyItem(HierarchyItemViewModel item)
    {
        _hierarchyClipboard = SnapshotHierarchy(item);
        StatusMessage = $"已复制层级对象 {item.DisplayName}";
        OnPropertyChanged(nameof(HasHierarchyClipboard));
    }

    /// <summary>剪贴板里是否有可粘贴的对象（右键菜单/快捷键门控）。</summary>
    public bool HasHierarchyClipboard => _hierarchyClipboard is not null;

    public HierarchyItemViewModel? PasteHierarchyItem(HierarchyItemViewModel parent)
    {
        if (_hierarchyClipboard is null)
        {
            StatusMessage = "当前没有可粘贴的层级对象";
            return null;
        }

        var snapshot = CloneHierarchySnapshot(_hierarchyClipboard);
        snapshot.Name = GetUniqueHierarchyName(parent, snapshot.Name);
        _commandBus.Execute(new MapEngine.Avalonia.Commands.VmAddEmptyObjectCommand(this, parent.Id, snapshot));
        return FindHierarchyById(snapshot.Id);
    }

    public HierarchyItemViewModel? PasteHierarchyToRoot()
    {
        // 粘贴到当前选中对象的父级（同级粘贴）。
        // 若没有选中，或选中的是包根节点，则粘贴到第一个根节点下。
        var targetParent = SelectedHierarchyItem?.Parent ?? HierarchyRoots.FirstOrDefault();
        return targetParent is null ? null : PasteHierarchyItem(targetParent);
    }

    public HierarchyItemViewModel? DuplicateHierarchyItem(HierarchyItemViewModel item)
    {
        if (item.Parent is null)
        {
            StatusMessage = "地图包根节点不支持复制副本";
            return null;
        }

        var snapshot = CloneHierarchySnapshot(SnapshotHierarchy(item));
        snapshot.Name = GetDuplicateName(item.Parent.Children.Select(child => child.Name), item.Name);
        _commandBus.Execute(new MapEngine.Avalonia.Commands.VmAddEmptyObjectCommand(this, item.Parent.Id, snapshot));
        return FindHierarchyById(snapshot.Id);
    }

    /// <summary>
    /// 获取所有层级项的扁平化集合（递归遍历所有子节点）。
    /// </summary>
    public IEnumerable<HierarchyItemViewModel> HierarchyItems
    {
        get
        {
            foreach (var root in HierarchyRoots)
            {
                foreach (var item in FlattenHierarchy(root))
                {
                    yield return item;
                }
            }
        }
    }

    private IEnumerable<HierarchyItemViewModel> FlattenHierarchy(HierarchyItemViewModel item)
    {
        yield return item;
        foreach (var child in item.Children)
        {
            foreach (var descendant in FlattenHierarchy(child))
            {
                yield return descendant;
            }
        }
    }
}
