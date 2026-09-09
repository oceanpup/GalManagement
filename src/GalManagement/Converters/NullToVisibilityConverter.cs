using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace GalManagement.Converters;

/// <summary>null 或空白字符串映射为可见性;参数为 "invert" 时取反。</summary>
public class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var isNull = value is null || (value is string s && string.IsNullOrWhiteSpace(s));
        if (parameter?.ToString() == "invert")
            isNull = !isNull;
        return isNull ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
