using System.Globalization;
using System.Windows.Data;
using GalManagement.Models;

namespace GalManagement.Converters;

public class GameStatusToTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is GameStatus status
            ? status switch
            {
                GameStatus.Completed => "玩过",
                GameStatus.Playing => "在玩",
                GameStatus.WantToPlay => "想玩",
                GameStatus.Shelved => "搁置",
                _ => status.ToString(),
            }
            : string.Empty;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
