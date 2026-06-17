using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace AnchorLauncher.Converters;

/// <summary>true → Collapsed, false → Visible. Used for empty-state panels.</summary>
public class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is Visibility.Collapsed;
}
