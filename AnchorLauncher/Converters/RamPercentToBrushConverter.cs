using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace AnchorLauncher.Converters;

/// <summary>RAM gauge colour: green &lt; 70%, amber 70–85%, red &gt; 85%.</summary>
public class RamPercentToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush Green = Freeze("#10B981");
    private static readonly SolidColorBrush Amber = Freeze("#F59E0B");
    private static readonly SolidColorBrush Red   = Freeze("#EF4444");

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var p = value is double d ? d : 0;
        return p > 85 ? Red : p >= 70 ? Amber : Green;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;

    private static SolidColorBrush Freeze(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        b.Freeze();
        return b;
    }
}
