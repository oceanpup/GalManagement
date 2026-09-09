using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace GalManagement.Converters;

/// <summary>按焦点偏移把封面裁剪成竖版缩略图(3:4)。输入依次为:封面完整路径、水平焦点(0–1)、垂直焦点(0–1)。</summary>
public class ThumbnailCropConverter : IMultiValueConverter
{
    private const double TargetAspect = 48.0 / 64.0;

    public object? Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 3 || values[0] is not string path || string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return null;

        var offsetX = values[1] is double x ? Math.Clamp(x, 0, 1) : 0.5;
        var offsetY = values[2] is double y ? Math.Clamp(y, 0, 1) : 0.5;

        var bitmap = new BitmapImage();
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
        }

        var srcAspect = (double)bitmap.PixelWidth / bitmap.PixelHeight;
        int cropX, cropY, cropW, cropH;

        if (srcAspect > TargetAspect)
        {
            // 横屏:裁剪宽度
            cropH = bitmap.PixelHeight;
            cropW = Math.Max(1, (int)Math.Round(cropH * TargetAspect));
            cropX = Math.Clamp((int)Math.Round(offsetX * bitmap.PixelWidth - cropW / 2.0), 0, bitmap.PixelWidth - cropW);
            cropY = 0;
        }
        else
        {
            // 竖屏:裁剪高度
            cropW = bitmap.PixelWidth;
            cropH = Math.Max(1, (int)Math.Round(cropW / TargetAspect));
            cropY = Math.Clamp((int)Math.Round(offsetY * bitmap.PixelHeight - cropH / 2.0), 0, bitmap.PixelHeight - cropH);
            cropX = 0;
        }

        var cropped = new CroppedBitmap(bitmap, new Int32Rect(cropX, cropY, cropW, cropH));
        cropped.Freeze();
        return cropped;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
