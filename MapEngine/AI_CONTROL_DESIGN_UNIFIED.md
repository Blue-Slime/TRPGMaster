# MapEngine AI 控制方案设计（统一架构版）

## 核心设计：CommandBus 统一调度，零分支自适应单机/联机

### 架构图

```
┌─────────────┐   ┌─────────────┐   ┌─────────────┐
│ 手动编辑UI  │   │   AI Agent  │   │  远程玩家   │
│ (鼠标/键盘) │   │ (MCP Tools) │   │ (WebSocket) │
└──────┬──────┘   └──────┬──────┘   └──────┬──────┘
       │                 │                 │
       └─────────────────┼─────────────────┘
                         ↓
                 ┌───────────────┐
                 │  CommandBus   │ ← 唯一入口
                 │  .Execute()   │
                 └───────┬───────┘
                         ↓
                 ┌───────────────┐
                 │ IWorldCommand │
                 │  .Execute()   │
                 └───────┬───────┘
                         ↓
                 ┌───────────────┐
                 │     World     │ ← 权威状态
                 │ (GameObject树)│
                 └───────┬───────┘
                         ↓
         ┌───────────────┴───────────────┐
         ↓                               ↓
┌─────────────────┐           ┌─────────────────┐
│ World.Changed   │           │ 联机广播（可选）│
│      事件       │           │ NetworkSender   │
└────────┬────────┘           └────────┬────────┘
         ↓                             ↓
┌─────────────────┐           ┌─────────────────┐
│  ViewModel 同步 │           │  房间内其他人   │
│ (UI 自动刷新)   │           │ (收到后apply)   │
└─────────────────┘           └─────────────────┘
```

### 关键特性

1. **零分支自适应**：
   - 单机模式：`_networkSender == null`，Execute() 跳过网络发送
   - 联机模式：注入 `NetworkSender` 后自动序列化并发送命令
   - **AI/UI 代码完全无感知**，无需 `if (isNetworked)` 判断

2. **乐观更新**：
   - 本地命令立即 Execute(World)，不等服务端确认
   - 降低延迟，用户体验流畅

3. **统一 Undo 栈**：
   - 手动编辑、AI 操作、远程命令都进同一个 Undo 栈
   - 用户可撤销任何操作（包括队友的改动）

---

## 实施代码

### 1. CommandBus 改造

```csharp
// MapEngine.Core/Commands/CommandBus.cs
public sealed class CommandBus
{
    private readonly Stack<IWorldCommand> _undoStack = new();
    private readonly Stack<IWorldCommand> _redoStack = new();
    private readonly World _world;
    private INetworkSender? _networkSender;  // null = 单机模式

    public event EventHandler<CommandExecutedEventArgs>? CommandExecuted;

    public CommandBus(World world)
    {
        _world = world;
    }

    /// <summary>联机时调用此方法注入 NetworkSender，单机时保持 null</summary>
    public void SetNetworkSender(INetworkSender? sender)
    {
        _networkSender = sender;
    }

    /// <summary>本地命令入口（手动编辑/AI）</summary>
    public void Execute(IWorldCommand command)
    {
        // 1. 本地立即执行（乐观更新）
        command.Execute(_world);
        _undoStack.Push(command);
        _redoStack.Clear();

        // 2. 联机环境：序列化并发送（不阻塞）
        if (_networkSender != null)
        {
            var json = CommandSerializer.Serialize(command);
            _ = _networkSender.SendAsync(json);  // fire-and-forget
        }

        // 3. 触发事件
        CommandExecuted?.Invoke(this, new CommandExecutedEventArgs(command, CommandAction.Execute));
    }

    /// <summary>远程命令入口（收到服务端广播时调用）</summary>
    public void ApplyRemote(IWorldCommand command)
    {
        command.Execute(_world);
        _undoStack.Push(command);  // 允许撤销远程操作
        CommandExecuted?.Invoke(this, new CommandExecutedEventArgs(command, CommandAction.RemoteApply));
    }

    public void Undo()
    {
        if (!CanUndo) return;
        
        var command = _undoStack.Pop();
        command.Undo(_world);
        _redoStack.Push(command);

        // 联机环境：发送 Undo 命令
        if (_networkSender != null)
        {
            var json = CommandSerializer.SerializeUndo(command);
            _ = _networkSender.SendAsync(json);
        }

        CommandExecuted?.Invoke(this, new CommandExecutedEventArgs(command, CommandAction.Undo));
    }

    public void Redo()
    {
        if (!CanRedo) return;
        
        var command = _redoStack.Pop();
        command.Execute(_world);
        _undoStack.Push(command);

        if (_networkSender != null)
        {
            var json = CommandSerializer.Serialize(command);
            _ = _networkSender.SendAsync(json);
        }

        CommandExecuted?.Invoke(this, new CommandExecutedEventArgs(command, CommandAction.Redo));
    }

    public bool CanUndo => _undoStack.Count > 0;
    public bool CanRedo => _redoStack.Count > 0;
    public World GetWorld() => _world;  // 供 AI 查询用
}

public interface INetworkSender
{
    Task SendAsync(string commandJson);
}

public enum CommandAction
{
    Execute,
    Undo,
    Redo,
    RemoteApply  // 新增：标记远程命令
}
```

### 2. AI Agent 实现

```csharp
// MapEngine.Agent/AgentService.cs
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
        _registry.RegisterTool("map_add_object", AddObject);
        _registry.RegisterTool("map_delete_object", DeleteObject);
        _registry.RegisterTool("map_move_object", MoveObject);
        _registry.RegisterTool("map_set_property", SetProperty);
        _registry.RegisterTool("map_add_component", AddComponent);
        _registry.RegisterTool("map_query_objects", QueryObjects);
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

        _commandBus.Execute(cmd);  // ← 唯一调用，自动适配单机/联机
        
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
            return new { success = false, error = "Object not found" };

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
            .Where(obj => filter == null || obj.Name.Contains(filter))
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
}
```

### 3. MapEditorInstance 集成

```csharp
// MapEngine.Avalonia/Hosting/MapEditorInstance.cs
internal sealed class MapEditorInstance : IMapEditorInstance
{
    private readonly MainWindowViewModel _viewModel;
    private readonly AgentService _agentService;
    private readonly IMapEditorHost _host;

    public MapEditorInstance(MainWindowViewModel viewModel, IMapEditorHost host)
    {
        _viewModel = viewModel;
        _host = host;
        
        // 注入 CommandBus（ViewModel 持有）
        _agentService = new AgentService(_viewModel.CommandBus);
        
        viewModel.CommandBus.CommandExecuted += OnCommandBusExecuted;
    }

    public Task<string> ProcessMessageAsync(string jsonRpc, CancellationToken ct = default)
    {
        var result = _agentService.ProcessMessage(jsonRpc);
        return Task.FromResult(result);
    }

    // ... 其他方法保持不变
}
```

### 4. 主程序初始化

```csharp
// 单机模式（默认）
var world = new World();
var commandBus = new CommandBus(world);
var viewModel = new MainWindowViewModel(commandBus, world);
// NetworkSender 不注入，commandBus._networkSender == null

// 切换到联机模式（用户点击"加入房间"后）
var ws = await ConnectToRoomAsync(roomId);
var sender = new WebSocketCommandSender(ws);
commandBus.SetNetworkSender(sender);  // ← 一行代码启用联机

// 监听远程命令
ws.OnCommandReceived += (json) =>
{
    var cmd = CommandSerializer.Deserialize(json);
    commandBus.ApplyRemote(cmd);  // 不触发二次发送
};

// 断开联机
commandBus.SetNetworkSender(null);  // ← 一行代码回到单机
```

---

## 对比其他方案

| 方案 | 代码分支 | 单机延迟 | 联机延迟 | 切换成本 | 测试复杂度 |
|------|---------|---------|---------|---------|-----------|
| **统一架构** | ✅ 零分支 | ✅ 0ms | ✅ 乐观更新 | ✅ 一行代码 | ⭐⭐⭐⭐⭐ |
| Hybrid 分支 | ❌ if/else | ✅ 0ms | ✅ 乐观更新 | ⚠️ 需切换 Dispatcher | ⭐⭐⭐ |
| 纯 WebSocket | ❌ 强制联机 | ❌ 10-50ms | ⚠️ 往返等待 | ❌ 无单机模式 | ⭐⭐ |
| 直调 WorldCommands | ❌ 无 Undo | ✅ 0ms | ❌ 无法联机 | N/A | ⭐⭐⭐⭐ |

---

## 实施检查清单

### Phase 1: 核心改造（本周）

#### 1.1 CommandBus 迁移
- [ ] 将 CommandBus 从操作 ISceneState 改为操作 World
- [ ] 添加 `_networkSender` 字段（可选）
- [ ] 实现 `SetNetworkSender()` 方法
- [ ] 实现 `ApplyRemote()` 方法
- [ ] Execute() 中添加自适应网络发送逻辑

#### 1.2 新增 WorldCommand
- [ ] `WorldAddComponentCommand`
- [ ] `WorldRemoveComponentCommand`

```csharp
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

    public void Execute(World world)
    {
        var obj = world.FindById(_objectId);
        if (obj is null) return;

        _component = _componentType switch
        {
            "Vision" => new VisionComponent(),
            "Wall" => new WallComponent(),
            "Token" => new TokenComponent(),
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
```

#### 1.3 MainWindowViewModel 改造
- [ ] 构造函数注入 World
- [ ] 订阅 World.Changed 事件
- [ ] 实现 OnWorldChanged() 刷新 UI

```csharp
private void OnWorldChanged(object? sender, WorldChangedEventArgs e)
{
    if (e.Flags.HasFlag(DirtyFlags.Hierarchy))
        RebuildHierarchy();
    
    if (e.Flags.HasFlag(DirtyFlags.Spatial))
        RefreshSpatialIndex();
    
    if (e.Flags.HasFlag(DirtyFlags.Render))
        InvalidateCanvas();
}
```

### Phase 2: AI Agent 集成（本周）

- [ ] AgentService 构造函数改为接收 CommandBus
- [ ] 实现 6 个 MCP Tools
- [ ] MapEditorInstance 传递 CommandBus
- [ ] 集成测试

```csharp
[Test]
public void AI_AddObject_SinglePlayer()
{
    var world = new World();
    var bus = new CommandBus(world);
    var agent = new AgentService(bus);
    
    var request = JsonRpcSerializer.CreateRequest("map_add_object", new
    {
        name = "Wall1",
        type = "Wall",
        x = 100,
        y = 200
    });
    
    var response = agent.ProcessMessage(request);
    
    Assert.That(world.ObjectCount, Is.EqualTo(1));
}
```

### Phase 3: 联机切换（Step 5-9 完成后）

#### 3.1 序列化层
- [ ] CommandSerializer.Serialize(IWorldCommand)
- [ ] CommandSerializer.Deserialize(string)
- [ ] 支持所有 WorldCommand 类型

#### 3.2 网络层
- [ ] INetworkSender 接口
- [ ] WebSocketCommandSender 实现
- [ ] 服务端命令广播逻辑

#### 3.3 客户端启动流程
- [ ] 加入房间时调用 `commandBus.SetNetworkSender(sender)`
- [ ] 监听 WebSocket 消息并调用 `commandBus.ApplyRemote(cmd)`
- [ ] 离开房间时调用 `commandBus.SetNetworkSender(null)`

---

## 行业方案参考

### Figma（设计协作）
- 本地操作立即生效，后台自动同步
- 单机/联机无差异，连接后透明同步
- **我们的方案最接近 Figma 模式**

### VS Code Live Share（代码协作）
- 乐观更新：不等服务端确认
- 编辑命令序列化后异步发送

### Unity Netcode（游戏联机）
- NetworkVariable 本地改动自动同步
- 开发者无需手动发送网络消息

### Google Docs（文档协作）
- Operational Transformation 解决冲突
- 每个编辑操作可序列化、重放

---

## 总结

### 为什么这是最佳方案

1. **零分支**：`_networkSender == null` 自动跳过网络发送，无需 if/else
2. **透明联机**：注入 NetworkSender 即启用联机，AI/UI 代码无需改动
3. **统一 Undo**：所有操作（手动/AI/远程）进同一个栈
4. **测试简单**：单机测试覆盖核心逻辑
5. **性能最优**：单机零开销，联机乐观更新

### 完整调用链

**手动编辑**：
```
鼠标拖动 → CommandBus.Execute(cmd) → cmd.Execute(world) 
  → [联机时] networkSender.SendAsync(json) 
  → World.Changed 事件 → UI 刷新
```

**AI 操作**：
```
Claude → AgentService.AddObject() → CommandBus.Execute(cmd) 
  → [后续同上]
```

**远程操作**：
```
WebSocket 收到 → CommandBus.ApplyRemote(cmd) → cmd.Execute(world)
  → World.Changed 事件 → UI 刷新（不二次发送）
```
