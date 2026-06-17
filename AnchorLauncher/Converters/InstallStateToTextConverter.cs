using System.Globalization;
using System.Windows.Data;
using AnchorLauncher.Models.Marketplace;

namespace AnchorLauncher.Converters;

/// <summary>Localized label for the marketplace Install button, driven by its <see cref="InstallState"/>.
/// (Setter.Value can't hold a Binding, so the button's Content binds to State through this converter.)</summary>
public class InstallStateToTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var loc = Services.Platform.Loc.I;
        return value is InstallState s
            ? s switch
            {
                InstallState.Installing => loc["installing"],
                InstallState.Installed  => loc["installed"],
                InstallState.Failed     => loc["retry"],
                _                       => loc["install"],
            }
            : loc["install"];
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}
