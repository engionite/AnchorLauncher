namespace AnchorLauncher.Models.Instances;

/// <summary>One recorded mod file state inside a snapshot.</summary>
public class SnapshotModState
{
    public string FileName { get; set; } = string.Empty;   // canonical name, no .disabled
    public long   Size     { get; set; }
    public string Sha1     { get; set; } = string.Empty;
    public bool   Enabled  { get; set; }
}

/// <summary>A point-in-time manifest of the instance's mods/ directory.</summary>
public class ModSnapshot
{
    public Guid     Id        { get; set; } = Guid.NewGuid();
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    /// <summary>What triggered the snapshot, e.g. "Before installing Sodium".</summary>
    public string   Reason    { get; set; } = string.Empty;
    public List<SnapshotModState> Mods { get; set; } = new();

    /// <summary>Computed at list time by diffing against the previous snapshot. Not persisted.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string ChangeSummary { get; set; } = string.Empty;

    [System.Text.Json.Serialization.JsonIgnore]
    public string TimestampDisplay => Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
}
