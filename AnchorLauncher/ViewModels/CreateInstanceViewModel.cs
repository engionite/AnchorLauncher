using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AnchorLauncher.Models.Instances;
using AnchorLauncher.Services.Instances;

namespace AnchorLauncher.ViewModels;

public partial class CreateInstanceViewModel : ObservableObject
{
    private readonly MojangManifestService _manifest = new();
    private readonly InstanceService       _instances = new();
    private readonly ModLoaderService      _loaders   = new();
    private readonly InstanceContentService _content  = new();

    private MojangVersionManifest? _fullManifest;

    [ObservableProperty] private string _instanceName = string.Empty;
    [ObservableProperty] private ObservableCollection<MojangVersionEntry> _versions = new();
    [ObservableProperty] private MojangVersionEntry? _selectedVersion;
    [ObservableProperty] private ModLoaderType _selectedLoader = ModLoaderType.Vanilla;

    // Loader version picker (shown only for non-Vanilla loaders)
    [ObservableProperty] private bool _showLoaderVersions;
    [ObservableProperty] private bool _isLoadingLoaderVersions;
    [ObservableProperty] private ObservableCollection<string> _loaderVersions = new();
    [ObservableProperty] private string? _selectedLoaderVersion;

    private CancellationTokenSource? _loaderCts;
    private int _loaderFetchSeq;   // monotonic guard: only the newest fetch may write results

    // Successful responses cached per (loader, mcVersion) so switching back is instant
    private readonly Dictionary<(ModLoaderType, string), List<LoaderVersion>> _loaderCache = new();

    // Version-type filters
    [ObservableProperty] private bool _showReleases  = true;
    [ObservableProperty] private bool _showSnapshots;
    [ObservableProperty] private bool _showBeta;
    [ObservableProperty] private bool _showAlpha;

    // Optionally seed the new instance's in-game settings (options.txt etc.) from an existing one,
    // so players don't have to re-tune Minecraft's options for every instance they create.
    [ObservableProperty] private ObservableCollection<MinecraftInstance> _existingInstances = new();
    [ObservableProperty] private MinecraftInstance? _copySettingsFrom;

    [ObservableProperty] private bool   _isLoadingVersions;
    [ObservableProperty] private bool   _isCreating;
    [ObservableProperty] private double _progress;
    [ObservableProperty] private string _progressStatus = string.Empty;
    [ObservableProperty] private string _errorMessage = string.Empty;

    /// <summary>Raised with the finished instance once creation + install succeed.</summary>
    public event EventHandler<MinecraftInstance>? Created;

    // ── Version manifest ────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task LoadVersionsAsync()
    {
        try
        {
            IsLoadingVersions = true;
            ErrorMessage = string.Empty;
            ExistingInstances = new ObservableCollection<MinecraftInstance>(await _instances.LoadAllAsync());
            _fullManifest = await _manifest.GetManifestAsync();
            if (_fullManifest == null)
            {
                ErrorMessage = Services.Platform.Loc.I["ci_e_versions"];
                return;
            }
            ApplyFilter();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CreateVM] LoadVersionsAsync failed: {ex}");
            ErrorMessage = Services.Platform.Loc.I["ci_e_versions"];
        }
        finally
        {
            IsLoadingVersions = false;
        }
    }

    partial void OnShowReleasesChanged(bool value)  => ApplyFilter();
    partial void OnShowSnapshotsChanged(bool value) => ApplyFilter();
    partial void OnShowBetaChanged(bool value)      => ApplyFilter();
    partial void OnShowAlphaChanged(bool value)     => ApplyFilter();

    private void ApplyFilter()
    {
        if (_fullManifest == null) return;

        var filtered = _fullManifest.Versions.Where(v =>
            (ShowReleases  && v.IsRelease)  ||
            (ShowSnapshots && v.IsSnapshot) ||
            (ShowBeta      && v.IsBeta)     ||
            (ShowAlpha     && v.IsAlpha));

        Versions = new ObservableCollection<MojangVersionEntry>(filtered);
        SelectedVersion = Versions.FirstOrDefault();
    }

    // ── Loader version picker ───────────────────────────────────────────────────

    partial void OnSelectedLoaderChanged(ModLoaderType value) => _ = DebouncedRefreshAsync();

    partial void OnSelectedVersionChanged(MojangVersionEntry? value)
    {
        if (SelectedLoader != ModLoaderType.Vanilla)
            _ = DebouncedRefreshAsync();
    }

    /// <summary>
    /// Debounces rapid loader/version switches (300ms), then refreshes. A monotonic sequence
    /// number guarantees an in-flight stale fetch can never clobber a newer one, and the old
    /// CTS is cancelled+disposed atomically before the new fetch starts.
    /// </summary>
    private async Task DebouncedRefreshAsync()
    {
        var seq = Interlocked.Increment(ref _loaderFetchSeq);

        // Swap in a fresh CTS, atomically retiring the previous fetch
        var fresh = new CancellationTokenSource();
        var old   = Interlocked.Exchange(ref _loaderCts, fresh);
        try { old?.Cancel(); old?.Dispose(); } catch { }
        var ct = fresh.Token;

        // Clear any stale "no versions" error whenever the loader or MC version changes
        ErrorMessage = string.Empty;

        if (SelectedLoader == ModLoaderType.Vanilla || SelectedVersion == null)
        {
            ShowLoaderVersions    = false;
            LoaderVersions        = new ObservableCollection<string>();
            SelectedLoaderVersion = null;
            return;
        }

        var loader    = SelectedLoader;
        var mcVersion = SelectedVersion.Id;

        ShowLoaderVersions = true;

        // Cache hit → instant, no spinner, no network
        if (_loaderCache.TryGetValue((loader, mcVersion), out var cached))
        {
            ApplyLoaderList(cached, loader, mcVersion);
            IsLoadingLoaderVersions = false;
            return;
        }

        try
        {
            IsLoadingLoaderVersions = true;
            SelectedLoaderVersion   = null;
            LoaderVersions          = new ObservableCollection<string>();

            // 300ms debounce: absorb rapid version-list scrolling before hitting the network
            await Task.Delay(300, ct);

            var list = await _loaders.GetLoaderVersionsAsync(loader, mcVersion, ct);

            // Stale guard: a newer selection has superseded this fetch
            if (seq != _loaderFetchSeq || ct.IsCancellationRequested) return;

            if (list.Count > 0)
                _loaderCache[(loader, mcVersion)] = list;

            ApplyLoaderList(list, loader, mcVersion);
        }
        catch (OperationCanceledException) { /* superseded — newest fetch owns the UI */ }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CreateVM] DebouncedRefreshAsync failed: {ex}");
            if (seq == _loaderFetchSeq)
                ErrorMessage = Services.Platform.Loc.I["ci_e_loaderlist"];
        }
        finally
        {
            // Only the newest fetch may clear the spinner
            if (seq == _loaderFetchSeq)
                IsLoadingLoaderVersions = false;
        }
    }

    private void ApplyLoaderList(List<LoaderVersion> list, ModLoaderType loader, string mcVersion)
    {
        LoaderVersions        = new ObservableCollection<string>(list.Select(v => v.Version));
        SelectedLoaderVersion = list.FirstOrDefault(v => v.Stable)?.Version ?? LoaderVersions.FirstOrDefault();

        if (LoaderVersions.Count == 0)
            ErrorMessage = string.Format(Services.Platform.Loc.I["ci_e_noloaderver"], loader, mcVersion);
    }

    // ── Create ──────────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task CreateAsync()
    {
        if (IsCreating) return;

        if (string.IsNullOrWhiteSpace(InstanceName))
        {
            ErrorMessage = Services.Platform.Loc.I["ci_e_name"];
            return;
        }
        if (SelectedVersion == null)
        {
            ErrorMessage = Services.Platform.Loc.I["ci_e_version"];
            return;
        }
        if (SelectedLoader != ModLoaderType.Vanilla && string.IsNullOrEmpty(SelectedLoaderVersion))
        {
            ErrorMessage = Services.Platform.Loc.I["ci_e_loaderver"];
            return;
        }

        try
        {
            IsCreating   = true;
            ErrorMessage = string.Empty;
            Progress     = 0;
            ProgressStatus = Services.Platform.Loc.I["ci_s_creating"];

            var progress = new Progress<DownloadProgress>(p =>
            {
                Progress       = p.Percent;
                ProgressStatus = p.Status;
            });

            var loaderVersion = SelectedLoader == ModLoaderType.Vanilla ? null : SelectedLoaderVersion;
            var instance = await _instances.CreateAsync(
                InstanceName.Trim(), SelectedVersion.Id, SelectedVersion.Type,
                SelectedLoader, loaderVersion);

            if (SelectedLoader != ModLoaderType.Vanilla)
            {
                instance.Status = InstanceStatus.Installing;
                ProgressStatus  = string.Format(Services.Platform.Loc.I["ci_s_installing"], SelectedLoader);
                await _loaders.InstallAsync(instance, progress);
                await _instances.SaveAsync(instance); // persist LaunchVersionId/loader version
            }

            if (CopySettingsFrom != null)
            {
                ProgressStatus = Services.Platform.Loc.I["ci_s_copying"];
                _content.CopyGameSettings(CopySettingsFrom.GameDir, instance.GameDir);
            }

            instance.Status = InstanceStatus.Idle;
            ProgressStatus  = Services.Platform.Loc.I["ci_s_done"];
            Created?.Invoke(this, instance);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CreateVM] CreateAsync failed: {ex}");
            ErrorMessage = $"{Services.Platform.Loc.I["ci_e_failed"]} {ex.Message}";
        }
        finally
        {
            IsCreating = false;
        }
    }
}
