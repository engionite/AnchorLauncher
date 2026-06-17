using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace AnchorLauncher.Converters;

/// <summary>Color-codes a console line by detected log level (INFO/WARN/ERROR/FATAL).</summary>
public class LogLevelToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush Error  = Freeze("#FF6B5B");
    private static readonly SolidColorBrush Warn   = Freeze("#F59E0B");
    private static readonly SolidColorBrush Info   = Freeze("#C8C8CE");
    private static readonly SolidColorBrush Muted  = Freeze("#8A8A90");
    private static readonly SolidColorBrush Anchor = Freeze("#FF7A45");

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var line = value as string ?? string.Empty;
        if (line.StartsWith("[Anchor]", StringComparison.Ordinal)) return Anchor;
        if (Contains(line, "ERROR") || Contains(line, "FATAL") || Contains(line, "Exception")) return Error;
        if (Contains(line, "WARN")) return Warn;
        if (Contains(line, "INFO")) return Info;
        return Muted;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;

    private static bool Contains(string s, string token)
        => s.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;

    private static SolidColorBrush Freeze(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        b.Freeze();
        return b;
    }
}
