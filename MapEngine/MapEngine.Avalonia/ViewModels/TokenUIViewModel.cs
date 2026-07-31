using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using Avalonia.Media;
using MapEngine.Core.Components;

namespace MapEngine.Avalonia.ViewModels;

/// <summary>
/// Token UI 覆盖层的 ViewModel（名字标签 + 血量条 + 状态徽章）
/// 坐标为屏幕坐标，由 TokenUIManager 从世界坐标转换而来
/// </summary>
public partial class TokenUIViewModel : ViewModelBase
{
    [ObservableProperty] private string _id = string.Empty;
    [ObservableProperty] private string _name = string.Empty;

    /// <summary>状态效果列表（由 TokenUIManager 每帧同步）</summary>
    public List<ConditionEntry> Conditions { get; set; } = [];

    /// <summary>屏幕坐标 X（Canvas.Left）</summary>
    [ObservableProperty] private double _screenX;

    /// <summary>屏幕坐标 Y（Canvas.Top）</summary>
    [ObservableProperty] private double _screenY;

    [ObservableProperty] private int _currentHP = 100;
    [ObservableProperty] private int _maxHP = 100;
    [ObservableProperty] private bool _isVisible = true;

    /// <summary>血量百分比 (0.0 - 1.0)</summary>
    public double HPPercent => MaxHP > 0 ? (double)CurrentHP / MaxHP : 0;

    /// <summary>血量条颜色（绿→黄→红）</summary>
    public IBrush HPBarColor
    {
        get
        {
            var percent = HPPercent;
            if (percent > 0.6) return Brushes.Green;
            if (percent > 0.3) return Brushes.Yellow;
            return Brushes.Red;
        }
    }

    /// <summary>血量条背景色</summary>
    public IBrush HPBarBackground => new SolidColorBrush(Color.FromArgb(128, 80, 0, 0));

    partial void OnCurrentHPChanged(int value)
    {
        OnPropertyChanged(nameof(HPPercent));
        OnPropertyChanged(nameof(HPBarColor));
    }

    partial void OnMaxHPChanged(int value)
    {
        OnPropertyChanged(nameof(HPPercent));
        OnPropertyChanged(nameof(HPBarColor));
    }
}
