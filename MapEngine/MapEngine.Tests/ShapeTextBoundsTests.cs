using MapEngine.Avalonia.Services;
using MapEngine.Avalonia.ViewModels;
using MapEngine.Core.Components;
using Xunit;

namespace MapEngine.Tests;

/// <summary>
/// Shape/Text 的包围盒必须来自组件几何，而不是格子尺寸（CellSize）。
/// 回归目标：选中框和命中区曾经对所有对象都是 50×50，
/// 导致大锥形/长文本几乎点不中，选中框也贴在中心一小块。
/// </summary>
public class ShapeTextBoundsTests
{
    private const double CellSize = 50.0;   // MapViewportConstants.CellSize

    private static HierarchyItemViewModel BuildItem(string objectType) =>
        new(new HierarchyNodeDto
        {
            Id = $"node-{objectType}",
            Name = objectType,
            Icon = "▭",
            ObjectType = objectType,
            HasMapPosition = true,
            ScaleX = 1,
            ScaleY = 1
        });

    [Fact]
    public void Token_KeepsCellSizedBounds()
    {
        var item = BuildItem("Token");

        Assert.Equal(CellSize, item.SpriteWidth);
        Assert.Equal(CellSize, item.SpriteHeight);
    }

    [Fact]
    public void RectShape_BoundsFollowComponentSize()
    {
        var item = BuildItem("Shape");
        item.AddComponent(new ShapeComponent
        {
            ShapeType = "rect",
            Width = 400,
            Height = 120,
            StrokeWidth = 2
        });

        // 400 + 描边(1) + 常量内边距(2) 各一侧 → 406
        Assert.Equal(406, item.SpriteWidth, 3);
        Assert.Equal(126, item.SpriteHeight, 3);
    }

    [Fact]
    public void ConeShape_BoundsFollowRadius()
    {
        var item = BuildItem("Shape");
        item.AddComponent(new ShapeComponent
        {
            ShapeType = "cone",
            ConeRadius = 600,
            ConeAngle = 30,
            StrokeWidth = 2
        });

        // 半径 600 → 直径 1200（+描边/内边距），远大于旧的 50
        Assert.True(item.SpriteWidth > 1200);
        Assert.True(item.SpriteHeight > 1200);
    }

    [Fact]
    public void FreehandShape_BoundsCoverAllPoints()
    {
        var item = BuildItem("Shape");
        item.AddComponent(new ShapeComponent
        {
            ShapeType = "freehand",
            StrokeWidth = 2,
            Points = [(0, 0), (150, -40), (-90, 220)]
        });

        // 最大 |x| = 150，最大 |y| = 220
        Assert.Equal(306, item.SpriteWidth, 3);
        Assert.Equal(446, item.SpriteHeight, 3);
    }

    [Fact]
    public void Text_BoundsGrowWithFontSizeAndContent()
    {
        var item = BuildItem("Text");
        item.AddComponent(new TextComponent { Text = "宝箱", FontSize = 20 });

        var narrow = item.SpriteWidth;
        var shortHeight = item.SpriteHeight;

        item.TextContent = "宝箱在这块石头后面";
        Assert.True(item.SpriteWidth > narrow);

        item.TextFontSize = 48;
        Assert.True(item.SpriteHeight > shortHeight);
    }

    [Fact]
    public void Text_MultilineGrowsHeightNotWidth()
    {
        var item = BuildItem("Text");
        item.AddComponent(new TextComponent { Text = "第一行", FontSize = 16 });

        var singleWidth = item.SpriteWidth;
        var singleHeight = item.SpriteHeight;

        item.TextContent = "第一行\n第二行\n第三行";

        Assert.Equal(singleWidth, item.SpriteWidth, 3);
        Assert.True(item.SpriteHeight > singleHeight * 2);
    }

    [Fact]
    public void TinyShape_StillHasClickableExtent()
    {
        var item = BuildItem("Shape");
        item.AddComponent(new ShapeComponent { ShapeType = "rect", Width = 1, Height = 1, StrokeWidth = 1 });

        // MinPickExtent = 12 → 至少 24 宽高，否则 1px 的形状无法点选
        Assert.Equal(24, item.SpriteWidth, 3);
        Assert.Equal(24, item.SpriteHeight, 3);
    }
}
