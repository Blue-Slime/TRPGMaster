using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace MapEngine.Avalonia.Converters;

/// <summary>
/// 将 IsPlayerControlled(bool) 转换为徽章背景色：
/// true(PJ) → 蓝紫色，false(PNJ) → 灰褐色。
/// </summary>
public class BoolToPlayerBadgeBrushConverter : IValueConverter
{
    private static readonly IBrush PjBrush  = new SolidColorBrush(Color.Parse("#3B82F6")); // 蓝
    private static readonly IBrush NpcBrush = new SolidColorBrush(Color.Parse("#6B7280")); // 灰

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? PjBrush : NpcBrush;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
