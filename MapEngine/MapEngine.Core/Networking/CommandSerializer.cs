using System.Text.Json;
using MapEngine.Core.Commands;
using MapEngine.Core.Components;

namespace MapEngine.Core.Networking;

/// <summary>
/// 命令序列化器：将 IWorldCommand 转换为 JSON（用于网络传输）
/// </summary>
public static class CommandSerializer
{
    /// <summary>
    /// 序列化正向命令
    /// </summary>
    public static string Serialize(IWorldCommand command)
    {
        string commandType;
        object paramsObj;

        switch (command)
        {
            case WorldAddObjectCommand cmd:
                commandType = "AddObject";
                paramsObj = new
                {
                    object_id = cmd.ObjectId.ToString(),
                    parent_id = cmd.ParentId?.ToString(),
                    name = cmd.Name,
                    type = cmd.ObjectType,
                    x = cmd.Transform.X,
                    y = cmd.Transform.Y,
                    rotation = cmd.Transform.Rotation,
                    scale_x = cmd.Transform.ScaleX,
                    scale_y = cmd.Transform.ScaleY
                };
                break;
            case WorldDeleteObjectCommand cmd:
                commandType = "DeleteObject";
                paramsObj = new { object_id = cmd.ObjectId.ToString() };
                break;
            case WorldMoveObjectCommand cmd:
                commandType = "MoveObject";
                paramsObj = new
                {
                    object_id = cmd.ObjectId.ToString(),
                    x = cmd.NewX,
                    y = cmd.NewY
                };
                break;
            case WorldSetPropertyCommand cmd:
                commandType = "SetProperty";
                paramsObj = new
                {
                    object_id = cmd.ObjectId.ToString(),
                    property = cmd.PropertyName,
                    value = cmd.NewValue
                };
                break;
            case WorldAddComponentCommand cmd:
                commandType = "AddComponent";
                paramsObj = new
                {
                    object_id = cmd.ObjectId.ToString(),
                    component_type = cmd.ComponentType
                };
                break;
            case WorldRemoveComponentCommand cmd:
                commandType = "RemoveComponent";
                paramsObj = new
                {
                    object_id = cmd.ObjectId.ToString(),
                    component_type = cmd.ComponentType
                };
                break;
            case WorldRenameCommand cmd:
                commandType = "Rename";
                paramsObj = new
                {
                    object_id = cmd.ObjectId.ToString(),
                    name = cmd.NewName
                };
                break;
            case WorldUpdateGraphNodeCommand cmd:
                commandType = "UpdateGraphNode";
                paramsObj = new
                {
                    object_id = cmd.ObjectId.ToString(),
                    property = cmd.Property,
                    value = cmd.NewValue
                };
                break;
            case WorldUpdateGraphLinkCommand cmd:
                commandType = "UpdateGraphLink";
                paramsObj = new
                {
                    object_id = cmd.ObjectId.ToString(),
                    link_id = cmd.LinkId,
                    property = cmd.Property,
                    value = cmd.NewValue
                };
                break;
            case WorldAddGraphLinkCommand cmd:
                commandType = "AddGraphLink";
                paramsObj = GraphLinkPayload(cmd.ObjectId, cmd.Link);
                break;
            case WorldRemoveGraphLinkCommand cmd:
                commandType = "RemoveGraphLink";
                paramsObj = new
                {
                    object_id = cmd.ObjectId.ToString(),
                    link_id = cmd.LinkId
                };
                break;
            default:
                throw new NotSupportedException($"Cannot serialize command type: {command.GetType().Name}");
        }

        return JsonSerializer.Serialize(new
        {
            command_type = commandType,
            @params = paramsObj
        });
    }

    /// <summary>
    /// 序列化逆命令（用于 Undo 时发送到服务端）
    /// 前置条件：command 必须已经 Execute() 过（oldValue/snapshot 已填充）
    /// </summary>
    public static string SerializeInverse(IWorldCommand command)
    {
        string commandType;
        object paramsObj;

        switch (command)
        {
            case WorldAddObjectCommand cmd:
                // AddObject 的逆操作是 DeleteObject
                commandType = "DeleteObject";
                paramsObj = new { object_id = cmd.ObjectId.ToString() };
                break;

            case WorldDeleteObjectCommand cmd:
                // DeleteObject 的逆操作是 AddObject（用快照重建）
                if (cmd.Snapshot is not { } snapshot)
                    throw new InvalidOperationException("DeleteObject command must be executed before SerializeInverse");

                var tf = snapshot.GetComponent<TransformComponent>();
                commandType = "AddObject";
                paramsObj = new
                {
                    object_id = snapshot.Id.ToString(),
                    parent_id = cmd.SnapshotParentId?.ToString(),
                    name = snapshot.Name,
                    type = snapshot.ObjectType,
                    x = tf?.X ?? 0,
                    y = tf?.Y ?? 0,
                    rotation = tf?.Rotation ?? 0,
                    scale_x = tf?.ScaleX ?? 1,
                    scale_y = tf?.ScaleY ?? 1
                };
                break;

            case WorldMoveObjectCommand cmd:
                // MoveObject 的逆操作是 MoveObject（交换 old/new）
                commandType = "MoveObject";
                paramsObj = new
                {
                    object_id = cmd.ObjectId.ToString(),
                    x = cmd.OldX,
                    y = cmd.OldY
                };
                break;

            case WorldSetPropertyCommand cmd:
                // SetProperty 的逆操作是 SetProperty（使用 oldValue）
                commandType = "SetProperty";
                paramsObj = new
                {
                    object_id = cmd.ObjectId.ToString(),
                    property = cmd.PropertyName,
                    value = cmd.OldValue
                };
                break;

            case WorldAddComponentCommand cmd:
                // AddComponent 的逆操作是 RemoveComponent
                commandType = "RemoveComponent";
                paramsObj = new
                {
                    object_id = cmd.ObjectId.ToString(),
                    component_type = cmd.ComponentType
                };
                break;

            case WorldRemoveComponentCommand cmd:
                // RemoveComponent 的逆操作是 AddComponent
                commandType = "AddComponent";
                paramsObj = new
                {
                    object_id = cmd.ObjectId.ToString(),
                    component_type = cmd.ComponentType
                };
                break;

            case WorldRenameCommand cmd:
                // Rename 的逆操作是 Rename（使用 oldName）
                if (cmd.OldName is not { } oldName)
                    throw new InvalidOperationException("Rename command must be executed before SerializeInverse");

                commandType = "Rename";
                paramsObj = new
                {
                    object_id = cmd.ObjectId.ToString(),
                    name = oldName
                };
                break;

            case WorldUpdateGraphNodeCommand cmd:
                // 逆操作是同一命令带旧值
                commandType = "UpdateGraphNode";
                paramsObj = new
                {
                    object_id = cmd.ObjectId.ToString(),
                    property = cmd.Property,
                    value = cmd.OldValue
                };
                break;

            case WorldUpdateGraphLinkCommand cmd:
                commandType = "UpdateGraphLink";
                paramsObj = new
                {
                    object_id = cmd.ObjectId.ToString(),
                    link_id = cmd.LinkId,
                    property = cmd.Property,
                    value = cmd.OldValue
                };
                break;

            case WorldAddGraphLinkCommand cmd:
                // AddGraphLink 的逆操作是 RemoveGraphLink
                commandType = "RemoveGraphLink";
                paramsObj = new
                {
                    object_id = cmd.ObjectId.ToString(),
                    link_id = cmd.Link.LinkId
                };
                break;

            case WorldRemoveGraphLinkCommand cmd:
                // RemoveGraphLink 的逆操作是 AddGraphLink（用摘除时的快照重建）
                if (cmd.RemovedLink is not { } removed)
                    throw new InvalidOperationException("RemoveGraphLink command must be executed before SerializeInverse");

                commandType = "AddGraphLink";
                paramsObj = GraphLinkPayload(cmd.ObjectId, removed);
                break;

            default:
                throw new NotSupportedException($"Cannot serialize inverse for command type: {command.GetType().Name}");
        }

        return JsonSerializer.Serialize(new
        {
            command_type = commandType,
            @params = paramsObj
        });
    }

    /// <summary>
    /// 反序列化网络命令（需要 World 查询旧值用于 Undo）
    /// </summary>
    public static IWorldCommand Deserialize(string commandType, JsonElement paramsJson, World world)
    {
        return commandType switch
        {
            "AddObject" => DeserializeAddObject(paramsJson),
            "DeleteObject" => DeserializeDeleteObject(paramsJson),
            "MoveObject" => DeserializeMoveObject(paramsJson, world),
            "SetProperty" => DeserializeSetProperty(paramsJson),
            "AddComponent" => DeserializeAddComponent(paramsJson),
            "RemoveComponent" => DeserializeRemoveComponent(paramsJson),
            "Rename" => DeserializeRename(paramsJson),
            "UpdateGraphNode" => DeserializeUpdateGraphNode(paramsJson),
            "UpdateGraphLink" => DeserializeUpdateGraphLink(paramsJson),
            "AddGraphLink" => DeserializeAddGraphLink(paramsJson),
            "RemoveGraphLink" => DeserializeRemoveGraphLink(paramsJson),
            _ => throw new NotSupportedException($"Unknown command type: {commandType}")
        };
    }

    /// <summary>GraphLink 的完整线格式。AddGraphLink 正向/逆向共用，避免两处字段不一致。</summary>
    private static object GraphLinkPayload(Guid objectId, GraphLinkComponent link) => new
    {
        object_id = objectId.ToString(),
        link_id = link.LinkId,
        target_node_id = link.TargetNodeId,
        kind = (int)link.Kind,
        is_bidirectional = link.IsBidirectional,
        label = link.Label,
        visibility = (int)link.Visibility,
        is_passable = link.IsPassable,
        cost = link.Cost,
        color = link.Color,
        width = link.Width,
        stroke_style = (int)link.StrokeStyle
    };

    private static WorldRenameCommand DeserializeRename(JsonElement json)
    {
        var objectId = Guid.Parse(json.GetProperty("object_id").GetString()!);
        var name = json.GetProperty("name").GetString() ?? "";
        return new WorldRenameCommand(objectId, name);
    }

    private static WorldUpdateGraphNodeCommand DeserializeUpdateGraphNode(JsonElement json)
    {
        var objectId = Guid.Parse(json.GetProperty("object_id").GetString()!);
        var property = json.GetProperty("property").GetString()!;
        var value = ReadLooseValue(json.GetProperty("value"));
        return new WorldUpdateGraphNodeCommand(objectId, property, value);
    }

    private static WorldUpdateGraphLinkCommand DeserializeUpdateGraphLink(JsonElement json)
    {
        var objectId = Guid.Parse(json.GetProperty("object_id").GetString()!);
        var linkId = json.GetProperty("link_id").GetString()!;
        var property = json.GetProperty("property").GetString()!;
        var value = ReadLooseValue(json.GetProperty("value"));
        return new WorldUpdateGraphLinkCommand(objectId, linkId, property, value);
    }

    private static WorldAddGraphLinkCommand DeserializeAddGraphLink(JsonElement json)
    {
        var objectId = Guid.Parse(json.GetProperty("object_id").GetString()!);
        var link = new GraphLinkComponent
        {
            // LinkId 由发起端决定，收到后必须沿用，否则两端边 ID 不一致后续更新会失配
            LinkId = json.TryGetProperty("link_id", out var lid) && lid.GetString() is { Length: > 0 } s
                ? s : Guid.NewGuid().ToString("N"),
            TargetNodeId = json.GetProperty("target_node_id").GetString() ?? "",
            Kind = (GraphLinkKind)ReadInt(json, "kind"),
            IsBidirectional = ReadBool(json, "is_bidirectional", true),
            Label = json.TryGetProperty("label", out var lb) ? lb.GetString() ?? "" : "",
            Visibility = (GraphVisibility)ReadInt(json, "visibility"),
            IsPassable = ReadBool(json, "is_passable", true),
            Cost = json.TryGetProperty("cost", out var c) && c.TryGetDouble(out var cv) ? cv : 1,
            Color = json.TryGetProperty("color", out var col) ? col.GetString() ?? "#8A8F98" : "#8A8F98",
            Width = json.TryGetProperty("width", out var w) && w.TryGetDouble(out var wv) ? wv : 2,
            StrokeStyle = (StrokeStyle)ReadInt(json, "stroke_style")
        };
        return new WorldAddGraphLinkCommand(objectId, link);
    }

    private static WorldRemoveGraphLinkCommand DeserializeRemoveGraphLink(JsonElement json)
    {
        var objectId = Guid.Parse(json.GetProperty("object_id").GetString()!);
        var linkId = json.GetProperty("link_id").GetString()!;
        return new WorldRemoveGraphLinkCommand(objectId, linkId);
    }

    private static int ReadInt(JsonElement json, string name)
        => json.TryGetProperty(name, out var el) && el.TryGetInt32(out var v) ? v : 0;

    private static bool ReadBool(JsonElement json, string name, bool fallback)
        => json.TryGetProperty(name, out var el)
           && el.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? el.GetBoolean() : fallback;

    private static object? ReadLooseValue(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Number => value.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        _ => value.ToString()
    };

    private static WorldAddObjectCommand DeserializeAddObject(JsonElement json)
    {
        var objectId = Guid.Parse(json.GetProperty("object_id").GetString()!);
        var parentId = json.TryGetProperty("parent_id", out var parentIdElem) && parentIdElem.ValueKind == JsonValueKind.String
            ? Guid.Parse(parentIdElem.GetString()!)
            : (Guid?)null;
        var name = json.GetProperty("name").GetString()!;
        var type = json.GetProperty("type").GetString()!;

        var transform = new TransformComponent
        {
            X = json.GetProperty("x").GetDouble(),
            Y = json.GetProperty("y").GetDouble(),
            Rotation = json.TryGetProperty("rotation", out var rot) ? rot.GetDouble() : 0,
            ScaleX = json.TryGetProperty("scale_x", out var sx) ? sx.GetDouble() : 1,
            ScaleY = json.TryGetProperty("scale_y", out var sy) ? sy.GetDouble() : 1
        };

        return new WorldAddObjectCommand(objectId, parentId, name, type, transform, new SpriteRendererComponent());
    }

    private static WorldDeleteObjectCommand DeserializeDeleteObject(JsonElement json)
    {
        var objectId = Guid.Parse(json.GetProperty("object_id").GetString()!);
        return new WorldDeleteObjectCommand(objectId);
    }

    private static WorldMoveObjectCommand DeserializeMoveObject(JsonElement json, World world)
    {
        var objectId = Guid.Parse(json.GetProperty("object_id").GetString()!);
        var newX = json.GetProperty("x").GetDouble();
        var newY = json.GetProperty("y").GetDouble();

        // 查询当前位置作为 oldX/oldY（用于 Undo）
        var obj = world.FindById(objectId);
        var tf = obj?.GetComponent<TransformComponent>();
        var oldX = tf?.X ?? 0;
        var oldY = tf?.Y ?? 0;

        return new WorldMoveObjectCommand(objectId, oldX, oldY, newX, newY);
    }

    private static WorldSetPropertyCommand DeserializeSetProperty(JsonElement json)
    {
        var objectId = Guid.Parse(json.GetProperty("object_id").GetString()!);
        var property = json.GetProperty("property").GetString()!;
        var value = json.GetProperty("value");

        object? parsedValue = value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => value.ToString()
        };

        return new WorldSetPropertyCommand(objectId, property, parsedValue);
    }

    private static WorldAddComponentCommand DeserializeAddComponent(JsonElement json)
    {
        var objectId = Guid.Parse(json.GetProperty("object_id").GetString()!);
        var componentType = json.GetProperty("component_type").GetString()!;
        return new WorldAddComponentCommand(objectId, componentType);
    }

    private static WorldRemoveComponentCommand DeserializeRemoveComponent(JsonElement json)
    {
        var objectId = Guid.Parse(json.GetProperty("object_id").GetString()!);
        var componentType = json.GetProperty("component_type").GetString()!;
        return new WorldRemoveComponentCommand(objectId, componentType);
    }
}
