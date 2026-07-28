using MapEngine.Core;
using MapEngine.Core.Commands;
using MapEngine.Core.Components;
using MapEngine.Core.Networking;
using System.Text.Json;
using Xunit;

namespace MapEditor.Tests;

/// <summary>
/// 地图同步架构集成测试
/// 覆盖：序列化/逆序列化、乐观更新、回声过滤、远程命令不入栈、Undo 同步
/// </summary>
public class MapNetworkingTests
{
    // ─────────────────────────────────────────────
    //  CommandSerializer
    // ─────────────────────────────────────────────

    [Fact]
    public void CommandSerializer_SerializesAddObjectCommand()
    {
        var cmd = new WorldAddObjectCommand(
            Guid.NewGuid(), null, "Wall1", "Wall",
            new TransformComponent { X = 100, Y = 200, Rotation = 45, ScaleX = 1.5, ScaleY = 2 },
            new SpriteRendererComponent()
        );

        var json = CommandSerializer.Serialize(cmd);

        Assert.Contains("\"command_type\":\"AddObject\"", json);
        Assert.Contains("Wall1", json);
        Assert.Contains("\"x\":100", json);
        Assert.Contains("\"y\":200", json);
    }

    [Fact]
    public void CommandSerializer_SerializesMoveObjectCommand()
    {
        var cmd = new WorldMoveObjectCommand(Guid.NewGuid(), 10, 20, 100, 200);
        var json = CommandSerializer.Serialize(cmd);

        Assert.Contains("\"command_type\":\"MoveObject\"", json);
        Assert.Contains("\"x\":100", json);
        Assert.Contains("\"y\":200", json);
    }

    [Fact]
    public void CommandSerializer_SerializesInverse_MoveObject()
    {
        var id = Guid.NewGuid();
        var cmd = new WorldMoveObjectCommand(id, 10, 20, 100, 200);

        // MoveObject 逆命令不需要 Execute 前置，OldX/OldY 在构造时已知
        var inverseJson = CommandSerializer.SerializeInverse(cmd);

        Assert.Contains("\"command_type\":\"MoveObject\"", inverseJson);
        // 逆操作目标坐标应为 OldX/OldY
        Assert.Contains("\"x\":10", inverseJson);
        Assert.Contains("\"y\":20", inverseJson);
    }

    [Fact]
    public void CommandSerializer_SerializesInverse_AddObject_IsDeleteObject()
    {
        var id = Guid.NewGuid();
        var cmd = new WorldAddObjectCommand(
            id, null, "Wall", "Wall",
            new TransformComponent(), new SpriteRendererComponent()
        );

        var inverseJson = CommandSerializer.SerializeInverse(cmd);

        Assert.Contains("\"command_type\":\"DeleteObject\"", inverseJson);
        Assert.Contains(id.ToString(), inverseJson);
    }

    [Fact]
    public void CommandSerializer_SerializesInverse_DeleteObject_IsAddObject()
    {
        var world = new World();
        var id = Guid.NewGuid();
        var addCmd = new WorldAddObjectCommand(
            id, null, "Token", "Token",
            new TransformComponent { X = 50, Y = 60 },
            new SpriteRendererComponent()
        );
        addCmd.Execute(world);

        var deleteCmd = new WorldDeleteObjectCommand(id);
        deleteCmd.Execute(world); // 执行后 Snapshot 填充

        var inverseJson = CommandSerializer.SerializeInverse(deleteCmd);

        Assert.Contains("\"command_type\":\"AddObject\"", inverseJson);
        Assert.Contains("Token", inverseJson);
        Assert.Contains("\"x\":50", inverseJson);
    }

    [Fact]
    public void CommandSerializer_DeserializesAddObjectCommand()
    {
        var world = new World();
        var json = JsonDocument.Parse("""
        {
            "object_id": "12345678-1234-1234-1234-123456789abc",
            "name": "TestWall",
            "type": "Wall",
            "x": 50,
            "y": 60
        }
        """);

        var cmd = CommandSerializer.Deserialize("AddObject", json.RootElement, world);

        var addCmd = Assert.IsType<WorldAddObjectCommand>(cmd);
        Assert.Equal("TestWall", addCmd.Name);
        Assert.Equal(50, addCmd.Transform.X);
        Assert.Equal(60, addCmd.Transform.Y);
    }

    [Fact]
    public void CommandSerializer_DeserializesMoveObject_QueriesOldPosition()
    {
        var world = new World();
        var id = Guid.NewGuid();

        // 先在 world 里创建对象在 (10, 20)
        var addCmd = new WorldAddObjectCommand(
            id, null, "Mover", "Token",
            new TransformComponent { X = 10, Y = 20 },
            new SpriteRendererComponent()
        );
        addCmd.Execute(world);

        var json = JsonDocument.Parse($$"""
        {
            "object_id": "{{id}}",
            "x": 100,
            "y": 200
        }
        """);

        var cmd = CommandSerializer.Deserialize("MoveObject", json.RootElement, world);

        var moveCmd = Assert.IsType<WorldMoveObjectCommand>(cmd);
        Assert.Equal(10, moveCmd.OldX);
        Assert.Equal(20, moveCmd.OldY);
        Assert.Equal(100, moveCmd.NewX);
        Assert.Equal(200, moveCmd.NewY);
    }

    // ─────────────────────────────────────────────
    //  服务端：MapSyncHandler
    // ─────────────────────────────────────────────

    [Fact]
    public void MapSyncHandler_ExecutesCommandAndBroadcastsDelta_WithUserId()
    {
        var world = new World();
        var commandBus = new CommandBus(world);
        var handler = new MapSyncHandler(world, commandBus);

        MapStateDelta? broadcastedDelta = null;
        handler.DeltaBroadcast += (s, delta) => broadcastedDelta = delta;

        var request = new MapCommandRequest
        {
            CommandType = "AddObject",
            Params = JsonDocument.Parse("""
            {
                "object_id": "12345678-1234-1234-1234-123456789abc",
                "name": "Wall1",
                "type": "Wall",
                "x": 100,
                "y": 200
            }
            """).RootElement,
            UserId = "user123",
            ExpectedVersion = 0
        };

        var result = handler.HandleCommand(request);

        Assert.True(result.Success);
        Assert.Equal(1, result.NewVersion);
        Assert.NotNull(broadcastedDelta);
        Assert.Equal("AddObject", broadcastedDelta!.CommandType);
        Assert.Equal(1, broadcastedDelta.Version);
        Assert.Equal("user123", broadcastedDelta.UserId);  // UserId 原样转发
        Assert.Single(world.AllObjects());
    }

    [Fact]
    public void MapSyncHandler_ReturnsFullSync()
    {
        var world = new World();
        var commandBus = new CommandBus(world);
        var handler = new MapSyncHandler(world, commandBus);

        var request1 = new MapCommandRequest
        {
            CommandType = "AddObject",
            Params = JsonDocument.Parse($$"""{"object_id":"{{Guid.NewGuid()}}","name":"Obj1","type":"Wall","x":10,"y":20}""").RootElement,
            UserId = "user"
        };
        var request2 = new MapCommandRequest
        {
            CommandType = "AddObject",
            Params = JsonDocument.Parse($$"""{"object_id":"{{Guid.NewGuid()}}","name":"Obj2","type":"Door","x":30,"y":40}""").RootElement,
            UserId = "user"
        };
        handler.HandleCommand(request1);
        handler.HandleCommand(request2);

        var fullSync = handler.HandleFullSyncRequest(new MapFullSyncRequest());

        Assert.Equal(2, fullSync.Document.Objects.Count);
        Assert.Equal(2, fullSync.Version);
    }

    // ─────────────────────────────────────────────
    //  客户端：MapSyncClient
    // ─────────────────────────────────────────────

    [Fact]
    public void MapSyncClient_AppliesRemoteDelta()
    {
        var world = new World();
        var commandBus = new CommandBus(world);
        var client = new MapSyncClient(world, commandBus, "clientA");
        client.SetInitialVersion(0);

        var delta = new MapStateDelta
        {
            CommandType = "AddObject",
            Params = JsonDocument.Parse("""
            {"object_id":"12345678-1234-1234-1234-123456789abc","name":"RemoteWall","type":"Wall","x":50,"y":60}
            """).RootElement,
            Version = 1,
            UserId = "clientB"  // 来自别人
        };

        client.ApplyDelta(delta);

        Assert.Equal(1, client.GetLocalVersion());
        Assert.Single(world.AllObjects());
        Assert.Equal("RemoteWall", world.AllObjects().First().Name);
    }

    [Fact]
    public void MapSyncClient_FiltersEcho_OnlyUpdatesVersion()
    {
        // 回声过滤：自己命令的广播回声只更新版本，不重复执行
        var world = new World();
        var commandBus = new CommandBus(world);
        var client = new MapSyncClient(world, commandBus, "clientA");
        client.SetInitialVersion(0);

        // 模拟 clientA 已本地执行了 AddObject（乐观更新）
        var id = Guid.NewGuid();
        var localCmd = new WorldAddObjectCommand(
            id, null, "LocalWall", "Wall",
            new TransformComponent { X = 10, Y = 20 },
            new SpriteRendererComponent()
        );
        commandBus.Execute(localCmd);  // 本地已执行，world 有 1 个对象

        // 模拟服务端广播回来（同一 UserId）
        var echo = new MapStateDelta
        {
            CommandType = "AddObject",
            Params = JsonDocument.Parse($$"""
            {"object_id":"{{id}}","name":"LocalWall","type":"Wall","x":10,"y":20}
            """).RootElement,
            Version = 1,
            UserId = "clientA"  // 自己的回声
        };

        client.ApplyDelta(echo);

        // 版本号更新，但 world 中对象数量不变（未重复执行）
        Assert.Equal(1, client.GetLocalVersion());
        Assert.Single(world.AllObjects());
    }

    [Fact]
    public void MapSyncClient_RemoteCommand_NotInUndoStack()
    {
        // 远程命令不进入 Undo 栈
        var world = new World();
        var commandBus = new CommandBus(world);
        var client = new MapSyncClient(world, commandBus, "clientA");
        client.SetInitialVersion(0);

        var delta = new MapStateDelta
        {
            CommandType = "AddObject",
            Params = JsonDocument.Parse("""
            {"object_id":"12345678-1234-1234-1234-123456789abc","name":"BsWall","type":"Wall","x":0,"y":0}
            """).RootElement,
            Version = 1,
            UserId = "clientB"
        };

        client.ApplyDelta(delta);

        Assert.Single(world.AllObjects());
        Assert.False(commandBus.CanUndo);  // 远程命令不可 Undo
    }

    [Fact]
    public void MapSyncClient_DetectsVersionMismatch()
    {
        var world = new World();
        var commandBus = new CommandBus(world);
        var client = new MapSyncClient(world, commandBus, "clientA");
        client.SetInitialVersion(0);

        string? errorMessage = null;
        client.ConnectionError += (s, msg) => errorMessage = msg;

        var delta = new MapStateDelta
        {
            CommandType = "AddObject",
            Params = JsonDocument.Parse("""
            {"object_id":"12345678-1234-1234-1234-123456789abc","name":"Wall","type":"Wall","x":0,"y":0}
            """).RootElement,
            Version = 5,  // 跳号
            UserId = "clientB"
        };

        client.ApplyDelta(delta);

        Assert.NotNull(errorMessage);
        Assert.Contains("Version mismatch", errorMessage);
        Assert.Empty(world.AllObjects());  // 命令未被应用
    }

    [Fact]
    public void MapSyncClient_AppliesFullSync_ClearsLocalState()
    {
        var world = new World();
        var commandBus = new CommandBus(world);
        var client = new MapSyncClient(world, commandBus, "clientA");

        // 本地已有对象和 Undo 历史
        var localCmd = new WorldAddObjectCommand(
            Guid.NewGuid(), null, "OldObj", "Wall",
            new TransformComponent(), new SpriteRendererComponent()
        );
        commandBus.Execute(localCmd);
        Assert.True(commandBus.CanUndo);

        // 接收全量同步
        var doc = new SceneDocument
        {
            Objects =
            [
                new GameObjectData
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = "SyncedObject",
                    ObjectType = "Token",
                    Components =
                    [
                        new ComponentData
                        {
                            Type = "Transform",
                            Properties = new Dictionary<string, object?>
                            {
                                ["x"] = 100.0, ["y"] = 200.0, ["z"] = 0.0,
                                ["rotation"] = 0.0, ["scaleX"] = 1.0, ["scaleY"] = 1.0
                            }
                        }
                    ]
                }
            ]
        };

        client.ApplyFullSync(new MapFullSync { Document = doc, Version = 10 });

        Assert.Equal(10, client.GetLocalVersion());
        Assert.Single(world.AllObjects());
        Assert.Equal("SyncedObject", world.AllObjects().First().Name);
        Assert.False(commandBus.CanUndo);  // FullSync 后 Undo 栈被清空
    }

    // ─────────────────────────────────────────────
    //  CommandBus：Undo 联机行为
    // ─────────────────────────────────────────────

    [Fact]
    public void CommandBus_Undo_SendsInverseCommand_WhenNetworkSenderSet()
    {
        var world = new World();
        var commandBus = new CommandBus(world);

        var sentJsons = new List<string>();
        var fakeSender = new FakeNetworkSender(sentJsons);
        commandBus.SetNetworkSender(fakeSender);

        var id = Guid.NewGuid();
        var cmd = new WorldMoveObjectCommand(id, 10, 20, 100, 200);

        // 先创建对象，避免 Execute 找不到对象
        var addCmd = new WorldAddObjectCommand(
            id, null, "Mover", "Token",
            new TransformComponent { X = 10, Y = 20 },
            new SpriteRendererComponent()
        );
        addCmd.Execute(world);

        commandBus.Execute(cmd);   // 发送正向命令
        sentJsons.Clear();          // 清空，只观察 Undo 的发送

        commandBus.Undo();         // Undo → 应发送逆命令

        Assert.Single(sentJsons);
        Assert.Contains("\"command_type\":\"MoveObject\"", sentJsons[0]);
        Assert.Contains("\"x\":10", sentJsons[0]);  // 逆命令目标坐标是 OldX
        Assert.Contains("\"y\":20", sentJsons[0]);
    }

    [Fact]
    public void CommandBus_Undo_LocalOnly_WhenNoNetworkSender()
    {
        // 单机模式：Undo 只影响本地，不发送
        var world = new World();
        var commandBus = new CommandBus(world);
        // 不设置 networkSender

        var id = Guid.NewGuid();
        var addCmd = new WorldAddObjectCommand(
            id, null, "Obj", "Wall",
            new TransformComponent(), new SpriteRendererComponent()
        );
        commandBus.Execute(addCmd);
        Assert.Single(world.AllObjects());

        commandBus.Undo();
        Assert.Empty(world.AllObjects());  // 单机 Undo 正常工作
    }

    [Fact]
    public void CommandBus_ApplyRemote_NotInUndoStack()
    {
        var world = new World();
        var commandBus = new CommandBus(world);

        var remoteCmd = new WorldAddObjectCommand(
            Guid.NewGuid(), null, "RemoteObj", "Wall",
            new TransformComponent(), new SpriteRendererComponent()
        );

        commandBus.ApplyRemote(remoteCmd);

        Assert.Single(world.AllObjects());
        Assert.False(commandBus.CanUndo);  // 远程命令不入 Undo 栈
    }
}

/// <summary>
/// 测试用假网络发送器
/// </summary>
internal sealed class FakeNetworkSender : INetworkSender
{
    private readonly List<string> _sent;

    public FakeNetworkSender(List<string> sent) => _sent = sent;

    public Task SendAsync(string commandJson)
    {
        _sent.Add(commandJson);
        return Task.CompletedTask;
    }
}
