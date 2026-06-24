using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Windows;
using System.Windows.Media;
using AnchorLauncher.Models;
using AnchorLauncher.Models.Auth;
using AnchorLauncher.Services.Auth;
using AnchorLauncher.Services.Net;
using AnchorLauncher.Services.Storage;
using AnchorLauncher.Views.Auth;
using AnchorLauncher.Views.Onboarding;
using AnchorLauncher.Views.Splash;

namespace AnchorLauncher;

public partial class App : Application
{
    // Lives for the whole app session; shows "Anchor Launcher" on the user's Discord profile.
    private readonly Services.Platform.DiscordRichPresenceService _discord = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Capture any unhandled exception to a crash log so startup failures are diagnosable.
        DispatcherUnhandledException += (_, ev) => { LogCrash("Dispatcher", ev.Exception); ev.Handled = true; };
        AppDomain.CurrentDomain.UnhandledException += (_, ev) => LogCrash("AppDomain", ev.ExceptionObject as Exception);
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, ev) => { LogCrash("Task", ev.Exception); ev.SetObserved(); };

        // After a self-update: close the previous version (passed via --replaced-pid) and remove its
        // leftover *.old.exe, so an update never leaves two launchers running.
        Services.Platform.SelfUpdateService.FinishPendingUpdate(e.Args);

        // Publish Discord Rich Presence (best-effort; no-ops if Discord isn't running / id unset).
        _discord.Start();

        var splash = new SplashWindow();
        splash.SplashCompleted = RouteAfterSplashAsync;
        splash.Show();
    }

    private static void LogCrash(string source, Exception? ex)
    {
        try
        {
            var dir = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AnchorLauncher", "logs");
            System.IO.Directory.CreateDirectory(dir);
            System.IO.File.AppendAllText(System.IO.Path.Combine(dir, "crash.log"),
                $"[{DateTime.Now:O}] [{source}]\n{ex}\n\n");
        }
        catch { /* logging must never throw */ }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Clear the Discord presence as we shut down.
        try { _discord.Dispose(); } catch { /* never block shutdown */ }

        try
        {
            // Auto-sync on close (bounded so a hung copy can't trap the shutdown)
            var settings = LauncherStorageService.LoadGlobalSettingsAsync().GetAwaiter().GetResult();
            if (settings.AutoSyncOnClose)
            {
                Debug.WriteLine("[App] Auto-sync on close…");
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                var result = new Services.Sync.CloudSyncService()
                    .SyncNowAsync(settings, cts.Token).GetAwaiter().GetResult();
                Debug.WriteLine($"[App] Auto-sync: {result}");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[App] OnExit auto-sync failed: {ex.Message}");
        }
        base.OnExit(e);
    }

    /// <summary>Applies persisted network, proxy and theme settings before any window or network use.</summary>
    private async Task ApplyStartupSettingsAsync()
    {
        try
        {
            var s = await LauncherStorageService.LoadGlobalSettingsAsync();

            // Network knobs (read by DownloadHelper + the launch pipeline)
            NetworkConfig.DownloadThreads = Math.Clamp(s.DownloadThreads, 4, 32);
            NetworkConfig.RetryDownloads  = s.RetryDownloads;
            NetworkConfig.RetryAttempts   = Math.Max(1, s.RetryAttempts);
            NetworkConfig.TimeoutSeconds  = Math.Max(10, s.HttpTimeoutSeconds);

            // Proxy → applied to all HttpClients created without an explicit handler
            if (s.ProxyMode != ProxyMode.None && !string.IsNullOrWhiteSpace(s.ProxyHost))
            {
                var scheme = s.ProxyMode == ProxyMode.Socks5 ? "socks5" : "http";
                HttpClient.DefaultProxy = new WebProxy($"{scheme}://{s.ProxyHost}:{s.ProxyPort}");
                Debug.WriteLine($"[App] Proxy → {scheme}://{s.ProxyHost}:{s.ProxyPort}");
            }

            // Theme (Dark / OLED Black): swap the surface brushes before MainWindow resolves them
            Services.Platform.ThemeService.Apply(s.Theme);

            // Global animation timing preference
            Services.Platform.AnimationHelper.Configure(s.AnimationSpeed, s.ShowAnimations);

            // UI language: drives the live-localized chrome/Settings/page headers
            Services.Platform.Loc.I.SetLanguage(Services.Platform.Loc.CodeFromDisplay(s.Language));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[App] ApplyStartupSettingsAsync failed: {ex}");
        }
    }

    private async Task RouteAfterSplashAsync()
    {
        try
        {
            await ApplyStartupSettingsAsync();

            var config = await LauncherStorageService.LoadConfigAsync();

            if (config == null || !config.FirstRunComplete)
            {
                // First run: Onboarding → LoginWindow → MainWindow
                var onboarding = new OnboardingWindow();
                Current.MainWindow = onboarding;
                onboarding.Show();
                return;
            }

            // Returning user: skip login when session is valid, otherwise re-authenticate
            if (await ShouldSkipLoginAsync())
            {
                var main = new MainWindow();
                Current.MainWindow = main;
                main.Show();
            }
            else
            {
                // Expired session: LoginWindow → MainWindow
                var login = new LoginWindow();
                Current.MainWindow = login;
                login.Show();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[App] RouteAfterSplashAsync failed: {ex}");
            // Failsafe: never leave the user with nothing to click
            var main = new MainWindow();
            Current.MainWindow = main;
            main.Show();
        }
    }

    // true  → go straight to MainWindow (valid token, silently refreshed token, or offline)
    // false → show LoginWindow (active account whose session genuinely can't be refreshed while online)
    private static async Task<bool> ShouldSkipLoginAsync()
    {
        try
        {
            var store = await TokenVaultService.LoadAccountsAsync();

            // No active account = user previously chose offline = respect that, skip login
            if (store?.ActiveAccountId == null) return true;

            var id = store.ActiveAccountId.Value;
            ILauncherAccount? account = store.ActiveAccountType switch
            {
                AccountType.Microsoft => store.MicrosoftAccounts.Find(a => a.Id == id),
                AccountType.ElyBy     => store.ElyAccounts.Find(a => a.Id == id),
                _                     => null
            };

            // Account record missing (store inconsistency) = don't block the user
            if (account == null) return true;

            // Token still comfortably valid → MainWindow directly
            if (account.TokenExpiry > DateTime.UtcNow.AddMinutes(5)) return true;

            // Expired → silently refresh so the user is NOT logged out on open. The short-lived
            // game token expires in ~24h, but the refresh token lives for weeks; this was the
            // "sometimes it logs me out" bug — startup simply never attempted the refresh.
            if (await TryRefreshActiveAsync(store, account)) return true;

            // Refresh failed: only fall back to the sign-in screen when we're actually online (a
            // genuine auth failure). Offline / transient network → keep the user signed in.
            return !System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[App] ShouldSkipLoginAsync failed: {ex}");
            return true; // On any error, don't block the user
        }
    }

    /// <summary>Best-effort silent token refresh for the active account; persists the fresh token
    /// on success. Returns false when the session can't be refreshed (caller decides what to do).</summary>
    private static async Task<bool> TryRefreshActiveAsync(AccountsStore store, ILauncherAccount account)
    {
        try
        {
            if (account is MicrosoftAccount ms)
            {
                var fresh = await new MicrosoftAuthService().TrySilentRefreshAsync(ms);
                if (fresh == null) return false;
                fresh.Id = ms.Id;   // keep the same account identity in the store
                var i = store.MicrosoftAccounts.FindIndex(a => a.Id == ms.Id);
                if (i >= 0) store.MicrosoftAccounts[i] = fresh;
                await TokenVaultService.SaveAccountsAsync(store);
                Debug.WriteLine("[App] Microsoft token silently refreshed on startup.");
                return true;
            }
            if (account is ElyAccount ely)
            {
                var fresh = await new ElyAuthService().TryRefreshAsync(ely);
                if (fresh == null) return false;
                await TokenVaultService.SaveAccountsAsync(store);   // ely refreshed in place
                Debug.WriteLine("[App] Ely.by token refreshed on startup.");
                return true;
            }
        }
        catch (Exception ex) { Debug.WriteLine($"[App] TryRefreshActiveAsync failed: {ex.Message}"); }
        return false;
    }
}
