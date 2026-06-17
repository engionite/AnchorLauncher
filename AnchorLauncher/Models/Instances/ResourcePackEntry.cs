namespace AnchorLauncher.Models.Instances;

/// <summary>A pack (folder or .zip) in the instance's resourcepacks/ folder.</summary>
public class ResourcePackEntry
{
    public string Name     { get; set; } = string.Empty;
    public string FullPath { get; set; } = string.Empty;
    public bool   IsFolder { get; set; }
}
