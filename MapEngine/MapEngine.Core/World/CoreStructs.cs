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

    public TokenData()
    {
        InitiativeOrder = 0;
        IsPlayerControlled = false;
        MovementSpeed = 30;
        CurrentHP = 100;
        MaxHP = 100;
    }
}
