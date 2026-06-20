using System.Globalization;
using System.Windows.Data;

namespace AnchorLauncher.Converters;

public class FirstLetterConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is string s && s.Length > 0 ? s[0].ToString().ToUpper() : "?";

    // One-way converter: nothing flows back to the source.
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}
