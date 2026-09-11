using System.Globalization;
using System.Windows.Data;

namespace GalManagement.Converters;

/// <summary>布尔取反(用于 IsEnabled 等反向绑定)。</summary>
public class InverseBooleanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is not true;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is not true;
}
