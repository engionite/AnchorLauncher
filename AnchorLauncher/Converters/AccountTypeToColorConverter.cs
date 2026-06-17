using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using AnchorLauncher.Models.Auth;

namespace AnchorLauncher.Converters;

public class AccountTypeToColorConverter : IValueConverter
{
    private static readonly SolidColorBrush MicrosoftBrush =
        new(Color.FromRgb(0xFF, 0x45, 0x00)); // #FF4500 accent

    private static readonly SolidColorBrush ElyByBrush =
        new(Color.FromRgb(0x00, 0xB4, 0xFF)); // #00B4FF bedrock blue

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is AccountType t && t == AccountType.ElyBy ? ElyByBrush : MicrosoftBrush;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
