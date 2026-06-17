using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace AnchorLauncher.Converters;

/// <summary>Crops the 8×8 face region from a local skin PNG path → a crisp head thumbnail.</summary>
public class SkinFileHeadConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        try
        {
            if (value is not string path || !File.Exists(path)) return null;

            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption  = BitmapCacheOption.OnLoad;
            bmp.UriSource    = new Uri(path);
            bmp.EndInit();
            bmp.Freeze();

            if (bmp.PixelWidth < 16) return bmp;                 // too small to crop a face
            var head = new CroppedBitmap(bmp, new System.Windows.Int32Rect(8, 8, 8, 8));
            head.Freeze();
            return head;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SkinFileHead] crop failed: {ex.Message}");
            return null;
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}
