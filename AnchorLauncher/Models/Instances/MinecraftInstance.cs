using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AnchorLauncher.Models.Instances;

/// <summary>How an instance's Minecraft version compares to the latest Mojang release.</summary>
public enum VersionStatusKind { Unknown, UpToDate, UpdateAvailable, Snapshot }

/// <summary>
/// One sandboxed game instance. Persisted as <c>instance.json</c> inside its own
/// folder under <c>%APPDATA%\AnchorLauncher\instances\[name]\</c>. Observable so the
/// instance card reflects live launch state without rebuilding the collection.
/// </summary>
public partial class MinecraftInstance : ObservableObject
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [ObservableProperty] private string _name = string.Empty;

    /// <summary>Minecraft version id, e.g. "1.20.4".</summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>Mojang manifest type: release | snapshot | old_beta | old_alpha.</summary>
    public string VersionType { get; set; } = "release";

    public ModLoaderType ModLoader { get; set; } = ModLoaderType.Vanilla;

    /// <summary>Resolved loader version (Fabric/Forge/etc.); null for Vanilla.</summary>
    public string? ModLoaderVersion { get; set; }

    /// <summary>
    /// The version id to actually launch. Equals <see cref="Version"/> for Vanilla; for a
    /// mod loader it is the installed profile id (e.g. "fabric-loader-0.15.7-1.20.4") whose
    /// metadata inheritsFrom the vanilla version. Set by the loader installer.
    /// </summary>
    public string? LaunchVersionId { get; set; }

    /// <summary>Absolute path to the sandboxed game directory (passed as -gameDir).</summary>
    public string GameDir { get; set; } = string.Empty;

    public string? ThumbnailPath { get; set; }

    /// <summary>Per-instance icon: a built-in key (e.g. "grass", "creeper") or an absolute path to a
    /// custom image. Null = default. Observable so the cards update the moment it changes.</summary>
    [ObservableProperty] private string? _iconId;

    /// <summary>Per-instance launch overrides (null fields inherit the global defaults).</summary>
    public InstanceSettings Settings { get; set; } = new();

    public DateTime Created { get; set; } = DateTime.UtcNow;

    [ObservableProperty] private DateTime? _lastPlayed;

    /// <summary>Accumulated play time in minutes (only grows when "Record playtime" is on).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PlaytimeDisplay))]
    [NotifyPropertyChangedFor(nameof(HasPlaytime))]
    private long _playtimeMinutes;

    /// <summary>Human label for the card, e.g. "12h 30m" / "45m". Empty when nothing recorded.</summary>
    [JsonIgnore]
    public string PlaytimeDisplay =>
        PlaytimeMinutes <= 0 ? string.Empty
        : PlaytimeMinutes < 60 ? $"{PlaytimeMinutes}m"
        : $"{PlaytimeMinutes / 60}h {PlaytimeMinutes % 60}m";

    [JsonIgnore]
    public bool HasPlaytime => PlaytimeMinutes > 0;

    /// <summary>Transient — runtime only, excluded from serialization.</summary>
    [JsonIgnore]
    [ObservableProperty] private InstanceStatus _status = InstanceStatus.Idle;

    /// <summary>Transient: a version-switch snapshot exists, so "Undo Last Switch" is offered.</summary>
    [ObservableProperty]
    [property: JsonIgnore] private bool _hasSwitchSnapshot;

    /// <summary>Transient: card badge text, e.g. "Switched from 1.20.4".</summary>
    [ObservableProperty]
    [property: JsonIgnore] private string? _switchedFromLabel;

    /// <summary>Transient: version freshness vs the Mojang latest release (drives the card badge).</summary>
    [ObservableProperty]
    [property: JsonIgnore] private VersionStatusKind _versionStatus = VersionStatusKind.Unknown;

    /// <summary>Convenience label for the loader chip, e.g. "Fabric 0.15.7".</summary>
    [JsonIgnore]
    public string LoaderLabel =>
        ModLoader == ModLoaderType.Vanilla
            ? "Vanilla"
            : string.IsNullOrEmpty(ModLoaderVersion)
                ? ModLoader.ToString()
                : $"{ModLoader} {ModLoaderVersion}";
}
