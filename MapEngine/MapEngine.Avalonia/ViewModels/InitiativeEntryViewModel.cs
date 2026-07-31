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
            {
                OnPropertyChanged(nameof(CurrentHP));
                OnPropertyChanged(nameof(HPPercent));
            }
            else if (e.PropertyName == nameof(_source.MaxHP))
            {
                OnPropertyChanged(nameof(MaxHP));
                OnPropertyChanged(nameof(HPPercent));
            }
            else if (e.PropertyName == "Components")
            {
                // Token 组件变化时刷新状态列表
                OnPropertyChanged(nameof(TokenConditions));
            }
        };
    }

    public HierarchyItemViewModel Source => _source;

    public string Name => _source.DisplayName;
    public string Icon => _source.Icon;
    public int InitiativeOrder => _source.InitiativeOrder;

    public int CurrentHP
    {
        get
        {
            // 防御性：如果 Token 组件被删除，返回默认值
            var tokenComp = _source.GetComponent<MapEngine.Core.Components.TokenComponent>();
            return tokenComp?.CurrentHP ?? 0;
        }
        set
        {
            var tokenComp = _source.GetComponent<MapEngine.Core.Components.TokenComponent>();
            if (tokenComp != null)
            {
                _source.CurrentHP = value;
            }
        }
    }

    public int MaxHP
    {
        get
        {
            // 防御性：如果 Token 组件被删除，返回默认值
            var tokenComp = _source.GetComponent<MapEngine.Core.Components.TokenComponent>();
            return tokenComp?.MaxHP ?? 100;
        }
        set
        {
            var tokenComp = _source.GetComponent<MapEngine.Core.Components.TokenComponent>();
            if (tokenComp != null)
            {
                _source.MaxHP = value;
            }
        }
    }

    public double HPPercent
    {
        get
        {
            var maxHP = MaxHP;
            return maxHP > 0 ? (double)CurrentHP / maxHP : 0.0;
        }
    }

    public bool IsCurrentTurn
    {
        get => _isCurrentTurn;
        set => SetProperty(ref _isCurrentTurn, value);
    }

    /// <summary>Token 状态列表（来自 TokenComponent.Conditions）。防御性：组件缺失时返回空集合。</summary>
    public ObservableCollection<ConditionEntryViewModel> TokenConditions
    {
        get
        {
            // 防御性检查：如果 Token 组件被删除，返回空集合
            var tokenComp = _source.GetComponent<MapEngine.Core.Components.TokenComponent>();
            if (tokenComp == null)
            {
                return new ObservableCollection<ConditionEntryViewModel>();
            }

            var tokenEditor = _source.ComponentEditors
                .OfType<TokenComponentEditor>()
                .FirstOrDefault();
            return tokenEditor?.Conditions ?? new ObservableCollection<ConditionEntryViewModel>();
        }
    }
}
