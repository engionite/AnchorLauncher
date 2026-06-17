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

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
