using System;
using System.Collections.Generic;
using System.Numerics;

namespace MapEngine.Core.Data;

/// <summary>
/// Transform 数据（位置、旋转、缩放）
/// 值类型，零 GC 开销
/// </summary>
public struct TransformData
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }
    public double Rotation { get; set; }
    public double ScaleX { get; set; }
    public double ScaleY { get; set; }

    public TransformData()
    {
        X = 0;
        Y = 0;
        Z = 0;
        Rotation = 0;
        ScaleX = 1;
        ScaleY = 1;
    }

    public TransformData(double x, double y)
    {
        X = x;
        Y = y;
        Z = 0;
        Rotation = 0;
        ScaleX = 1;
        ScaleY = 1;
    }
}

/// <summary>
/// Sprite 渲染数据
/// 可空值类型，未使用时零开销
/// </summary>
public struct SpriteData
{
    public string TexturePath { get; set; }
    public double Opacity { get; set; }
    public string TintColor { get; set; }
    public int AlignX { get; set; }  // 0=left, 1=center, 2=right
    public int AlignY { get; set; }  // 0=top, 1=center, 2=bottom
    public bool HitTestEnabled { get; set; }

    public SpriteData()
    {
        TexturePath = string.Empty;
        Opacity = 1.0;
        TintColor = "#FFFFFF";
        AlignX = 1;
        AlignY = 1;
        HitTestEnabled = true;
    }
}

/// <summary>
/// Vision 视野数据
/// </summary>
public struct VisionData
{
    public bool Enabled { get; set; }
    public double Radius { get; set; }
    public double Orientation { get; set; }
    public List<VisionConeData> Cones { get; set; }

    public VisionData()
    {
        Enabled = false;
        Radius = 60;
        Orientation = 0;
        Cones = [];
    }
}

/// <summary>
/// 二维点，用于持久化顶点列表。
/// 不能直接存 ValueTuple：System.Text.Json 不序列化 ValueTuple 的字段。
/// </summary>
public struct PointData
{
    public double X { get; set; }
    public double Y { get; set; }
}

/// <summary>
/// 矢量形状数据（ShapeComponent 的存档投影）
/// </summary>
public struct ShapeData
{
    public string ShapeType { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public double X2 { get; set; }
    public double Y2 { get; set; }
    public List<PointData> Points { get; set; }
    public double ConeAngle { get; set; }
    public double ConeRadius { get; set; }
    public double Rotation { get; set; }
    public string StrokeColor { get; set; }
    public string FillColor { get; set; }
    public double StrokeWidth { get; set; }
    public bool IsFilled { get; set; }
    public int StrokeStyle { get; set; }

    public ShapeData()
    {
        ShapeType = "rect";
        Width = 60;
        Height = 60;
        X2 = 0;
        Y2 = 0;
        Points = [];
        ConeAngle = 30;
        ConeRadius = 120;
        Rotation = 0;
        StrokeColor = "#845EF7";
        FillColor = "#40845EF7";
        StrokeWidth = 2;
        IsFilled = true;
        StrokeStyle = 0;
    }
}

/// <summary>
/// 文本标注数据（TextComponent 的存档投影）
/// </summary>
public struct TextData
{
    public string Text { get; set; }
    public double FontSize { get; set; }
    public string Color { get; set; }
    public string BackgroundColor { get; set; }
    public bool IsBold { get; set; }
    public bool IsItalic { get; set; }
    public int Align { get; set; }

    public TextData()
    {
        Text = string.Empty;
        FontSize = 16;
        Color = "#F8F9FA";
        BackgroundColor = "#A0000000";
        IsBold = false;
        IsItalic = false;
        Align = 1;
    }
}

/// <summary>
/// 拓扑节点数据（GraphNodeComponent 的存档投影）
/// </summary>
public struct GraphNodeData
{
    public int Kind { get; set; }
    public string DisplayName { get; set; }
    public string Description { get; set; }
    public int Visibility { get; set; }
    public int RenderMode { get; set; }
    public string IconAssetRef { get; set; }
    public string Color { get; set; }
    public double Size { get; set; }
    public string Shape { get; set; }

    public GraphNodeData()
    {
        Kind = 0;                // Location
        DisplayName = string.Empty;
        Description = string.Empty;
        Visibility = 0;          // Hidden
        RenderMode = 1;          // Icon
        IconAssetRef = string.Empty;
        Color = "#4A90E2";
        Size = 48;
        Shape = "circle";
    }
}

/// <summary>
/// 单条拓扑通道数据（GraphLinkComponent 的存档投影）。
/// 一个对象可挂多条，故 GraphLinksData 以列表形式存档。
/// </summary>
public struct GraphLinkData
{
    public string LinkId { get; set; }
    public string TargetNodeId { get; set; }
    public int Kind { get; set; }
    public bool IsBidirectional { get; set; }
    public string Label { get; set; }
    public int Visibility { get; set; }
    public bool IsPassable { get; set; }
    public double Cost { get; set; }
    public string Color { get; set; }
    public double Width { get; set; }
    public int StrokeStyle { get; set; }

    public GraphLinkData()
    {
        LinkId = string.Empty;
        TargetNodeId = string.Empty;
        Kind = 0;                // Normal
        IsBidirectional = true;
        Label = string.Empty;
        Visibility = 0;          // Hidden
        IsPassable = true;
        Cost = 1;
        Color = "#8A8F98";
        Width = 2;
        StrokeStyle = 0;         // Solid
    }
}

/// <summary>
/// 单个视野锥数据
/// </summary>
public struct VisionConeData
{
    public string Id { get; set; }
    public string Name { get; set; }
    public double CenterOffset { get; set; }
    public double Range { get; set; }
    public double FieldOfView { get; set; }
    public bool IsEnabled { get; set; }

    public VisionConeData()
    {
        Id = Guid.NewGuid().ToString("N");
        Name = "视野锥";
        CenterOffset = 0;
        Range = 12;
        FieldOfView = 90;
        IsEnabled = true;
    }
}

/// <summary>
/// Token 状态条目数据（ConditionEntry 的存档投影）
/// </summary>
public struct ConditionData
{
    public string Id { get; set; }
    public string Name { get; set; }
    public string Icon { get; set; }
    public int StackCount { get; set; }
    public int RemainingRounds { get; set; }
    public string ColorHex { get; set; }

    public ConditionData()
    {
        Id = Guid.NewGuid().ToString("N");
        Name = string.Empty;
        Icon = "🎭";
        StackCount = 1;
        RemainingRounds = -1;
        ColorHex = "#10B981";
    }
}

/// <summary>
/// Token 核心数据（InitiativeOrder/HP/IsPlayerControlled 的存档投影）
/// </summary>
public struct TokenData
{
    public int InitiativeOrder { get; set; }
    public bool IsPlayerControlled { get; set; }
    public double MovementSpeed { get; set; }
    public int CurrentHP { get; set; }
    public int MaxHP { get; set; }
    public string Shape { get; set; }

    public TokenData()
    {
        InitiativeOrder = 0;
        IsPlayerControlled = false;
        MovementSpeed = 30;
        CurrentHP = 100;
        MaxHP = 100;
        Shape = "Rectangle";
    }
}

/// <summary>
/// 墙体路径数据（WallPathComponent 的存档投影）
/// 描述一条由锚点定义的墙体折线/闭合路径
/// </summary>
public struct WallPathData
{
    /// <summary>
    /// 锚点列表（墙体骨架）
    /// </summary>
    public List<PointData> Points { get; set; }

    /// <summary>
    /// 是否闭合路径（首尾相连）
    /// </summary>
    public bool IsClosed { get; set; }

    /// <summary>
    /// 视线阻挡等级（0=穿透, 1=半透明, 2=完全阻挡）
    /// </summary>
    public int Sight { get; set; }

    /// <summary>
    /// 移动阻挡等级（0=可穿过, 1=困难地形, 2=阻挡）
    /// </summary>
    public int Move { get; set; }

    /// <summary>
    /// 声音阻挡等级（0=穿透, 1=衰减, 2=隔绝）
    /// </summary>
    public int Sound { get; set; }

    /// <summary>
    /// 光照阻挡等级（0=穿透, 1=衰减, 2=阻挡）
    /// </summary>
    public int Light { get; set; }

    /// <summary>
    /// 墙体厚度（像素）
    /// </summary>
    public double Thickness { get; set; }

    /// <summary>
    /// 墙体颜色（十六进制，如 "#E74C3C"）
    /// </summary>
    public string Color { get; set; }

    /// <summary>
    /// 门窗段列表（可选）
    /// </summary>
    public List<DoorSegmentData>? Doors { get; set; }

    public WallPathData()
    {
        Points = [];
        IsClosed = false;
        Sight = 2;      // 默认完全阻挡视线
        Move = 2;       // 默认阻挡移动
        Sound = 1;      // 默认衰减声音
        Light = 1;      // 默认衰减光照
        Thickness = 5;
        Color = "#E74C3C";
        Doors = null;
    }
}

/// <summary>
/// 门窗段数据（定义在墙体锚点区间上的门窗）
/// </summary>
public struct DoorSegmentData
{
    /// <summary>
    /// 门窗唯一标识
    /// </summary>
    public string Id { get; set; }

    /// <summary>
    /// 起始锚点索引（在 WallPathData.Points 中的位置）
    /// </summary>
    public int StartAnchorIndex { get; set; }

    /// <summary>
    /// 结束锚点索引（在 WallPathData.Points 中的位置）
    /// </summary>
    public int EndAnchorIndex { get; set; }

    /// <summary>
    /// 门窗类型（0=普通门, 1=窗户, 2=拱门, 3=密门）
    /// </summary>
    public int Kind { get; set; }

    /// <summary>
    /// 门状态（0=关闭, 1=开启, 2=锁定）
    /// </summary>
    public int State { get; set; }

    /// <summary>
    /// 开门方向（0=向左, 1=向右, 2=双向）
    /// </summary>
    public int Swing { get; set; }

    /// <summary>
    /// 视线覆盖值（null=继承墙体配置）
    /// </summary>
    public int? SightOverride { get; set; }

    /// <summary>
    /// 移动覆盖值（null=继承墙体配置）
    /// </summary>
    public int? MoveOverride { get; set; }

    public DoorSegmentData()
    {
        Id = Guid.NewGuid().ToString("N");
        StartAnchorIndex = 0;
        EndAnchorIndex = 0;
        Kind = 0;           // 普通门
        State = 0;          // 关闭
        Swing = 0;          // 向左
        SightOverride = null;
        MoveOverride = null;
    }
}
