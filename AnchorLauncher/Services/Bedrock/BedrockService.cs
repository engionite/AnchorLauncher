using System.Diagnostics;
using System.IO;

namespace AnchorLauncher.Services.Bedrock;

/// <summary>
/// Bedrock (UWP) detection and launch. Detection checks the per-user package folder that
/// Windows creates for Microsoft.MinecraftUWP_8wekyb3d8bbwe — present exactly when the
/// package is installed for this user. (The WinRT PackageManager API would require
/// retargeting the project to a windows10 TFM; the folder check is equivalent for this
/// package and keeps the net8.0-windows build/publish unchanged.)
/// </summary>
public class BedrockService
{
    private const string PackageFamily = "Microsoft.MinecraftUWP_8wekyb3d8bbwe";
    private const string StoreUri      = "ms-windows-store://pdp/?productid=9NBLGGH2JHXJ";
    private const string LaunchArg     = $"shell:AppsFolder\\{PackageFamily}!App";

    public bool IsInstalled()
    {
        try
        {
            var packagesDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Packages", PackageFamily);
            return Directory.Exists(packagesDir);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Bedrock] IsInstalled check failed: {ex.Message}");
            return false;
        }
    }

    public void Launch()
    {
        Process.Start(new ProcessStartInfo("explorer.exe", LaunchArg) { UseShellExecute = true });
        Debug.WriteLine("[Bedrock] Launch requested.");
    }

    public void OpenStore()
    {
        Process.Start(new ProcessStartInfo(StoreUri) { UseShellExecute = true });
        Debug.WriteLine("[Bedrock] Store page opened.");
    }
}
