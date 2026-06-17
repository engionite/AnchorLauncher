using System.Globalization;
using System.Windows.Data;
using AnchorLauncher.Models.Auth;

namespace AnchorLauncher.Converters;

/// <summary>Localized account-type subtitle ("Ely.by Account" / "Microsoft Account").</summary>
public class AccountTypeToStringConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var loc = Services.Platform.Loc.I;
        return value is AccountType.ElyBy ? loc["acc_ely"] : loc["acc_ms"];
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}
