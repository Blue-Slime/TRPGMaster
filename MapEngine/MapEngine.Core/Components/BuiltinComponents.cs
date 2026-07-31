using MapEngine.Core.Data;

namespace MapEngine.Core.Components;

public sealed class TransformComponent : ComponentBase
{
    public override string TypeName => "Transform";

    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }
    public double Rotation { get; set; }
    public double ScaleX { get; set; } = 1;
    public double ScaleY { get; set; } = 1;
    public bool HasMapPosition { get; set; }

    public override IComponent Clone() => new TransformComponent
    {
        X = X, Y = Y, Z = Z,
        Rotation = Rotation,
        ScaleX = ScaleX, ScaleY = ScaleY,
        HasMapPosition = HasMapPosition
    };
}

public sealed class SpriteRendererComponent : ComponentBase
{
    public override string TypeName => "SpriteRenderer";

    public string Color { get; set; } = "#FF4444";
    public double Opacity { get; set; } = 1;
    public string SourceAssetPath { get; set; } = string.Empty;
    public string SourceAssetKind { get; set; } = string.Empty;
    public string SourceAssetName { get; set; } = string.Empty;
    /// <summary>横向对齐：0=左, 1=居中, 2=右</summary>
    public int AlignX { get; set; } = 1;
    /// <summary>纵向对齐：0=上, 1=居中, 2=下</summary>
    public int AlignY { get; set; } = 1;
    public bool HitTestEnabled { get; set; } = true;

    public override IComponent Clone() => new SpriteRendererComponent
    {
        Color = Color,
        Opacity = Opacity,
        SourceAssetPath = SourceAssetPath,
        SourceAssetKind = SourceAssetKind,
        SourceAssetName = SourceAssetName,
        AlignX = AlignX,
        AlignY = AlignY,
        HitTestEnabled = HitTestEnabled
    };
}

public sealed class VisionComponent : ComponentBase
{
    public override string TypeName => "Vision";

    public bool Enabled { get; set; }
    public double Radius { get; set; } = 60;
    /// <summary>朝向角度(0-360°,0=正右,90=正上,逆时针)。</summary>
    public double Orientation { get; set; }
    /// <summary>视野锥列表。空列表 = 使用默认全向视野(半径为 Radius)。</summary>
    public List<VisionConeData> VisionCones { get; set; } = [];

    public override IComponent Clone() => new VisionComponent
    {
        Enabled = Enabled,
        Radius = Radius,
        Orientation = Orientation,
        VisionCones = VisionCones.Select(c => new VisionConeData
        {
            Id = c.Id, Name = c.Name,
            CenterOffset = c.CenterOffset, Range = c.Range,
            FieldOfView = c.FieldOfView, IsEnabled = c.IsEnabled
        }).ToList()
    };
}

/// <summary>
/// Token 状态标记（中毒/昏迷等 TRPG 战斗状态）。
/// </summary>
public sealed class ConditionEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public string Icon { get; set; } = "🎭";
    public int StackCount { get; set; } = 1;
    public int RemainingRounds { get; set; } = -1;
    public string ColorHex { get; set; } = "#10B981";
}

public sealed class TokenComponent : ComponentBase
{
    public override string TypeName => "Token";

    public string TokenName { get; set; } = string.Empty;
    public int InitiativeOrder { get; set; }
    public bool IsPlayerControlled { get; set; }
    public double MovementSpeed { get; set; } = 30;

    public int CurrentHP { get; set; } = 100;
    public int MaxHP { get; set; } = 100;

    /// <summary>是否显示在先攻追踪器中。默认 false（需主动加入先攻表）。</summary>
    public bool IsInInitiativeTracker { get; set; } = false;

    /// <summary>当前状态列表（中毒/昏迷/隐形等）。</summary>
    public List<ConditionEntry> Conditions { get; set; } = [];

    public override IComponent Clone() => new TokenComponent
    {
        TokenName = TokenName,
        InitiativeOrder = InitiativeOrder,
        IsPlayerControlled = IsPlayerControlled,
        MovementSpeed = MovementSpeed,
        CurrentHP = CurrentHP,
        MaxHP = MaxHP,
        IsInInitiativeTracker = IsInInitiativeTracker,
        Conditions = Conditions.Select(c => new ConditionEntry
        {
            Id = c.Id,
            Name = c.Name,
            Icon = c.Icon,
            StackCount = c.StackCount,
            RemainingRounds = c.RemainingRounds,
            ColorHex = c.ColorHex
        }).ToList()
    };
}

/// <summary>描边样式</summary>
public enum StrokeStyle { Solid, Dashed, Dotted }

/// <summary>
/// 矢量形状组件——覆盖枭熊 2 的四类绘制工具：
///   line      — 直线（起点=对象中心，终点=中心+X2/Y2）
///   rect      — 矩形（宽 Width、高 Height）
///   ellipse   — 椭圆/圆（宽 Width、高 Height）
///   cone      — 锥形（从中心向 Points[0] 扩展，半角 ConeAngle）
///   wedge     — 楔形扇区（类似 cone，但有两条边）
///   polygon   — 任意多边形（Points 为顶点列表，相对对象中心）
///   freehand  — 自由笔触折线（Points 为轨迹点，相对对象中心）
/// 世界坐标中心由 TransformComponent.X/Y 决定。
/// </summary>
public sealed class ShapeComponent : ComponentBase
{
    public override string TypeName => "Shape";

    // ── 形状类型 ───────────────────────────────────────────────────────────
    /// <summary>line | rect | ellipse | cone | wedge | polygon | freehand</summary>
    public string ShapeType { get; set; } = "rect";

    // ── 尺寸（rect / ellipse 用）─────────────────────────────────────────
    /// <summary>世界单位宽度</summary>
    public double Width { get; set; } = 60;
    /// <summary>世界单位高度</summary>
    public double Height { get; set; } = 60;

    // ── 线段终点（line 用，相对中心）──────────────────────────────────────
    public double X2 { get; set; }
    public double Y2 { get; set; }

    // ── 多边形/锥形/自由笔触顶点（相对对象中心的世界偏移）────────────────
    public List<(double X, double Y)> Points { get; set; } = [];

    // ── cone / wedge ──────────────────────────────────────────────────────
    /// <summary>锥形/扇形半角（度）</summary>
    public double ConeAngle { get; set; } = 30;
    /// <summary>锥形/扇形半径（世界单位）</summary>
    public double ConeRadius { get; set; } = 120;
    /// <summary>锥形/扇形朝向（度，0 = +X 方向，逆时针为正）</summary>
    public double Rotation { get; set; }

    // ── 视觉样式 ──────────────────────────────────────────────────────────
    public string StrokeColor { get; set; } = "#845EF7";
    public string FillColor   { get; set; } = "#40845EF7";
    public double StrokeWidth { get; set; } = 2;
    public bool   IsFilled    { get; set; } = true;
    public StrokeStyle StrokeStyle { get; set; } = StrokeStyle.Solid;

    public override IComponent Clone() => new ShapeComponent
    {
        ShapeType   = ShapeType,
        Width       = Width,   Height    = Height,
        X2          = X2,      Y2        = Y2,
        Points      = [.. Points],
        ConeAngle   = ConeAngle, ConeRadius = ConeRadius,
        Rotation    = Rotation,
        StrokeColor = StrokeColor,
        FillColor   = FillColor,
        StrokeWidth = StrokeWidth,
        IsFilled    = IsFilled,
        StrokeStyle = StrokeStyle,
    };
}

/// <summary>文本水平对齐</summary>
public enum TextAlign { Left, Center, Right }

/// <summary>
/// 地图文本标注组件（对齐枭熊 2 的 Text 工具）。
/// 世界坐标锚点由 TransformComponent.X/Y 决定；
/// 渲染走 Avalonia 覆盖层（GL 层没有字体栈），因此字号是"世界单位"，
/// 由覆盖层按当前 zoom 折算成屏幕像素。
/// </summary>
public sealed class TextComponent : ComponentBase
{
    public override string TypeName => "Text";

    /// <summary>文本内容（支持多行，\n 分隔）</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>字号（世界单位，zoom=1 时等于屏幕像素）</summary>
    public double FontSize { get; set; } = 16;

    /// <summary>文字颜色 #RRGGBB / #AARRGGBB</summary>
    public string Color { get; set; } = "#F8F9FA";

    /// <summary>背景板颜色（透明则不画底板）</summary>
    public string BackgroundColor { get; set; } = "#A0000000";

    public bool IsBold { get; set; }
    public bool IsItalic { get; set; }

    /// <summary>水平对齐（相对锚点）</summary>
    public TextAlign Align { get; set; } = TextAlign.Center;

    public override IComponent Clone() => new TextComponent
    {
        Text            = Text,
        FontSize        = FontSize,
        Color           = Color,
        BackgroundColor = BackgroundColor,
        IsBold          = IsBold,
        IsItalic        = IsItalic,
        Align           = Align,
    };
}

