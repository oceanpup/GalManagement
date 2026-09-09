using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using GalManagement.Models;

namespace GalManagement.Converters;

public class GameStatusToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var color = value is GameStatus status
            ? status switch
            {
                GameStatus.Completed => "#FF4CAF50",
                GameStatus.Playing => "#FF2196F3",
                GameStatus.WantToPlay => "#FFFF9800",
                GameStatus.Shelved => "#FF9E9E9E",
                _ => "#FF9E9E9E",
            }
            : "#FF9E9E9E";

        return new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
