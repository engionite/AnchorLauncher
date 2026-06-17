namespace AnchorLauncher.Models.Instances;

/// <summary>
/// Per-instance launch overrides. Every field is nullable — null means "inherit the
/// global default". Persisted inside the instance's instance.json.
/// </summary>
public class InstanceSettings
{
    public int?    MemoryMB     { get; set; }
    public string? JavaPath     { get; set; }
    public int?    WindowWidth  { get; set; }
    public int?    WindowHeight { get; set; }
    public bool?   Fullscreen   { get; set; }
    public string? JvmArgs      { get; set; }
}
