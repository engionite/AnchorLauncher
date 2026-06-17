using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AnchorLauncher.Models;
using AnchorLauncher.Models.Instances;
using AnchorLauncher.Services.Instances;
using AnchorLauncher.Services.Storage;

namespace AnchorLauncher.ViewModels;

public partial class EditInstanceViewModel : ObservableObject
{
    private readonly InstanceContentService _content   = new();
    private readonly InstanceService        _instances = new();
    private readonly SnapshotService        _snapshots = new();
    private readonly MinecraftInstance      _instance;
    private GlobalSettings _global = new();

    public string InstanceName => _instance.Name;

    [ObservableProperty] private int _selectedTabIndex;

    [ObservableProperty] private ObservableCollection<ModEntry>           _mods          = new();
    [ObservableProperty] private ObservableCollection<ResourcePackEntry>  _resourcePacks = new();
    [ObservableProperty] private ObservableCollection<WorldSaveEntry>     _saves         = new();

    // Snapshot history (Mods tab)
    [ObservableProperty] private bool _isSnapshotPanelOpen;
    [ObservableProperty] private ObservableCollection<ModSnapshot> _snapshotHistory = new();

    // Shaderpacks (live-monitored via FileSystemWatcher)
    [ObservableProperty] private ObservableCollection<ModEntry> _shaderpacks = new();
    private FileSystemWatcher? _shaderWatcher;

    /// <summary>Raised when the user clicks "Download More" shaders — shell navigates to the marketplace.</summary>
    public event EventHandler? DownloadShadersRequested;

    // Settings tab — an "override" toggle plus the value for each field
    [ObservableProperty] private bool   _overrideMemory;
    [ObservableProperty] private int    _memoryMB = 2048;
    [ObservableProperty] private bool   _overrideJava;
    [ObservableProperty] private string _javaPath = string.Empty;
    [ObservableProperty] private bool   _overrideResolution;
    [ObservableProperty] private int    _windowWidth  = 854;
    [ObservableProperty] private int    _windowHeight = 480;
    [ObservableProperty] private bool   _overrideFullscreen;
    [ObservableProperty] private bool   _fullscreen;
    [ObservableProperty] private bool   _overrideJvm;
    [ObservableProperty] private string _jvmArgs = string.Empty;

    [ObservableProperty] private string _statusMessage = string.Empty;

    public event EventHandler? Closed;

    public EditInstanceViewModel(MinecraftInstance instance)
    {
        _instance = instance;
        _ = InitAsync();
    }

    private async Task InitAsync()
    {
        _global = await LauncherStorageService.LoadGlobalSettingsAsync();
        LoadContent();
        LoadSettings();
    }

    private void LoadContent()
    {
        Mods          = new ObservableCollection<ModEntry>(_content.ListMods(_instance.GameDir));
        ResourcePacks = new ObservableCollection<ResourcePackEntry>(_content.ListResourcePacks(_instance.GameDir));
        Saves         = new ObservableCollection<WorldSaveEntry>(_content.ListSaves(_instance.GameDir));
        Shaderpacks   = new ObservableCollection<ModEntry>(_content.ListShaderpacks(_instance.GameDir));
        StartShaderWatcher();
    }

    // ── Shaderpacks (hot-swap with live directory monitor) ──────────────────────

    private void StartShaderWatcher()
    {
        try
        {
            var dir = Path.Combine(_instance.GameDir, "shaderpacks");
            Directory.CreateDirectory(dir);

            _shaderWatcher?.Dispose();
            _shaderWatcher = new FileSystemWatcher(dir)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName,
                EnableRaisingEvents = true
            };
            FileSystemEventHandler refresh = (_, _) => System.Windows.Application.Current?.Dispatcher.InvokeAsync(RefreshShaderpacks);
            _shaderWatcher.Created += refresh;
            _shaderWatcher.Deleted += refresh;
            _shaderWatcher.Renamed += (_, _) => System.Windows.Application.Current?.Dispatcher.InvokeAsync(RefreshShaderpacks);
        }
        catch (Exception ex) { Debug.WriteLine($"[EditVM] StartShaderWatcher failed: {ex}"); }
    }

    private void RefreshShaderpacks()
    {
        try { Shaderpacks = new ObservableCollection<ModEntry>(_content.ListShaderpacks(_instance.GameDir)); }
        catch (Exception ex) { Debug.WriteLine($"[EditVM] RefreshShaderpacks failed: {ex}"); }
    }

    /// <summary>Called by the dialog on close to release the directory watcher.</summary>
    public void Shutdown()
    {
        try { _shaderWatcher?.Dispose(); _shaderWatcher = null; }
        catch (Exception ex) { Debug.WriteLine($"[EditVM] Shutdown failed: {ex}"); }
    }

    [RelayCommand]
    private void ToggleShaderpack(ModEntry? pack)
    {
        if (pack == null) return;
        try { _content.SetShaderpackEnabled(pack, pack.IsEnabled); }
        catch (Exception ex) { Debug.WriteLine($"[EditVM] ToggleShaderpack failed: {ex}"); }
    }

    [RelayCommand]
    private void DeleteShaderpack(ModEntry? pack)
    {
        if (pack == null) return;
        try
        {
            _content.DeleteShaderpack(pack);
            Shaderpacks.Remove(pack);
        }
        catch (Exception ex) { Debug.WriteLine($"[EditVM] DeleteShaderpack failed: {ex}"); }
    }

    [RelayCommand]
    private void DownloadMoreShaders()
    {
        try { DownloadShadersRequested?.Invoke(this, EventArgs.Empty); }
        catch (Exception ex) { Debug.WriteLine($"[EditVM] DownloadMoreShaders failed: {ex}"); }
    }

    /// <summary>Installs .zip files dropped onto the shaderpacks panel.</summary>
    public void InstallDroppedShaderpacks(IEnumerable<string> paths)
    {
        try
        {
            foreach (var path in paths.Where(p => p.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) && File.Exists(p)))
                _content.InstallShaderpack(_instance.GameDir, path);
            // The watcher refreshes the list automatically
            StatusMessage = "Shaderpack(s) installed.";
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[EditVM] InstallDroppedShaderpacks failed: {ex}");
            StatusMessage = "Shaderpack install failed.";
        }
    }

    private void LoadSettings()
    {
        var s = _instance.Settings;
        OverrideMemory     = s.MemoryMB.HasValue;                       MemoryMB     = s.MemoryMB ?? _global.MemoryMB;
        OverrideJava       = !string.IsNullOrWhiteSpace(s.JavaPath);    JavaPath     = s.JavaPath ?? _global.JavaPath ?? string.Empty;
        OverrideResolution = s.WindowWidth.HasValue || s.WindowHeight.HasValue;
        WindowWidth        = s.WindowWidth  ?? _global.WindowWidth;
        WindowHeight       = s.WindowHeight ?? _global.WindowHeight;
        OverrideFullscreen = s.Fullscreen.HasValue;                     Fullscreen   = s.Fullscreen ?? _global.Fullscreen;
        OverrideJvm        = !string.IsNullOrWhiteSpace(s.JvmArgs);     JvmArgs      = s.JvmArgs ?? _global.JvmArgs;
    }

    // ── Mods ────────────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task ToggleModAsync(ModEntry? mod)
    {
        if (mod == null) return;
        try
        {
            // Snapshot the on-disk state before mutating it
            await _snapshots.TakeSnapshotAsync(_instance,
                $"Before toggling {mod.FileName}");
            _content.SetModEnabled(mod, mod.IsEnabled);
        }
        catch (Exception ex) { Debug.WriteLine($"[EditVM] ToggleMod failed: {ex}"); }
    }

    // ── Snapshot history ────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task ToggleSnapshotPanelAsync()
    {
        try
        {
            IsSnapshotPanelOpen = !IsSnapshotPanelOpen;
            if (IsSnapshotPanelOpen)
            {
                var list = await Task.Run(() => _snapshots.ListSnapshots(_instance));
                SnapshotHistory = new ObservableCollection<ModSnapshot>(list);
            }
        }
        catch (Exception ex) { Debug.WriteLine($"[EditVM] ToggleSnapshotPanel failed: {ex}"); }
    }

    [RelayCommand]
    private async Task RestoreSnapshotAsync(ModSnapshot? snapshot)
    {
        if (snapshot == null) return;
        try
        {
            // Record where we are now so the restore itself is undoable
            await _snapshots.TakeSnapshotAsync(_instance, "Before restoring snapshot");

            StatusMessage = "Restoring snapshot…";
            StatusMessage = await _snapshots.RestoreSnapshotAsync(_instance, snapshot);

            // Refresh both the mod list and the history
            Mods = new ObservableCollection<ModEntry>(_content.ListMods(_instance.GameDir));
            var list = await Task.Run(() => _snapshots.ListSnapshots(_instance));
            SnapshotHistory = new ObservableCollection<ModSnapshot>(list);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[EditVM] RestoreSnapshot failed: {ex}");
            StatusMessage = "Restore failed.";
        }
    }

    [RelayCommand] private void OpenModsFolder()          => OpenSub("mods");
    [RelayCommand] private void OpenResourcePacksFolder() => OpenSub("resourcepacks");
    [RelayCommand] private void OpenSavesFolder()         => OpenSub("saves");

    // ── Resource packs ──────────────────────────────────────────────────────────

    [RelayCommand]
    private void MovePackUp(ResourcePackEntry? pack)
    {
        if (pack == null) return;
        var i = ResourcePacks.IndexOf(pack);
        if (i > 0) { ResourcePacks.Move(i, i - 1); PersistPackOrder(); }
    }

    [RelayCommand]
    private void MovePackDown(ResourcePackEntry? pack)
    {
        if (pack == null) return;
        var i = ResourcePacks.IndexOf(pack);
        if (i >= 0 && i < ResourcePacks.Count - 1) { ResourcePacks.Move(i, i + 1); PersistPackOrder(); }
    }

    [RelayCommand]
    private void DeletePack(ResourcePackEntry? pack)
    {
        if (pack == null) return;
        try
        {
            _content.DeleteResourcePack(pack);
            ResourcePacks.Remove(pack);
            PersistPackOrder();
        }
        catch (Exception ex) { Debug.WriteLine($"[EditVM] DeletePack failed: {ex}"); }
    }

    private void PersistPackOrder() => _content.SaveResourcePackOrder(_instance.GameDir, ResourcePacks);

    // ── Worlds ──────────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task BackupWorldAsync(WorldSaveEntry? world)
    {
        if (world == null) return;
        try
        {
            StatusMessage = $"Backing up {world.Name}…";
            await _content.BackupWorldAsync(world, _instance.GameDir);
            StatusMessage = $"Backed up {world.Name} to backups/.";
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[EditVM] BackupWorld failed: {ex}");
            StatusMessage = "Backup failed.";
        }
    }

    /// <summary>Deletes a world after the caller has confirmed.</summary>
    public void DeleteWorld(WorldSaveEntry world)
    {
        try
        {
            _content.DeleteWorld(world);
            Saves.Remove(world);
            StatusMessage = $"Deleted {world.Name}.";
        }
        catch (Exception ex) { Debug.WriteLine($"[EditVM] DeleteWorld failed: {ex}"); }
    }

    // ── Settings ────────────────────────────────────────────────────────────────

    public void SetJavaPath(string path)
    {
        JavaPath     = path;
        OverrideJava = true;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        try
        {
            var s = _instance.Settings;
            s.MemoryMB     = OverrideMemory     ? MemoryMB : null;
            s.JavaPath     = OverrideJava       ? (string.IsNullOrWhiteSpace(JavaPath) ? null : JavaPath) : null;
            s.WindowWidth  = OverrideResolution ? WindowWidth  : null;
            s.WindowHeight = OverrideResolution ? WindowHeight : null;
            s.Fullscreen   = OverrideFullscreen ? Fullscreen : null;
            s.JvmArgs      = OverrideJvm         ? (string.IsNullOrWhiteSpace(JvmArgs) ? null : JvmArgs) : null;

            await _instances.SaveAsync(_instance);
            Closed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex) { Debug.WriteLine($"[EditVM] SaveAsync failed: {ex}"); }
    }

    [RelayCommand]
    private void Cancel() => Closed?.Invoke(this, EventArgs.Empty);

    private void OpenSub(string sub)
    {
        try { _content.OpenInExplorer(Path.Combine(_instance.GameDir, sub)); }
        catch (Exception ex) { Debug.WriteLine($"[EditVM] OpenSub failed: {ex}"); }
    }
}
