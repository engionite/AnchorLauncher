using AnchorLauncher.Models;

namespace AnchorLauncher.Models.Instances;

/// <summary>The resolved launch settings after layering an instance's overrides over the globals.</summary>
public record EffectiveLaunchOptions(
    int MemoryMB, string? JavaPath, int Width, int Height, bool Fullscreen, string JvmArgs,
    string? ServerIp = null, int? ServerPort = null,
    ElyPatchMode ElyPatch = ElyPatchMode.ElyOnly)
{
    public static EffectiveLaunchOptions Resolve(GlobalSettings g, InstanceSettings s) => new(
        s.MemoryMB     ?? g.MemoryMB,
        string.IsNullOrWhiteSpace(s.JavaPath) ? g.JavaPath : s.JavaPath,
        s.WindowWidth  ?? g.WindowWidth,
        s.WindowHeight ?? g.WindowHeight,
        s.Fullscreen   ?? g.Fullscreen,
        ResolveJvmArgs(g, s),
        ElyPatch: g.ElyPatchMode);

    /// <summary>
    /// In Optimized launch mode, prepend the Aikar-style G1GC flag set (this is what makes the
    /// "Optimized Launch" toggle actually do something), then append any user flags it doesn't
    /// already cover. In Standard mode, just use the user/instance JVM args.
    /// </summary>
    private static string ResolveJvmArgs(GlobalSettings g, InstanceSettings s)
    {
        var userArgs = string.IsNullOrWhiteSpace(s.JvmArgs) ? g.JvmArgs : s.JvmArgs;
        if (g.LaunchPerfMode != LaunchPerfMode.Optimized)
            return userArgs;

        var optimized = string.Join(" ", GlobalSettings.OptimizedFlags);
        var extras = (userArgs ?? string.Empty)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(a => !optimized.Contains(a, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        return extras.Length == 0 ? optimized : optimized + " " + string.Join(" ", extras);
    }
}
