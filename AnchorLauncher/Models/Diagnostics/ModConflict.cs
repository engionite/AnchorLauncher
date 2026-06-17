namespace AnchorLauncher.Models.Diagnostics;

public enum ConflictSeverity { Warning, Error }

/// <summary>What kind of finding this is — drives the one-click Auto-Fix.</summary>
public enum ConflictKind { MissingDependency, VersionMismatch, Incompatible }

/// <summary>One plain-English pre-launch finding (missing dependency, version mismatch, incompatibility).</summary>
public class ModConflict
{
    public ConflictSeverity Severity { get; set; } = ConflictSeverity.Warning;
    public string Message { get; set; } = string.Empty;
    public ConflictKind Kind { get; set; } = ConflictKind.MissingDependency;

    /// <summary>mods/ filename of the mod raising the conflict.</summary>
    public string? PrimaryFile { get; set; }
    /// <summary>mods/ filename of the other mod involved (for <see cref="ConflictKind.Incompatible"/>).</summary>
    public string? SecondaryFile { get; set; }

    /// <summary>How many other installed mods depend on Primary / Secondary — Auto-Fix disables the one with fewer.</summary>
    public int PrimaryDependents { get; set; }
    public int SecondaryDependents { get; set; }
}
