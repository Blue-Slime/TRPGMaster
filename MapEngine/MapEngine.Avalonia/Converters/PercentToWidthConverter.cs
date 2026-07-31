using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace MapEngine.Avalonia.Converters;

/// <summary>
/// 将百分比 (0.0-1.0) 转换为血量条宽度（固定 80px 血条）
/// </summary>
public class PercentToWidthConverter : IValueConverter
{
    private const double MaxWidth = 80.0;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is double percent)
        {
            return Math.Max(0, Math.Min(MaxWidth, percent * MaxWidth));
        }
        return 0.0;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
