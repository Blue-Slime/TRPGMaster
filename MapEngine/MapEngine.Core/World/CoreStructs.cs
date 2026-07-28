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
