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
            _ => throw new NotSupportedException($"Unknown command type: {commandType}")
        };
    }

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
