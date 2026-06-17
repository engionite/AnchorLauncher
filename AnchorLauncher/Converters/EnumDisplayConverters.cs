using System.Globalization;
using System.Windows.Data;
using AnchorLauncher.Models;

namespace AnchorLauncher.Converters;

/// <summary>Friendly names for ThemeMode.</summary>
public class ThemeModeToStringConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        ThemeMode.Dark      => "Dark",
        ThemeMode.OledBlack => "OLED Black",
        _                   => value?.ToString() ?? string.Empty
    };
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Friendly names for ProxyMode.</summary>
public class ProxyModeToStringConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        ProxyMode.None   => "No proxy",
        ProxyMode.Http   => "HTTP proxy",
        ProxyMode.Socks5 => "SOCKS5 proxy",
        _                => value?.ToString() ?? string.Empty
    };
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Friendly names for ElyPatchMode.</summary>
public class ElyPatchModeToStringConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        ElyPatchMode.Always  => "Always apply",
        ElyPatchMode.ElyOnly => "Only with an Ely.by account",
        ElyPatchMode.Never   => "Never",
        _                    => value?.ToString() ?? string.Empty
    };
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Friendly names for AnimationSpeed.</summary>
public class AnimationSpeedToStringConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        AnimationSpeed.None    => "None (instant)",
        AnimationSpeed.Reduced => "Reduced",
        AnimationSpeed.Normal  => "Normal",
        AnimationSpeed.Fast    => "Fast",
        _                      => value?.ToString() ?? string.Empty
    };
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
