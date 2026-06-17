using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using AnchorLauncher.Models.Instances;

namespace AnchorLauncher.Converters;

/// <summary>
/// Renders a <see cref="VersionStatusKind"/> as the instance card's freshness badge.
/// ConverterParameter selects the aspect: "text", "fg" (foreground brush), "bg" (pill background),
/// or "vis" (collapsed when Unknown).
/// </summary>
public class VersionStatusConverter : IValueConverter
{
    private static readonly Brush Green = new SolidColorBrush(Color.FromRgb(0x10, 0xB9, 0x81));
    private static readonly Brush Amber = new SolidColorBrush(Color.FromRgb(0xF5, 0x9E, 0x0B));
    private static readonly Brush Blue  = new SolidColorBrush(Color.FromRgb(0x3B, 0x82, 0xF6));
    private static readonly Brush GreenBg = new SolidColorBrush(Color.FromArgb(0x26, 0x10, 0xB9, 0x81));
    private static readonly Brush AmberBg = new SolidColorBrush(Color.FromArgb(0x26, 0xF5, 0x9E, 0x0B));
    private static readonly Brush BlueBg  = new SolidColorBrush(Color.FromArgb(0x26, 0x3B, 0x82, 0xF6));

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var status = value is VersionStatusKind k ? k : VersionStatusKind.Unknown;
        var aspect = (parameter as string)?.ToLowerInvariant() ?? "text";

        return aspect switch
        {
            "vis" => status == VersionStatusKind.Unknown ? Visibility.Collapsed : Visibility.Visible,
            "fg"  => status switch
            {
                VersionStatusKind.UpToDate        => Green,
                VersionStatusKind.UpdateAvailable => Amber,
                VersionStatusKind.Snapshot        => Blue,
                _                                 => Green
            },
            "bg"  => status switch
            {
                VersionStatusKind.UpToDate        => GreenBg,
                VersionStatusKind.UpdateAvailable => AmberBg,
                VersionStatusKind.Snapshot        => BlueBg,
                _                                 => GreenBg
            },
            _     => status switch
            {
                VersionStatusKind.UpToDate        => "Up to date",
                VersionStatusKind.UpdateAvailable => "Update available",
                VersionStatusKind.Snapshot        => "Snapshot",
                _                                 => string.Empty
            }
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}
