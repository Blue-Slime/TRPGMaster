using MapEngine.Core;
using MapEngine.Core.Components;
using Xunit;

namespace MapEngine.Tests;

/// <summary>
/// S4 测试: World 序列化/反序列化完整性验证。
/// </summary>
public class WorldSerializationTests
{
    [Fact]
    public void World_SaveToJson_LoadFromJson_PreservesStructure()
    {
        // Arrange: 构建一个含多层级/多组件的 World
        var world = new World();

        var root = new GameObject { Name = "Root" };
        root.AddComponent(new TransformComponent { X = 100, Y = 200, Z = 1 });
        root.AddComponent(new SpriteRendererComponent { Color = "#FF0000", Opacity = 0.8 });
        world.AddObject(root);

        var child = new GameObject { Name = "Child" };
        child.AddComponent(new TransformComponent { X = 150, Y = 250 });
        child.AddComponent(new VisionComponent { Enabled = true, Radius = 60 });
        world.AddObject(child, root);

        var wall = new GameObject { Name = "Wall" };
        wall.AddComponent(new TransformComponent { X = 0, Y = 0 });
        wall.AddComponent(new WallComponent { X1 = 0, Y1 = 0, X2 = 100, Y2 = 0, Thickness = 10 });
        world.AddObject(wall);

        // Act: 序列化 → 反序列化
        var json = world.SaveToJson();
        var restored = World.FromJson(json);

        // Assert: 验证结构
        Assert.Equal(2, restored.Roots.Count); // root + wall
        Assert.Equal(3, restored.ObjectCount); // root + child + wall

        var restoredRoot = restored.Roots.First(r => r.Name == "Root");
        Assert.Single(restoredRoot.Children);
        Assert.Equal("Child", restoredRoot.Children[0].Name);

        var restoredChild = restoredRoot.Children[0];
        var vision = restoredChild.GetComponent<VisionComponent>();
        Assert.NotNull(vision);
        Assert.True(vision.Enabled);
        Assert.Equal(60, vision.Radius);

        var restoredWall = restored.Roots.First(r => r.Name == "Wall");
        var wallComp = restoredWall.GetComponent<WallComponent>();
        Assert.NotNull(wallComp);
        Assert.Equal(100, wallComp.X2);
        Assert.Equal(10, wallComp.Thickness);
    }

    [Fact]
    public void World_LoadFromJson_ClearsExistingWorld()
    {
        // Arrange: 先填充一个 World
        var world = new World();
        var obj1 = new GameObject { Name = "ToBeCleared" };
        world.AddObject(obj1);
        Assert.Equal(1, world.ObjectCount);

        // Act: 加载新的 JSON（只含一个不同的对象）
        var newWorld = new World();
        var obj2 = new GameObject { Name = "NewObject" };
        obj2.AddComponent(new TransformComponent { X = 42 });
        newWorld.AddObject(obj2);
        var json = newWorld.SaveToJson();

        world.LoadFromJson(json);

        // Assert: 旧对象被清除,只剩新对象
        Assert.Equal(1, world.ObjectCount);
        Assert.Equal("NewObject", world.Roots[0].Name);
        Assert.Null(world.FindById(obj1.Id));
    }

    [Fact]
    public void World_SaveToJson_ReturnsValidJson()
    {
        var world = new World();
        var obj = new GameObject { Name = "Test" };
        obj.AddComponent(new TransformComponent());
        world.AddObject(obj);

        var json = world.SaveToJson();

        Assert.NotNull(json);
        Assert.Contains("\"version\":", json.ToLower());
        Assert.Contains("\"objects\":", json.ToLower());
        Assert.Contains("Test", json);
    }
}
