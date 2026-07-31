using CommunityToolkit.Mvvm.ComponentModel;
using MapEngine.Core.Components;

namespace MapEngine.Avalonia.ViewModels;

/// <summary>
/// Token 状态条目的 ViewModel 包装，用于 UI 绑定
/// </summary>
public sealed partial class ConditionEntryViewModel : ObservableObject
{
    private readonly ConditionEntry _source;

    public ConditionEntryViewModel(ConditionEntry source)
    {
        _source = source;
    }

    public ConditionEntry Source => _source;

    public string Id => _source.Id;

    public string Name
    {
        get => _source.Name;
        set
        {
            if (_source.Name != value)
            {
                _source.Name = value;
                OnPropertyChanged();
            }
        }
    }

    public string Icon
    {
        get => _source.Icon;
        set
        {
            if (_source.Icon != value)
            {
                _source.Icon = value;
                OnPropertyChanged();
            }
        }
    }

    public int StackCount
    {
        get => _source.StackCount;
        set
        {
            if (_source.StackCount != value)
            {
                _source.StackCount = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(StackCountText));
            }
        }
    }

    public int RemainingRounds
    {
        get => _source.RemainingRounds;
        set
        {
            if (_source.RemainingRounds != value)
            {
                _source.RemainingRounds = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(RemainingRoundsText));
            }
        }
    }

    public string ColorHex
    {
        get => _source.ColorHex;
        set
        {
            if (_source.ColorHex != value)
            {
                _source.ColorHex = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>堆叠显示文本：大于 1 时显示 "×N"</summary>
    public string StackCountText => StackCount > 1 ? $"×{StackCount}" : string.Empty;

    /// <summary>回合倒计时文本：-1 显示 "永久"，否则显示回合数</summary>
    public string RemainingRoundsText => RemainingRounds < 0 ? "永久" : $"{RemainingRounds} 回合";
}
