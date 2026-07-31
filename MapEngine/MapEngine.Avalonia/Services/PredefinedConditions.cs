using MapEngine.Core.Components;

namespace MapEngine.Avalonia.Services;

/// <summary>
/// 预定义的常见 TRPG 状态库
/// </summary>
public static class PredefinedConditions
{
    public static readonly ConditionEntry[] CommonConditions =
    [
        new() { Name = "中毒", Icon = "🤢", ColorHex = "#10B981" },
        new() { Name = "昏迷", Icon = "😵", ColorHex = "#EF4444" },
        new() { Name = "隐形", Icon = "👻", ColorHex = "#8B5CF6" },
        new() { Name = "失明", Icon = "🙈", ColorHex = "#64748B" },
        new() { Name = "魅惑", Icon = "💖", ColorHex = "#EC4899" },
        new() { Name = "麻痹", Icon = "⚡", ColorHex = "#F59E0B" },
        new() { Name = "减速", Icon = "🐌", ColorHex = "#3B82F6" },
        new() { Name = "护盾", Icon = "🛡️", ColorHex = "#06B6D4" },
        new() { Name = "流血", Icon = "🩸", ColorHex = "#DC2626" },
        new() { Name = "燃烧", Icon = "🔥", ColorHex = "#F97316" },
        new() { Name = "冰冻", Icon = "❄️", ColorHex = "#0EA5E9" },
        new() { Name = "沉默", Icon = "🤐", ColorHex = "#6B7280" },
        new() { Name = "虚弱", Icon = "😰", ColorHex = "#A78BFA" },
        new() { Name = "狂暴", Icon = "😡", ColorHex = "#B91C1C" },
        new() { Name = "加速", Icon = "💨", ColorHex = "#22D3EE" },
        new() { Name = "反射", Icon = "✨", ColorHex = "#FBBF24" },
    ];

    /// <summary>
    /// 创建预设状态的副本（含新 ID）
    /// </summary>
    public static ConditionEntry Clone(ConditionEntry template)
    {
        return new ConditionEntry
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = template.Name,
            Icon = template.Icon,
            StackCount = template.StackCount,
            RemainingRounds = template.RemainingRounds,
            ColorHex = template.ColorHex
        };
    }
}
