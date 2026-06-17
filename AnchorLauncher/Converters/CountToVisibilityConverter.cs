using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace AnchorLauncher.Converters;

/// <summary>Visible when the bound count is zero — for empty-state placeholders.</summary>
public class CountToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is int n && n == 0 ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}
