using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace MapEngine.Avalonia.Converters;

/// <summary>
/// 将字符串与 ConverterParameter 比较，相等返回 true。
/// </summary>
public class StringEqualsConverter : IValueConverter
{
    public static readonly StringEqualsConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string str || parameter is not string target)
            return false;

        return string.Equals(str, target, StringComparison.Ordinal);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
