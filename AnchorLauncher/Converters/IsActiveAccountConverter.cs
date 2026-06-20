using System.Globalization;
using System.Windows.Data;
using AnchorLauncher.Models.Auth;

namespace AnchorLauncher.Converters;

public class IsActiveAccountConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2) return false;
        if (values[0] is ILauncherAccount a && values[1] is ILauncherAccount b)
            return a.Id == b.Id;
        return false;
    }

    // One-way multi-binding: nothing flows back to the sources.
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        var result = new object[targetTypes.Length];
        for (int i = 0; i < result.Length; i++) result[i] = Binding.DoNothing;
        return result;
    }
}
