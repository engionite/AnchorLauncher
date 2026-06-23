using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AnchorLauncher.Models.Instances;
using AnchorLauncher.Models.Marketplace;
using AnchorLauncher.Services.Instances;
using AnchorLauncher.Services.Marketplace;

namespace AnchorLauncher.ViewModels;

public partial class MarketplaceViewModel : ObservableObject
{
    private const int PageSize = 20;

    private readonly MarketplaceService _service         = new();
    private readonly InstanceService    _instanceService = new();

    private int _modrinthOffset, _curseIndex, _modrinthTotal, _curseTotal;
    private CancellationTokenSource? _searchCts;
    private CancellationTokenSource? _debounceCts;

    public const string AllVersionsLabel = "All versions";

    /// <summary>Sort options for the marketplace sort ComboBox.</summary>
    public static IReadOnlyList<SortMode> SortModes { get; } =
        (SortMode[])Enum.GetValues(typeof(SortMode));

    /// <summary>One-shot preset applied by shell navigation (e.g. "Download More" shaders).</summary>
    public static ProjectType? PendingPresetType { get; set; }

    /// <summary>One-shot search text applied by shell navigation (e.g. crash auto-fix → missing dependency).</summary>
    public static string? PendingSearchQuery { get; set; }

    [ObservableProperty] private string      _searchQuery  = string.Empty;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EligibilityWarning))]
    [NotifyPropertyChangedFor(nameof(HasEligibilityWarning))]
    private ProjectType _selectedType = ProjectType.Mod;
    [ObservableProperty] private SortMode    _selectedSort = SortMode.MostDownloaded;
    [ObservableProperty] private ObservableCollection<string> _gameVersions = new();
    [ObservableProperty] private string  _selectedGameVersion = AllVersionsLabel;
    [ObservableProperty] private bool    _compatibleOnly = true;
    [ObservableProperty] private ObservableCollection<MarketplaceItem>   _results   = new();
    [ObservableProperty] private ObservableCollection<MinecraftInstance> _instances = new();
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EligibilityWarning))]
    [NotifyPropertyChangedFor(nameof(HasEligibilityWarning))]
    private MinecraftInstance? _selectedInstance;

    /// <summary>True only for content the selected instance's loader genuinely cannot load.
    /// Vanilla can't run mods or shaders (no loader); resource packs work everywhere; modpacks
    /// are left unrestricted to avoid false warnings.</summary>
    private static bool IsTypeEligible(ProjectType type, ModLoaderType loader) => type switch
    {
        ProjectType.Mod     => loader != ModLoaderType.Vanilla,
        ProjectType.Modpack => loader != ModLoaderType.Vanilla,   // a modpack is a bundle of mods
        ProjectType.Shader  => loader != ModLoaderType.Vanilla,
        _                   => true,                              // ResourcePack works on vanilla
    };

    public bool HasEligibilityWarning =>
        SelectedInstance != null && !IsTypeEligible(SelectedType, SelectedInstance.ModLoader);

    /// <summary>A precise, non-blocking notice when the chosen instance can't load the chosen content type.</summary>
    public string EligibilityWarning
    {
        get
        {
            if (SelectedInstance == null || IsTypeEligible(SelectedType, SelectedInstance.ModLoader))
                return string.Empty;
            var loc = Services.Platform.Loc.I;
            return SelectedType == ProjectType.Shader ? loc["elig_shaders"] : loc["elig_mods"];
        }
    }
    [ObservableProperty] private bool   _isLoading;
    [ObservableProperty] private bool   _isLoadingMore;
    [ObservableProperty] private bool   _hasResults;
    [ObservableProperty] private bool   _hasMore;
    [ObservableProperty] private string _statusMessage = string.Empty;

    public MarketplaceViewModel()
    {
        _ = InitAsync();
    }

    private bool _initializing = true;

    private async Task InitAsync()
    {
        try
        {
            // Consume any navigation preset (set before the page was constructed).
            // Safe to set the property here: Retrigger() no-ops while _initializing.
            if (PendingPresetType is { } preset)
            {
                SelectedType      = preset;
                PendingPresetType = null;
            }

            // Crash auto-fix routes here pre-filtered to the missing dependency's name
            if (PendingSearchQuery is { Length: > 0 } query)
            {
                SearchQuery        = query;
                PendingSearchQuery = null;
            }

            var list = await _instanceService.LoadAllAsync();
            Instances        = new ObservableCollection<MinecraftInstance>(list);
            SelectedInstance = Instances.FirstOrDefault(); // LoadAll orders by LastPlayed desc → last launched

            // Version filter dropdown: all release versions from the (cached) manifest
            var manifest = await new MojangManifestService().GetManifestAsync();
            var versions = new List<string> { AllVersionsLabel };
            if (manifest != null)
                versions.AddRange(manifest.Versions.Where(v => v.IsRelease).Select(v => v.Id));
            GameVersions = new ObservableCollection<string>(versions);

            // Default the filter to the selected instance's MC version
            SelectedGameVersion = SelectedInstance != null && versions.Contains(SelectedInstance.Version)
                ? SelectedInstance.Version
                : AllVersionsLabel;

            _initializing = false;
            await SearchAsync(reset: true);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MarketVM] InitAsync failed: {ex}");
            _initializing = false;
        }
    }

    // ── Reactive triggers ───────────────────────────────────────────────────────

    partial void OnSearchQueryChanged(string value)        => Debounce();
    partial void OnSelectedTypeChanged(ProjectType value)  => Retrigger();
    partial void OnSelectedSortChanged(SortMode value)     => Retrigger();
    partial void OnSelectedGameVersionChanged(string value) => Retrigger();
    partial void OnCompatibleOnlyChanged(bool value)       => Retrigger();

    partial void OnSelectedInstanceChanged(MinecraftInstance? value)
    {
        // Re-aim the version filter at the newly selected instance's version
        if (!_initializing && value != null && GameVersions.Contains(value.Version))
        {
            SelectedGameVersion = value.Version; // its change handler re-triggers the search
            return;
        }
        Retrigger();
    }

    private void Retrigger()
    {
        if (!_initializing) _ = SearchAsync(reset: true);
    }

    private void Debounce()
    {
        _debounceCts?.Cancel();
        var cts = _debounceCts = new CancellationTokenSource();
        _ = DebounceRunAsync(cts.Token);
    }

    private async Task DebounceRunAsync(CancellationToken ct)
    {
        try { await Task.Delay(400, ct); }
        catch (TaskCanceledException) { return; }
        if (!ct.IsCancellationRequested) await SearchAsync(reset: true);
    }

    // ── Search / paging ─────────────────────────────────────────────────────────

    private async Task SearchAsync(bool reset)
    {
        _searchCts?.Cancel();
        var cts = _searchCts = new CancellationTokenSource();
        var ct  = cts.Token;

        try
        {
            if (reset)
            {
                _modrinthOffset = 0; _curseIndex = 0;
                _modrinthTotal  = 0; _curseTotal = 0;
                Results    = new ObservableCollection<MarketplaceItem>();
                HasResults = false;
                IsLoading  = true;
            }
            else
            {
                IsLoadingMore = true;
            }
            StatusMessage = string.Empty;

            var versionFilter = SelectedGameVersion == AllVersionsLabel ? null : SelectedGameVersion;
            var res = await _service.SearchAsync(
                SearchQuery ?? string.Empty, SelectedType, SelectedInstance,
                versionFilter, CompatibleOnly, SelectedSort,
                _modrinthOffset, _curseIndex, PageSize, ct);

            if (ct.IsCancellationRequested) return;

            foreach (var item in res.Items)
                Results.Add(item);

            _modrinthTotal  = res.ModrinthTotal;
            _curseTotal     = res.CurseTotal;
            _modrinthOffset += PageSize;
            _curseIndex     += PageSize;

            HasMore    = _modrinthOffset < _modrinthTotal || _curseIndex < _curseTotal;
            HasResults = Results.Count > 0;
            if (!HasResults)
                StatusMessage = "No results — try a different search or filter.";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MarketVM] SearchAsync failed: {ex}");
            StatusMessage = "Search failed — check your connection.";
        }
        finally
        {
            IsLoading     = false;
            IsLoadingMore = false;
        }
    }

    [RelayCommand]
    private async Task LoadMoreAsync()
    {
        if (!HasMore || IsLoading || IsLoadingMore) return;
        await SearchAsync(reset: false);
    }

    // ── Install ─────────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task InstallAsync(MarketplaceItem? item)
    {
        if (item == null || item.State == InstallState.Installing) return;
        var loc = Services.Platform.Loc.I;
        if (SelectedInstance == null)
        {
            StatusMessage = loc["mp_pickinstance"];
            return;
        }

        // Accurate eligibility gate: Vanilla genuinely can't load mods or shaders.
        if (!IsTypeEligible(item.Type, SelectedInstance.ModLoader))
        {
            StatusMessage = EligibilityWarning;
            return;
        }

        try
        {
            item.State    = InstallState.Installing;
            StatusMessage = string.Format(loc["mp_installing"], item.Name);

            // Auto-snapshot the mods/ state before changing it (rollback safety net)
            if (item.Type == ProjectType.Mod)
                await new SnapshotService().TakeSnapshotAsync(SelectedInstance, $"Before installing {item.Name}");

            await _service.InstallAsync(item, SelectedInstance);
            item.State    = InstallState.Installed;
            StatusMessage = string.Format(loc["mp_installed"], item.Name, SelectedInstance.Name);

            // Offer to install any required dependencies the mod needs to run (e.g. Fabric API).
            await OfferDependenciesAsync(item, SelectedInstance, loc);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MarketVM] InstallAsync failed: {ex}");
            item.State    = InstallState.Failed;
            StatusMessage = $"{loc["mp_failed"]}: {ex.Message}";
        }
    }

    /// <summary>Resolves a just-installed mod's required Modrinth dependencies that aren't present
    /// yet and, if any, offers to install them in one click.</summary>
    private async Task OfferDependenciesAsync(MarketplaceItem item, MinecraftInstance instance, Services.Platform.Loc loc)
    {
        try
        {
            var missing = await _service.ResolveMissingDependenciesAsync(item, instance);
            if (missing.Count == 0) return;

            var dlg = new Views.Instances.DependencyDialog(item.Name, missing.Select(d => d.Name))
            {
                Owner = System.Windows.Application.Current?.MainWindow
            };
            if (dlg.ShowDialog() != true) return;

            foreach (var dep in missing)
            {
                StatusMessage = string.Format(loc["mp_installing"], dep.Name);
                await _service.InstallDependencyAsync(dep, instance);
            }
            StatusMessage = string.Format(loc["mp_installed"], item.Name, instance.Name);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MarketVM] dependency offer failed: {ex}");
        }
    }
}
