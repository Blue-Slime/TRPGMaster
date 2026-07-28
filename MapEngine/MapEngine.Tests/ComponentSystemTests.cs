using MapEngine.Core.Components;
using MapEngine.Avalonia.Services;
using Xunit;

namespace MapEditor.Tests;

public class ComponentSystemTests
{
    [Fact]
    public void GameObject_AddComponent_AttachesOwner()
    {
        var go = new GameObject { Name = "Test" };
        var transform = go.AddComponent(new TransformComponent { X = 10, Y = 20 });

        Assert.Same(go, transform.Owner);
        Assert.Single(go.Components);
    }

    [Fact]
    public void GameObject_GetComponent_ReturnsCorrectType()
    {
        var go = new GameObject { Name = "Test" };
        go.AddComponent(new TransformComponent { X = 5 });
        go.AddComponent(new SpriteRendererComponent { Opacity = 0.5 });

        var t = go.GetComponent<TransformComponent>();
        var s = go.GetComponent<SpriteRendererComponent>();
        var v = go.GetComponent<VisionComponent>();

        Assert.NotNull(t);
        Assert.Equal(5.0, t!.X);
        Assert.NotNull(s);
        Assert.Equal(0.5, s!.Opacity);
        Assert.Null(v);
    }

    [Fact]
    public void GameObject_RemoveComponent_DetachesOwner()
    {
        var go = new GameObject { Name = "Test" };
        var t = go.AddComponent(new TransformComponent());

        var removed = go.RemoveComponent<TransformComponent>();

        Assert.True(removed);
        Assert.Null(t.Owner);
        Assert.Empty(go.Components);
    }

    [Fact]
    public void GameObject_Clone_DeepCopiesComponentsAndChildren()
    {
        var parent = new GameObject { Name = "Parent" };
        parent.AddComponent(new TransformComponent { X = 1, Y = 2 });

        var child = new GameObject { Name = "Child" };
        child.AddComponent(new TokenComponent { TokenName = "Goblin" });
        child.Parent = parent;
        parent.Children.Add(child);

        var clone = parent.Clone();

        Assert.NotEqual(parent.Id, clone.Id);
        Assert.Equal("Parent", clone.Name);
        Assert.Single(clone.Components);
        Assert.Equal(1.0, clone.GetComponent<TransformComponent>()!.X);

        Assert.Single(clone.Children);
        var clonedChild = clone.Children[0];
        Assert.NotEqual(child.Id, clonedChild.Id);
        Assert.Equal("Goblin", clonedChild.GetComponent<TokenComponent>()!.TokenName);
        Assert.Same(clone, clonedChild.Parent);
    }

    [Fact]
    public void SceneSerializer_RoundTrip_PreservesData()
    {
        var go = new GameObject { Name = "Hero", ObjectType = "Token", Tags = ["player", "warrior"] };
        go.AddComponent(new TransformComponent { X = 100, Y = -50, ScaleX = 2 });
        go.AddComponent(new SpriteRendererComponent { Opacity = 0.8, SourceAssetPath = "/assets/hero.png" });
        go.AddComponent(new VisionComponent { Enabled = true, Radius = 120 });
        go.AddComponent(new TokenComponent { TokenName = "Aragorn", MovementSpeed = 30 });

        var doc = SceneSerializer.ToDocument([go]);
        var json = SceneSerializer.Serialize(doc);
        var restored = SceneSerializer.Deserialize(json);
        Assert.NotNull(restored);

        var objects = SceneSerializer.FromDocument(restored!);
        Assert.Single(objects);

        var r = objects[0];
        Assert.Equal("Hero", r.Name);
        Assert.Equal("Token", r.ObjectType);
        Assert.Contains("player", r.Tags);

        var t = r.GetComponent<TransformComponent>()!;
        Assert.Equal(100.0, t.X);
        Assert.Equal(-50.0, t.Y);
        Assert.Equal(2.0, t.ScaleX);

        var s = r.GetComponent<SpriteRendererComponent>()!;
        Assert.Equal(0.8, s.Opacity);
        Assert.Equal("/assets/hero.png", s.SourceAssetPath);

        var v = r.GetComponent<VisionComponent>()!;
        Assert.True(v.Enabled);
        Assert.Equal(120.0, v.Radius);

        var tk = r.GetComponent<TokenComponent>()!;
        Assert.Equal("Aragorn", tk.TokenName);
        Assert.Equal(30.0, tk.MovementSpeed);
    }

    [Fact]
    public void LegacyMigrator_RoundTrip_PreservesData()
    {
        var dto = new HierarchyNodeDto
        {
            Id = "test-node-1",
            Name = "Goblin",
            Icon = "👹",
            ObjectType = "Token",
            X = 50,
            Y = -30,
            ScaleX = 1.5,
            ScaleY = 1.5,
            Opacity = 0.9,
            SpriteColor = "#00FF00",
            SourceAssetPath = "/assets/goblin.asset",
            SourceAssetKind = "StaticObjectClass",
            SourceAssetName = "Goblin",
            VisionEnabled = true,
            VisionRadius = 60,
            HasMapPosition = true,
            Tags = ["enemy", "npc"]
        };

        var go = LegacyMigrator.FromHierarchyNodeDto(dto);

        Assert.Equal("Goblin", go.Name);
        Assert.Equal("👹", go.Icon);

        var t = go.GetComponent<TransformComponent>()!;
        Assert.Equal(50.0, t.X);
        Assert.Equal(-30.0, t.Y);
        Assert.True(t.HasMapPosition);

        var s = go.GetComponent<SpriteRendererComponent>()!;
        Assert.Equal("#00FF00", s.Color);
        Assert.Equal(0.9, s.Opacity);

        var v = go.GetComponent<VisionComponent>()!;
        Assert.True(v.Enabled);
        Assert.Equal(60.0, v.Radius);

        var backDto = LegacyMigrator.ToHierarchyNodeDto(go);
        Assert.Equal("Goblin", backDto.Name);
        Assert.Equal(50.0, backDto.X);
        Assert.Equal("#00FF00", backDto.SpriteColor);
        Assert.True(backDto.VisionEnabled);
        Assert.Contains("enemy", backDto.Tags);
    }
}
