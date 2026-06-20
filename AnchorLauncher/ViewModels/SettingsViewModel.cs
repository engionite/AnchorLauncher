using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AnchorLauncher.Models;
using AnchorLauncher.Services.Launch;
using AnchorLauncher.Services.Platform;
using AnchorLauncher.Services.Storage;

namespace AnchorLauncher.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly JavaRuntimeService _java = new();

    // ── Java / launch defaults ──
    [ObservableProperty] private int    _memoryMB = 2048;
    [ObservableProperty] private string _javaPath = string.Empty;
    [ObservableProperty] private int    _windowWidth  = 854;
    [ObservableProperty] private int    _windowHeight = 480;
    [ObservableProperty] private bool   _fullscreen;
    [ObservableProperty] private string _jvmArgs = "-XX:+UseG1GC";
    [ObservableProperty] private string _statusMessage = string.Empty;

    // ── General ──
    [ObservableProperty] private InstanceSort    _instanceSort = InstanceSort.LastPlayed;
    [ObservableProperty] private InstanceView    _instanceView = InstanceView.Grid;
    [ObservableProperty] private bool            _showPlaytimeOnCards = true;
    [ObservableProperty] private bool            _recordPlaytime = true;
    [ObservableProperty] private LaunchAction    _onLaunchAction = LaunchAction.KeepOpen;
    [ObservableProperty] private GameCloseAction _onGameCloseAction = GameCloseAction.Reopen;

    // ── Appearance (was "Launcher") ──
    [ObservableProperty] private ThemeMode _theme = ThemeMode.Dark;
    [ObservableProperty] private int       _uiScalePercent = 100;
    [ObservableProperty] private string    _language = "English";
    [ObservableProperty] private bool      _showInstanceNames = true;
    [ObservableProperty] private bool      _minimizeToTray;
    [ObservableProperty] private bool      _startWithWindows;
    [ObservableProperty] private bool      _checkUpdatesOnStartup = true;
    [ObservableProperty] private string    _consoleFont = "Consolas";
    [ObservableProperty] private int       _consoleFontSize = 13;
    [ObservableProperty] private AnimationSpeed _animationSpeed = AnimationSpeed.Normal;
    [ObservableProperty] private bool      _showAnimations = true;

    // ── Minecraft ──
    [ObservableProperty] private ElyPatchMode _elyPatchMode = ElyPatchMode.ElyOnly;
    [ObservableProperty] private bool _startMaximized;
    [ObservableProperty] private bool _showConsoleOnCrash = true;
    [ObservableProperty] private bool _hideConsoleOnGameClose;

    // ── Services ──
    [ObservableProperty] private string _logPasteService = "https://api.mclo.gs/1/log";
    [ObservableProperty] private string _metadataServer  = "https://elyprismlauncher.github.io/meta/v1";
    [ObservableProperty] private string _assetsServer     = "https://resources.download.minecraft.net";
    [ObservableProperty] private string _curseForgeApiKey = string.Empty;
    [ObservableProperty] private string _modrinthApiUrl   = "https://api.modrinth.com/v2";

    // Apply a user-supplied CurseForge key to the client immediately (fires on load and on edit).
    partial void OnCurseForgeApiKeyChanged(string value)
        => Services.Marketplace.CurseForgeClient.UserApiKey = value;

    // ── Minecraft launch ──
    [ObservableProperty] private LaunchPerfMode _launchPerfMode = LaunchPerfMode.Standard;
    [ObservableProperty] private bool   _closeOnLaunch;
    [ObservableProperty] private bool   _keepOpenMinimized;
    [ObservableProperty] private bool   _showConsoleOnLaunch = true;
    [ObservableProperty] private string _preLaunchScript = string.Empty;

    // ── Java manager ──
    [ObservableProperty] private ObservableCollection<JavaRuntimeService.JavaInstall> _detectedJavas = new();
    [ObservableProperty] private bool _isScanningJava;

    // ── Network ──
    [ObservableProperty] private int       _downloadThreads = 16;
    [ObservableProperty] private bool      _retryDownloads = true;
    [ObservableProperty] private int       _retryAttempts = 3;
    [ObservableProperty] private int       _httpTimeoutSeconds = 60;
    [ObservableProperty] private ProxyMode _proxyMode = ProxyMode.None;
    [ObservableProperty] private string    _proxyHost = string.Empty;
    [ObservableProperty] private int       _proxyPort = 8080;

    // ── Cloud sync ──
    [ObservableProperty] private CloudProvider _cloudProvider = CloudProvider.OneDrive;
    [ObservableProperty] private string _cloudCustomPath = string.Empty;
    [ObservableProperty] private bool _syncWorlds = true;
    [ObservableProperty] private bool _syncScreenshots = true;
    [ObservableProperty] private bool _syncOptions = true;
    [ObservableProperty] private bool _syncControls;
    [ObservableProperty] private bool _autoSyncOnClose;
    [ObservableProperty] private bool _isSyncing;
    [ObservableProperty] private string _syncStatus = string.Empty;

    // ── Privacy / About ──
    [ObservableProperty] private string _cacheStatus = string.Empty;
    [ObservableProperty] private string _updateStatus = string.Empty;
    [ObservableProperty] private bool _isCheckingUpdate;

    public static IReadOnlyList<CloudProvider> CloudProviders { get; } = (CloudProvider[])Enum.GetValues(typeof(CloudProvider));
    public static IReadOnlyList<ThemeMode>     Themes         { get; } = (ThemeMode[])Enum.GetValues(typeof(ThemeMode));
    public static IReadOnlyList<ProxyMode>     ProxyModes     { get; } = (ProxyMode[])Enum.GetValues(typeof(ProxyMode));
    public static IReadOnlyList<int>           UiScaleOptions { get; } = new[] { 90, 100, 110, 125 };

    public static IReadOnlyList<InstanceSort>    InstanceSorts    { get; } = (InstanceSort[])Enum.GetValues(typeof(InstanceSort));
    public static IReadOnlyList<InstanceView>    InstanceViews    { get; } = (InstanceView[])Enum.GetValues(typeof(InstanceView));
    public static IReadOnlyList<LaunchAction>    LaunchActions    { get; } = (LaunchAction[])Enum.GetValues(typeof(LaunchAction));
    public static IReadOnlyList<GameCloseAction> GameCloseActions { get; } = (GameCloseAction[])Enum.GetValues(typeof(GameCloseAction));
    public static IReadOnlyList<AnimationSpeed>  AnimationSpeeds  { get; } = (AnimationSpeed[])Enum.GetValues(typeof(AnimationSpeed));
    public static IReadOnlyList<ElyPatchMode>    ElyPatchModes    { get; } = (ElyPatchMode[])Enum.GetValues(typeof(ElyPatchMode));
    public static IReadOnlyList<string>          ConsoleFonts     { get; } = new[] { "Consolas", "Courier New", "Cascadia Code", "JetBrains Mono" };
    public static IReadOnlyList<int>             ConsoleFontSizes { get; } = new[] { 8, 9, 10, 11, 12, 13, 14, 15, 16 };

    /// <summary>15 launcher languages (all currently map to English strings; selection persists).</summary>
    public static IReadOnlyList<string> Languages { get; } = new[]
    {
        "English (en-US)", "Español (es-ES)", "Русский (ru-RU)", "Українська (uk-UA)",
        "中文简体 (zh-CN)", "Eesti (et-EE)", "Deutsch (de-DE)", "Français (fr-FR)",
        "Português (pt-BR)", "日本語 (ja-JP)", "한국어 (ko-KR)", "Polski (pl-PL)",
        "Italiano (it-IT)", "Nederlands (nl-NL)", "Türkçe (tr-TR)"
    };

    /// <summary>True while LoadAsync is populating properties, so live-apply handlers don't re-fire.</summary>
    private bool _isLoading;

    public string AppVersion        => $"v{UpdateCheckService.CurrentVersion}";
    public string BuildDate          => System.IO.File.GetLastWriteTime(Environment.ProcessPath ?? ".").ToString("yyyy-MM-dd");
    public string OptimizedFlagsText => string.Join("  ", GlobalSettings.OptimizedFlags);

    public SettingsViewModel()
    {
        _ = LoadAsync();
        _ = RefreshJavasAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            _isLoading = true;
            var s = await LauncherStorageService.LoadGlobalSettingsAsync();
            MemoryMB     = s.MemoryMB;
            JavaPath     = s.JavaPath ?? string.Empty;
            WindowWidth  = s.WindowWidth;
            WindowHeight = s.WindowHeight;
            Fullscreen   = s.Fullscreen;
            JvmArgs      = s.JvmArgs;

            InstanceSort        = s.InstanceSort;
            InstanceView        = s.InstanceView;
            ShowPlaytimeOnCards = s.ShowPlaytimeOnCards;
            RecordPlaytime      = s.RecordPlaytime;
            OnLaunchAction      = s.OnLaunchAction;
            OnGameCloseAction   = s.OnGameCloseAction;

            Theme                 = s.Theme;
            UiScalePercent        = s.UiScalePercent;
            Language              = NormalizeLanguage(s.Language);
            ShowInstanceNames     = s.ShowInstanceNames;
            MinimizeToTray        = s.MinimizeToTray;
            CheckUpdatesOnStartup = s.CheckUpdatesOnStartup;
            StartWithWindows      = StartupRegistryService.IsEnabled();   // authoritative source
            ConsoleFont           = s.ConsoleFont;
            ConsoleFontSize       = s.ConsoleFontSize;
            AnimationSpeed        = s.AnimationSpeed;
            ShowAnimations        = s.ShowAnimations;

            ElyPatchMode           = s.ElyPatchMode;
            StartMaximized         = s.StartMaximized;
            ShowConsoleOnCrash     = s.ShowConsoleOnCrash;
            HideConsoleOnGameClose = s.HideConsoleOnGameClose;

            LogPasteService  = s.LogPasteService;
            MetadataServer   = s.MetadataServer;
            AssetsServer     = s.AssetsServer;
            CurseForgeApiKey = s.CurseForgeApiKey;
            ModrinthApiUrl   = s.ModrinthApiUrl;

            LaunchPerfMode      = s.LaunchPerfMode;
            CloseOnLaunch       = s.CloseOnLaunch;
            KeepOpenMinimized   = s.KeepOpenMinimized;
            ShowConsoleOnLaunch = s.ShowConsoleOnLaunch;
            PreLaunchScript     = s.PreLaunchScript ?? string.Empty;

            DownloadThreads    = s.DownloadThreads;
            RetryDownloads     = s.RetryDownloads;
            RetryAttempts      = s.RetryAttempts;
            HttpTimeoutSeconds = s.HttpTimeoutSeconds;
            ProxyMode          = s.ProxyMode;
            ProxyHost          = s.ProxyHost ?? string.Empty;
            ProxyPort          = s.ProxyPort;

            CloudProvider   = s.CloudProvider;
            CloudCustomPath = s.CloudCustomPath ?? string.Empty;
            SyncWorlds      = s.SyncWorlds;
            SyncScreenshots = s.SyncScreenshots;
            SyncOptions     = s.SyncOptions;
            SyncControls    = s.SyncControls;
            AutoSyncOnClose = s.AutoSyncOnClose;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SettingsVM] LoadAsync failed: {ex}");
        }
        finally { _isLoading = false; }
    }

    // "Start with Windows" applies immediately (registry is the source of truth)
    partial void OnStartWithWindowsChanged(bool value)
    {
        try { StartupRegistryService.SetEnabled(value); }
        catch (Exception ex) { Debug.WriteLine($"[SettingsVM] StartWithWindows failed: {ex}"); }
    }

    // ── Live-apply settings (the global auto-save below persists them to disk) ──

    partial void OnThemeChanged(ThemeMode value)
    {
        if (_isLoading) return;
        try { ThemeService.Apply(value); }
        catch (Exception ex) { Debug.WriteLine($"[SettingsVM] Theme apply failed: {ex}"); }
    }

    partial void OnUiScalePercentChanged(int value)
    {
        if (_isLoading) return;
        try { (System.Windows.Application.Current?.MainWindow as MainWindow)?.ApplyUiScale(value); }
        catch (Exception ex) { Debug.WriteLine($"[SettingsVM] UI scale apply failed: {ex}"); }
    }

    partial void OnMinimizeToTrayChanged(bool value)
    {
        if (_isLoading) return;
        try { (System.Windows.Application.Current?.MainWindow as MainWindow)?.SetMinimizeToTray(value); }
        catch (Exception ex) { Debug.WriteLine($"[SettingsVM] Minimize-to-tray apply failed: {ex}"); }
    }

    // ── Auto-persist: every setting change is written to disk (debounced) ─────────
    // There is no "Save gate": changing any control persists it, so navigating away
    // and back keeps the value. Pure UI-state properties (status text, scan results,
    // busy flags) are excluded so they don't trigger needless disk writes.

    private static readonly HashSet<string> _nonPersistentProps = new()
    {
        nameof(StatusMessage), nameof(DetectedJavas), nameof(IsScanningJava),
        nameof(CacheStatus),   nameof(UpdateStatus),  nameof(IsCheckingUpdate),
        nameof(SyncStatus),    nameof(IsSyncing),
    };

    private System.Windows.Threading.DispatcherTimer? _autoSaveTimer;

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (_isLoading) return;
        if (e.PropertyName is null || _nonPersistentProps.Contains(e.PropertyName)) return;
        ScheduleAutoSave();
    }

    private void ScheduleAutoSave()
    {
        // Debounce so slider drags / typing coalesce into a single write.
        if (_autoSaveTimer is null)
        {
            _autoSaveTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(350)
            };
            _autoSaveTimer.Tick += (_, _) =>
            {
                _autoSaveTimer!.Stop();
                _ = PersistAsync();
            };
        }
        _autoSaveTimer.Stop();
        _autoSaveTimer.Start();
    }

    /// <summary>Writes current settings to disk and live-applies the runtime network knobs.</summary>
    private async Task PersistAsync()
    {
        try
        {
            await LauncherStorageService.SaveGlobalSettingsAsync(BuildSettings());
            Services.Net.NetworkConfig.DownloadThreads = Math.Clamp(DownloadThreads, 4, 32);
            Services.Net.NetworkConfig.RetryDownloads  = RetryDownloads;
            Services.Net.NetworkConfig.RetryAttempts   = Math.Max(1, RetryAttempts);
            Services.Net.NetworkConfig.TimeoutSeconds  = Math.Max(10, HttpTimeoutSeconds);
        }
        catch (Exception ex) { Debug.WriteLine($"[SettingsVM] auto-persist failed: {ex}"); }
    }

    /// <summary>Flush a pending debounced save immediately — called when the page is navigated away.</summary>
    public async Task FlushAsync()
    {
        if (_autoSaveTimer is { IsEnabled: true })
        {
            _autoSaveTimer.Stop();
            await PersistAsync();
        }
    }

    public void SetJavaPath(string path)        => JavaPath = path;
    public void SetCloudCustomPath(string path) => CloudCustomPath = path;
    public void SetPreLaunchScript(string path) => PreLaunchScript = path;

    // Explicit "Save Settings" button. Settings already auto-save on every change;
    // this just flushes any pending debounce and confirms to the user.
    [RelayCommand]
    private async Task SaveAsync()
    {
        try
        {
            if (_autoSaveTimer is { IsEnabled: true }) _autoSaveTimer.Stop();
            await PersistAsync();
            StatusMessage = "All settings saved ✓";
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SettingsVM] SaveAsync failed: {ex}");
            StatusMessage = "Save failed.";
        }
    }

    private GlobalSettings BuildSettings() => new()
    {
        MemoryMB     = MemoryMB,
        JavaPath     = string.IsNullOrWhiteSpace(JavaPath) ? null : JavaPath,
        WindowWidth  = WindowWidth,
        WindowHeight = WindowHeight,
        Fullscreen   = Fullscreen,
        JvmArgs      = JvmArgs,

        InstanceSort        = InstanceSort,
        InstanceView        = InstanceView,
        ShowPlaytimeOnCards = ShowPlaytimeOnCards,
        RecordPlaytime      = RecordPlaytime,
        OnLaunchAction      = OnLaunchAction,
        OnGameCloseAction   = OnGameCloseAction,

        Theme                 = Theme,
        UiScalePercent        = UiScalePercent,
        Language              = Language,
        ShowInstanceNames     = ShowInstanceNames,
        MinimizeToTray        = MinimizeToTray,
        StartWithWindows      = StartWithWindows,
        CheckUpdatesOnStartup = CheckUpdatesOnStartup,
        ConsoleFont           = ConsoleFont,
        ConsoleFontSize       = Math.Clamp(ConsoleFontSize, 8, 16),
        AnimationSpeed        = AnimationSpeed,
        ShowAnimations        = ShowAnimations,

        ElyPatchMode           = ElyPatchMode,
        StartMaximized         = StartMaximized,
        ShowConsoleOnCrash     = ShowConsoleOnCrash,
        HideConsoleOnGameClose = HideConsoleOnGameClose,

        LogPasteService  = string.IsNullOrWhiteSpace(LogPasteService)  ? "https://api.mclo.gs/1/log" : LogPasteService,
        MetadataServer   = string.IsNullOrWhiteSpace(MetadataServer)   ? "https://elyprismlauncher.github.io/meta/v1" : MetadataServer,
        AssetsServer     = string.IsNullOrWhiteSpace(AssetsServer)     ? "https://resources.download.minecraft.net" : AssetsServer,
        CurseForgeApiKey = CurseForgeApiKey,
        ModrinthApiUrl   = string.IsNullOrWhiteSpace(ModrinthApiUrl)   ? "https://api.modrinth.com/v2" : ModrinthApiUrl,

        LaunchPerfMode      = LaunchPerfMode,
        CloseOnLaunch       = CloseOnLaunch,
        KeepOpenMinimized   = KeepOpenMinimized,
        ShowConsoleOnLaunch = ShowConsoleOnLaunch,
        PreLaunchScript     = string.IsNullOrWhiteSpace(PreLaunchScript) ? null : PreLaunchScript,

        DownloadThreads    = Math.Clamp(DownloadThreads, 4, 32),
        RetryDownloads     = RetryDownloads,
        RetryAttempts      = Math.Max(1, RetryAttempts),
        HttpTimeoutSeconds = Math.Max(10, HttpTimeoutSeconds),
        ProxyMode          = ProxyMode,
        ProxyHost          = string.IsNullOrWhiteSpace(ProxyHost) ? null : ProxyHost,
        ProxyPort          = ProxyPort,

        CloudProvider   = CloudProvider,
        CloudCustomPath = string.IsNullOrWhiteSpace(CloudCustomPath) ? null : CloudCustomPath,
        SyncWorlds      = SyncWorlds,
        SyncScreenshots = SyncScreenshots,
        SyncOptions     = SyncOptions,
        SyncControls    = SyncControls,
        AutoSyncOnClose = AutoSyncOnClose
    };

    private static string NormalizeLanguage(string stored)
    {
        if (Languages.Contains(stored)) return stored;
        // Back-compat: old saves stored "English"
        return Languages.FirstOrDefault(l => l.StartsWith("English", StringComparison.OrdinalIgnoreCase)) ?? "English (en-US)";
    }

    // Language change → toast (strings are English today; selection persists on Save)
    partial void OnLanguageChanged(string value)
    {
        // Apply the language live across the localized chrome (nav, Settings, page headers)
        Services.Platform.Loc.I.SetLanguage(Services.Platform.Loc.CodeFromDisplay(value));
        if (_isLoading) return;
        StatusMessage = $"Language applied: {value}.";
    }

    // Animation speed / master toggle → live multiplier
    partial void OnAnimationSpeedChanged(AnimationSpeed value)
    {
        if (_isLoading) return;
        Services.Platform.AnimationHelper.Configure(value, ShowAnimations);
    }

    partial void OnShowAnimationsChanged(bool value)
    {
        if (_isLoading) return;
        Services.Platform.AnimationHelper.Configure(AnimationSpeed, value);
    }

    // ── Java manager ──

    [RelayCommand]
    private async Task RefreshJavasAsync()
    {
        try
        {
            IsScanningJava = true;
            var list = await _java.ListDetectedJavasAsync();
            DetectedJavas = new ObservableCollection<JavaRuntimeService.JavaInstall>(list);
        }
        catch (Exception ex) { Debug.WriteLine($"[SettingsVM] RefreshJavas failed: {ex}"); }
        finally { IsScanningJava = false; }
    }

    [RelayCommand]
    private void SetJavaDefault(JavaRuntimeService.JavaInstall? java)
    {
        if (java == null) return;
        JavaPath = java.Path;
        StatusMessage = $"Default Java set to Java {java.Major}.";
    }

    // ── Privacy ──

    [RelayCommand]
    private async Task ClearCacheAsync()
    {
        try
        {
            CacheStatus = "Clearing…";
            var freed = await CacheService.ClearAsync();
            CacheStatus = $"Cleared {CacheService.FormatBytes(freed)}. Instances and accounts kept.";
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SettingsVM] ClearCache failed: {ex}");
            CacheStatus = "Clear failed.";
        }
    }

    [RelayCommand]
    private async Task ResetSettingsAsync()
    {
        try
        {
            var defaults = new GlobalSettings();
            await LauncherStorageService.SaveGlobalSettingsAsync(defaults);
            StartupRegistryService.SetEnabled(false);
            await LoadAsync();
            StatusMessage = "All settings reset to defaults.";
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SettingsVM] ResetSettings failed: {ex}");
            StatusMessage = "Reset failed.";
        }
    }

    // ── About ──

    [RelayCommand]
    private async Task CheckUpdateAsync()
    {
        if (IsCheckingUpdate) return;
        try
        {
            IsCheckingUpdate = true;
            UpdateStatus = "Checking…";
            var result = await new UpdateCheckService().CheckAsync();
            UpdateStatus = result.Message;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SettingsVM] CheckUpdate failed: {ex}");
            UpdateStatus = "Update check failed.";
        }
        finally { IsCheckingUpdate = false; }
    }

    [RelayCommand]
    private void OpenGitHub()
    {
        try
        {
            Process.Start(new ProcessStartInfo("https://github.com/engionite/AnchorLauncher") { UseShellExecute = true });
        }
        catch (Exception ex) { Debug.WriteLine($"[SettingsVM] OpenGitHub failed: {ex}"); }
    }

    // ── Cloud sync ──

    [RelayCommand]
    private async Task SyncNowAsync()
    {
        if (IsSyncing) return;
        try
        {
            IsSyncing  = true;
            SyncStatus = "Syncing…";
            var settings = BuildSettings();
            await LauncherStorageService.SaveGlobalSettingsAsync(settings);
            SyncStatus = await new Services.Sync.CloudSyncService().SyncNowAsync(settings);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SettingsVM] SyncNowAsync failed: {ex}");
            SyncStatus = "Sync failed — see debug log.";
        }
        finally { IsSyncing = false; }
    }
}
