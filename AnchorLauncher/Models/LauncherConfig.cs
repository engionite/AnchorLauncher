namespace AnchorLauncher.Models;

public class LauncherConfig
{
    public LauncherMode Mode            { get; set; } = LauncherMode.Java;
    public bool         FirstRunComplete { get; set; } = false;
}
