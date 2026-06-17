using System.Diagnostics;
using Microsoft.Win32;

namespace AnchorLauncher.Services.Platform;

/// <summary>Toggles "start with Windows" via HKCU\...\Run.</summary>
public static class StartupRegistryService
{
    private const string RunKey    = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "AnchorLauncher";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) != null;
        }
        catch (Exception ex) { Debug.WriteLine($"[Startup] IsEnabled failed: {ex.Message}"); return false; }
    }

    public static void SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true)
                            ?? Registry.CurrentUser.CreateSubKey(RunKey);
            if (key == null) return;

            if (enabled)
                key.SetValue(ValueName, $"\"{ExePath()}\"");
            else if (key.GetValue(ValueName) != null)
                key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        catch (Exception ex) { Debug.WriteLine($"[Startup] SetEnabled failed: {ex.Message}"); }
    }

    private static string ExePath()
        => Process.GetCurrentProcess().MainModule?.FileName ?? Environment.ProcessPath ?? string.Empty;
}
