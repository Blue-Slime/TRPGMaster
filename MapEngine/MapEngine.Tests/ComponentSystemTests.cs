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

    [Fact]
    public void SceneSerializer_RoundTrip_PreservesShapeAndText()
    {
        var shapeGo = new GameObject { Name = "Cone", ObjectType = "Shape" };
        shapeGo.AddComponent(new TransformComponent { X = 12, Y = -8 });
        shapeGo.AddComponent(new ShapeComponent
        {
            ShapeType = "cone",
            ConeAngle = 45,
            ConeRadius = 200,
            Rotation = 90,
            StrokeColor = "#FF6B6B",
            FillColor = "#40FF6B6B",
            StrokeWidth = 4,
            IsFilled = false,
            StrokeStyle = StrokeStyle.Dashed,
            Points = [(1, 2), (3, 4)]
        });

        var textGo = new GameObject { Name = "Label", ObjectType = "Text" };
        textGo.AddComponent(new TransformComponent { X = -5, Y = 5 });
        textGo.AddComponent(new TextComponent
        {
            Text = "宝箱在这里",
            FontSize = 28,
            Color = "#FFD43B",
            BackgroundColor = "#00000000",
            IsBold = true,
            IsItalic = true,
            Align = TextAlign.Right
        });

        var json = SceneSerializer.Serialize(SceneSerializer.ToDocument([shapeGo, textGo]));
        var restored = SceneSerializer.FromDocument(SceneSerializer.Deserialize(json)!);

        var shape = restored[0].GetComponent<ShapeComponent>()!;
        Assert.Equal("cone", shape.ShapeType);
        Assert.Equal(45.0, shape.ConeAngle);
        Assert.Equal(200.0, shape.ConeRadius);
        Assert.Equal(90.0, shape.Rotation);
        Assert.Equal("#FF6B6B", shape.StrokeColor);
        Assert.Equal(4.0, shape.StrokeWidth);
        Assert.False(shape.IsFilled);
        Assert.Equal(StrokeStyle.Dashed, shape.StrokeStyle);
        Assert.Equal([(1.0, 2.0), (3.0, 4.0)], shape.Points);

        var text = restored[1].GetComponent<TextComponent>()!;
        Assert.Equal("宝箱在这里", text.Text);
        Assert.Equal(28.0, text.FontSize);
        Assert.Equal("#FFD43B", text.Color);
        Assert.Equal("#00000000", text.BackgroundColor);
        Assert.True(text.IsBold);
        Assert.True(text.IsItalic);
        Assert.Equal(TextAlign.Right, text.Align);
    }

    [Fact]
    public void LegacyMigrator_RoundTrip_PreservesShapeAndText()
    {
        var go = new GameObject { Name = "Blast", ObjectType = "Shape" };
        go.AddComponent(new TransformComponent { X = 3, Y = 4 });
        go.AddComponent(new ShapeComponent { ShapeType = "circle", Width = 88, Points = [(9, 9)] });
        go.AddComponent(new TextComponent { Text = "AoE", FontSize = 12 });

        var dto = LegacyMigrator.ToHierarchyNodeDto(go);
        Assert.NotNull(dto.ShapeV2);
        Assert.NotNull(dto.TextV2);

        var back = LegacyMigrator.FromHierarchyNodeDto(dto);
        var shape = back.GetComponent<ShapeComponent>()!;
        Assert.Equal("circle", shape.ShapeType);
        Assert.Equal(88.0, shape.Width);
        Assert.Equal([(9.0, 9.0)], shape.Points);

        var text = back.GetComponent<TextComponent>()!;
        Assert.Equal("AoE", text.Text);
        Assert.Equal(12.0, text.FontSize);
    }
}
