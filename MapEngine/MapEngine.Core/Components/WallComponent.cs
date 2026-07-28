using MapEngine.Core.Spatial;

namespace MapEngine.Core.Components;

/// <summary>
/// 统一墙组件。一段线段，四个独立感知通道（Foundry 式多感知），支持单向和门。
/// 替代旧版三份互相矛盾的 WallComponent / WallSegment 定义。
/// 实现 ISpatialComponent:以线段包围盒登记进 World 空间索引。
/// </summary>
public sealed class WallComponent : ComponentBase, ISpatialComponent
{
    public RectD GetLocalBounds() => RectD.FromSegment(X1, Y1, X2, Y2);

    public override string TypeName => "Wall";

    // 线段坐标（相对父 Floor 本地空间）
    public double X1 { get; set; }
    public double Y1 { get; set; }
    public double X2 { get; set; }
    public double Y2 { get; set; }

    // 四个感知通道：None / Limited / Normal
    public SenseLevel Sight  { get; set; } = SenseLevel.Normal;
    public SenseLevel Move   { get; set; } = SenseLevel.Normal;
    public SenseLevel Sound  { get; set; } = SenseLevel.Normal;
    public SenseLevel Light  { get; set; } = SenseLevel.Normal;

    // 单向墙：射线源在哪侧才阻挡（靠叉积符号判断）
    public WallDir Dir { get; set; } = WallDir.Both;

    // 门（None=普通墙，Door=可开关，Secret=GM专属）
    public DoorKind Door  { get; set; } = DoorKind.None;
    public DoorState State { get; set; } = DoorState.Closed;

    // 渲染参数
    public double Thickness      { get; set; } = 5;
    public string? TileTexturePath { get; set; }

    // 剔除标记：WallsCutaway 模式下该墙是否参与剔除
    public bool NoCutaway { get; set; } = false;

    /// <summary>
    /// 是否对给定感知类型阻挡。
    /// Limited 语义：由 VisionSystem 在扫描时累计穿越次数，>1 才阻挡。
    /// </summary>
    public bool Blocks(SenseType type) => type switch
    {
        SenseType.Sight  => Sight  != SenseLevel.None && (Door == DoorKind.None || State != DoorState.Open),
        SenseType.Move   => Move   != SenseLevel.None && (Door == DoorKind.None || State == DoorState.Locked),
        SenseType.Sound  => Sound  != SenseLevel.None && (Door == DoorKind.None || State != DoorState.Open),
        SenseType.Light  => Light  != SenseLevel.None && (Door == DoorKind.None || State != DoorState.Open),
        _ => false
    };

    public override IComponent Clone() => new WallComponent
    {
        X1 = X1, Y1 = Y1, X2 = X2, Y2 = Y2,
        Sight = Sight, Move = Move, Sound = Sound, Light = Light,
        Dir = Dir, Door = Door, State = State,
        Thickness = Thickness, TileTexturePath = TileTexturePath,
        NoCutaway = NoCutaway
    };
}

// ── 枚举 ────────────────────────────────────────────────

/// <summary>感知阻挡等级。Limited = 单层可穿视，双层阻挡（树林/地形墙）。</summary>
public enum SenseLevel { None = 0, Limited = 10, Normal = 20 }

/// <summary>感知类型，用于 Blocks() 和视野扫描的候选边过滤。</summary>
public enum SenseType  { Sight, Move, Sound, Light }

/// <summary>单向墙方向：Both = 双向阻挡，Left/Right = 只在该侧阻挡（通过叉积判断）。</summary>
public enum WallDir    { Both, Left, Right }

/// <summary>门类型。</summary>
public enum DoorKind   { None, Door, Secret }

/// <summary>门状态。Open = 对所有感知透明；Locked = 对 Move 仍阻挡。</summary>
public enum DoorState  { Closed, Open, Locked }

// ── 预设（UI 快捷按钮对应的字段组合）─────────────────────

public static class WallPresets
{
    public static WallComponent Normal()    => new() { Sight = SenseLevel.Normal,  Move = SenseLevel.Normal,  Sound = SenseLevel.Normal,  Light = SenseLevel.Normal };
    public static WallComponent Window()    => new() { Sight = SenseLevel.Normal,  Move = SenseLevel.Normal,  Sound = SenseLevel.None,    Light = SenseLevel.Normal };
    public static WallComponent Terrain()   => new() { Sight = SenseLevel.Limited, Move = SenseLevel.None,    Sound = SenseLevel.None,    Light = SenseLevel.Limited };
    public static WallComponent Ethereal()  => new() { Sight = SenseLevel.Normal,  Move = SenseLevel.None,    Sound = SenseLevel.Normal,  Light = SenseLevel.None };
    public static WallComponent Invisible() => new() { Sight = SenseLevel.None,    Move = SenseLevel.Normal,  Sound = SenseLevel.None,    Light = SenseLevel.None };
    public static WallComponent Door()      => new() { Sight = SenseLevel.Normal,  Move = SenseLevel.Normal,  Sound = SenseLevel.Normal,  Light = SenseLevel.Normal, Door = DoorKind.Door };
    public static WallComponent SecretDoor()=> new() { Sight = SenseLevel.Normal,  Move = SenseLevel.Normal,  Sound = SenseLevel.Normal,  Light = SenseLevel.Normal, Door = DoorKind.Secret };
}
