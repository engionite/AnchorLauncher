using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AnchorLauncher.Models;
using AnchorLauncher.Models.Auth;
using AnchorLauncher.Models.Diagnostics;
using AnchorLauncher.Models.Instances;
using AnchorLauncher.Services.Auth;
using AnchorLauncher.Services.Diagnostics;
using AnchorLauncher.Services.Instances;
using AnchorLauncher.Services.Launch;
using AnchorLauncher.Services.Storage;

namespace AnchorLauncher.ViewModels;

public partial class InstancesViewModel : ObservableObject
{
    private const int MaxConsoleLines = 1000;

    private readonly InstanceService        _instanceService = new();
    private readonly GameLauncherService    _launcher        = new();
    private readonly InstanceContentService _content         = new();
    private readonly CrashAnalyzerService   _analyzer        = new();
    private readonly ModScannerService      _scanner         = new();
    private readonly JavaRuntimeService     _java            = new();
    private readonly Services.Instances.SnapshotService _snapshots = new();

    /// <summary>
    /// View-provided hook: navigates the shell to the marketplace pre-filtered to the given mod name.
    /// Set by InstancesPage (shell navigation is view-layer). Used by the missing-dependency auto-fix.
    /// </summary>
    public Action<string>? RequestMarketplaceForMod { get; set; }

    /// <summary>
    /// View-provided hook: shows the conflict dialog and returns true to launch anyway.
    /// Set by InstancesPage (dialog display is view-layer orchestration). Null = no UI → launch.
    /// </summary>
    public Func<List<ModConflict>, MinecraftInstance, bool>? ConfirmConflictLaunch { get; set; }

    /// <summary>Shared across navigations so a running game and its console survive page changes.</summary>
    public static InstancesViewModel Shared { get; } = new();

    private Process? _activeProcess;
    private CancellationTokenSource? _launchCts;
    private MinecraftInstance? _crashedInstance;
    private GlobalSettings? _launchGlobal;
    private (string ip, int port)? _pendingServer;
    private DateTime? _sessionStartUtc;   // set at launch, consumed for playtime on exit

    /// <summary>Launches an instance and auto-connects to a server (Home page quick-connect).</summary>
    public void LaunchWithServer(MinecraftInstance instance, string ip, int port)
    {
        _pendingServer = (ip, port);
        LaunchCommand.Execute(instance);
    }

    [ObservableProperty] private ObservableCollection<MinecraftInstance> _instances = new();
    [ObservableProperty] private bool _hasInstances;
    [ObservableProperty] private bool _isLoading;

    // Instance-card display preferences (mirrored from global settings on load)
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CardWidth))]
    private bool _isListView;
    [ObservableProperty] private bool _showInstanceNames   = true;
    [ObservableProperty] private bool _showPlaytimeOnCards = true;

    /// <summary>Card width per layout: a compact tile in grid view, a wide row in list view.</summary>
    public double CardWidth => IsListView ? 720 : 262;

    // Launch / console state
    [ObservableProperty] private bool   _isConsoleOpen;
    [ObservableProperty] private bool   _isPreparing;
    [ObservableProperty] private bool   _isGameRunning;
    [ObservableProperty] private double _launchProgress;
    [ObservableProperty] private string _launchStatus = string.Empty;
    [ObservableProperty] private string _activeInstanceName = string.Empty;
    [ObservableProperty] private ObservableCollection<string> _consoleLines = new();

    // Console appearance + filter (DESIGN 8)
    [ObservableProperty] private string _consoleFont = "Consolas";
    [ObservableProperty] private double _consoleFontSize = 11.5;
    [ObservableProperty] private string _consoleFilter = string.Empty;

    private System.ComponentModel.ICollectionView? _consoleView;

    // Crash analysis
    [ObservableProperty] private bool _hasCrash;
    [ObservableProperty] private CrashAnalysis? _crash;

    // Live RAM profiling (active while the game runs)
    [ObservableProperty] private string _ramDisplay = string.Empty;
    [ObservableProperty] private double _ramPercent;
    [ObservableProperty] private string _ramSuggestion = string.Empty;

    private System.Windows.Threading.DispatcherTimer? _ramTimer;
    private int  _ramMaxMB = 2048;
    private bool _ramToastShown;

    public InstancesViewModel()
    {
        // Live console filter over the default view (the ListBox binds to ConsoleLines)
        _consoleView = System.Windows.Data.CollectionViewSource.GetDefaultView(ConsoleLines);
        _consoleView.Filter = o => string.IsNullOrEmpty(ConsoleFilter) ||
            (o as string)?.IndexOf(ConsoleFilter, StringComparison.OrdinalIgnoreCase) >= 0;

        _ = LoadAsync();
    }

    partial void OnConsoleFilterChanged(string value)
    {
        try { _consoleView?.Refresh(); }
        catch (Exception ex) { Debug.WriteLine($"[InstancesVM] console filter failed: {ex.Message}"); }
    }

    // ── Loading ─────────────────────────────────────────────────────────────────

    public async Task LoadAsync()
    {
        try
        {
            IsLoading = true;
            var list = await _instanceService.LoadAllAsync();   // LastPlayed desc by default

            // Honor the instance-sort preference + console appearance
            var global = await LauncherStorageService.LoadGlobalSettingsAsync();
            ConsoleFont     = string.IsNullOrWhiteSpace(global.ConsoleFont) ? "Consolas" : global.ConsoleFont;
            ConsoleFontSize = Math.Clamp(global.ConsoleFontSize, 8, 16);
            IsListView          = global.InstanceView == InstanceView.List;
            ShowInstanceNames   = global.ShowInstanceNames;
            ShowPlaytimeOnCards = global.ShowPlaytimeOnCards;
            if (global.InstanceSort == InstanceSort.Name)
                list = list.OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase).ToList();

            var switcher = new VersionSwitchService();
            foreach (var inst in list)
                switcher.RefreshSwitchBadge(inst);

            // Version freshness badge vs the Mojang latest release (best-effort; uses cached manifest)
            try
            {
                var manifest = await new MojangManifestService().GetManifestAsync();
                var latest   = manifest?.Latest.Release;
                foreach (var inst in list)
                    inst.VersionStatus = inst.VersionType == "snapshot"
                        ? VersionStatusKind.Snapshot
                        : string.IsNullOrEmpty(latest) ? VersionStatusKind.Unknown
                        : string.Equals(inst.Version, latest, StringComparison.OrdinalIgnoreCase)
                            ? VersionStatusKind.UpToDate
                            : VersionStatusKind.UpdateAvailable;
            }
            catch (Exception ex) { Debug.WriteLine($"[InstancesVM] version badge failed: {ex.Message}"); }

            Instances = new ObservableCollection<MinecraftInstance>(list);
            HasInstances = Instances.Count > 0;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[InstancesVM] LoadAsync failed: {ex}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>Cheap re-read of the card display preferences + sort order, run on each navigation to
    /// the page so changes made in Settings take effect without a restart (no instance reload).</summary>
    public async Task RefreshDisplayPreferencesAsync()
    {
        try
        {
            var g = await LauncherStorageService.LoadGlobalSettingsAsync();
            IsListView          = g.InstanceView == InstanceView.List;
            ShowInstanceNames   = g.ShowInstanceNames;
            ShowPlaytimeOnCards = g.ShowPlaytimeOnCards;
            ConsoleFont         = string.IsNullOrWhiteSpace(g.ConsoleFont) ? "Consolas" : g.ConsoleFont;
            ConsoleFontSize     = Math.Clamp(g.ConsoleFontSize, 8, 16);

            // Re-apply the sort preference in place (avoids rebuilding the card collection)
            var ordered = g.InstanceSort == InstanceSort.Name
                ? Instances.OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase).ToList()
                : Instances.OrderByDescending(i => i.LastPlayed ?? DateTime.MinValue).ToList();
            for (int i = 0; i < ordered.Count; i++)
            {
                int cur = Instances.IndexOf(ordered[i]);
                if (cur != i) Instances.Move(cur, i);
            }
        }
        catch (Exception ex) { Debug.WriteLine($"[InstancesVM] RefreshDisplayPreferences failed: {ex}"); }
    }

    public void AddInstance(MinecraftInstance instance)
    {
        Instances.Insert(0, instance);
        HasInstances = Instances.Count > 0;
    }

    // ── Modpack import (Modrinth .mrpack) ───────────────────────────────────────
    [ObservableProperty] private bool   _isImporting;
    [ObservableProperty] private string _importStatus = string.Empty;
    [ObservableProperty] private double _importProgress;

    /// <summary>Imports a Modrinth .mrpack into a new instance, with progress, then reloads the list.</summary>
    public async Task ImportModpackAsync(string path)
    {
        if (IsImporting) return;
        try
        {
            var loc = Services.Platform.Loc.I;
            IsImporting    = true;
            ImportProgress = 0;
            ImportStatus   = loc["imp_reading"];

            var progress = new Progress<DownloadProgress>(p => Post(() =>
            {
                ImportProgress = p.Percent;
                ImportStatus   = p.Status;
            }));

            var inst = await new Services.Instances.ModpackImportService()
                .ImportMrpackAsync(path, null, progress);

            await LoadAsync();   // reload from disk so the imported instance shows with its badges
            ImportStatus = $"{loc["imp_done"]} {inst.Name}";
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[InstancesVM] import failed: {ex}");
            ImportStatus = $"{Services.Platform.Loc.I["imp_failed"]}: {ex.Message}";
        }
        finally { IsImporting = false; }
    }

    /// <summary>Sets a per-instance icon (built-in key or custom image path) and persists it.
    /// IconId is observable, so the card updates the moment it changes.</summary>
    public async Task SetInstanceIconAsync(MinecraftInstance instance, string? iconId)
    {
        try
        {
            instance.IconId = iconId;
            await _instanceService.SaveAsync(instance);
        }
        catch (Exception ex) { Debug.WriteLine($"[InstancesVM] SetInstanceIcon failed: {ex}"); }
    }

    // ── Launch pipeline ─────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task LaunchAsync(MinecraftInstance? instance)
    {
        if (instance == null || IsPreparing || IsGameRunning) return;

        // Pre-launch conflict check (cached per mod-state hash; instant when unchanged)
        try
        {
            var conflicts = await _scanner.ScanAsync(instance);
            if (conflicts.Count > 0 && ConfirmConflictLaunch != null &&
                !ConfirmConflictLaunch(conflicts, instance))
            {
                return; // user chose not to launch
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[InstancesVM] conflict scan failed (launching anyway): {ex}");
        }

        _launchCts = new CancellationTokenSource();
        try
        {
            var global = await LauncherStorageService.LoadGlobalSettingsAsync();
            _launchGlobal = global;

            IsConsoleOpen  = global.ShowConsoleOnLaunch;
            ConsoleLines.Clear();
            HasCrash       = false;
            Crash          = null;
            IsPreparing    = true;
            LaunchProgress = 0;
            LaunchStatus   = "Preparing…";
            ActiveInstanceName = instance.Name;
            instance.Status = InstanceStatus.Launching;

            var auth    = await BuildLaunchAuthAsync();
            var options = EffectiveLaunchOptions.Resolve(global, instance.Settings);

            // Optimized launch mode layers in the G1GC performance flag set
            if (global.LaunchPerfMode == LaunchPerfMode.Optimized)
                options = options with { JvmArgs = MergeFlags(options.JvmArgs, GlobalSettings.OptimizedFlags) };

            // "Start the game window maximized" → open at the primary screen's full size
            if (global.StartMaximized && !options.Fullscreen)
            {
                var bounds = System.Windows.Forms.Screen.PrimaryScreen?.Bounds;
                if (bounds is { Width: > 0, Height: > 0 } b)
                    options = options with { Width = b.Width, Height = b.Height };
            }

            // Quick-connect server from the Home page (one-shot)
            if (_pendingServer is { } srv && !string.IsNullOrWhiteSpace(srv.ip))
            {
                options = options with { ServerIp = srv.ip, ServerPort = srv.port };
                AppendLog($"[Anchor] Auto-connecting to {srv.ip}:{srv.port} on launch.");
            }
            _pendingServer = null;

            // Run the user's pre-launch script (if any) before touching the game
            await RunPreLaunchScriptAsync(global, instance);

            var progress = new Progress<DownloadProgress>(p => Post(() =>
            {
                LaunchProgress = p.Percent;
                LaunchStatus   = p.Status;
            }));

            var process = await _launcher.PrepareAndLaunchAsync(
                instance, auth, options, progress,
                line => Post(() => AppendLog(line)),
                _launchCts.Token);

            _activeProcess = process;
            process.Exited += (s, _) =>
            {
                var code = (s as Process)?.ExitCode ?? 0;
                Post(() => OnGameExited(instance, code));
            };

            instance.Status      = InstanceStatus.Running;
            instance.LastPlayed  = DateTime.UtcNow;
            _sessionStartUtc     = DateTime.UtcNow;   // for playtime accounting on exit
            await _instanceService.SaveAsync(instance);

            IsPreparing   = false;
            IsGameRunning = true;
            LaunchStatus  = "Running";
            AppendLog($"[Anchor] {instance.Name} launched (pid {process.Id}).");

            StartRamMonitor(options.MemoryMB);
            ApplyPostLaunchWindowBehavior(global);
        }
        catch (OperationCanceledException)
        {
            instance.Status = InstanceStatus.Idle;
            IsPreparing = false;
            AppendLog("[Anchor] Launch cancelled.");
            LaunchStatus = "Cancelled";
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[InstancesVM] LaunchAsync failed: {ex}");
            instance.Status = InstanceStatus.Idle;
            IsPreparing = false;
            AppendLog($"[Anchor] Launch failed: {ex.Message}");
            LaunchStatus = "Launch failed";
            ShowCrash(instance, ex.Message);
        }
    }

    [RelayCommand]
    private void CancelLaunch()
    {
        try { _launchCts?.Cancel(); }
        catch (Exception ex) { Debug.WriteLine($"[InstancesVM] CancelLaunch failed: {ex}"); }
    }

    [RelayCommand]
    private void StopGame()
    {
        try { _activeProcess?.Kill(entireProcessTree: true); }
        catch (Exception ex) { Debug.WriteLine($"[InstancesVM] StopGame failed: {ex}"); }
    }

    [RelayCommand]
    private void ToggleConsole() => IsConsoleOpen = !IsConsoleOpen;

    [RelayCommand]
    private void CopyConsole()
    {
        try
        {
            System.Windows.Clipboard.SetText(string.Join("\n", ConsoleLines));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[InstancesVM] CopyConsole failed: {ex}");
        }
    }

    // ── Live RAM profiling ──────────────────────────────────────────────────────

    private void StartRamMonitor(int maxMB)
    {
        _ramMaxMB      = Math.Max(512, maxMB);
        _ramToastShown = false;
        RamSuggestion  = string.Empty;
        RamPercent     = 0;
        RamDisplay     = string.Empty;

        _ramTimer ??= new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        _ramTimer.Tick -= RamTimer_Tick;
        _ramTimer.Tick += RamTimer_Tick;
        _ramTimer.Start();
    }

    private void StopRamMonitor()
    {
        _ramTimer?.Stop();
        RamDisplay    = string.Empty;
        RamPercent    = 0;
        RamSuggestion = string.Empty;
    }

    private void RamTimer_Tick(object? sender, EventArgs e)
    {
        try
        {
            var proc = _activeProcess;
            if (proc == null || proc.HasExited) { StopRamMonitor(); return; }

            proc.Refresh();
            var usedMB = proc.WorkingSet64 / (1024.0 * 1024.0);

            RamPercent = Math.Clamp(usedMB * 100.0 / _ramMaxMB, 0, 100);
            RamDisplay = $"RAM: {usedMB / 1024.0:0.0} GB / {_ramMaxMB / 1024.0:0.0} GB";

            // Over 90% of the allocation → suggest more RAM (once per launch)
            if (RamPercent > 90 && !_ramToastShown)
            {
                _ramToastShown = true;
                RamSuggestion  = $"Memory is at {RamPercent:0}% of the {_ramMaxMB / 1024.0:0.0} GB allocation — consider increasing it in Edit Instance → Settings.";
                AppendLog($"[Anchor] {RamSuggestion}");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[InstancesVM] RamTimer_Tick failed: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task ForceGcAsync()
    {
        try
        {
            var proc = _activeProcess;
            if (proc == null || proc.HasExited) return;

            // jcmd ships with JDKs next to java.exe; JRE-only runtimes don't have it
            var javaDir = Path.GetDirectoryName(proc.StartInfo.FileName);
            var jcmd    = javaDir != null ? Path.Combine(javaDir, "jcmd.exe") : null;

            if (jcmd != null && File.Exists(jcmd))
            {
                AppendLog("[Anchor] Requesting JVM garbage collection (jcmd GC.run)…");
                var psi = new ProcessStartInfo(jcmd, $"{proc.Id} GC.run")
                {
                    UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardOutput = true, RedirectStandardError = true
                };
                using var gc = Process.Start(psi);
                if (gc != null)
                {
                    await gc.WaitForExitAsync();
                    AppendLog(gc.ExitCode == 0
                        ? "[Anchor] GC request sent."
                        : $"[Anchor] jcmd exited with code {gc.ExitCode}.");
                }
            }
            else
            {
                AppendLog("[Anchor] This Java runtime has no jcmd (JRE-only) — cannot trigger GC externally.");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[InstancesVM] ForceGcAsync failed: {ex}");
            AppendLog($"[Anchor] Force GC failed: {ex.Message}");
        }
    }

    [RelayCommand]
    private void DismissRamSuggestion() => RamSuggestion = string.Empty;

    private void OnGameExited(MinecraftInstance instance, int exitCode)
    {
        instance.Status = InstanceStatus.Idle;
        IsGameRunning   = false;
        StopRamMonitor();
        _activeProcess  = null;
        AppendLog($"[Anchor] {instance.Name} exited (code {exitCode}).");
        LaunchStatus = $"Exited ({exitCode})";

        // Record playtime for this session (only when the preference is on)
        if (_sessionStartUtc is { } start && _launchGlobal?.RecordPlaytime == true)
        {
            var minutes = (long)Math.Round((DateTime.UtcNow - start).TotalMinutes);
            if (minutes > 0)
            {
                instance.PlaytimeMinutes += minutes;
                _ = _instanceService.SaveAsync(instance);
                AppendLog($"[Anchor] Session: {minutes}m  ·  total {instance.PlaytimeDisplay}.");
            }
        }
        _sessionStartUtc = null;

        // Restore the launcher window per the "on game close" preference
        ApplyGameCloseWindowBehavior();

        // A non-zero exit means a crash — analyze and surface a plain-English summary
        if (exitCode != 0)
        {
            ShowCrash(instance, null);
        }
        else if (_launchGlobal?.HideConsoleOnGameClose == true)
        {
            IsConsoleOpen = false;
        }
    }

    private void ApplyGameCloseWindowBehavior()
    {
        try
        {
            var win = System.Windows.Application.Current?.MainWindow;
            if (win == null || _launchGlobal == null) return;
            switch (_launchGlobal.OnGameCloseAction)
            {
                case GameCloseAction.Reopen:
                    win.Show();
                    win.WindowState = System.Windows.WindowState.Normal;
                    win.Activate();
                    break;
                case GameCloseAction.KeepMinimized:
                    win.WindowState = System.Windows.WindowState.Minimized;
                    break;
                // Nothing: leave as-is
            }
        }
        catch (Exception ex) { Debug.WriteLine($"[InstancesVM] game-close window behavior failed: {ex}"); }
    }

    // ── Crash analysis ──────────────────────────────────────────────────────────

    private void ShowCrash(MinecraftInstance instance, string? extra)
    {
        try
        {
            var log = string.Join("\n", ConsoleLines);
            if (!string.IsNullOrEmpty(extra)) log += "\n" + extra;

            Crash            = _analyzer.Analyze(instance.GameDir, log);
            _crashedInstance = instance;
            HasCrash         = true;
            if (_launchGlobal?.ShowConsoleOnCrash != false)
                IsConsoleOpen = true;   // ensure the crash banner is visible
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[InstancesVM] ShowCrash failed: {ex}");
        }
    }

    [RelayCommand]
    private async Task ApplyCrashFixAsync()
    {
        if (Crash == null || _crashedInstance == null) return;
        var instance = _crashedInstance;
        var fix      = Crash;
        try
        {
            switch (fix.Fix)
            {
                // ── OOM → add 2 GB and relaunch ─────────────────────────────────────
                case CrashFixKind.IncreaseMemory:
                {
                    var global  = await LauncherStorageService.LoadGlobalSettingsAsync();
                    var current = instance.Settings.MemoryMB ?? global.MemoryMB;
                    var bumped  = current + 2048;
                    instance.Settings.MemoryMB = bumped;
                    await _instanceService.SaveAsync(instance);
                    AppendLog($"[Anchor] Auto-fix: memory raised to {bumped / 1024.0:0.#} GB. Relaunching…");
                    HasCrash = false;
                    await LaunchAsync(instance);
                    break;
                }

                // ── Wrong Java → download the correct JRE, pin it, relaunch ──────────
                case CrashFixKind.DownloadJava:
                {
                    var major = fix.RequiredJavaMajor > 0 ? fix.RequiredJavaMajor : 17;
                    AppendLog($"[Anchor] Auto-fix: provisioning Java {major}…");
                    IsConsoleOpen = true;

                    var progress = new Progress<DownloadProgress>(p => Post(() =>
                        LaunchStatus = p.Status));

                    var javaExe = await _java.ResolveJavaAsync(major, progress);
                    if (string.IsNullOrEmpty(javaExe) || !File.Exists(javaExe))
                    {
                        AppendLog($"[Anchor] Auto-fix failed: could not obtain a Java {major} runtime.");
                        LaunchStatus = "Auto-fix failed";
                        return;
                    }

                    instance.Settings.JavaPath = javaExe;   // pin the correct runtime
                    await _instanceService.SaveAsync(instance);
                    AppendLog($"[Anchor] Auto-fix: Java {major} ready ({javaExe}). Relaunching…");
                    HasCrash = false;
                    await LaunchAsync(instance);
                    break;
                }

                // ── Missing dependency → open the marketplace filtered to that mod ───
                case CrashFixKind.InstallDependency:
                {
                    var name = fix.DependencyName;
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        _content.OpenInExplorer(Path.Combine(instance.GameDir, "mods"));
                        break;
                    }

                    if (RequestMarketplaceForMod != null)
                    {
                        AppendLog($"[Anchor] Auto-fix: opening the marketplace for ‘{name}’ — install it, then relaunch.");
                        HasCrash = false;
                        RequestMarketplaceForMod(name!);
                    }
                    else
                    {
                        _content.OpenInExplorer(Path.Combine(instance.GameDir, "mods"));
                    }
                    break;
                }

                // ── Mod conflict → snapshot, disable the jar, relaunch ──────────────
                case CrashFixKind.DisableConflictingMod:
                {
                    var file = fix.ConflictingModFile;
                    if (string.IsNullOrWhiteSpace(file))
                    {
                        _content.OpenInExplorer(Path.Combine(instance.GameDir, "mods"));
                        break;
                    }

                    // Safety net before any file change
                    await _snapshots.TakeSnapshotAsync(instance, $"Before disabling {file} (crash auto-fix)");

                    var modsDir = Path.Combine(instance.GameDir, "mods");
                    var jar     = Path.Combine(modsDir, file!);
                    var disabled = jar + ".disabled";
                    if (File.Exists(jar))
                    {
                        if (File.Exists(disabled)) File.Delete(disabled);
                        File.Move(jar, disabled);
                        AppendLog($"[Anchor] Auto-fix: disabled ‘{file}’ (snapshot saved). Relaunching…");
                        HasCrash = false;
                        await LaunchAsync(instance);
                    }
                    else
                    {
                        AppendLog($"[Anchor] Auto-fix: ‘{file}’ was no longer in the mods folder.");
                        _content.OpenInExplorer(modsDir);
                    }
                    break;
                }

                // ── Manual fallback ─────────────────────────────────────────────────
                case CrashFixKind.OpenModsFolder:
                    _content.OpenInExplorer(Path.Combine(instance.GameDir, "mods"));
                    break;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[InstancesVM] ApplyCrashFix failed: {ex}");
            AppendLog($"[Anchor] Auto-fix failed: {ex.Message}");
        }
    }

    [RelayCommand]
    private void DismissCrash() => HasCrash = false;

    // ── CRUD commands ───────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task CloneAsync(MinecraftInstance? instance)
    {
        if (instance == null) return;
        try
        {
            var clone = await _instanceService.CloneAsync(instance);
            AddInstance(clone);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[InstancesVM] CloneAsync failed: {ex}");
        }
    }

    public async Task DeleteAsync(MinecraftInstance instance)
    {
        try
        {
            await _instanceService.DeleteAsync(instance);
            Instances.Remove(instance);
            HasInstances = Instances.Count > 0;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[InstancesVM] DeleteAsync failed: {ex}");
        }
    }

    public async Task ExportAsync(MinecraftInstance instance, string zipPath)
    {
        try { await _instanceService.ExportAsync(instance, zipPath); }
        catch (Exception ex) { Debug.WriteLine($"[InstancesVM] ExportAsync failed: {ex}"); }
    }

    public async Task ImportAsync(string zipPath)
    {
        try
        {
            var inst = await _instanceService.ImportAsync(zipPath);
            AddInstance(inst);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[InstancesVM] ImportAsync failed: {ex}");
        }
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────

    private static async Task<LaunchAuth> BuildLaunchAuthAsync()
    {
        try
        {
            var store = await TokenVaultService.LoadAccountsAsync();
            if (store.ActiveAccountId is Guid id && store.ActiveAccountType is AccountType type)
            {
                if (type == AccountType.Microsoft)
                {
                    var acc = store.MicrosoftAccounts.FirstOrDefault(a => a.Id == id);
                    if (acc != null)
                        return new LaunchAuth(
                            acc.Username, acc.Uuid,
                            TokenVaultService.Unprotect(acc.EncryptedMinecraftToken), "msa", false);
                }
                else if (type == AccountType.ElyBy)
                {
                    var acc = store.ElyAccounts.FirstOrDefault(a => a.Id == id);
                    if (acc != null)
                        return new LaunchAuth(
                            acc.Username, acc.Uuid,
                            TokenVaultService.Unprotect(acc.EncryptedAccessToken), "msa", true);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[InstancesVM] BuildLaunchAuthAsync failed: {ex}");
        }
        return LaunchAuth.Offline();
    }

    private static string MergeFlags(string existing, string[] toAdd)
    {
        var have = (existing ?? string.Empty)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).ToList();
        foreach (var flag in toAdd)
            if (!have.Any(h => h.Equals(flag, StringComparison.OrdinalIgnoreCase)))
                have.Add(flag);
        return string.Join(' ', have);
    }

    private async Task RunPreLaunchScriptAsync(GlobalSettings global, MinecraftInstance instance)
    {
        var script = global.PreLaunchScript;
        if (string.IsNullOrWhiteSpace(script) || !File.Exists(script)) return;

        try
        {
            AppendLog($"[Anchor] Running pre-launch script: {script}");
            var ext = Path.GetExtension(script).ToLowerInvariant();
            var psi = ext switch
            {
                ".ps1" => new ProcessStartInfo("powershell.exe", $"-NoProfile -ExecutionPolicy Bypass -File \"{script}\""),
                ".sh"  => new ProcessStartInfo("bash.exe", $"\"{script}\""),
                _      => new ProcessStartInfo("cmd.exe", $"/c \"{script}\"")
            };
            psi.UseShellExecute        = false;
            psi.CreateNoWindow         = true;
            psi.WorkingDirectory       = instance.GameDir;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError  = true;

            using var proc = Process.Start(psi);
            if (proc == null) return;

            async Task PumpAsync(StreamReader r)
            {
                string? l;
                while ((l = await r.ReadLineAsync()) != null) { var line = l; Post(() => AppendLog($"[script] {line}")); }
            }
            _ = PumpAsync(proc.StandardOutput);
            _ = PumpAsync(proc.StandardError);

            await proc.WaitForExitAsync(_launchCts?.Token ?? CancellationToken.None);
            AppendLog($"[Anchor] Pre-launch script exited (code {proc.ExitCode}).");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[InstancesVM] pre-launch script failed: {ex}");
            AppendLog($"[Anchor] Pre-launch script failed: {ex.Message}");
        }
    }

    private static void ApplyPostLaunchWindowBehavior(GlobalSettings global)
    {
        try
        {
            var win = System.Windows.Application.Current?.MainWindow;
            if (win == null) return;
            switch (global.OnLaunchAction)
            {
                case LaunchAction.Close:    win.Close(); break;
                case LaunchAction.Minimize: win.WindowState = System.Windows.WindowState.Minimized; break;
                // KeepOpen: leave the window as-is
            }
        }
        catch (Exception ex) { Debug.WriteLine($"[InstancesVM] post-launch window behavior failed: {ex}"); }
    }

    private void AppendLog(string line)
    {
        ConsoleLines.Add(line);
        while (ConsoleLines.Count > MaxConsoleLines)
            ConsoleLines.RemoveAt(0);
    }

    /// <summary>Thread-safe console feed for out-of-page operations (e.g. version switches).</summary>
    public void LogExternal(string line) => Post(() =>
    {
        IsConsoleOpen = true;
        AppendLog(line);
    });

    /// <summary>Undoes the most recent version switch for the instance.</summary>
    [RelayCommand]
    private async Task UndoSwitchAsync(MinecraftInstance? instance)
    {
        if (instance == null || instance.Status != InstanceStatus.Idle) return;
        try
        {
            var result = await new VersionSwitchService().UndoLastSwitchAsync(instance, LogExternal);
            LaunchStatus = result;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[InstancesVM] UndoSwitch failed: {ex}");
            LogExternal($"[Anchor] Undo failed: {ex.Message}");
        }
    }

    private static void Post(Action action)
    {
        var disp = System.Windows.Application.Current?.Dispatcher;
        if (disp == null || disp.CheckAccess())
            action();                 // already on the UI thread
        else
            disp.InvokeAsync(action); // marshal immediately, non-blocking
    }
}
