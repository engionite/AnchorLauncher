using CommunityToolkit.Mvvm.ComponentModel;

namespace AnchorLauncher.Models.Instances;

/// <summary>A .jar in the instance's mods/ folder. Disabled mods carry a .disabled suffix.</summary>
public partial class ModEntry : ObservableObject
{
    public string FileName { get; set; } = string.Empty;   // display name without .disabled
    public string FullPath { get; set; } = string.Empty;   // current on-disk path
    [ObservableProperty] private bool _isEnabled;
}
