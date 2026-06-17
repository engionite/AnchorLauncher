using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using AnchorLauncher.Models.Marketplace;

namespace AnchorLauncher.Converters;

/// <summary>Source badge colour — Modrinth green, CurseForge orange.</summary>
public class ModSourceToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var hex = value switch
        {
            ModSource.Modrinth   => "#1BD96A",
            ModSource.CurseForge => "#F16436",
            _                    => "#6B7280"
        };
        return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}
