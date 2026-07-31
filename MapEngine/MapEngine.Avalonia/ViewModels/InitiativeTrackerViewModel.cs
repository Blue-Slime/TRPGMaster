using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace MapEngine.Avalonia.ViewModels;

/// <summary>
/// 先攻追踪器面板 ViewModel，管理战斗回合顺序。
/// </summary>
public sealed class InitiativeTrackerViewModel : ObservableObject
{
    private readonly MainWindowViewModel _mainWindow;
    private int _currentTurnIndex;
    private int _turnCounter;

    public InitiativeTrackerViewModel(MainWindowViewModel mainWindow)
    {
        _mainWindow = mainWindow;
        Entries = new ObservableCollection<InitiativeEntryViewModel>();

        NextTurnCommand = new RelayCommand(NextTurn, () => Entries.Count > 0);
        PreviousTurnCommand = new RelayCommand(PreviousTurn, () => Entries.Count > 0);
        ResetTurnCommand = new RelayCommand(ResetTurn);
        SelectEntryCommand = new RelayCommand<InitiativeEntryViewModel>(SelectEntry);

        // 监听 HierarchyRoots 变化（增删节点时）
        _mainWindow.HierarchyRoots.CollectionChanged += (_, _) => RefreshEntries();

        // 监听所有对象的 ObjectType 变化（非Token 变 Token 时刷新）
        _mainWindow.HierarchyRoots.CollectionChanged += OnHierarchyChanged;

        // 订阅所有现有节点的 ObjectType 变化
        foreach (var root in _mainWindow.HierarchyRoots)
        {
            SubscribeToObjectTypeChange(root);
        }

        RefreshEntries();
    }

    public ObservableCollection<InitiativeEntryViewModel> Entries { get; }

    public int TurnCounter
    {
        get => _turnCounter;
        private set => SetProperty(ref _turnCounter, value);
    }

    public RelayCommand NextTurnCommand { get; }
    public RelayCommand PreviousTurnCommand { get; }
    public RelayCommand ResetTurnCommand { get; }
    public RelayCommand<InitiativeEntryViewModel> SelectEntryCommand { get; }

    /// <summary>从先攻表移除指定条目（由 UI 直接调用）。</summary>
    public void RemoveEntry(InitiativeEntryViewModel entry)
    {
        _mainWindow.RemoveFromInitiativeTracker(entry.Source);
    }

    public void RefreshEntries()
    {
        // 取消旧订阅
        foreach (var entry in Entries)
        {
            UnsubscribeFromEntry(entry);
        }

        Entries.Clear();

        // 过滤所有 Token 对象且 IsInInitiativeTracker = true
        // 防御性编程：过滤掉 Token 组件缺失或对象状态异常的情况
        var tokens = _mainWindow.HierarchyItems
            .Where(x => x != null && x.ObjectType == "Token")
            .Select(x => new
            {
                Item = x,
                TokenComp = x.GetComponent<MapEngine.Core.Components.TokenComponent>()
            })
            .Where(x => x.TokenComp != null && x.TokenComp.IsInInitiativeTracker)
            .Select(x => x.Item)
            .OrderByDescending(x => x.InitiativeOrder)
            .ThenBy(x => x.DisplayName)
            .ToList(); // 立即求值，避免延迟执行时组件状态变化

        System.Diagnostics.Debug.WriteLine($"[RefreshEntries] 扫描到 {_mainWindow.HierarchyItems.Count()} 个对象，其中 {tokens.Count} 个在先攻表中");
        foreach (var item in _mainWindow.HierarchyItems.Where(x => x != null && x.ObjectType == "Token"))
        {
            var tokenComp = item.GetComponent<MapEngine.Core.Components.TokenComponent>();
            System.Diagnostics.Debug.WriteLine($"  - {item.Name}: ObjectType='{item.ObjectType}', IsInInitiativeTracker={tokenComp?.IsInInitiativeTracker ?? false}");
        }

        foreach (var token in tokens)
        {
            // 二次检查：确保 Token 组件仍然存在
            var tokenComp = token.GetComponent<MapEngine.Core.Components.TokenComponent>();
            if (tokenComp != null && tokenComp.IsInInitiativeTracker)
            {
                var entry = new InitiativeEntryViewModel(token);
                SubscribeToEntry(entry);
                Entries.Add(entry);
            }
        }

        // 刷新后调整当前回合索引，防止越界
        AdjustCurrentTurnAfterRemoval();
        NextTurnCommand.NotifyCanExecuteChanged();
        PreviousTurnCommand.NotifyCanExecuteChanged();
    }

    private void SubscribeToEntry(InitiativeEntryViewModel entry)
    {
        entry.Source.PropertyChanged += OnEntryPropertyChanged;
    }

    private void UnsubscribeFromEntry(InitiativeEntryViewModel entry)
    {
        entry.Source.PropertyChanged -= OnEntryPropertyChanged;
    }

    private void OnEntryPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // 如果先攻值或对象类型改变，重新排序
        if (e.PropertyName == nameof(HierarchyItemViewModel.InitiativeOrder) ||
            e.PropertyName == nameof(HierarchyItemViewModel.ObjectType))
        {
            RefreshEntries();
        }

        // 监听 TokenComponent 的任何属性变化（包括 IsInInitiativeTracker）
        if (e.PropertyName == "Components")
        {
            // 防御性检查：如果是 Token 组件被删除导致的变化，刷新列表
            if (sender is HierarchyItemViewModel item)
            {
                var tokenComp = item.GetComponent<MapEngine.Core.Components.TokenComponent>();
                if (tokenComp == null)
                {
                    System.Diagnostics.Debug.WriteLine($"[OnEntryPropertyChanged] {item.Name} 的 Token 组件已被删除，刷新先攻表");
                }
            }
            RefreshEntries();
        }
    }

    private void NextTurn()
    {
        if (Entries.Count == 0) return;

        _currentTurnIndex = (_currentTurnIndex + 1) % Entries.Count;
        if (_currentTurnIndex == 0)
        {
            TurnCounter++;
            DecrementAllConditionRounds();
        }
        UpdateCurrentTurnFlags();
    }

    /// <summary>回合结束时递减所有 Token 的状态回合数，移除归零的状态。</summary>
    private void DecrementAllConditionRounds()
    {
        foreach (var entry in Entries)
        {
            var tokenComp = entry.Source.GetComponent<MapEngine.Core.Components.TokenComponent>();
            if (tokenComp == null) continue;

            // 倒序遍历，方便移除元素
            for (int i = tokenComp.Conditions.Count - 1; i >= 0; i--)
            {
                var condition = tokenComp.Conditions[i];
                if (condition.RemainingRounds > 0)
                {
                    condition.RemainingRounds--;
                    if (condition.RemainingRounds == 0)
                    {
                        // 回合数归零，移除状态
                        tokenComp.Conditions.RemoveAt(i);
                        System.Diagnostics.Debug.WriteLine($"[DecrementAllConditionRounds] {entry.Name} 的状态 '{condition.Name}' 已到期移除");
                    }
                }
            }

            // 通知 UI 刷新状态列表
            entry.Source.NotifyTokenComponentChanged();
        }
    }

    private void PreviousTurn()
    {
        if (Entries.Count == 0) return;

        _currentTurnIndex = (_currentTurnIndex - 1 + Entries.Count) % Entries.Count;
        if (_currentTurnIndex == Entries.Count - 1)
        {
            TurnCounter = Math.Max(1, TurnCounter - 1);
        }
        UpdateCurrentTurnFlags();
    }

    private void ResetTurn()
    {
        _currentTurnIndex = 0;
        TurnCounter = 1;
        UpdateCurrentTurnFlags();
    }

    private void UpdateCurrentTurnFlags()
    {
        for (int i = 0; i < Entries.Count; i++)
        {
            Entries[i].IsCurrentTurn = (i == _currentTurnIndex);
        }
    }

    private void AdjustCurrentTurnAfterRemoval()
    {
        if (Entries.Count == 0)
        {
            _currentTurnIndex = 0;
            return;
        }

        if (_currentTurnIndex >= Entries.Count)
        {
            _currentTurnIndex = 0;
        }

        UpdateCurrentTurnFlags();
    }

    private void SelectEntry(InitiativeEntryViewModel? entry)
    {
        if (entry == null) return;
        _mainWindow.SelectedHierarchyItem = entry.Source;
    }

    private void OnHierarchyChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // 订阅新增节点的 ObjectType 属性变化
        if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems != null)
        {
            foreach (HierarchyItemViewModel item in e.NewItems)
            {
                SubscribeToObjectTypeChange(item);
            }
        }

        // 取消订阅移除节点
        if (e.Action == NotifyCollectionChangedAction.Remove && e.OldItems != null)
        {
            foreach (HierarchyItemViewModel item in e.OldItems)
            {
                UnsubscribeFromObjectTypeChange(item);
            }
        }
    }

    private void SubscribeToObjectTypeChange(HierarchyItemViewModel item)
    {
        item.PropertyChanged += OnAnyItemPropertyChanged;
        item.PropertyChanged += OnComponentsPropertyChanged;  // 订阅 Components 变化

        // 订阅子节点集合变化（当子节点被添加/删除时）
        item.Children.CollectionChanged += OnChildrenCollectionChanged;

        // 递归订阅现有子节点
        foreach (var child in item.Children)
        {
            SubscribeToObjectTypeChange(child);
        }
    }

    private void UnsubscribeFromObjectTypeChange(HierarchyItemViewModel item)
    {
        item.PropertyChanged -= OnAnyItemPropertyChanged;
        item.PropertyChanged -= OnComponentsPropertyChanged;  // 取消订阅 Components 变化

        // 取消订阅子节点集合变化
        item.Children.CollectionChanged -= OnChildrenCollectionChanged;

        // 递归取消订阅子节点
        foreach (var child in item.Children)
        {
            UnsubscribeFromObjectTypeChange(child);
        }
    }

    /// <summary>
    /// 当任意对象的子节点集合变化时（新增/删除子节点），订阅新节点并刷新
    /// </summary>
    private void OnChildrenCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // 订阅新增的子节点
        if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems != null)
        {
            foreach (HierarchyItemViewModel item in e.NewItems)
            {
                SubscribeToObjectTypeChange(item);
                System.Diagnostics.Debug.WriteLine($"[OnChildrenCollectionChanged] 订阅新节点: {item.Name}");
            }
        }

        // 取消订阅移除的子节点
        if (e.Action == NotifyCollectionChangedAction.Remove && e.OldItems != null)
        {
            foreach (HierarchyItemViewModel item in e.OldItems)
            {
                UnsubscribeFromObjectTypeChange(item);
                System.Diagnostics.Debug.WriteLine($"[OnChildrenCollectionChanged] 取消订阅节点: {item.Name}");
            }
        }

        // 子节点变化时刷新先攻列表
        RefreshEntries();
    }

    private void OnAnyItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // 任何对象的 ObjectType 变化都刷新先攻列表
        if (e.PropertyName == nameof(HierarchyItemViewModel.ObjectType))
        {
            var item = sender as HierarchyItemViewModel;
            System.Diagnostics.Debug.WriteLine($"[OnAnyItemPropertyChanged] {item?.Name} ObjectType 变为 '{item?.ObjectType}'，触发 RefreshEntries");
            RefreshEntries();
        }
    }

    /// <summary>
    /// 监听所有对象的 Components 属性变化（包括 IsInInitiativeTracker 切换）
    /// </summary>
    private void OnComponentsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == "Components")
        {
            var item = sender as HierarchyItemViewModel;
            if (item != null)
            {
                var tokenComp = item.GetComponent<MapEngine.Core.Components.TokenComponent>();
                if (tokenComp == null)
                {
                    System.Diagnostics.Debug.WriteLine($"[OnComponentsPropertyChanged] {item.Name} Token 组件已删除或缺失，触发 RefreshEntries");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[OnComponentsPropertyChanged] {item.Name} Components 变化 (IsInInitiativeTracker={tokenComp.IsInInitiativeTracker})，触发 RefreshEntries");
                }
            }
            RefreshEntries();
        }
    }
}
