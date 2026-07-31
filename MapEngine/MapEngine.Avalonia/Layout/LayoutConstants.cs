namespace MapEngine.Avalonia.Layout;

/// <summary>
/// 编辑器布局尺寸的单一真相来源。
/// 所有胶囊/抽屉的定位数值从这里读取，不在 XAML 或 code-behind 中硬编码。
/// </summary>
public static class LayoutConstants
{
    // ── 窗口边距 ────────────────────────────────────────────────────
    /// <summary>所有浮动元素距窗口边缘的统一边距。</summary>
    public const double EdgeMargin = 14.0;

    /// <summary>胶囊之间的水平/垂直间隙。</summary>
    public const double CapsuleGap = 8.0;

    // ── 抽屉尺寸 ────────────────────────────────────────────────────
    /// <summary>左抽屉（场景层级）宽度，与 XAML Width 一致。</summary>
    public const double LeftDrawerWidth   = 260.0;

    /// <summary>右抽屉（Inspector）宽度，与 XAML Width 一致。</summary>
    public const double RightDrawerWidth  = 320.0;

    /// <summary>底部抽屉（素材库）高度，与 XAML Height 一致。</summary>
    public const double BottomDrawerHeight = 280.0;

    // ── ActionCapsule（左下固定胶囊）───────────────────────────────
    /// <summary>左下设置胶囊的高度（Padding*2 + 按钮高度）。</summary>
    public const double ActionCapsuleHeight = 48.0;

    /// <summary>
    /// 底部安全高度：ActionCapsule 占用的空间。
    /// 左右抽屉、工具条的 Margin.Bottom 最小值。
    /// = ActionCapsuleHeight + EdgeMargin * 2
    /// </summary>
    public const double BottomSafeMargin = ActionCapsuleHeight + EdgeMargin * 2; // 76px

    // ── 主工具条（ToolbarCapsule）──────────────────────────────────
    /// <summary>主工具条的实际渲染宽度（Padding*2 + 按钮宽度）。</summary>
    public const double ToolbarCapsuleWidth = 54.0;

    /// <summary>工具条内部顶部 Padding。</summary>
    public const double ToolbarPaddingTop = 10.0;

    /// <summary>模式切换按钮高度（Height=44）。</summary>
    public const double ToolbarModeButtonHeight = 44.0;

    /// <summary>工具条分隔线区域高度（Border 1px + Margin top/bottom 各 6px）。</summary>
    public const double ToolbarDividerHeight = 13.0;

    /// <summary>主工具列表中每个工具按钮的占用高度（Height=40 + Margin(0,2)*2）。</summary>
    public const double ToolItemHeight = 44.0;

    /// <summary>
    /// 工具列表第一个按钮顶部到 ToolbarCapsule 顶部的偏移（含 Padding + 模式按钮 + 分隔线）。
    /// = ToolbarPaddingTop + ToolbarModeButtonHeight + ToolbarDividerHeight
    /// </summary>
    public const double ToolListOffsetFromTop =
        ToolbarPaddingTop + ToolbarModeButtonHeight + ToolbarDividerHeight; // 67px

    // ── 子工具胶囊 ──────────────────────────────────────────────────
    /// <summary>
    /// 子工具胶囊的 Margin.Left 基准值（主工具条右边缘 + 间隙）。
    /// 实际值由 DrawerLayoutBehavior 在运行时动态计算（含 LeftDrawer 推移）。
    /// 此常量作为初始/回落值。
    /// </summary>
    public const double SubToolCapsuleBaseLeft = ToolbarCapsuleWidth + EdgeMargin + CapsuleGap; // 76px

    // ── BottomDrawer 触发的上移量 ───────────────────────────────────
    /// <summary>
    /// BottomDrawer 打开时，StatusBar/ViewportCapsule 需要上移的量。
    /// = BottomDrawerHeight + EdgeMargin（抽屉高度 + 与抽屉之间的间距）
    /// </summary>
    public const double BottomDrawerUpShift = BottomDrawerHeight + EdgeMargin; // 294px
}
