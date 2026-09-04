using MapEngine.Avalonia.Services;
using MapEngine.Avalonia.Graphics;
using MapEngine.Avalonia.ViewModels;
using MapEngine.Core.Components;
using Avalonia;
using Xunit;

namespace MapEditor.Tests;

/// <summary>
/// 精灵图加载显示流程测试（AssetRef 哈希寻址）
/// </summary>
public class SpriteLoadingTests
{
    [Fact]
    public void MapSpriteAssetResolver_ReturnsNull_WhenAssetRefIsEmpty()
    {
        var result = MapSpriteAssetResolver.ResolveSpritePath("");
        Assert.Null(result);
    }

    [Fact]
    public void MapSpriteAssetResolver_ReturnsNull_WhenAssetRefIsNull()
    {
        var result = MapSpriteAssetResolver.ResolveSpritePath(null);
        Assert.Null(result);
    }

    [Fact]
    public void MapSpriteAssetResolver_ReturnsNull_WhenHashNotInLibrary()
    {
        // 不存在的哈希（64 位十六进制），素材库里找不到对应文件
        var result = MapSpriteAssetResolver.ResolveSpritePath(new string('a', 64));
        Assert.Null(result);
    }

    [Fact]
    public void SpriteRendererComponent_HasCorrectDefaults()
    {
        var sprite = new SpriteRendererComponent();

        Assert.Equal(string.Empty, sprite.AssetRef);
        Assert.Equal(string.Empty, sprite.SourceAssetKind);
        Assert.Equal(1.0, sprite.Opacity);
        Assert.Equal(1, sprite.AlignX);
        Assert.Equal(1, sprite.AlignY);
    }

    [Fact]
    public void SpriteRendererComponent_Clone_PreservesAllFields()
    {
        var original = new SpriteRendererComponent
        {
            AssetRef = "e3b0c44298fc1c14",
            SourceAssetKind = "Image",
            Opacity = 0.75,
            AlignX = 0,
            AlignY = 2
        };

        var clone = (SpriteRendererComponent)original.Clone();

        Assert.NotSame(original, clone);
        Assert.Equal(original.AssetRef, clone.AssetRef);
        Assert.Equal(original.SourceAssetKind, clone.SourceAssetKind);
        Assert.Equal(original.Opacity, clone.Opacity);
        Assert.Equal(original.AlignX, clone.AlignX);
        Assert.Equal(original.AlignY, clone.AlignY);
    }

    [Fact]
    public void HierarchyItemViewModel_SpriteProperties_ReflectBackingComponent()
    {
        var dto = new HierarchyNodeDto
        {
            Id = "test-node",
            Name = "TestToken",
            ObjectType = "Token",
            AssetRef = "e3b0c44298fc1c14",
            SourceAssetKind = "Image",
            Opacity = 0.8
        };

        var vm = new HierarchyItemViewModel(dto);

        Assert.Equal("e3b0c44298fc1c14", vm.AssetRef);
        Assert.Equal("Image", vm.SourceAssetKind);
        Assert.Equal(0.8, vm.Opacity);
    }

    [Fact]
    public void MapSceneBuilder_SkipsSprites_WhenAssetRefIsEmpty()
    {
        var mainVm = new MainWindowViewModel();

        // 创建一个没有素材引用的对象
        var item = new HierarchyItemViewModel(new HierarchyNodeDto
        {
            Id = "test-1",
            Name = "NoSprite",
            ObjectType = "Token",
            X = 100,
            Y = 100,
            AssetRef = "",
            SourceAssetKind = "Image"
        });

        mainVm.HierarchyRoots.Add(item);

        // BuildSprites 应该跳过这个对象（因为 spritePath 为 null）
        var scene = MapSceneBuilder.Build(mainVm, new Size(800, 600), new Point(400, 300));

        // 验证精灵列表为空（无有效素材引用）
        Assert.Empty(scene!.Sprites);
    }

    [Fact]
    public void MapSceneBuilder_BuildsScene_WhenAssetRefIsEmpty()
    {
        // 验证无精灵图时场景构建正常
        var mainVm = new MainWindowViewModel();

        var item = new HierarchyItemViewModel(new HierarchyNodeDto
        {
            Id = "test-2",
            Name = "NoSpriteRect",
            ObjectType = "Token",
            X = 50,
            Y = 50,
            SpriteColor = "#FF0000",
            AssetRef = "",
            SourceAssetKind = ""
        });

        mainVm.HierarchyRoots.Add(item);

        var scene = MapSceneBuilder.Build(mainVm, new Size(800, 600), new Point(400, 300));

        // 场景应该成功构建
        Assert.NotNull(scene);
        // 没有有效素材引用时，精灵列表为空
        Assert.Empty(scene!.Sprites);
    }
}
