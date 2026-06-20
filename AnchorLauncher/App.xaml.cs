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

    // true  → go straight to MainWindow (valid token, or no account = offline user)
    // false → show LoginWindow (active account exists but token is expired)
    private static async Task<bool> ShouldSkipLoginAsync()
    {
        try
        {
            var store = await TokenVaultService.LoadAccountsAsync();

            // No active account = user previously chose offline = respect that, skip login
            if (store?.ActiveAccountId == null) return true;

            var id = store.ActiveAccountId.Value;
            ILauncherAccount? account = null;

            if (store.ActiveAccountType == AccountType.Microsoft)
                account = store.MicrosoftAccounts.Find(a => a.Id == id);
            else if (store.ActiveAccountType == AccountType.ElyBy)
                account = store.ElyAccounts.Find(a => a.Id == id);

            // Account record missing (store inconsistency) = don't block the user
            if (account == null) return true;

            // Token still valid → MainWindow directly
            return account.TokenExpiry > DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[App] ShouldSkipLoginAsync failed: {ex}");
            return true; // On any error, don't block the user
        }
    }
}
