namespace AnchorLauncher.Models.Instances;

/// <summary>A singleplayer world in the instance's saves/ folder.</summary>
public class WorldSaveEntry
{
    public string  Name     { get; set; } = string.Empty;
    public string  FullPath { get; set; } = string.Empty;
    public string? IconPath { get; set; }   // saves/<world>/icon.png if present
}
