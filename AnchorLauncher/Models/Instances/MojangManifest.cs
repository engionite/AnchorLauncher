using System.Text.Json.Serialization;

namespace AnchorLauncher.Models.Instances;

/// <summary>Root of Mojang's version_manifest_v2.json.</summary>
public class MojangVersionManifest
{
    [JsonPropertyName("latest")]   public MojangLatest Latest { get; set; } = new();
    [JsonPropertyName("versions")] public List<MojangVersionEntry> Versions { get; set; } = new();
}

public class MojangLatest
{
    [JsonPropertyName("release")]  public string Release  { get; set; } = string.Empty;
    [JsonPropertyName("snapshot")] public string Snapshot { get; set; } = string.Empty;
}

public class MojangVersionEntry
{
    [JsonPropertyName("id")]          public string Id          { get; set; } = string.Empty;
    /// <summary>release | snapshot | old_beta | old_alpha</summary>
    [JsonPropertyName("type")]        public string Type        { get; set; } = string.Empty;
    /// <summary>Per-version metadata JSON (client jar, libraries, assets, args).</summary>
    [JsonPropertyName("url")]         public string Url         { get; set; } = string.Empty;
    [JsonPropertyName("releaseTime")] public DateTime ReleaseTime { get; set; }
    [JsonPropertyName("sha1")]        public string? Sha1       { get; set; }

    [JsonIgnore] public bool IsRelease  => Type == "release";
    [JsonIgnore] public bool IsSnapshot => Type == "snapshot";
    [JsonIgnore] public bool IsBeta     => Type == "old_beta";
    [JsonIgnore] public bool IsAlpha    => Type == "old_alpha";
}
