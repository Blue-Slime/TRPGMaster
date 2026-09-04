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
        go.AddComponent(new SpriteRendererComponent { Opacity = 0.8, AssetRef = "e3b0c44298fc1c14" });
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
        Assert.Equal("e3b0c44298fc1c14", s.AssetRef);

        var v = r.GetComponent<VisionComponent>()!;
        Assert.True(v.Enabled);
        Assert.Equal(120.0, v.Radius);

        var tk = r.GetComponent<TokenComponent>()!;
        Assert.Equal("Aragorn", tk.TokenName);
        Assert.Equal(30.0, tk.MovementSpeed);
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
}
