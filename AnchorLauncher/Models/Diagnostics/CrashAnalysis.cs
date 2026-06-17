namespace AnchorLauncher.Models.Diagnostics;

public enum CrashFixKind
{
    None,
    IncreaseMemory,        // OutOfMemoryError → add 2 GB to the allocation and relaunch
    DownloadJava,          // wrong Java → fetch the correct Adoptium JRE, pin it, relaunch
    InstallDependency,     // missing mod dependency → open the marketplace filtered to that mod
    DisableConflictingMod, // mod conflict → rename the offending jar to .disabled and relaunch
    OpenModsFolder         // last-resort manual fix when the culprit can't be pinpointed
}

/// <summary>Plain-English interpretation of a launch failure plus an optional one-click fix.</summary>
public class CrashAnalysis
{
    public string       Title       { get; set; } = "Game crashed";
    public string       Explanation { get; set; } = string.Empty;
    public CrashFixKind Fix         { get; set; } = CrashFixKind.None;
    public string?      FixLabel    { get; set; }
    public string?      ReportPath  { get; set; }

    // ── Data the one-click auto-fix needs ────────────────────────────────────────
    /// <summary>Java major the game actually requires (8/17/21) — for <see cref="CrashFixKind.DownloadJava"/>.</summary>
    public int RequiredJavaMajor { get; set; }

    /// <summary>Human-readable name of the missing mod — for <see cref="CrashFixKind.InstallDependency"/>.</summary>
    public string? DependencyName { get; set; }

    /// <summary>mods/ filename of the jar to disable — for <see cref="CrashFixKind.DisableConflictingMod"/>.</summary>
    public string? ConflictingModFile { get; set; }
}
