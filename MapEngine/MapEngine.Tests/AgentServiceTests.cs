using MapEngine.Agent;
using MapEngine.Core;
using MapEngine.Core.Commands;
using MapEngine.Core.Components;
using Xunit;

namespace MapEditor.Tests;

public class AgentServiceTests
{
    private World _world = null!;
    private CommandBus _commandBus = null!;
    private AgentService _agentService = null!;

    public AgentServiceTests()
    {
        _world = new World();
        _commandBus = new CommandBus(_world);
        _agentService = new AgentService(_commandBus);
    }

    [Fact]
    public void MapAddObject_CreatesObjectInWorld()
    {
        var request = """
        {
            "jsonrpc": "2.0",
            "id": 1,
            "method": "map_add_object",
            "params": {
                "name": "TestWall",
                "type": "Wall",
                "x": 100,
                "y": 200
            }
        }
        """;

        var response = _agentService.ProcessMessage(request);

        Assert.Contains("\"success\":true", response);
        Assert.Contains("object_id", response);
        Assert.Single(_world.AllObjects());

        var obj = _world.AllObjects().First();
        Assert.Equal("TestWall", obj.Name);
        Assert.Equal("Wall", obj.ObjectType);

        var tf = obj.GetComponent<TransformComponent>();
        Assert.NotNull(tf);
        Assert.Equal(100, tf!.X);
        Assert.Equal(200, tf.Y);
    }

    [Fact]
    public void MapQueryObjects_ReturnsAllObjects()
    {
        // 添加测试对象
        var addCmd1 = new WorldAddObjectCommand(
            Guid.NewGuid(), null, "Object1", "Prop",
            new TransformComponent { X = 10, Y = 20 },
            new SpriteRendererComponent()
        );
        var addCmd2 = new WorldAddObjectCommand(
            Guid.NewGuid(), null, "Object2", "Token",
            new TransformComponent { X = 30, Y = 40 },
            new SpriteRendererComponent()
        );
        _commandBus.Execute(addCmd1);
        _commandBus.Execute(addCmd2);

        var request = """
        {
            "jsonrpc": "2.0",
            "id": 2,
            "method": "map_query_objects",
            "params": {}
        }
        """;

        var response = _agentService.ProcessMessage(request);

        Assert.Contains("Object1", response);
        Assert.Contains("Object2", response);
        Assert.Contains("\"objects\"", response);
    }

    [Fact]
    public void MapQueryObjects_WithFilter_ReturnsFilteredObjects()
    {
        var addCmd1 = new WorldAddObjectCommand(
            Guid.NewGuid(), null, "Wall1", "Wall",
            new TransformComponent { X = 10, Y = 20 },
            new SpriteRendererComponent()
        );
        var addCmd2 = new WorldAddObjectCommand(
            Guid.NewGuid(), null, "Door1", "Door",
            new TransformComponent { X = 30, Y = 40 },
            new SpriteRendererComponent()
        );
        _commandBus.Execute(addCmd1);
        _commandBus.Execute(addCmd2);

        var request = """
        {
            "jsonrpc": "2.0",
            "id": 3,
            "method": "map_query_objects",
            "params": {
                "filter": "Wall"
            }
        }
        """;

        var response = _agentService.ProcessMessage(request);

        Assert.Contains("Wall1", response);
        Assert.DoesNotContain("Door1", response);
    }

    [Fact]
    public void MapMoveObject_UpdatesObjectPosition()
    {
        var objectId = Guid.NewGuid();
        var addCmd = new WorldAddObjectCommand(
            objectId, null, "MovableObject", "Prop",
            new TransformComponent { X = 10, Y = 20 },
            new SpriteRendererComponent()
        );
        _commandBus.Execute(addCmd);

        var request = $$"""
        {
            "jsonrpc": "2.0",
            "id": 4,
            "method": "map_move_object",
            "params": {
                "object_id": "{{objectId}}",
                "x": 50,
                "y": 60
            }
        }
        """;

        var response = _agentService.ProcessMessage(request);

        Assert.Contains("\"success\":true", response);

        var obj = _world.FindById(objectId);
        var tf = obj!.GetComponent<TransformComponent>();
        Assert.Equal(50, tf!.X);
        Assert.Equal(60, tf.Y);
    }

    [Fact]
    public void MapSetProperty_UpdatesObjectProperty()
    {
        var objectId = Guid.NewGuid();
        var addCmd = new WorldAddObjectCommand(
            objectId, null, "TestObject", "Prop",
            new TransformComponent { X = 10, Y = 20 },
            new SpriteRendererComponent()
        );
        _commandBus.Execute(addCmd);

        var request = $$"""
        {
            "jsonrpc": "2.0",
            "id": 5,
            "method": "map_set_property",
            "params": {
                "object_id": "{{objectId}}",
                "property": "Name",
                "value": "RenamedObject"
            }
        }
        """;

        var response = _agentService.ProcessMessage(request);

        Assert.Contains("\"success\":true", response);

        var obj = _world.FindById(objectId);
        Assert.Equal("RenamedObject", obj!.Name);
    }

    [Fact]
    public void MapAddComponent_AddsComponentToObject()
    {
        var objectId = Guid.NewGuid();
        var addCmd = new WorldAddObjectCommand(
            objectId, null, "TestObject", "Prop",
            new TransformComponent { X = 10, Y = 20 },
            new SpriteRendererComponent()
        );
        _commandBus.Execute(addCmd);

        var request = $$"""
        {
            "jsonrpc": "2.0",
            "id": 6,
            "method": "map_add_component",
            "params": {
                "object_id": "{{objectId}}",
                "component_type": "Vision"
            }
        }
        """;

        var response = _agentService.ProcessMessage(request);

        Assert.Contains("\"success\":true", response);

        var obj = _world.FindById(objectId);
        var vision = obj!.GetComponent<VisionComponent>();
        Assert.NotNull(vision);
    }

    [Fact]
    public void MapDeleteObject_RemovesObjectFromWorld()
    {
        var objectId = Guid.NewGuid();
        var addCmd = new WorldAddObjectCommand(
            objectId, null, "ToDelete", "Prop",
            new TransformComponent { X = 10, Y = 20 },
            new SpriteRendererComponent()
        );
        _commandBus.Execute(addCmd);

        Assert.Single(_world.AllObjects());

        var request = $$"""
        {
            "jsonrpc": "2.0",
            "id": 7,
            "method": "map_delete_object",
            "params": {
                "object_id": "{{objectId}}"
            }
        }
        """;

        var response = _agentService.ProcessMessage(request);

        Assert.Contains("\"success\":true", response);
        Assert.Empty(_world.AllObjects());
    }

    [Fact]
    public void UndoRedo_WorksWithAgentCommands()
    {
        var request = """
        {
            "jsonrpc": "2.0",
            "id": 8,
            "method": "map_add_object",
            "params": {
                "name": "UndoTest",
                "type": "Prop",
                "x": 100,
                "y": 200
            }
        }
        """;

        _agentService.ProcessMessage(request);
        Assert.Single(_world.AllObjects());

        _commandBus.Undo();
        Assert.Empty(_world.AllObjects());

        _commandBus.Redo();
        Assert.Single(_world.AllObjects());
    }
}
