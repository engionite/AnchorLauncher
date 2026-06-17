using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AnchorLauncher.Models;
using AnchorLauncher.Services.Storage;

namespace AnchorLauncher.ViewModels;

public partial class OnboardingViewModel : ObservableObject
{
    [ObservableProperty] private bool _isSelecting;

    /// <summary>Raised once the edition has been persisted to disk.</summary>
    public event EventHandler<LauncherMode>? EditionSelected;

    [RelayCommand]
    private async Task SelectEditionAsync(LauncherMode mode)
    {
        if (IsSelecting) return;
        IsSelecting = true;

        try
        {
            var config = new LauncherConfig { Mode = mode, FirstRunComplete = true };
            await LauncherStorageService.SaveConfigAsync(config);
            EditionSelected?.Invoke(this, mode);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[OnboardingVM] SelectEditionAsync failed: {ex}");
            IsSelecting = false;
        }
    }
}
