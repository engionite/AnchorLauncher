using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using AnchorLauncher.Models;

namespace AnchorLauncher.Services.Platform;

/// <summary>
/// Applies a <see cref="ThemeMode"/> by swapping the surface Color/Brush resources on
/// <see cref="Application.Current"/>. Shell chrome binds these via DynamicResource so the change
/// is live; pages re-resolve them on their next navigation. Called once at startup and again
/// whenever the theme dropdown changes.
/// </summary>
public static class ThemeService
{
    public static void Apply(ThemeMode mode)
    {
        var res = Application.Current?.Resources;
        if (res == null) return;

        try
        {
            if (mode == ThemeMode.OledBlack)
            {
                Set(res, "AppBackground", "#000000");
                Set(res, "Surface1",      "#0A0A0A");
                Set(res, "Surface2",      "#111111");
            }
            else // Dark (default palette)
            {
                Set(res, "AppBackground", "#0F0F13");
                Set(res, "Surface1",      "#18181C");
                Set(res, "Surface2",      "#232329");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ThemeService] Apply({mode}) failed: {ex.Message}");
        }
    }

    private static void Set(ResourceDictionary res, string key, string hex)
    {
        var color = (Color)ColorConverter.ConvertFromString(hex);
        res[key + "Color"] = color;
        res[key + "Brush"] = new SolidColorBrush(color);
    }
}
