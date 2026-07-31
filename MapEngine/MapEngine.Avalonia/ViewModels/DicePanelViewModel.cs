using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace MapEngine.Avalonia.ViewModels;

/// <summary>
/// 骰子面板 ViewModel。支持标准 TRPG 公式：
/// XdY、XdY+Z、XdY-Z、XdYkh/kl（keep highest/lowest）。
/// </summary>
public sealed class DicePanelViewModel : ObservableObject
{
    private static readonly Random _rng = new();

    // 最多保留最近 50 条历史
    private const int MaxHistory = 50;

    private string _formula = "1d20";
    private string _errorMessage = string.Empty;
    private DiceResultViewModel? _lastResult;

    public DicePanelViewModel()
    {
        History = new ObservableCollection<DiceResultViewModel>();
        RollCommand         = new RelayCommand(Roll, () => !string.IsNullOrWhiteSpace(_formula));
        RollPresetCommand   = new RelayCommand<string>(RollPreset);
        ClearHistoryCommand = new RelayCommand(() => History.Clear());
    }

    // ── 绑定属性 ──────────────────────────────────────────────────────

    public string Formula
    {
        get => _formula;
        set
        {
            if (SetProperty(ref _formula, value))
            {
                ErrorMessage = string.Empty;
                RollCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        private set => SetProperty(ref _errorMessage, value);
    }

    public DiceResultViewModel? LastResult
    {
        get => _lastResult;
        private set => SetProperty(ref _lastResult, value);
    }

    public ObservableCollection<DiceResultViewModel> History { get; }

    // ── 命令 ──────────────────────────────────────────────────────────

    public RelayCommand RollCommand { get; }
    public RelayCommand<string> RollPresetCommand { get; }
    public RelayCommand ClearHistoryCommand { get; }

    // ── 快捷骰预设 ────────────────────────────────────────────────────

    public static readonly DicePreset[] Presets =
    [
        new("d4",   "🔺", "d4",   "#8B5CF6"),
        new("d6",   "⬛", "d6",   "#3B82F6"),
        new("d8",   "🔷", "d8",   "#06B6D4"),
        new("d10",  "🔹", "d10",  "#10B981"),
        new("d12",  "💠", "d12",  "#F59E0B"),
        new("d20",  "🎲", "d20",  "#EF4444"),
        new("d100", "💯", "d100", "#6B7280"),
    ];

    // ── 核心投掷逻辑 ──────────────────────────────────────────────────

    private void RollPreset(string? preset)
    {
        if (preset is null) return;
        Formula = preset;
        Roll();
    }

    private void Roll()
    {
        ErrorMessage = string.Empty;
        var formula = _formula.Trim();
        if (string.IsNullOrEmpty(formula)) return;

        try
        {
            var result = EvaluateFormula(formula);
            LastResult = result;

            // 推入历史（最新在前）
            History.Insert(0, result);
            while (History.Count > MaxHistory)
                History.RemoveAt(History.Count - 1);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    // ── 公式解析器 ────────────────────────────────────────────────────
    // 支持格式：[X]dY[kh/kl[N]][+/-Z]
    // 示例：1d20  2d6+3  d20  4d6kh3  3d8-1  2d10kl1

    private static readonly Regex FormulaPattern = new(
        @"^(?:(?<count>\d+)?d(?<sides>\d+)(?:kh(?<kh>\d+)|kl(?<kl>\d+))?(?<mod>[+-]\d+)?|(?<flat>\d+))$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static DiceResultViewModel EvaluateFormula(string formula)
    {
        // 支持复合公式如 "2d6+1d4+3"？先处理单段，扩展留后
        var m = FormulaPattern.Match(formula.Replace(" ", ""));
        if (!m.Success)
            throw new FormatException($"无法识别的公式：\"{formula}\"");

        // 纯常数
        if (m.Groups["flat"].Success)
        {
            int flat = int.Parse(m.Groups["flat"].Value);
            return new DiceResultViewModel(formula, flat, [], flat.ToString(), flat.ToString());
        }

        int count  = m.Groups["count"].Success  ? int.Parse(m.Groups["count"].Value)  : 1;
        int sides  = int.Parse(m.Groups["sides"].Value);
        int mod    = m.Groups["mod"].Success    ? int.Parse(m.Groups["mod"].Value)    : 0;

        if (count < 1 || count > 100)
            throw new ArgumentOutOfRangeException(nameof(count), $"骰子数量须在 1-100 之间，收到 {count}");
        if (sides < 2 || sides > 1000)
            throw new ArgumentOutOfRangeException(nameof(sides), $"骰子面数须在 2-1000 之间，收到 {sides}");

        var rolls = Enumerable.Range(0, count).Select(_ => _rng.Next(1, sides + 1)).ToArray();

        int[] kept;
        string keepNote = string.Empty;

        if (m.Groups["kh"].Success)
        {
            int n = int.Parse(m.Groups["kh"].Value);
            n = Math.Min(n, count);
            kept = rolls.OrderByDescending(x => x).Take(n).ToArray();
            keepNote = $" kh{n}";
        }
        else if (m.Groups["kl"].Success)
        {
            int n = int.Parse(m.Groups["kl"].Value);
            n = Math.Min(n, count);
            kept = rolls.OrderBy(x => x).Take(n).ToArray();
            keepNote = $" kl{n}";
        }
        else
        {
            kept = rolls;
        }

        int total = kept.Sum() + mod;

        // 骰子详情字符串 "[3, 14, 7]" or "[~~3~~, 14, 7]"（弃骰标记）
        var keptSet = kept.ToHashSet();
        var rollsDisplay = string.Join(", ", rolls.Select(r =>
        {
            // 标记弃骰（若 kh/kl 激活且该值不在 kept 集里）
            bool discarded = keepNote.Length > 0 && !keptSet.Contains(r);
            return discarded ? $"({r})" : r.ToString();
        }));

        string detailStr = count == 1 && kept.Length == 1
            ? rolls[0].ToString()
            : $"[{rollsDisplay}]{keepNote}";

        if (mod != 0)
            detailStr += (mod > 0 ? $" + {mod}" : $" - {Math.Abs(mod)}");

        return new DiceResultViewModel(formula, total, rolls, total.ToString(), detailStr);
    }
}

/// <summary>单次投掷结果（历史记录条目）。</summary>
public sealed class DiceResultViewModel : ObservableObject
{
    public DiceResultViewModel(string formula, int total, int[] individualRolls, string totalDisplay, string detailDisplay)
    {
        Formula        = formula;
        Total          = total;
        IndividualRolls = individualRolls;
        TotalDisplay   = totalDisplay;
        DetailDisplay  = detailDisplay;
        Timestamp      = DateTime.Now;
    }

    public string   Formula         { get; }
    public int      Total           { get; }
    public int[]    IndividualRolls { get; }
    public string   TotalDisplay    { get; }
    public string   DetailDisplay   { get; }
    public DateTime Timestamp       { get; }

    public string TimestampDisplay  => Timestamp.ToString("HH:mm:ss");

    // 结果颜色：20面骰满值/1 特殊着色（仅 d20 自然大/小成功）
    public bool IsNaturalCritical => IndividualRolls.Length == 1 && IndividualRolls[0] == 20 && Formula.Contains("20");
    public bool IsNaturalFumble   => IndividualRolls.Length == 1 && IndividualRolls[0] == 1  && Formula.Contains("20");
}

/// <summary>快捷骰预设按钮数据。</summary>
public sealed record DicePreset(string Formula, string Icon, string Label, string ColorHex);
