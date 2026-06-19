using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AnchorLauncher.Models.Auth;
using AnchorLauncher.Models.Home;
using AnchorLauncher.Models.Instances;
using AnchorLauncher.Models.Marketplace;
using AnchorLauncher.Services.Auth;
using AnchorLauncher.Services.Instances;
using AnchorLauncher.Services.Marketplace;
using AnchorLauncher.Services.Storage;

namespace AnchorLauncher.ViewModels;

/// <summary>
/// The landing page: a welcome hero, a "Continue Playing" rail of recent instances, a live
/// "Trending Today" list pulled from Modrinth, and an Anchor News column. Launch actions defer
/// to the shared <see cref="InstancesViewModel"/>; navigation is handled by view-injected hooks.
/// </summary>
public partial class HomeViewModel : ObservableObject
{
    private readonly InstanceService _instanceService = new();
    private readonly ModrinthClient  _modrinth        = new();

    [ObservableProperty] private string _greeting   = "Welcome to Anchor";
    [ObservableProperty] private string _subtitle   = string.Empty;
    [ObservableProperty] private bool   _hasAccount;
    [ObservableProperty] private string _quickLaunchLabel = "Quick Launch";

    [ObservableProperty] private ObservableCollection<MinecraftInstance> _recentInstances = new();
    [ObservableProperty] private bool _hasInstances;

    [ObservableProperty] private ObservableCollection<MarketplaceItem> _trendingMods = new();
    [ObservableProperty] private bool _isLoadingMods = true;

    [ObservableProperty] private ObservableCollection<NewsItem> _news = new();

    // Recent screenshots strip
    [ObservableProperty] private ObservableCollection<string> _recentScreenshots = new();
    [ObservableProperty] private bool _hasScreenshots;

    // Server favorites quick-connect
    [ObservableProperty] private ObservableCollection<ServerFavorite> _serverFavorites = new();
    [ObservableProperty] private string _newServerName = string.Empty;
    [ObservableProperty] private string _newServerIp = string.Empty;
    [ObservableProperty] private int    _newServerPort = 25565;

    private readonly ServerFavoritesService _serverStore = new();
    private MinecraftInstance? _lastPlayed;
    private List<MinecraftInstance> _allInstances = new();

    // ── View-injected navigation hooks (set by HomePage) ─────────────────────
    public Action?         NavigateToInstances    { get; set; }
    public Action?         CreateInstance         { get; set; }
    public Action<string>? OpenMarketplaceForMod  { get; set; }
    public Action?         RequestSignIn          { get; set; }

    public HomeViewModel()
    {
        BuildNews();
        _ = InitAsync();
    }

    private async Task InitAsync()
    {
        await LoadAccountAsync();
        await LoadInstancesAsync();
        LoadServerFavorites();
        await LoadScreenshotsAsync();
        await LoadTrendingAsync();
    }

    private void LoadServerFavorites()
    {
        try { ServerFavorites = new ObservableCollection<ServerFavorite>(_serverStore.Load()); }
        catch (Exception ex) { Debug.WriteLine($"[HomeVM] LoadServerFavorites failed: {ex}"); }
    }

    /// <summary>Newest 3 screenshots across all instances' screenshots/ folders.</summary>
    private async Task LoadScreenshotsAsync()
    {
        try
        {
            var shots = await Task.Run(() =>
            {
                var files = new List<(string Path, DateTime When)>();
                foreach (var inst in _allInstances)
                {
                    var dir = Path.Combine(inst.GameDir, "screenshots");
                    if (!Directory.Exists(dir)) continue;
                    foreach (var f in Directory.EnumerateFiles(dir, "*.png"))
                        files.Add((f, File.GetLastWriteTimeUtc(f)));
                }
                return files.OrderByDescending(f => f.When).Take(3).Select(f => f.Path).ToList();
            });

            RecentScreenshots = new ObservableCollection<string>(shots);
            HasScreenshots    = RecentScreenshots.Count > 0;
        }
        catch (Exception ex) { Debug.WriteLine($"[HomeVM] LoadScreenshots failed: {ex}"); }
    }

    // ── Account → greeting ───────────────────────────────────────────────────
    private async Task LoadAccountAsync()
    {
        try
        {
            var store = await TokenVaultService.LoadAccountsAsync();
            ILauncherAccount? active = null;
            if (store.ActiveAccountId is { } id)
                active = store.MicrosoftAccounts.Cast<ILauncherAccount>()
                            .Concat(store.ElyAccounts)
                            .FirstOrDefault(a => a.Id == id);

            HasAccount = active != null;
            var loc = Services.Platform.Loc.I;
            Greeting   = active != null ? $"{loc["home_welcome"]} {active.Username}" : "Welcome to Anchor";
            Subtitle   = active != null
                ? $"{DateTime.Now.ToString("dddd, MMMM d", CultureFor(loc.Current))}  ·  {loc["home_ready"]}"
                : loc["home_signin"];
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[HomeVM] LoadAccount failed: {ex}");
        }
    }

    /// <summary>Best-effort CultureInfo from a 2-letter language code (for localized date formatting).</summary>
    private static System.Globalization.CultureInfo CultureFor(string code)
    {
        try { return System.Globalization.CultureInfo.GetCultureInfo(code); }
        catch { return System.Globalization.CultureInfo.InvariantCulture; }
    }

    // ── Instances → Continue Playing + Quick Launch ──────────────────────────
    private async Task LoadInstancesAsync()
    {
        try
        {
            var all = await _instanceService.LoadAllAsync();   // ordered by LastPlayed desc
            _allInstances   = all;
            RecentInstances = new ObservableCollection<MinecraftInstance>(all.Take(3));
            HasInstances    = RecentInstances.Count > 0;

            _lastPlayed = all.FirstOrDefault();
            var ql = Services.Platform.Loc.I["home_quick_launch"];
            QuickLaunchLabel = _lastPlayed != null ? $"{ql} · {_lastPlayed.Name}" : ql;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[HomeVM] LoadInstances failed: {ex}");
        }
    }

    // ── Trending mods (Modrinth, by downloads) ───────────────────────────────
    private async Task LoadTrendingAsync()
    {
        try
        {
            IsLoadingMods = true;
            var (items, _) = await _modrinth.SearchAsync(
                query: string.Empty, type: ProjectType.Mod, gameVersion: null, loader: null,
                sort: SortMode.MostDownloaded, offset: 0, limit: 5, ct: default);

            TrendingMods = new ObservableCollection<MarketplaceItem>(items);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[HomeVM] LoadTrending failed: {ex}");
        }
        finally
        {
            IsLoadingMods = false;
        }
    }

    private void BuildNews()
    {
        var loc = Services.Platform.Loc.I;
        News = new ObservableCollection<NewsItem>
        {
            new() { Date = "v1.0.3", Title = loc["news5_t"], Summary = loc["news5_b"] },
            new() { Date = "v1.0.2", Title = loc["news6_t"], Summary = loc["news6_b"] },
            new() { Date = "Beta",    Title = loc["news4_t"], Summary = loc["news4_b"] },
            new() { Date = "v1.0",    Title = loc["news1_t"], Summary = loc["news1_b"] },
            new() { Date = "Auth",    Title = loc["news2_t"], Summary = loc["news2_b"] },
            new() { Date = "Phase 7", Title = loc["news3_t"], Summary = loc["news3_b"] },
        };
    }

    // ── Commands ─────────────────────────────────────────────────────────────

    [RelayCommand]
    private void QuickLaunch()
    {
        try
        {
            if (_lastPlayed == null) { CreateInstance?.Invoke(); return; }
            NavigateToInstances?.Invoke();
            InstancesViewModel.Shared.LaunchCommand.Execute(_lastPlayed);
        }
        catch (Exception ex) { Debug.WriteLine($"[HomeVM] QuickLaunch failed: {ex}"); }
    }

    [RelayCommand]
    private void LaunchInstance(MinecraftInstance? instance)
    {
        try
        {
            if (instance == null) return;
            NavigateToInstances?.Invoke();
            InstancesViewModel.Shared.LaunchCommand.Execute(instance);
        }
        catch (Exception ex) { Debug.WriteLine($"[HomeVM] LaunchInstance failed: {ex}"); }
    }

    [RelayCommand]
    private void NewInstance() => CreateInstance?.Invoke();

    [RelayCommand]
    private void InstallMod(MarketplaceItem? item)
    {
        if (item != null) OpenMarketplaceForMod?.Invoke(item.Name);
    }

    [RelayCommand]
    private void SignIn() => RequestSignIn?.Invoke();

    // ── Server favorites ─────────────────────────────────────────────────────

    [RelayCommand]
    private void AddServer()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(NewServerIp)) return;
            ServerFavorites.Add(new ServerFavorite
            {
                Name = string.IsNullOrWhiteSpace(NewServerName) ? NewServerIp : NewServerName.Trim(),
                Ip   = NewServerIp.Trim(),
                Port = NewServerPort <= 0 ? 25565 : NewServerPort
            });
            _serverStore.Save(ServerFavorites);
            NewServerName = string.Empty;
            NewServerIp   = string.Empty;
            NewServerPort = 25565;
        }
        catch (Exception ex) { Debug.WriteLine($"[HomeVM] AddServer failed: {ex}"); }
    }

    [RelayCommand]
    private void RemoveServer(ServerFavorite? fav)
    {
        try
        {
            if (fav == null) return;
            ServerFavorites.Remove(fav);
            _serverStore.Save(ServerFavorites);
        }
        catch (Exception ex) { Debug.WriteLine($"[HomeVM] RemoveServer failed: {ex}"); }
    }

    [RelayCommand]
    private void ConnectServer(ServerFavorite? fav)
    {
        try
        {
            if (fav == null) return;
            if (_lastPlayed == null) { CreateInstance?.Invoke(); return; }
            NavigateToInstances?.Invoke();
            InstancesViewModel.Shared.LaunchWithServer(_lastPlayed, fav.Ip, fav.Port);
        }
        catch (Exception ex) { Debug.WriteLine($"[HomeVM] ConnectServer failed: {ex}"); }
    }
}
