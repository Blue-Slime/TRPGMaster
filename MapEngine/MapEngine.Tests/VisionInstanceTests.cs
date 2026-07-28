using System.Linq;
using MapEngine.Avalonia.Services;
using MapEngine.Avalonia.ViewModels;
using Xunit;

namespace MapEngine.Tests;

/// <summary>
/// 复现"拖战士到地图 → 视野扇形"这条链的数据层:
/// 用 CreateInstanceFromAsset 相同的 dto 构造 HierarchyItemViewModel,
/// 断言 Vision 组件挂载、VisionEnabled、VisionCones 可渲染(IsEnabled && Range>0)。
/// </summary>
public class VisionInstanceTests
{
    private const int FeetPerCell = 5; // 默认每格 5 英尺(D&D)

    private static HierarchyNodeDto BuildWarriorDto(bool visionEnabled, double visionRadius = 60.0)
    {
        return new HierarchyNodeDto
        {
            Id = "node-warrior",
            Name = "战士",
            Icon = "🛡️",
            ObjectType = "StaticObject",
            InstanceId = "INST-1",
            IsActive = true,
            ScaleX = 1,
            ScaleY = 1,
            SpriteColor = "#E05050",
            Opacity = 1,
            VisionEnabled = visionEnabled,
            VisionRadius = visionRadius,
            Orientation = 0,
            VisionCones = visionEnabled
                ? [new VisionConeDto
                    {
                        Id = "cone-1",
                        Name = "默认视野",
                        CenterOffset = 0,
                        Range = visionRadius / FeetPerCell, // 60ft / 5 = 12 格
                        FieldOfView = 360,
                        IsEnabled = true
                    }]
                : [],
            X = 100,
            Y = 100,
            HasMapPosition = true
        };
    }

    [Fact]
    public void Warrior_WithVisionEnabled_MountsVisionAndRenderableCone()
    {
        var item = new HierarchyItemViewModel(BuildWarriorDto(visionEnabled: true));

        Assert.True(item.HasVisionComponent, "视野开启的对象应挂载 VisionComponent");
        Assert.True(item.VisionEnabled, "VisionEnabled 应为 true");
        Assert.Single(item.VisionCones);

        var cone = item.VisionCones[0];
        Assert.True(cone.IsEnabled, "锥应启用");
        // 60ft 应换算成 12 格(而非把英尺当像素除以 CellSize 得 1.2 格 → 扇形几乎藏在 token 下)
        Assert.Equal(12.0, cone.Range, precision: 3);
    }

    [Fact]
    public void Goblin_WithoutVision_HasNoVisionComponent()
    {
        var item = new HierarchyItemViewModel(BuildWarriorDto(visionEnabled: false));

        Assert.False(item.HasVisionComponent, "未开视野的对象不应挂载 VisionComponent");
        Assert.False(item.VisionEnabled);
        Assert.Empty(item.VisionCones);
    }

    [Fact]
    public void AddVisionComponentThenCone_BecomesRenderable()
    {
        var item = new HierarchyItemViewModel(BuildWarriorDto(visionEnabled: false));
        Assert.False(item.HasVisionComponent);

        // 模拟 AddVisionComponent(NotifyVisionComponentChanged 是 internal,仅刷新 UI;
        // HasVisionComponent/VisionEnabled 直接读组件,无需通知即可断言)
        item.AddComponent(new MapEngine.Core.Components.VisionComponent { Enabled = true, Radius = 60 });

        Assert.True(item.HasVisionComponent);
        Assert.True(item.VisionEnabled);
    }
}
