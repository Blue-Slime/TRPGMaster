using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using MapEngine.Avalonia.Services;
using MapEngine.Avalonia.ViewModels;
using Xunit;

namespace MapEngine.Tests;

/// <summary>
/// Token UI 覆盖层（Hybrid 架构的 Avalonia 层）的定位验证。
/// 重点回归：Canvas.Left/Top 必须落在 Canvas 的直接子元素上，
/// 否则所有 Token UI 会堆叠在原点。
/// </summary>
public class TokenUIOverlayTests
{
    private const double ContentOrigin = 204800 / 2.0; // MapViewportConstants.WorldOriginContent

    /// <summary>headless Avalonia 应用只需初始化一次</summary>
    private static readonly object InitLock = new();
    private static bool _initialized;

    private static void EnsureAvalonia()
    {
        lock (InitLock)
        {
            if (_initialized) return;
            AppBuilder.Configure<Application>()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions())
                .SetupWithoutStarting();
            _initialized = true;
        }
    }

    private static Canvas CreateArrangedCanvas(double width, double height)
    {
        var canvas = new Canvas { Width = width, Height = height };
        // Bounds 需要经过布局才有效；TokenUIManager 在 Bounds 为 0 时会跳过同步
        canvas.Measure(new Size(width, height));
        canvas.Arrange(new Rect(0, 0, width, height));
        return canvas;
    }

    private static HierarchyItemViewModel CreateToken(
        MainWindowViewModel vm, string id, string name, double x, double y)
    {
        var item = vm.BuildHierarchyItemPublic(new MapEngine.Avalonia.Services.HierarchyNodeDto
        {
            Id = id,
            Name = name,
            Icon = "🧝",              // ShouldRenderOnMap 要求 Icon 非空
            ObjectType = "Sprite",
            IsActive = true,
            HasMapPosition = true,    // CanNavigateToMap => HasMapPosition
            X = x,
            Y = y,
            ScaleX = 1,
            ScaleY = 1
        });
        return item;
    }

    [Fact]
    public void TokenAtCameraCenter_IsPositionedAtViewportCenter()
    {
        EnsureAvalonia();

        var vm = new MainWindowViewModel();
        var canvas = CreateArrangedCanvas(800, 600);

        // 世界原点处的 Token；相机也看向世界原点
        var token = CreateToken(vm, "11111111-1111-1111-1111-111111111111", "英雄", 0, 0);
        vm.MapRenderableItems.Add(token);

        var cameraCenter = new Point(token.MapLeft + token.SpriteWidth / 2.0,
                                     token.MapTop + token.SpriteHeight / 2.0);

        var manager = new TokenUIManager(canvas, vm, () => (cameraCenter, 1.0));
        manager.SyncFromViewModel(cameraCenter, 1.0);

        Assert.Single(canvas.Children);
        var host = canvas.Children[0];

        // Token 中心与相机中心重合 => 标签左边缘 = 视口中心X - HalfWidth(48)，上边缘 = 视口中心Y - AboveToken(56)
        Assert.Equal(400 - 48, Canvas.GetLeft(host), precision: 3);
        Assert.Equal(300 - 56, Canvas.GetTop(host), precision: 3);
    }

    [Fact]
    public void DistinctTokens_GetDistinctPositions_NotStackedAtOrigin()
    {
        EnsureAvalonia();

        var vm = new MainWindowViewModel();
        var canvas = CreateArrangedCanvas(800, 600);

        vm.MapRenderableItems.Add(
            CreateToken(vm, "11111111-1111-1111-1111-111111111111", "A", 0, 0));
        vm.MapRenderableItems.Add(
            CreateToken(vm, "22222222-2222-2222-2222-222222222222", "B", 100, 0));

        var cameraCenter = new Point(ContentOrigin, ContentOrigin);
        var manager = new TokenUIManager(canvas, vm, () => (cameraCenter, 1.0));
        manager.SyncFromViewModel(cameraCenter, 1.0);

        Assert.Equal(2, canvas.Children.Count);

        var lefts = new[]
        {
            Canvas.GetLeft(canvas.Children[0]),
            Canvas.GetLeft(canvas.Children[1])
        };

        // 回归点：两个 Token 不能重合，且都不应停留在 0（未定位的表现）
        Assert.NotEqual(lefts[0], lefts[1]);
        Assert.DoesNotContain(0.0, lefts);
        // X 相差 100 content 单位、zoom=1 => 屏幕上相差 100px
        Assert.Equal(100, Math.Abs(lefts[0] - lefts[1]), precision: 3);
    }

    [Fact]
    public void Zoom_ScalesScreenOffset()
    {
        EnsureAvalonia();

        var vm = new MainWindowViewModel();
        var canvas = CreateArrangedCanvas(800, 600);

        vm.MapRenderableItems.Add(
            CreateToken(vm, "11111111-1111-1111-1111-111111111111", "A", 0, 0));
        vm.MapRenderableItems.Add(
            CreateToken(vm, "22222222-2222-2222-2222-222222222222", "B", 100, 0));

        var cameraCenter = new Point(ContentOrigin, ContentOrigin);
        var manager = new TokenUIManager(canvas, vm, () => (cameraCenter, 2.0));
        manager.SyncFromViewModel(cameraCenter, 2.0);

        var gap = Math.Abs(
            Canvas.GetLeft(canvas.Children[0]) - Canvas.GetLeft(canvas.Children[1]));

        // zoom=2 => 间距翻倍
        Assert.Equal(200, gap, precision: 3);
    }

    [Fact]
    public void OffScreenToken_IsRemovedFromOverlay()
    {
        EnsureAvalonia();

        var vm = new MainWindowViewModel();
        var canvas = CreateArrangedCanvas(800, 600);

        var token = CreateToken(vm, "11111111-1111-1111-1111-111111111111", "英雄", 0, 0);
        vm.MapRenderableItems.Add(token);

        var cameraCenter = new Point(ContentOrigin, ContentOrigin);
        var manager = new TokenUIManager(canvas, vm, () => (cameraCenter, 1.0));
        manager.SyncFromViewModel(cameraCenter, 1.0);
        Assert.Single(canvas.Children);

        // 相机移开很远 => Token 出屏，宿主控件应被回收
        var farCamera = new Point(ContentOrigin + 100000, ContentOrigin);
        manager.SyncFromViewModel(farCamera, 1.0);
        Assert.Empty(canvas.Children);
    }

    [Fact]
    public void ResyncReusesHost_NoDuplicateChildren()
    {
        EnsureAvalonia();

        var vm = new MainWindowViewModel();
        var canvas = CreateArrangedCanvas(800, 600);

        vm.MapRenderableItems.Add(
            CreateToken(vm, "11111111-1111-1111-1111-111111111111", "英雄", 0, 0));

        var cameraCenter = new Point(ContentOrigin, ContentOrigin);
        var manager = new TokenUIManager(canvas, vm, () => (cameraCenter, 1.0));

        manager.SyncFromViewModel(cameraCenter, 1.0);
        var firstHost = canvas.Children[0];

        // 多次同步（模拟相机连续平移）不应重复创建视觉树
        for (var i = 1; i <= 5; i++)
        {
            manager.SyncFromViewModel(new Point(ContentOrigin + i, ContentOrigin), 1.0);
        }

        Assert.Single(canvas.Children);
        Assert.Same(firstHost, canvas.Children[0]);
    }

    [Fact]
    public void TokenWithoutName_IsNotRendered()
    {
        EnsureAvalonia();

        var vm = new MainWindowViewModel();
        var canvas = CreateArrangedCanvas(800, 600);

        vm.MapRenderableItems.Add(
            CreateToken(vm, "11111111-1111-1111-1111-111111111111", "   ", 0, 0));

        var cameraCenter = new Point(ContentOrigin, ContentOrigin);
        var manager = new TokenUIManager(canvas, vm, () => (cameraCenter, 1.0));
        manager.SyncFromViewModel(cameraCenter, 1.0);

        Assert.Empty(canvas.Children);
    }
}
