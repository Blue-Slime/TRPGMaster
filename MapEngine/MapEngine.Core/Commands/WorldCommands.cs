using MapEngine.Core.Components;

namespace MapEngine.Core.Commands;

/// <summary>
/// 在 World 中添加新对象(可选挂到父对象下)。
/// 替换 VmAddEmptyObjectCommand / VmCreateInstanceCommand。
/// </summary>
public sealed class WorldAddObjectCommand : IWorldCommand
{
    private readonly Guid _objectId;
    private readonly Guid? _parentId;
    private readonly string _name;
    private readonly string _objectType;
    private readonly TransformComponent _transform;
    private readonly SpriteRendererComponent _sprite;

    public WorldAddObjectCommand(
        Guid objectId,
        Guid? parentId,
        string name,
        string objectType,
        TransformComponent transform,
        SpriteRendererComponent sprite)
    {
        _objectId = objectId;
        _parentId = parentId;
        _name = name;
        _objectType = objectType;
        _transform = transform;
        _sprite = sprite;
    }

    public string Description => $"添加对象 {_name}";

    // 公开属性用于序列化
    public Guid ObjectId => _objectId;
    public Guid? ParentId => _parentId;
    public string Name => _name;
    public string ObjectType => _objectType;
    public TransformComponent Transform => _transform;

    public void Execute(World world)
    {
        var obj = new GameObject { Id = _objectId, Name = _name, ObjectType = _objectType };
        obj.AddComponent(_transform);
        obj.AddComponent(_sprite);

        var parent = _parentId.HasValue ? world.FindById(_parentId.Value) : null;
        world.AddObject(obj, parent);
    }

    public void Undo(World world)
    {
        var obj = world.FindById(_objectId);
        if (obj is not null) world.RemoveObject(obj);
    }
}

/// <summary>
/// 从 World 中删除对象(及其子树)。
/// 替换 VmDeleteHierarchyItemCommand。
/// </summary>
public sealed class WorldDeleteObjectCommand : IWorldCommand
{
    private readonly Guid _objectId;
    private GameObject? _snapshot;
    private Guid? _parentId;
    private int _insertIndex;

    public WorldDeleteObjectCommand(Guid objectId)
    {
        _objectId = objectId;
    }

    public string Description => $"删除对象 {_snapshot?.Name ?? _objectId.ToString()}";

    // 公开属性用于序列化
    public Guid ObjectId => _objectId;

    public void Execute(World world)
    {
        var obj = world.FindById(_objectId);
        if (obj is null) return;

        _snapshot = obj.Clone();
        _parentId = obj.Parent?.Id;
        _insertIndex = obj.Parent?.Children.IndexOf(obj) ?? -1;

        world.RemoveObject(obj);
    }

    public void Undo(World world)
    {
        if (_snapshot is null) return;

        var parent = _parentId.HasValue ? world.FindById(_parentId.Value) : null;
        world.AddObject(_snapshot, parent);

        // 恢复原索引位置
        if (parent is not null && _insertIndex >= 0)
        {
            parent.Children.Remove(_snapshot);
            var idx = Math.Min(_insertIndex, parent.Children.Count);
            parent.Children.Insert(idx, _snapshot);
        }
    }

    // 快照属性：Execute() 后可用，供 SerializeInverse 重建 AddObject 命令
    public GameObject? Snapshot => _snapshot;
    public Guid? SnapshotParentId => _parentId;
}

/// <summary>
/// 移动对象(及其子树)到新位置。
/// 替换 VmMoveObjectCommand。
/// </summary>
public sealed class WorldMoveObjectCommand : IWorldCommand
{
    private readonly Guid _objectId;
    private readonly double _newX, _newY;
    private readonly double _oldX, _oldY;
    private List<(Guid Id, double OldX, double OldY)>? _childPositions;

    public WorldMoveObjectCommand(Guid objectId, double oldX, double oldY, double newX, double newY)
    {
        _objectId = objectId;
        _oldX = oldX;
        _oldY = oldY;
        _newX = newX;
        _newY = newY;
    }

    public string Description => $"移动到 ({_newX:F0}, {_newY:F0})";

    // 公开属性用于序列化
    public Guid ObjectId => _objectId;
    public double OldX => _oldX;
    public double OldY => _oldY;
    public double NewX => _newX;
    public double NewY => _newY;

    public void Execute(World world)
    {
        var obj = world.FindById(_objectId);
        if (obj?.GetComponent<TransformComponent>() is not { } tf) return;

        var deltaX = _newX - _oldX;
        var deltaY = _newY - _oldY;

        // 记录子对象原位置
        if (_childPositions is null)
        {
            _childPositions = [];
            CollectChildPositions(obj, _childPositions);
        }

        // 移动主对象
        tf.X = _newX;
        tf.Y = _newY;

        // 移动子对象(相对父对象保持偏移)
        foreach (var (childId, oldCX, oldCY) in _childPositions)
        {
            var child = world.FindById(childId);
            if (child?.GetComponent<TransformComponent>() is { } ctf)
            {
                ctf.X = oldCX + deltaX;
                ctf.Y = oldCY + deltaY;
            }
        }

        world.NotifySpatialChanged(obj);
    }

    public void Undo(World world)
    {
        var obj = world.FindById(_objectId);
        if (obj?.GetComponent<TransformComponent>() is not { } tf) return;

        tf.X = _oldX;
        tf.Y = _oldY;

        if (_childPositions is not null)
        {
            foreach (var (childId, oldCX, oldCY) in _childPositions)
            {
                var child = world.FindById(childId);
                if (child?.GetComponent<TransformComponent>() is { } ctf)
                {
                    ctf.X = oldCX;
                    ctf.Y = oldCY;
                }
            }
        }

        world.NotifySpatialChanged(obj);
    }

    private static void CollectChildPositions(GameObject obj, List<(Guid, double, double)> list)
    {
        foreach (var child in obj.Children)
        {
            if (child.GetComponent<TransformComponent>() is { } tf)
                list.Add((child.Id, tf.X, tf.Y));
            CollectChildPositions(child, list);
        }
    }
}

/// <summary>
/// 设置对象属性(Name/ObjectType)或组件字段。
/// 替换 VmSetPropertyCommand。
/// </summary>
public sealed class WorldSetPropertyCommand : IWorldCommand
{
    private readonly Guid _objectId;
    private readonly string _property;
    private readonly object? _newValue;
    private object? _oldValue;

    public WorldSetPropertyCommand(Guid objectId, string property, object? newValue)
    {
        _objectId = objectId;
        _property = property;
        _newValue = newValue;
    }

    public string Description => $"设置 {_property} = {_newValue}";

    // 公开属性用于序列化
    public Guid ObjectId => _objectId;
    public string PropertyName => _property;
    public object? NewValue => _newValue;
    public object? OldValue => _oldValue; // Execute() 后可用，供 SerializeInverse 还原

    public void Execute(World world)
    {
        var obj = world.FindById(_objectId);
        if (obj is null) return;

        _oldValue = GetProperty(obj);
        SetProperty(obj, _newValue);

        // 空间属性变更需通知索引
        if (_property is "X" or "Y" or "Rotation" or "ScaleX" or "ScaleY")
            world.NotifySpatialChanged(obj);
        else
            world.MarkDirty(DirtyFlags.Render);
    }

    public void Undo(World world)
    {
        var obj = world.FindById(_objectId);
        if (obj is null) return;

        SetProperty(obj, _oldValue);

        if (_property is "X" or "Y" or "Rotation" or "ScaleX" or "ScaleY")
            world.NotifySpatialChanged(obj);
        else
            world.MarkDirty(DirtyFlags.Render);
    }

    private object? GetProperty(GameObject obj)
    {
        var tf = obj.GetComponent<TransformComponent>();
        var sr = obj.GetComponent<SpriteRendererComponent>();
        var vis = obj.GetComponent<VisionComponent>();

        return _property switch
        {
            "Name" => obj.Name,
            "ObjectType" => obj.ObjectType,
            "X" => tf?.X,
            "Y" => tf?.Y,
            "Z" => tf?.Z,
            "Rotation" => tf?.Rotation,
            "ScaleX" => tf?.ScaleX,
            "ScaleY" => tf?.ScaleY,
            "Opacity" => sr?.Opacity,
            "Color" => sr?.Color,
            "SpriteColor" => sr?.Color, // 兼容旧属性名
            "VisionEnabled" => vis?.Enabled,
            "VisionRadius" => vis?.Radius,
            "Orientation" => vis?.Orientation,
            _ => null
        };
    }

    private void SetProperty(GameObject obj, object? value)
    {
        var tf = obj.GetComponent<TransformComponent>();
        var sr = obj.GetComponent<SpriteRendererComponent>();
        var vis = obj.GetComponent<VisionComponent>();

        switch (_property)
        {
            case "Name": obj.Name = (string)(value ?? ""); break;
            case "ObjectType": obj.ObjectType = (string)(value ?? "Empty"); break;
            case "X" when tf is not null: tf.X = Convert.ToDouble(value); break;
            case "Y" when tf is not null: tf.Y = Convert.ToDouble(value); break;
            case "Z" when tf is not null: tf.Z = Convert.ToDouble(value); break;
            case "Rotation" when tf is not null: tf.Rotation = Convert.ToDouble(value); break;
            case "ScaleX" when tf is not null: tf.ScaleX = Convert.ToDouble(value); break;
            case "ScaleY" when tf is not null: tf.ScaleY = Convert.ToDouble(value); break;
            case "Opacity" when sr is not null: sr.Opacity = Convert.ToDouble(value); break;
            case "Color" when sr is not null: sr.Color = (string)(value ?? "#FFFFFF"); break;
            case "SpriteColor" when sr is not null: sr.Color = (string)(value ?? "#FFFFFF"); break; // 兼容旧属性名
            case "VisionEnabled" when vis is not null: vis.Enabled = Convert.ToBoolean(value); break;
            case "VisionRadius" when vis is not null: vis.Radius = Convert.ToDouble(value); break;
            case "Orientation" when vis is not null: vis.Orientation = Convert.ToDouble(value); break;
        }
    }
}

/// <summary>
/// 重命名对象。
/// 替换 VmRenameCommand(现在合并进 WorldSetPropertyCommand,但保留独立命令以保持语义清晰)。
/// </summary>
public sealed class WorldRenameCommand : IWorldCommand
{
    private readonly Guid _objectId;
    private readonly string _newName;
    private string? _oldName;

    public WorldRenameCommand(Guid objectId, string newName)
    {
        _objectId = objectId;
        _newName = newName;
    }

    public string Description => $"重命名为 {_newName}";

    // 公开属性用于序列化
    public Guid ObjectId => _objectId;
    public string NewName => _newName;
    public string? OldName => _oldName; // Execute() 后可用，供 SerializeInverse 还原

    public void Execute(World world)
    {
        var obj = world.FindById(_objectId);
        if (obj is null) return;
        _oldName = obj.Name;
        obj.Name = _newName;
        world.MarkDirty(DirtyFlags.Hierarchy);
    }

    public void Undo(World world)
    {
        if (_oldName is null) return;
        var obj = world.FindById(_objectId);
        if (obj is null) return;
        obj.Name = _oldName;
        world.MarkDirty(DirtyFlags.Hierarchy);
    }
}

/// <summary>
/// 为对象添加组件。
/// </summary>
public sealed class WorldAddComponentCommand : IWorldCommand
{
    private readonly Guid _objectId;
    private readonly string _componentType;
    private IComponent? _component;

    public WorldAddComponentCommand(Guid objectId, string componentType)
    {
        _objectId = objectId;
        _componentType = componentType;
    }

    public string Description => $"添加组件 {_componentType}";

    // 公开属性用于序列化
    public Guid ObjectId => _objectId;
    public string ComponentType => _componentType;

    public void Execute(World world)
    {
        var obj = world.FindById(_objectId);
        if (obj is null) return;

        _component = _componentType switch
        {
            "Vision" => new MapEngine.Core.Components.VisionComponent(),
            "Wall" => new MapEngine.Core.Components.WallComponent(),
            "Token" => new MapEngine.Core.Components.TokenComponent(),
            _ => throw new ArgumentException($"Unknown component: {_componentType}")
        };

        world.AddComponent(obj, _component);
    }

    public void Undo(World world)
    {
        if (_component is null) return;
        var obj = world.FindById(_objectId);
        if (obj is null) return;
        world.RemoveComponent(obj, _component);
    }
}

/// <summary>
/// 移除对象的组件。
/// </summary>
public sealed class WorldRemoveComponentCommand : IWorldCommand
{
    private readonly Guid _objectId;
    private readonly string _componentType;
    private IComponent? _removedComponent;

    public WorldRemoveComponentCommand(Guid objectId, string componentType)
    {
        _objectId = objectId;
        _componentType = componentType;
    }

    public string Description => $"移除组件 {_componentType}";

    // 公开属性用于序列化
    public Guid ObjectId => _objectId;
    public string ComponentType => _componentType;

    public void Execute(World world)
    {
        var obj = world.FindById(_objectId);
        if (obj is null) return;

        _removedComponent = _componentType switch
        {
            "Vision" => obj.GetComponent<MapEngine.Core.Components.VisionComponent>(),
            "Wall" => obj.GetComponent<MapEngine.Core.Components.WallComponent>(),
            "Token" => obj.GetComponent<MapEngine.Core.Components.TokenComponent>(),
            _ => null
        };

        if (_removedComponent is not null)
        {
            world.RemoveComponent(obj, _removedComponent);
        }
    }

    public void Undo(World world)
    {
        if (_removedComponent is null) return;
        var obj = world.FindById(_objectId);
        if (obj is null) return;
        world.AddComponent(obj, _removedComponent);
    }
}

/// <summary>
/// 更新 GraphNodeComponent 字段（单字段增量更新，减少网络传输）。
/// 前端 Inspector 编辑时按字段触发，而非整个组件替换。
/// </summary>
public sealed class WorldUpdateGraphNodeCommand : IWorldCommand
{
    private readonly Guid _objectId;
    private readonly string _property;
    private readonly object? _newValue;
    private object? _oldValue;

    public WorldUpdateGraphNodeCommand(Guid objectId, string property, object? newValue)
    {
        _objectId = objectId;
        _property = property;
        _newValue = newValue;
    }

    public string Description => $"设置节点 {_property} = {_newValue}";

    public Guid ObjectId => _objectId;
    public string Property => _property;
    public object? NewValue => _newValue;
    public object? OldValue => _oldValue;

    public void Execute(World world)
    {
        var obj = world.FindById(_objectId);
        var comp = obj?.GetComponent<GraphNodeComponent>();
        if (comp is null) return;

        _oldValue = _property switch
        {
            "Kind" => (int)comp.Kind,
            "DisplayName" => comp.DisplayName,
            "Description" => comp.Description,
            "Visibility" => (int)comp.Visibility,
            "RenderMode" => (int)comp.RenderMode,
            "IconAssetRef" => comp.IconAssetRef,
            "Color" => comp.Color,
            "Size" => comp.Size,
            "Shape" => comp.Shape,
            _ => null
        };

        switch (_property)
        {
            case "Kind":
                comp.Kind = (GraphNodeKind)Convert.ToInt32(_newValue);
                break;
            case "DisplayName":
                comp.DisplayName = _newValue?.ToString() ?? string.Empty;
                break;
            case "Description":
                comp.Description = _newValue?.ToString() ?? string.Empty;
                break;
            case "Visibility":
                comp.Visibility = (GraphVisibility)Convert.ToInt32(_newValue);
                break;
            case "RenderMode":
                comp.RenderMode = (GraphNodeRenderMode)Convert.ToInt32(_newValue);
                break;
            case "IconAssetRef":
                comp.IconAssetRef = _newValue?.ToString() ?? string.Empty;
                break;
            case "Color":
                comp.Color = _newValue?.ToString() ?? "#4A90E2";
                break;
            case "Size":
                comp.Size = Convert.ToDouble(_newValue);
                break;
            case "Shape":
                comp.Shape = _newValue?.ToString() ?? "circle";
                break;
        }

        world.MarkDirty(DirtyFlags.Render);
    }

    public void Undo(World world)
    {
        var obj = world.FindById(_objectId);
        var comp = obj?.GetComponent<GraphNodeComponent>();
        if (comp is null) return;

        switch (_property)
        {
            case "Kind":
                comp.Kind = (GraphNodeKind)Convert.ToInt32(_oldValue);
                break;
            case "DisplayName":
                comp.DisplayName = _oldValue?.ToString() ?? string.Empty;
                break;
            case "Description":
                comp.Description = _oldValue?.ToString() ?? string.Empty;
                break;
            case "Visibility":
                comp.Visibility = (GraphVisibility)Convert.ToInt32(_oldValue);
                break;
            case "RenderMode":
                comp.RenderMode = (GraphNodeRenderMode)Convert.ToInt32(_oldValue);
                break;
            case "IconAssetRef":
                comp.IconAssetRef = _oldValue?.ToString() ?? string.Empty;
                break;
            case "Color":
                comp.Color = _oldValue?.ToString() ?? "#4A90E2";
                break;
            case "Size":
                comp.Size = Convert.ToDouble(_oldValue);
                break;
            case "Shape":
                comp.Shape = _oldValue?.ToString() ?? "circle";
                break;
        }

        world.MarkDirty(DirtyFlags.Render);
    }
}

/// <summary>
/// 更新 GraphLinkComponent 字段。一个对象可挂多条 GraphLink，
/// 通过 LinkId 定位具体哪条边，避免多实例组件的混淆。
/// </summary>
public sealed class WorldUpdateGraphLinkCommand : IWorldCommand
{
    private readonly Guid _objectId;
    private readonly string _linkId;
    private readonly string _property;
    private readonly object? _newValue;
    private object? _oldValue;

    public WorldUpdateGraphLinkCommand(Guid objectId, string linkId, string property, object? newValue)
    {
        _objectId = objectId;
        _linkId = linkId;
        _property = property;
        _newValue = newValue;
    }

    public string Description => $"设置连接 {_linkId} 的 {_property} = {_newValue}";

    public Guid ObjectId => _objectId;
    public string LinkId => _linkId;
    public string Property => _property;
    public object? NewValue => _newValue;
    public object? OldValue => _oldValue;

    public void Execute(World world)
    {
        var obj = world.FindById(_objectId);
        var comp = obj?.GetComponents<GraphLinkComponent>()
            .FirstOrDefault(l => l.LinkId == _linkId);
        if (comp is null) return;

        _oldValue = _property switch
        {
            "TargetNodeId" => comp.TargetNodeId,
            "Kind" => (int)comp.Kind,
            "IsBidirectional" => comp.IsBidirectional,
            "Label" => comp.Label,
            "Visibility" => (int)comp.Visibility,
            "IsPassable" => comp.IsPassable,
            "Cost" => comp.Cost,
            "Color" => comp.Color,
            "Width" => comp.Width,
            "StrokeStyle" => (int)comp.StrokeStyle,
            _ => null
        };

        switch (_property)
        {
            case "TargetNodeId":
                comp.TargetNodeId = _newValue?.ToString() ?? string.Empty;
                break;
            case "Kind":
                comp.Kind = (GraphLinkKind)Convert.ToInt32(_newValue);
                break;
            case "IsBidirectional":
                comp.IsBidirectional = Convert.ToBoolean(_newValue);
                break;
            case "Label":
                comp.Label = _newValue?.ToString() ?? string.Empty;
                break;
            case "Visibility":
                comp.Visibility = (GraphVisibility)Convert.ToInt32(_newValue);
                break;
            case "IsPassable":
                comp.IsPassable = Convert.ToBoolean(_newValue);
                break;
            case "Cost":
                comp.Cost = Convert.ToDouble(_newValue);
                break;
            case "Color":
                comp.Color = _newValue?.ToString() ?? "#8A8F98";
                break;
            case "Width":
                comp.Width = Convert.ToDouble(_newValue);
                break;
            case "StrokeStyle":
                comp.StrokeStyle = (StrokeStyle)Convert.ToInt32(_newValue);
                break;
        }

        world.MarkDirty(DirtyFlags.Render);
    }

    public void Undo(World world)
    {
        var obj = world.FindById(_objectId);
        var comp = obj?.GetComponents<GraphLinkComponent>()
            .FirstOrDefault(l => l.LinkId == _linkId);
        if (comp is null) return;

        switch (_property)
        {
            case "TargetNodeId":
                comp.TargetNodeId = _oldValue?.ToString() ?? string.Empty;
                break;
            case "Kind":
                comp.Kind = (GraphLinkKind)Convert.ToInt32(_oldValue);
                break;
            case "IsBidirectional":
                comp.IsBidirectional = Convert.ToBoolean(_oldValue);
                break;
            case "Label":
                comp.Label = _oldValue?.ToString() ?? string.Empty;
                break;
            case "Visibility":
                comp.Visibility = (GraphVisibility)Convert.ToInt32(_oldValue);
                break;
            case "IsPassable":
                comp.IsPassable = Convert.ToBoolean(_oldValue);
                break;
            case "Cost":
                comp.Cost = Convert.ToDouble(_oldValue);
                break;
            case "Color":
                comp.Color = _oldValue?.ToString() ?? "#8A8F98";
                break;
            case "Width":
                comp.Width = Convert.ToDouble(_oldValue);
                break;
            case "StrokeStyle":
                comp.StrokeStyle = (StrokeStyle)Convert.ToInt32(_oldValue);
                break;
        }

        world.MarkDirty(DirtyFlags.Render);
    }
}

/// <summary>
/// 添加一条 GraphLink（一个对象可挂多条，不能用 WorldAddComponentCommand）。
/// 携带完整初始数据避免"先加空组件再逐字段 Update"的冗余同步。
/// </summary>
public sealed class WorldAddGraphLinkCommand : IWorldCommand
{
    private readonly Guid _objectId;
    private readonly GraphLinkComponent _link;

    public WorldAddGraphLinkCommand(Guid objectId, GraphLinkComponent link)
    {
        _objectId = objectId;
        _link = link;
    }

    public string Description => $"添加连接 → {_link.TargetNodeId}";

    public Guid ObjectId => _objectId;
    public GraphLinkComponent Link => _link;

    public void Execute(World world)
    {
        var obj = world.FindById(_objectId);
        if (obj is null) return;
        _link.Owner = obj;
        world.AddComponent(obj, _link);
    }

    public void Undo(World world)
    {
        var obj = world.FindById(_objectId);
        if (obj is null) return;
        world.RemoveComponent(obj, _link);
    }
}

/// <summary>
/// 移除一条 GraphLink（按 LinkId 定位）。
/// </summary>
public sealed class WorldRemoveGraphLinkCommand : IWorldCommand
{
    private readonly Guid _objectId;
    private readonly string _linkId;
    private GraphLinkComponent? _removedLink;

    public WorldRemoveGraphLinkCommand(Guid objectId, string linkId)
    {
        _objectId = objectId;
        _linkId = linkId;
    }

    public string Description => $"移除连接 {_linkId}";

    public Guid ObjectId => _objectId;
    public string LinkId => _linkId;
    public GraphLinkComponent? RemovedLink => _removedLink;

    public void Execute(World world)
    {
        var obj = world.FindById(_objectId);
        _removedLink = obj?.GetComponents<GraphLinkComponent>()
            .FirstOrDefault(l => l.LinkId == _linkId);

        if (_removedLink is not null)
            world.RemoveComponent(obj!, _removedLink);
    }

    public void Undo(World world)
    {
        if (_removedLink is null) return;
        var obj = world.FindById(_objectId);
        if (obj is null) return;
        world.AddComponent(obj, _removedLink);
    }
}
