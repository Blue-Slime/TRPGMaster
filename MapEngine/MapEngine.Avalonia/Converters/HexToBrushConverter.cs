using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace MapEngine.Avalonia.Converters;

/// <summary>
/// 把 "#RRGGBB" / "#AARRGGBB" 十六进制颜色字符串转成 <see cref="IBrush"/>。
/// 供 Inspector 里的颜色色块预览使用；解析失败返回透明，避免绑定异常导致整块面板不渲染。
/// </summary>
public class HexToBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string hex && !string.IsNullOrWhiteSpace(hex))
        {
            try
            {
                return new SolidColorBrush(Color.Parse(hex));
            }
            catch (FormatException)
            {
                // 用户正在输入半截 hex（如 "#84"），静默兜底
            }
        }
        return Brushes.Transparent;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
