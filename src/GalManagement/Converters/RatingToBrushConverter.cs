using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace GalManagement.Converters;

public class RatingToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var rating = value is double d ? d : 0;
        var color = rating switch
        {
            >= 8 => "#FF4CAF50",
            >= 6 => "#FFFFC107",
            >= 4 => "#FFFF9800",
            > 0 => "#FFEF5350",
            _ => "#FF9E9E9E",
        };

        return new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
