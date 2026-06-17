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
        string.IsNullOrWhiteSpace(s.JvmArgs) ? g.JvmArgs : s.JvmArgs,
        ElyPatch: g.ElyPatchMode);
}
