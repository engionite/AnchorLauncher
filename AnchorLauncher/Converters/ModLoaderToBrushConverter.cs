using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using AnchorLauncher.Models.Instances;

namespace AnchorLauncher.Converters;

/// <summary>Maps a <see cref="ModLoaderType"/> to its accent brush for the loader chip.</summary>
public class ModLoaderToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var hex = value switch
        {
            ModLoaderType.Fabric   => "#C8A165",
            ModLoaderType.Forge    => "#5B6F8C",
            ModLoaderType.NeoForge => "#F09A2F",
            ModLoaderType.Quilt    => "#9B59B6",
            _                       => "#6B7280" // Vanilla
        };
        return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}
