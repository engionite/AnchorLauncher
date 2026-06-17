using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace AnchorLauncher.Converters;

/// <summary>Visible when the bound int equals the ConverterParameter index; else Collapsed.</summary>
public class IndexToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int i && parameter is string p && int.TryParse(p, out var target))
            return i == target ? Visibility.Visible : Visibility.Collapsed;
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}
