using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using MapEngine.Avalonia.Services;

namespace MapEngine.Avalonia.ViewModels;

/// <summary>战争迷雾管理：揭示区域列表（GM 可绘制/擦除矩形或多边形区域）。</summary>
public partial class MainWindowViewModel
{
    private bool _isFogEnabled = false;
    private string _fogSubMode = "erase";
    private double _fogFadeDistance = 0.2;

    /// <summary>迷雾工具是否为当前主工具（控制子工具面板显隐）。</summary>
    public bool IsFogToolActive =>
        string.Equals(SelectedPrimaryTool?.Key, "fog", StringComparison.OrdinalIgnoreCase);

    /// <summary>迷雾子模式：擦除模式是否激活。</summary>
    public bool IsFogEraseModeActive => _fogSubMode == "erase";

    /// <summary>迷雾子模式：绘制模式是否激活。</summary>
    public bool IsFogPaintModeActive => _fogSubMode == "paint";

    /// <summary>迷雾开关按钮提示文字。</summary>
    public string FogToggleTip => _isFogEnabled ? "关闭战争迷雾" : "开启战争迷雾";

    public bool IsFogEnabled
    {
        get => _isFogEnabled;
        set
        {
            if (SetProperty(ref _isFogEnabled, value))
            {
                StatusMessage = value ? "战争迷雾：已开启" : "战争迷雾：已关闭";
                RefreshMapRenderableItemsPublic();
            }
        }
    }

    /// <summary>
    /// FOV 渐变距离比例（0.0-0.5）。表示视野边缘多大比例区域有渐变效果。
    /// 例如 0.2 表示视野边缘 20% 区域从完全可见渐变到完全遮罩。
    /// </summary>
    public double FogFadeDistance
    {
        get => _fogFadeDistance;
        set
        {
            var clamped = Math.Clamp(value, 0.0, 0.5);
            if (SetProperty(ref _fogFadeDistance, clamped))
            {
                RefreshMapRenderableItemsPublic();
            }
        }
    }

    /// <summary>迷雾工具子模式：paint=绘制遮挡 / erase=擦除（揭示）。</summary>
    public string FogSubMode
    {
        get => _fogSubMode;
        set
        {
            if (SetProperty(ref _fogSubMode, value))
            {
                OnPropertyChanged(nameof(IsFogEraseModeActive));
                OnPropertyChanged(nameof(IsFogPaintModeActive));
            }
        }
    }

    /// <summary>
    /// 已揭示（透明）的迷雾区域列表，每个元素是一个凸多边形（至少 4 点矩形）。
    /// 渲染时从全屏暗色层中抠除这些区域。
    /// </summary>
    public List<FogRegion> FogRevealedRegions { get; } = new();

    /// <summary>
    /// 添加一个揭示区域（矩形快捷方式）。世界坐标，左上角为原点，Y 向上为正。
    /// </summary>
    public void FogRevealRect(double wx, double wy, double w, double h)
    {
        if (w < 1 && h < 1) return;
        // 规范化（允许负宽高）
        if (w < 0) { wx += w; w = -w; }
        if (h < 0) { wy += h; h = -h; }

        FogRevealedRegions.Add(FogRegion.FromRect(wx, wy, w, h));
        RefreshMapRenderableItemsPublic();
    }

    /// <summary>
    /// 在指定区域内绘制遮挡（从揭示列表中裁剪掉该矩形）。
    /// 简单实现：移除与该矩形完全重叠的揭示区域，部分重叠不拆分。
    /// </summary>
    public void FogPaintRect(double wx, double wy, double w, double h)
    {
        if (w < 0) { wx += w; w = -w; }
        if (h < 0) { wy += h; h = -h; }

        FogRevealedRegions.RemoveAll(r =>
            r.Bounds.X >= wx && r.Bounds.Y >= wy &&
            r.Bounds.X + r.Bounds.Width  <= wx + w &&
            r.Bounds.Y + r.Bounds.Height <= wy + h);

        RefreshMapRenderableItemsPublic();
    }

    /// <summary>清除全部揭示区域（全图变暗）。</summary>
    public void FogClearAll()
    {
        FogRevealedRegions.Clear();
        RefreshMapRenderableItemsPublic();
    }

    /// <summary>揭示全图（移除所有遮挡）。</summary>
    public void FogRevealAll()
    {
        FogRevealedRegions.Clear();
        // 加一个覆盖全地图的大矩形
        const double big = 10000;
        FogRevealedRegions.Add(FogRegion.FromRect(-big / 2, -big / 2, big, big));
        RefreshMapRenderableItemsPublic();
    }
}

/// <summary>
/// 一个迷雾揭示区域。当前仅支持矩形（4 顶点），后续可扩展为任意凸多边形。
/// 坐标系：世界坐标（Y 向上）。
/// </summary>
public sealed class FogRegion
{
    /// <summary>包围盒（世界坐标）。</summary>
    public (double X, double Y, double Width, double Height) Bounds { get; private set; }

    /// <summary>顶点列表（世界坐标，顺时针）。</summary>
    public IReadOnlyList<(double X, double Y)> Points { get; private set; }

    private FogRegion() { Points = Array.Empty<(double, double)>(); }

    public static FogRegion FromRect(double wx, double wy, double w, double h)
    {
        return new FogRegion
        {
            Bounds = (wx, wy, w, h),
            Points = new[]
            {
                (wx,     wy),
                (wx + w, wy),
                (wx + w, wy + h),
                (wx,     wy + h),
                (wx,     wy),   // 闭合
            }
        };
    }
}
