using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AnchorLauncher.Models.Instances;
using AnchorLauncher.Services.Instances;

namespace AnchorLauncher.ViewModels;

public partial class VersionSwitchViewModel : ObservableObject
{
    private readonly VersionSwitchService  _switcher = new();
    private readonly MojangManifestService _manifest = new();
    private readonly MinecraftInstance     _instance;

    public string InstanceName   => _instance.Name;
    public string CurrentVersion => _instance.Version;

    [ObservableProperty] private ObservableCollection<string> _versions = new();
    [ObservableProperty] private string? _selectedVersion;
    [ObservableProperty] private ObservableCollection<SwitchPlanEntry> _plan = new();
    [ObservableProperty] private bool   _hasPlan;
    [ObservableProperty] private bool   _isBusy;
    [ObservableProperty] private double _progress;
    [ObservableProperty] private string _progressStatus = string.Empty;
    [ObservableProperty] private string _summary = string.Empty;
    [ObservableProperty] private string _errorMessage = string.Empty;

    /// <summary>Raised when the switch has completed successfully.</summary>
    public event EventHandler? Completed;

    public VersionSwitchViewModel(MinecraftInstance instance)
    {
        _instance = instance;
        _ = LoadVersionsAsync();
    }

    private async Task LoadVersionsAsync()
    {
        try
        {
            IsBusy = true;
            var manifest = await _manifest.GetManifestAsync();
            var releases = manifest?.Versions.Where(v => v.IsRelease)
                                             .Select(v => v.Id)
                                             .Where(v => v != _instance.Version)
                                             .ToList() ?? new List<string>();
            Versions = new ObservableCollection<string>(releases);
            SelectedVersion = Versions.FirstOrDefault();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SwitchVM] LoadVersionsAsync failed: {ex}");
            ErrorMessage = "Could not load the version list.";
        }
        finally { IsBusy = false; }
    }

    partial void OnSelectedVersionChanged(string? value)
    {
        // A new target invalidates any previous analysis
        HasPlan = false;
        Plan    = new ObservableCollection<SwitchPlanEntry>();
        Summary = string.Empty;
    }

    [RelayCommand]
    private async Task AnalyzeAsync()
    {
        if (IsBusy || string.IsNullOrEmpty(SelectedVersion)) return;
        try
        {
            IsBusy       = true;
            ErrorMessage = string.Empty;
            var progress = new Progress<DownloadProgress>(p => { Progress = p.Percent; ProgressStatus = p.Status; });

            var plan = await _switcher.PlanAsync(_instance, SelectedVersion!, progress);
            Plan    = new ObservableCollection<SwitchPlanEntry>(plan);
            HasPlan = true;

            var updates      = plan.Count(p => p.Status == SwitchModStatus.UpdateAvailable);
            var incompatible = plan.Count(p => p.Status != SwitchModStatus.UpdateAvailable);
            Summary = plan.Count == 0
                ? "No mods installed — the version will simply be switched."
                : $"{updates} mod(s) will be updated, {incompatible} will be disabled.";
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SwitchVM] AnalyzeAsync failed: {ex}");
            ErrorMessage = "Compatibility analysis failed — check your connection.";
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task SwitchAsync()
    {
        if (IsBusy || !HasPlan || string.IsNullOrEmpty(SelectedVersion)) return;
        try
        {
            IsBusy       = true;
            ErrorMessage = string.Empty;
            var progress = new Progress<DownloadProgress>(p => { Progress = p.Percent; ProgressStatus = p.Status; });

            // Progress also streams into the shared console drawer
            await _switcher.ExecuteAsync(
                _instance, SelectedVersion!, Plan.ToList(),
                InstancesViewModel.Shared.LogExternal, progress);

            Completed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SwitchVM] SwitchAsync failed: {ex}");
            ErrorMessage = $"Switch failed: {ex.Message}";
        }
        finally { IsBusy = false; }
    }
}
