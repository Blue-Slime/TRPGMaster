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

public sealed class TokenComponent : ComponentBase
{
    public override string TypeName => "Token";

    public string TokenName { get; set; } = string.Empty;
    public int InitiativeOrder { get; set; }
    public bool IsPlayerControlled { get; set; }
    public double MovementSpeed { get; set; } = 30;

    public override IComponent Clone() => new TokenComponent
    {
        TokenName = TokenName,
        InitiativeOrder = InitiativeOrder,
        IsPlayerControlled = IsPlayerControlled,
        MovementSpeed = MovementSpeed
    };
}

