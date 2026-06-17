using System.Globalization;
using System.Windows.Data;

namespace AnchorLauncher.Converters;

/// <summary>Two-way: true when the bound int equals the ConverterParameter index (for tab toggles).</summary>
public class IndexToBooleanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is int i && parameter is string p && int.TryParse(p, out var target) && i == target;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is true && parameter is string p && int.TryParse(p, out var target))
            return target;
        return Binding.DoNothing; // ignore the uncheck so a tab can't deselect itself
    }
}
