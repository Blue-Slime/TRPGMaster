using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.IO;
using Avalonia;
using Avalonia.Media;
using CommunityToolkit.Mvvm.Input;
using MapEngine.Core;
using MapEngine.Core.Commands;
using MapEngine.Core.Data;
using MapEngine.Core.Components;
using MapEngine.Avalonia.Commands;
using MapEngine.Avalonia.Graphics;
using MapEngine.Avalonia.Services;
using MapEngine.Core.Hosting;

namespace MapEngine.Avalonia.ViewModels;

public sealed class ToolActionViewModel : ViewModelBase
{
    private bool _isSelected;

    public ToolActionViewModel(string key, string icon, string label, string toolTip,
        bool isToggle = false)
    {
        Key = key;
        Icon = icon;
        Label = label;
        ToolTip = toolTip;
        IsToggle = isToggle;
    }

    public string Key { get; }

    public string Icon { get; }

    public string Label { get; }

    public string ToolTip { get; }

    /// <summary>
    /// true = 独立开关（不互斥，再次点击取消自己，不影响其他工具选中状态）。
    /// false = 互斥单选（同组只能激活一个）。
    /// </summary>
    public bool IsToggle { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (SetProperty(ref _isSelected, value))
            {
                OnPropertyChanged(nameof(Foreground));
                OnPropertyChanged(nameof(ShowSelectionBar));
                OnPropertyChanged(nameof(ShowToggleBackground));
            }
        }
    }

    /// <summary>单选工具选中时显示左侧竖条。</summary>
    public bool ShowSelectionBar => IsSelected && !IsToggle;

    /// <summary>开关工具激活时显示圆角高亮背景块。</summary>
    public bool ShowToggleBackground => IsSelected && IsToggle;

    /// <summary>
    /// Owlbear 风格 SVG 图标路径数据（Material Design Icons，24x24 viewBox）。
    /// Avalonia 内置类型转换器会把该字符串解析为 <see cref="Geometry"/>。
    /// 未匹配到 <see cref="Key"/> 时返回 null，模板退化为显示 <see cref="Icon"/> 文本。
    /// </summary>
    public string? IconPath => Key switch
    {
        // 主工具集
        "select"  => "M13.64,21.97C13.14,22.21 12.54,22 12.31,21.5L10.13,16.76L7.62,18.78C7.45,18.92 7.24,19 7,19A1,1 0 0,1 6,18V3A1,1 0 0,1 7,2C7.24,2 7.47,2.09 7.64,2.24L7.65,2.23L19.14,11.86C19.57,12.22 19.62,12.85 19.27,13.27C19.12,13.45 18.91,13.57 18.7,13.61L15.54,14.23L17.74,18.96C18,19.46 17.76,20.05 17.26,20.28L13.64,21.97Z",
        "pan"     => "M13,6V11H18V7.75L22.25,12L18,16.25V13H13V18H16.25L12,22.25L7.75,18H11V13H6V16.25L1.75,12L6,7.75V11H11V6H7.75L12,1.75L16.25,6H13Z",
        "draw"    => "M20.71,4.63L19.37,3.29C19,2.9 18.35,2.9 17.96,3.29L9,12.25L11.75,15L20.71,6.04C21.1,5.65 21.1,5 20.71,4.63M7,14A3,3 0 0,0 4,17C4,18.31 2.75,19 2,19C2.92,20.22 4.5,21 6,21A4,4 0 0,0 10,17A3,3 0 0,0 7,14Z",
        "text"    => "M18.5,4L19.66,8.35L18.7,8.61C18.25,7.74 17.79,6.87 17.26,6.43C16.73,6 16.11,6 15.5,6H13V16.5C13,17 13,17.5 13.33,17.75C13.67,18 14.33,18 15,18V19H9V18C9.67,18 10.33,18 10.67,17.75C11,17.5 11,17 11,16.5V6H8.5C7.89,6 7.27,6 6.74,6.43C6.21,6.87 5.75,7.74 5.3,8.61L4.34,8.35L5.5,4H18.5Z",
        "shape"   => "M3,3H21V21H3V3M5,5V19H19V5H5Z",
        "measure" => "M1.39,18.36L3.16,16.6L4.58,18L5.64,16.95L4.22,15.54L5.64,14.12L7.05,15.54L8.11,14.47L6.7,13.06L8.11,11.64L9.53,13.06L10.59,12L9.17,10.58L10.59,9.17L12,10.58L13.06,9.53L11.64,8.11L13.06,6.7L14.47,8.11L15.54,7.05L14.12,5.64L15.54,4.22L16.95,5.64L18,4.58L16.6,3.16L18.36,1.39L22.61,5.64L5.64,22.61L1.39,18.36Z",
        "laser"   => "M12,2A1,1 0 0,1 13,3V6.29C15.89,6.86 18,9.17 18,12C18,15.31 15.31,18 12,18C8.69,18 6,15.31 6,12C6,9.17 8.11,6.86 11,6.29V3A1,1 0 0,1 12,2M12,8A4,4 0 0,0 8,12A4,4 0 0,0 12,16A4,4 0 0,0 16,12A4,4 0 0,0 12,8M12,10A2,2 0 0,1 14,12A2,2 0 0,1 12,14A2,2 0 0,1 10,12A2,2 0 0,1 12,10M4,20H20V22H4V20Z",
        "fog"     => "M6,19A5,5 0 0,1 1,14A5,5 0 0,1 6,9C7,6.65 9.3,5 12,5C15.43,5 18.24,7.66 18.5,11.03L19,11A4,4 0 0,1 23,15A4,4 0 0,1 19,19H6M19,13H17V12A5,5 0 0,0 12,7C9.5,7 7.45,8.82 7.06,11.19C6.73,11.07 6.37,11 6,11A3,3 0 0,0 3,14A3,3 0 0,0 6,17H19A2,2 0 0,0 21,15A2,2 0 0,0 19,13Z",
        "attach"  => "M16.5,6V17.5A4,4 0 0,1 12.5,21.5A4,4 0 0,1 8.5,17.5V6A2.5,2.5 0 0,1 11,3.5A2.5,2.5 0 0,1 13.5,6V15.5A1,1 0 0,1 12.5,16.5A1,1 0 0,1 11.5,15.5V6H10V15.5A2.5,2.5 0 0,0 12.5,18A2.5,2.5 0 0,0 15,15.5V6A4,4 0 0,0 11,2A4,4 0 0,0 7,6V17.5A5.5,5.5 0 0,0 12.5,23A5.5,5.5 0 0,0 18,17.5V6H16.5Z",

        // 快捷操作
        "initiative" => "M6.92,5H5L14,14L15,13.06M19.96,2.29L17.25,7.71L14.29,4.75L19.96,2.29M11.29,15.04L9.96,16.37L11.38,17.79L10.32,18.85L8.9,17.43L7.84,18.5L9.26,19.91L8.19,21L2.54,15.33L3.61,14.27L5.03,15.68L6.09,14.62L4.67,13.2L5.73,12.14L7.15,13.56L8.5,12.22L11.29,15.04Z",
        "tray"       => "M2,12H4V17H20V12H22V17A2,2 0 0,1 20,19H4A2,2 0 0,1 2,17V12M12,2L7,7H10V13H14V7H17L12,2Z",
        "dice"       => "M5,3H19A2,2 0 0,1 21,5V19A2,2 0 0,1 19,21H5A2,2 0 0,1 3,19V5A2,2 0 0,1 5,3M7,7A1.5,1.5 0 0,0 5.5,8.5A1.5,1.5 0 0,0 7,10A1.5,1.5 0 0,0 8.5,8.5A1.5,1.5 0 0,0 7,7M17,7A1.5,1.5 0 0,0 15.5,8.5A1.5,1.5 0 0,0 17,10A1.5,1.5 0 0,0 18.5,8.5A1.5,1.5 0 0,0 17,7M12,10.5A1.5,1.5 0 0,0 10.5,12A1.5,1.5 0 0,0 12,13.5A1.5,1.5 0 0,0 13.5,12A1.5,1.5 0 0,0 12,10.5M7,14A1.5,1.5 0 0,0 5.5,15.5A1.5,1.5 0 0,0 7,17A1.5,1.5 0 0,0 8.5,15.5A1.5,1.5 0 0,0 7,14M17,14A1.5,1.5 0 0,0 15.5,15.5A1.5,1.5 0 0,0 17,17A1.5,1.5 0 0,0 18.5,15.5A1.5,1.5 0 0,0 17,14Z",
        "extensions" => "M20.5,11H19V7A2,2 0 0,0 17,5H13V3.5A2.5,2.5 0 0,0 10.5,1A2.5,2.5 0 0,0 8,3.5V5H4A2,2 0 0,0 2,7V10.8H3.5C5,10.8 6.2,12 6.2,13.5C6.2,15 5,16.2 3.5,16.2H2V20A2,2 0 0,0 4,22H7.8V20.5C7.8,19 9,17.8 10.5,17.8C12,17.8 13.2,19 13.2,20.5V22H17A2,2 0 0,0 19,20V16H20.5A2.5,2.5 0 0,0 23,13.5A2.5,2.5 0 0,0 20.5,11Z",

        _ => null,
    };

    /// <summary>是否有可用的 SVG 图标（用于模板在 Path / TextBlock 间切换）。</summary>
    public bool HasIconPath => IconPath is not null;

    /// <summary>图标着色：选中时高亮，未选中时次级文字色。</summary>
    public string Foreground => IsSelected ? "#F8F9FA" : "#909296";
}

/// <summary>
/// Shape 工具的子工具项（直线 / 常用形状 / 桌游形状 / 锚点多边形）。
/// </summary>
public sealed class ShapeSubToolViewModel : ViewModelBase
{
    private bool _isSelected;

    public ShapeSubToolViewModel(string key, string label, string icon, string toolTip)
    {
        Key = key;
        Label = label;
        Icon = icon;
        ToolTip = toolTip;
        _isSelected = key == "rect";
    }

    public string Key { get; }

    public string Label { get; }

    public string Icon { get; }

    public string ToolTip { get; }

    /// <summary>分组标题：用于子菜单按类别显示。</summary>
    public string Category => Key switch
    {
        "line" => "直线",
        "rect" or "circle" or "ellipse" => "常用形状",
        "cone" or "wedge" => "桌游常用形状",
        "polygon" => "锚点多边形",
        _ => "其他",
    };

    /// <summary>该项是否为其分类的首项（只有首项渲染分类标题）。</summary>
    public bool IsCategoryHeaderVisible => Key is "line" or "rect" or "cone" or "polygon";

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (SetProperty(ref _isSelected, value))
                OnPropertyChanged(nameof(Foreground));
        }
    }

    /// <summary>Material Design Icons 路径（24x24 viewBox）。</summary>
    public string? IconPath => Key switch
    {
        "line"    => "M15,3V7.5H13.5V5.56L5.56,13.5H7.5V15H3V10.5H4.5V12.44L12.44,4.5H10.5V3H15Z",
        "rect"    => "M2,4H22V20H2V4M4,6V18H20V6H4Z",
        "circle"  => "M12,2A10,10 0 0,0 2,12A10,10 0 0,0 12,22A10,10 0 0,0 22,12A10,10 0 0,0 12,2M12,4A8,8 0 0,1 20,12A8,8 0 0,1 12,20A8,8 0 0,1 4,12A8,8 0 0,1 12,4Z",
        "ellipse" => "M12,4C17.5,4 22,7.58 22,12C22,16.42 17.5,20 12,20C6.5,20 2,16.42 2,12C2,7.58 6.5,4 12,4M12,6C7.58,6 4,8.69 4,12C4,15.31 7.58,18 12,18C16.42,18 20,15.31 20,12C20,8.69 16.42,6 12,6Z",
        "cone"    => "M12,2L22,20H2L12,2M12,6.5L5.5,18H18.5L12,6.5Z",
        "wedge"   => "M12,2A10,10 0 0,1 22,12H12V2M11,4.07C7.05,4.56 4,7.92 4,12A8,8 0 0,0 12,20C15.09,20 17.79,18.25 19.13,15.69L21,16.5C19.28,19.77 15.9,22 12,22A10,10 0 0,1 2,12C2,6.82 5.94,2.55 11,2.05V4.07Z",
        "polygon" => "M12,2L2,9.27L5.82,21H18.18L22,9.27L12,2M12,4.47L19.24,9.74L16.44,19H7.56L4.76,9.74L12,4.47Z",
        _ => null,
    };

    public bool HasIconPath => IconPath is not null;

    public string Foreground => IsSelected ? "#F8F9FA" : "#909296";
}
