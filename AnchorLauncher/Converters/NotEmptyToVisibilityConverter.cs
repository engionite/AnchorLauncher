using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace AnchorLauncher.Converters;

/// <summary>Visible when the bound value is a non-empty string (else Collapsed).</summary>
public class NotEmptyToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is string s && !string.IsNullOrWhiteSpace(s) ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}
