using System.Windows;
using AnchorLauncher.Models;

namespace AnchorLauncher.Services.Platform;

/// <summary>
/// Global animation timing. Durations created via <see cref="Get"/> scale by the user's
/// Animation-Speed setting (and collapse to instant when animations are turned off), so the
/// whole launcher honours one motion preference.
/// </summary>
public static class AnimationHelper
{
    /// <summary>Multiplier applied to every animation duration. 0 = instant.</summary>
    public static double SpeedMultiplier { get; private set; } = 1.0;

    public static void Configure(AnimationSpeed speed, bool showAnimations)
    {
        SpeedMultiplier = !showAnimations ? 0.0 : speed switch
        {
            AnimationSpeed.None    => 0.0,
            AnimationSpeed.Reduced => 0.45,
            AnimationSpeed.Fast    => 0.6,
            _                      => 1.0,   // Normal
        };
    }

    /// <summary>A <see cref="Duration"/> scaled by the current speed setting.</summary>
    public static Duration Get(double milliseconds)
        => new Duration(TimeSpan.FromMilliseconds(Math.Max(0, milliseconds * SpeedMultiplier)));

    /// <summary>The scaled time in milliseconds (for code paths that need a raw number).</summary>
    public static double Ms(double milliseconds) => Math.Max(0, milliseconds * SpeedMultiplier);
}
