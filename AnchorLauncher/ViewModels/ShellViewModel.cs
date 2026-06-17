using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AnchorLauncher.Models;
using AnchorLauncher.Services.Storage;

namespace AnchorLauncher.ViewModels;

/// <summary>Owns the global Java/Bedrock mode switch (persisted to config.json).</summary>
public partial class ShellViewModel : ObservableObject
{
    [ObservableProperty] private LauncherMode _currentMode = LauncherMode.Java;

    /// <summary>Raised after the mode has been persisted, so the shell can re-route content.</summary>
    public event EventHandler<LauncherMode>? ModeChanged;

    public ShellViewModel()
    {
        CurrentMode = LauncherStorageService.CurrentConfig?.Mode ?? LauncherMode.Java;
    }

    [RelayCommand]
    private async Task SwitchModeAsync(LauncherMode mode)
    {
        try
        {
            if (mode == CurrentMode) return;
            CurrentMode = mode;

            var config = LauncherStorageService.CurrentConfig ?? new LauncherConfig { FirstRunComplete = true };
            config.Mode = mode;
            await LauncherStorageService.SaveConfigAsync(config);

            ModeChanged?.Invoke(this, mode);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ShellVM] SwitchModeAsync failed: {ex}");
        }
    }
}
