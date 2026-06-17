using System.Text.Json;
using System.Text.Json.Serialization;

namespace AnchorLauncher.Models.Instances;

/// <summary>
/// Mojang per-version metadata (also the shape produced by Fabric/Quilt/Forge profiles,
/// which may set <see cref="InheritsFrom"/> to merge over a vanilla parent).
/// </summary>
public class VersionJson
{
    [JsonPropertyName("id")]                 public string Id { get; set; } = string.Empty;
    [JsonPropertyName("inheritsFrom")]       public string? InheritsFrom { get; set; }
    [JsonPropertyName("mainClass")]          public string? MainClass { get; set; }
    [JsonPropertyName("type")]               public string? Type { get; set; }
    [JsonPropertyName("assets")]             public string? Assets { get; set; }
    [JsonPropertyName("minecraftArguments")] public string? MinecraftArguments { get; set; }

    [JsonPropertyName("assetIndex")] public AssetIndexRef? AssetIndex { get; set; }
    [JsonPropertyName("downloads")]  public VersionDownloads? Downloads { get; set; }
    [JsonPropertyName("javaVersion")] public JavaVersionRef? JavaVersion { get; set; }
    [JsonPropertyName("libraries")]  public List<Library> Libraries { get; set; } = new();

    /// <summary>Modern (1.13+) structured arguments. Raw because elements are string|object.</summary>
    [JsonPropertyName("arguments")]  public ArgumentsBlock? Arguments { get; set; }
}

public class ArgumentsBlock
{
    [JsonPropertyName("game")] public JsonElement? Game { get; set; }
    [JsonPropertyName("jvm")]  public JsonElement? Jvm  { get; set; }
}

public class AssetIndexRef
{
    [JsonPropertyName("id")]        public string Id { get; set; } = string.Empty;
    [JsonPropertyName("url")]       public string Url { get; set; } = string.Empty;
    [JsonPropertyName("sha1")]      public string? Sha1 { get; set; }
    [JsonPropertyName("totalSize")] public long TotalSize { get; set; }
}

public class VersionDownloads
{
    [JsonPropertyName("client")] public DownloadEntry? Client { get; set; }
}

public class DownloadEntry
{
    [JsonPropertyName("url")]  public string Url { get; set; } = string.Empty;
    [JsonPropertyName("sha1")] public string? Sha1 { get; set; }
    [JsonPropertyName("size")] public long Size { get; set; }
}

public class JavaVersionRef
{
    [JsonPropertyName("component")]    public string? Component { get; set; }
    [JsonPropertyName("majorVersion")] public int MajorVersion { get; set; }
}

public class Library
{
    [JsonPropertyName("name")]      public string Name { get; set; } = string.Empty;
    [JsonPropertyName("downloads")] public LibraryDownloads? Downloads { get; set; }
    [JsonPropertyName("rules")]     public List<Rule>? Rules { get; set; }
    /// <summary>Legacy classifier map, e.g. { "windows": "natives-windows" }.</summary>
    [JsonPropertyName("natives")]   public Dictionary<string, string>? Natives { get; set; }
    [JsonPropertyName("extract")]   public ExtractRule? Extract { get; set; }
    /// <summary>Fabric/Quilt maven libraries carry only a base repo url + coords.</summary>
    [JsonPropertyName("url")]       public string? Url { get; set; }
}

public class LibraryDownloads
{
    [JsonPropertyName("artifact")]    public Artifact? Artifact { get; set; }
    [JsonPropertyName("classifiers")] public Dictionary<string, Artifact>? Classifiers { get; set; }
}

public class Artifact
{
    [JsonPropertyName("path")] public string? Path { get; set; }
    [JsonPropertyName("url")]  public string Url { get; set; } = string.Empty;
    [JsonPropertyName("sha1")] public string? Sha1 { get; set; }
    [JsonPropertyName("size")] public long Size { get; set; }
}

public class Rule
{
    [JsonPropertyName("action")]   public string Action { get; set; } = "allow";
    [JsonPropertyName("os")]       public OsRule? Os { get; set; }
    [JsonPropertyName("features")] public Dictionary<string, bool>? Features { get; set; }
}

public class OsRule
{
    [JsonPropertyName("name")]    public string? Name { get; set; }
    [JsonPropertyName("arch")]    public string? Arch { get; set; }
    [JsonPropertyName("version")] public string? Version { get; set; }
}

public class ExtractRule
{
    [JsonPropertyName("exclude")] public List<string>? Exclude { get; set; }
}

/// <summary>Asset index file (indexes/&lt;id&gt;.json).</summary>
public class AssetIndexFile
{
    [JsonPropertyName("objects")] public Dictionary<string, AssetObject> Objects { get; set; } = new();
}

public class AssetObject
{
    [JsonPropertyName("hash")] public string Hash { get; set; } = string.Empty;
    [JsonPropertyName("size")] public long Size { get; set; }
}
