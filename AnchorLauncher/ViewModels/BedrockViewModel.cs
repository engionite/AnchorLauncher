using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AnchorLauncher.Services.Bedrock;

namespace AnchorLauncher.ViewModels;

public partial class BedrockViewModel : ObservableObject
{
    private readonly BedrockService _bedrock = new();

    [ObservableProperty] private bool   _isInstalled;
    [ObservableProperty] private string _statusMessage = string.Empty;

    public BedrockViewModel()
    {
        Refresh();
    }

    [RelayCommand]
    private void Refresh()
    {
        try
        {
            IsInstalled   = _bedrock.IsInstalled();
            StatusMessage = IsInstalled ? "Minecraft Bedrock is installed." : string.Empty;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[BedrockVM] Refresh failed: {ex}");
        }
    }

    [RelayCommand]
    private void Launch()
    {
        try
        {
            _bedrock.Launch();
            StatusMessage = "Launching Minecraft Bedrock…";
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[BedrockVM] Launch failed: {ex}");
            StatusMessage = "Launch failed — is Bedrock still installed?";
        }
    }

    [RelayCommand]
    private void OpenStore()
    {
        try
        {
            _bedrock.OpenStore();
            StatusMessage = "Microsoft Store opened — install Minecraft, then come back and Refresh.";
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[BedrockVM] OpenStore failed: {ex}");
            StatusMessage = "Could not open the Microsoft Store.";
        }
    }
}
