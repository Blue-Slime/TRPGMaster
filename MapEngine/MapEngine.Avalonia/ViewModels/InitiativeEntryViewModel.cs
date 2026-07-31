using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;

namespace MapEngine.Avalonia.ViewModels;

/// <summary>
/// 先攻追踪器中的单个条目，包装 HierarchyItemViewModel 并暴露 Token 字段。
/// </summary>
public sealed class InitiativeEntryViewModel : ObservableObject
{
    private readonly HierarchyItemViewModel _source;
    private bool _isCurrentTurn;

    public InitiativeEntryViewModel(HierarchyItemViewModel source)
    {
        _source = source;

        // 订阅源对象的属性变化，转发到此 ViewModel
        _source.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(_source.DisplayName))
                OnPropertyChanged(nameof(Name));
            else if (e.PropertyName == nameof(_source.InitiativeOrder))
                OnPropertyChanged(nameof(InitiativeOrder));
            else if (e.PropertyName == nameof(_source.CurrentHP))
                OnPropertyChanged(nameof(CurrentHP));
            else if (e.PropertyName == nameof(_source.MaxHP))
                OnPropertyChanged(nameof(MaxHP));
        };
    }

    public HierarchyItemViewModel Source => _source;

    public string Name => _source.DisplayName;
    public string Icon => _source.Icon;
    public int InitiativeOrder => _source.InitiativeOrder;

    public int CurrentHP
    {
        get => _source.CurrentHP;
        set => _source.CurrentHP = value;
    }

    public int MaxHP
    {
        get => _source.MaxHP;
        set => _source.MaxHP = value;
    }

    public double HPPercent => MaxHP > 0 ? (double)CurrentHP / MaxHP : 0.0;

    public bool IsCurrentTurn
    {
        get => _isCurrentTurn;
        set => SetProperty(ref _isCurrentTurn, value);
    }

    /// <summary>Token 状态列表（来自 TokenComponent.Conditions）</summary>
    public ObservableCollection<ConditionEntryViewModel> TokenConditions
    {
        get
        {
            var tokenEditor = _source.ComponentEditors
                .OfType<TokenComponentEditor>()
                .FirstOrDefault();
            return tokenEditor?.Conditions ?? new ObservableCollection<ConditionEntryViewModel>();
        }
    }
}
