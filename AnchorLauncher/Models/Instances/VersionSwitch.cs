namespace AnchorLauncher.Models.Instances;

public enum SwitchModStatus { UpdateAvailable, Incompatible, Unknown }

/// <summary>One installed mod's fate under a planned version switch.</summary>
public class SwitchPlanEntry
{
    public string FileName    { get; set; } = string.Empty;
    public string Sha1        { get; set; } = string.Empty;
    public string? ProjectId  { get; set; }
    public SwitchModStatus Status { get; set; } = SwitchModStatus.Unknown;
    public string? NewFileName { get; set; }
    public string? NewUrl      { get; set; }

    public string StatusLabel => Status switch
    {
        SwitchModStatus.UpdateAvailable => $"update found → {NewFileName}",
        SwitchModStatus.Incompatible    => "incompatible — will be disabled",
        _                               => "unknown source — will be disabled"
    };
}

/// <summary>Recorded pre-switch state, stored under [instance]\version_snapshots\.</summary>
public class VersionSnapshot
{
    public DateTime Timestamp               { get; set; } = DateTime.UtcNow;
    public string   PreviousVersion         { get; set; } = string.Empty;
    public string?  PreviousLoaderVersion   { get; set; }
    public string?  PreviousLaunchVersionId { get; set; }
    public string   PreviousVersionType     { get; set; } = "release";
    public List<SnapshotModState> ModStates { get; set; } = new();
}
