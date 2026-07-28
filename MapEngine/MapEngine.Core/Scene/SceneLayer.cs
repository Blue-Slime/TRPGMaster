namespace MapEngine.Core.Scene;

/// <summary>
/// 渲染层固定顺序（对齐 Owlbear Rodeo 2）。
/// 层内通过 Transform.Z / SortOrder 控制前后关系。
/// </summary>
public enum SceneLayer
{
    Map        = 0,
    Drawing    = 100,
    Prop       = 200,
    Mount      = 300,
    Character  = 400,
    Attachment = 500,
    Note       = 600,
    Fog        = 700,
    GM         = 800
}

/// <summary>楼层间连接（楼梯/梯子/传送门/电梯）。</summary>
public sealed class FloorConnection
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public double X { get; set; }
    public double Y { get; set; }
    public Guid TargetFloorId { get; set; }
    public double TargetX { get; set; }
    public double TargetY { get; set; }
    public string Label { get; set; } = "Stairs";
    public ConnectionType Type { get; set; } = ConnectionType.Stairs;
}

public enum ConnectionType { Stairs, Ladder, Portal, Elevator }
