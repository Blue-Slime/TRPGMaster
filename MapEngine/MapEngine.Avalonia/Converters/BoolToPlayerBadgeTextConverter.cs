using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace MapEngine.Avalonia.Converters;

/// <summary>
/// 将 IsPlayerControlled(bool) 转换为徽章文字：
/// true → "PJ"，false → "PNJ"。
/// </summary>
public class BoolToPlayerBadgeTextConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? "PJ" : "PNJ";

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
