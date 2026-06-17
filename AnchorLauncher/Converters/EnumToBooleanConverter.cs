using System.Globalization;
using System.Windows.Data;

namespace AnchorLauncher.Converters;

/// <summary>
/// Two-way enum ↔ bool binding for radio buttons. ConverterParameter is the enum member
/// name; IsChecked is true when the bound value equals that member.
/// </summary>
public class EnumToBooleanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value != null && parameter is string name &&
           value.ToString()!.Equals(name, StringComparison.Ordinal);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is true && parameter is string name)
            return Enum.Parse(targetType, name);
        return Binding.DoNothing;
    }
}
