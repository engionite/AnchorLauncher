using System.Globalization;
using System.Windows.Data;
using AnchorLauncher.Models.Marketplace;

namespace AnchorLauncher.Converters;

/// <summary>Friendly display names for marketplace sort modes.</summary>
public class SortModeToStringConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var loc = Services.Platform.Loc.I;
        return value switch
        {
            SortMode.MostDownloaded  => loc["sort_most"],
            SortMode.LeastDownloaded => loc["sort_least"],
            SortMode.RecentlyUpdated => loc["sort_recent"],
            SortMode.Oldest          => loc["sort_oldest"],
            SortMode.NewestRelease   => loc["sort_newest"],
            _                        => value?.ToString() ?? string.Empty
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}
