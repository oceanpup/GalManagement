using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace GalManagement.Converters;

/// <summary>布尔值取反后映射为可见性:false → Visible,true → Collapsed。</summary>
public class InverseBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
