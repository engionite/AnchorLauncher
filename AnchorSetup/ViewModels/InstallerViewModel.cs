using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows;
using AnchorSetup.Localization;
using AnchorSetup.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AnchorSetup.ViewModels;

public enum WizardStep { Welcome, License, Options, Installing, Finish }

/// <summary>A single entry in the sidebar stepper; its title re-localizes live.</summary>
public sealed partial class StepVm : ObservableObject
{
    public int    Number { get; }
    public string Key    { get; }
    public string Title  => SetupLoc.I[Key];

    [ObservableProperty] private bool _isActive;
    [ObservableProperty] private bool _isDone;

    public StepVm(int number, string key)
    {
        Number = number; Key = key;
        SetupLoc.I.PropertyChanged += (_, __) => OnPropertyChanged(nameof(Title));
    }
}

public sealed partial class InstallerViewModel : ObservableObject
{
    public IReadOnlyList<LanguageOption> Languages => SetupLoc.Languages;
    public ObservableCollection<StepVm> Steps { get; }
    public string LicenseText { get; } = LoadLicense();
    public SetupLoc L => SetupLoc.I;

    private CancellationTokenSource? _cts;

    [ObservableProperty] private WizardStep _currentStep = WizardStep.Welcome;
    [ObservableProperty] private LanguageOption _selectedLanguage;
    [ObservableProperty] private bool _acceptedLicense;
    [ObservableProperty] private string _installPath = InstallService.DefaultInstallDir;

    [ObservableProperty] private bool _desktopShortcut   = true;
    [ObservableProperty] private bool _startMenuShortcut = true;
    [ObservableProperty] private bool _runAtStartup;

    [ObservableProperty] private double _progress;
    [ObservableProperty] private string _statusKey = "ins_preparing";
    [ObservableProperty] private bool _isFailed;
    [ObservableProperty] private string _errorText = string.Empty;
    [ObservableProperty] private bool _confirmingCancel;
    [ObservableProperty] private bool _launchOnExit = true;

    public InstallerViewModel()
    {
        Steps = new ObservableCollection<StepVm>
        {
            new(1, "step_welcome"), new(2, "step_license"), new(3, "step_options"),
            new(4, "step_install"), new(5, "step_finish"),
        };

        // Pre-select the language matching the OS, falling back to English.
        var sys = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.ToLowerInvariant();
        _selectedLanguage = SetupLoc.Languages.FirstOrDefault(l => l.Code == sys) ?? SetupLoc.Languages[0];
        SetupLoc.I.SetLanguage(_selectedLanguage.Code);
        SetupLoc.I.PropertyChanged += (_, __) =>
        {
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(NextText));
        };

        UpdateStepStates();
    }

    public string StatusText => SetupLoc.I[StatusKey];

    // ── Derived button/visibility state ──────────────────────────────────────────
    public bool ShowBack        => CurrentStep is WizardStep.License or WizardStep.Options;
    public bool ShowNextInstall => CurrentStep is WizardStep.Welcome or WizardStep.License or WizardStep.Options;
    public bool ShowCancel      => CurrentStep != WizardStep.Finish && !IsInstalling;
    public bool ShowFinish      => CurrentStep == WizardStep.Finish;
    public bool IsInstalling    => CurrentStep == WizardStep.Installing && !IsFailed;
    public bool NextIsInstall   => CurrentStep == WizardStep.Options;
    public string NextText      => SetupLoc.I[NextIsInstall ? "btn_install" : "btn_next"];
    public bool NextEnabled     => CurrentStep != WizardStep.License || AcceptedLicense;

    partial void OnCurrentStepChanged(WizardStep value)
    {
        UpdateStepStates();
        RaiseChrome();
    }

    partial void OnAcceptedLicenseChanged(bool value)
    {
        OnPropertyChanged(nameof(NextEnabled));
        NextCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsFailedChanged(bool value) => RaiseChrome();
    partial void OnStatusKeyChanged(string value) => OnPropertyChanged(nameof(StatusText));

    partial void OnSelectedLanguageChanged(LanguageOption value)
    {
        if (value != null) SetupLoc.I.SetLanguage(value.Code);
    }

    private void RaiseChrome()
    {
        OnPropertyChanged(nameof(ShowBack));
        OnPropertyChanged(nameof(ShowNextInstall));
        OnPropertyChanged(nameof(ShowCancel));
        OnPropertyChanged(nameof(ShowFinish));
        OnPropertyChanged(nameof(IsInstalling));
        OnPropertyChanged(nameof(NextIsInstall));
        OnPropertyChanged(nameof(NextText));
        OnPropertyChanged(nameof(NextEnabled));
        NextCommand.NotifyCanExecuteChanged();
    }

    private void UpdateStepStates()
    {
        // Installing + Finish both light up the "Install" stepper node (index 3).
        int active = CurrentStep switch
        {
            WizardStep.Welcome    => 0,
            WizardStep.License    => 1,
            WizardStep.Options    => 2,
            WizardStep.Installing => 3,
            WizardStep.Finish     => 4,
            _ => 0
        };
        for (int i = 0; i < Steps.Count; i++)
        {
            Steps[i].IsActive = i == active;
            Steps[i].IsDone   = i < active;
        }
    }

    // ── Navigation ───────────────────────────────────────────────────────────────
    [RelayCommand]
    private void Back()
    {
        CurrentStep = CurrentStep switch
        {
            WizardStep.Options => WizardStep.License,
            WizardStep.License => WizardStep.Welcome,
            _ => CurrentStep
        };
    }

    private bool CanNext() => NextEnabled;

    [RelayCommand(CanExecute = nameof(CanNext))]
    private async Task NextAsync()
    {
        switch (CurrentStep)
        {
            case WizardStep.Welcome: CurrentStep = WizardStep.License; break;
            case WizardStep.License: CurrentStep = WizardStep.Options; break;
            case WizardStep.Options: await RunInstallAsync(); break;
        }
    }

    [RelayCommand]
    private void Retry() => _ = RunInstallAsync();

    private async Task RunInstallAsync()
    {
        IsFailed = false; ErrorText = string.Empty; Progress = 0; StatusKey = "ins_preparing";
        CurrentStep = WizardStep.Installing;
        RaiseChrome();

        _cts = new CancellationTokenSource();
        var progress = new Progress<InstallProgress>(r => { Progress = r.Percent; StatusKey = r.StatusKey; });
        try
        {
            var opts = new InstallOptions(
                InstallPath.Trim(), SelectedLanguage.AnchorValue,
                DesktopShortcut, StartMenuShortcut, RunAtStartup);

            await InstallService.InstallAsync(opts, progress, _cts.Token);
            await Task.Delay(450);                 // let the bar visibly reach 100%
            CurrentStep = WizardStep.Finish;
        }
        catch (OperationCanceledException) { /* aborted — window is closing */ }
        catch (Exception ex)
        {
            IsFailed = true;
            ErrorText = ex.Message;
            StatusKey = "ins_failed";
            RaiseChrome();
        }
    }

    // ── Cancel / quit ────────────────────────────────────────────────────────────
    [RelayCommand]
    private void Cancel() => ConfirmingCancel = true;

    [RelayCommand]
    private void DismissCancel() => ConfirmingCancel = false;

    [RelayCommand]
    private void ConfirmQuit()
    {
        _cts?.Cancel();
        Application.Current.Shutdown();
    }

    // ── Options ──────────────────────────────────────────────────────────────────
    [RelayCommand]
    private void Browse()
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog
        {
            Title = SetupLoc.I["opt_location"],
            InitialDirectory = Directory.Exists(InstallPath)
                ? InstallPath
                : Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
        };
        if (dlg.ShowDialog() == true)
            InstallPath = dlg.FolderName;
    }

    // ── Finish ───────────────────────────────────────────────────────────────────
    [RelayCommand]
    private void Finish()
    {
        if (LaunchOnExit)
        {
            try
            {
                Process.Start(new ProcessStartInfo(Path.Combine(InstallPath, InstallService.AppExeName))
                { UseShellExecute = true });
            }
            catch (Exception ex) { Debug.WriteLine($"[Setup] launch failed: {ex.Message}"); }
        }
        Application.Current.Shutdown();
    }

    private static string LoadLicense()
    {
        try
        {
            var asm = Assembly.GetExecutingAssembly();
            var name = asm.GetManifestResourceNames()
                          .FirstOrDefault(n => n.EndsWith("LICENSE.txt", StringComparison.OrdinalIgnoreCase));
            if (name == null) return string.Empty;
            using var s = asm.GetManifestResourceStream(name)!;
            using var r = new StreamReader(s);
            return r.ReadToEnd();
        }
        catch { return string.Empty; }
    }
}
