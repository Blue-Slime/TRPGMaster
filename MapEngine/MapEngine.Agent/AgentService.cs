using System.Text.Json;
using MapEngine.Core;
using MapEngine.Core.Commands;
using MapEngine.Core.Components;

namespace MapEngine.Agent;

/// <summary>
/// Agent 服务入口。不含网络传输（由 TRPGMaster 主程序负责）。
/// 提供两种使用方式：
/// 1. 直接调用 ProcessMessage(json) — 主程序 in-proc 调用
/// 2. stdio 模式 — 本地调试用，从 stdin 读 JSON-RPC，写 stdout
/// </summary>
public sealed class AgentService
{
    private readonly AgentToolRegistry _registry = new();
    private readonly CommandBus _commandBus;

    public AgentService(CommandBus commandBus)
    {
        _commandBus = commandBus;
        RegisterTools();
    }

    private void RegisterTools()
    {
        _registry.Register(new MapTool("map_add_object", "添加地图对象", AddObject));
        _registry.Register(new MapTool("map_delete_object", "删除地图对象", DeleteObject));
        _registry.Register(new MapTool("map_move_object", "移动地图对象", MoveObject));
        _registry.Register(new MapTool("map_set_property", "设置对象属性", SetProperty));
        _registry.Register(new MapTool("map_add_component", "为对象添加组件", AddComponent));
        _registry.Register(new MapTool("map_query_objects", "查询地图对象", QueryObjects));
    }

    private object AddObject(Dictionary<string, object> args)
    {
        var name = (string)args["name"];
        var type = args.GetValueOrDefault("type", "Empty") as string ?? "Empty";
        var x = Convert.ToDouble(args["x"]);
        var y = Convert.ToDouble(args["y"]);
        var parentId = args.ContainsKey("parent_id")
            ? Guid.Parse((string)args["parent_id"])
            : (Guid?)null;

        var objectId = Guid.NewGuid();
        var cmd = new WorldAddObjectCommand(
            objectId, parentId, name, type,
            new TransformComponent { X = x, Y = y },
            new SpriteRendererComponent()
        );

        _commandBus.Execute(cmd);

        return new { success = true, object_id = objectId.ToString() };
    }

    private object DeleteObject(Dictionary<string, object> args)
    {
        var objectId = Guid.Parse((string)args["object_id"]);
        var cmd = new WorldDeleteObjectCommand(objectId);
        _commandBus.Execute(cmd);
        return new { success = true };
    }

    private object MoveObject(Dictionary<string, object> args)
    {
        var objectId = Guid.Parse((string)args["object_id"]);
        var x = Convert.ToDouble(args["x"]);
        var y = Convert.ToDouble(args["y"]);

        var world = _commandBus.GetWorld();
        var obj = world.FindById(objectId);
        if (obj?.GetComponent<TransformComponent>() is not { } tf)
            return new { success = false, error = "Object not found or has no Transform" };

        var cmd = new WorldMoveObjectCommand(objectId, tf.X, tf.Y, x, y);
        _commandBus.Execute(cmd);
        return new { success = true };
    }

    private object SetProperty(Dictionary<string, object> args)
    {
        var objectId = Guid.Parse((string)args["object_id"]);
        var property = (string)args["property"];
        var value = args["value"];

        var cmd = new WorldSetPropertyCommand(objectId, property, value);
        _commandBus.Execute(cmd);
        return new { success = true };
    }

    private object AddComponent(Dictionary<string, object> args)
    {
        var objectId = Guid.Parse((string)args["object_id"]);
        var componentType = (string)args["component_type"];

        var cmd = new WorldAddComponentCommand(objectId, componentType);
        _commandBus.Execute(cmd);
        return new { success = true };
    }

    private object QueryObjects(Dictionary<string, object> args)
    {
        // 只读查询，直接读 World，不走命令
        var world = _commandBus.GetWorld();
        var filter = args.GetValueOrDefault("filter") as string;

        var results = world.AllObjects()
            .Where(obj => filter == null || obj.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .Select(obj => new
            {
                id = obj.Id.ToString(),
                name = obj.Name,
                type = obj.ObjectType,
                x = obj.GetComponent<TransformComponent>()?.X,
                y = obj.GetComponent<TransformComponent>()?.Y,
                components = obj.Components.Select(c => c.TypeName).ToArray()
            })
            .ToArray();

        return new { objects = results };
    }

    /// <summary>
    /// 处理一条 JSON-RPC 消息，返回响应 JSON。
    /// TRPGMaster 主程序通过网络收到消息后直接调用此方法。
    /// </summary>
    public string ProcessMessage(string jsonMessage)
    {
        var request = JsonRpcSerializer.ParseRequest(jsonMessage);
        if (request is null)
        {
            var error = JsonRpcResponse.Fail(null, RpcErrorCodes.ParseError, "Invalid JSON");
            return JsonRpcSerializer.Serialize(error);
        }

        var response = _registry.Dispatch(request);
        return JsonRpcSerializer.Serialize(response);
    }

    /// <summary>
    /// stdio 调试模式：从 stdin 逐行读取 JSON-RPC 请求，处理后写入 stdout。
    /// 用于本地开发调试，不用于生产。
    /// </summary>
    public async Task RunStdioLoop(CancellationToken cancellation = default)
    {
        using var reader = new StreamReader(Console.OpenStandardInput());
        using var writer = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };

        while (!cancellation.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellation);
            if (line is null) break;
            if (string.IsNullOrWhiteSpace(line)) continue;

            var response = ProcessMessage(line);
            await writer.WriteLineAsync(response);
        }
    }

    /// <summary>简单工具适配器：将 Func 包装为 IAgentTool</summary>
    private sealed class MapTool : IAgentTool
    {
        private readonly string _name;
        private readonly string _description;
        private readonly Func<Dictionary<string, object>, object> _handler;

        public MapTool(string name, string description, Func<Dictionary<string, object>, object> handler)
        {
            _name = name;
            _description = description;
            _handler = handler;
        }

        public string Name => _name;
        public string Description => _description;

        public JsonRpcResponse Execute(JsonElement? parameters)
        {
            try
            {
                var args = new Dictionary<string, object>();
                if (parameters.HasValue && parameters.Value.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in parameters.Value.EnumerateObject())
                    {
                        args[prop.Name] = prop.Value.ValueKind switch
                        {
                            JsonValueKind.String => prop.Value.GetString()!,
                            JsonValueKind.Number => prop.Value.GetDouble(),
                            JsonValueKind.True => true,
                            JsonValueKind.False => false,
                            _ => prop.Value.ToString()
                        };
                    }
                }

                var result = _handler(args);
                return JsonRpcResponse.Success(null, result);
            }
            catch (Exception ex)
            {
                return JsonRpcResponse.Fail(null, RpcErrorCodes.InternalError, ex.Message);
            }
        }
    }
}
