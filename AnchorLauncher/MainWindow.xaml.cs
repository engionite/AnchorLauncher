using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using AnchorLauncher.ViewModels;

namespace AnchorLauncher;

public partial class MainWindow : Window
{
    public AccountManagerViewModel AccountVM { get; } = new AccountManagerViewModel();
    public ShellViewModel          ShellVM   { get; } = new ShellViewModel();

    private System.Windows.Forms.NotifyIcon? _trayIcon;
    private bool _minimizeToTray;
    private bool _reallyClosing;

    // ── Windows 11 rounded-corner enforcement (DWM) ──────────────────────────
    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWCP_ROUND = 2;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            int preference = DWMWCP_ROUND;
            DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref preference, sizeof(int));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MainWindow] DWM round-corners failed: {ex.Message}");
        }
    }

    public MainWindow()
    {
        InitializeComponent();
        Loaded       += OnLoaded;
        StateChanged += OnStateChanged;
        Closing      += OnClosing;

        // Mode persisted by the VM → re-route the content frame and restyle the toggle
        ShellVM.ModeChanged += (_, _) =>
        {
            UpdateModeToggleVisual();
            NavigateTo(new Views.Instances.InstancesPage());
            SetNavActive(NavInstances);
        };
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Wire the account overlay to the shared ViewModel
        AccountOverlay.DataContext = AccountVM;

        // Sync account chip text on load and on ActiveAccount changes
        UpdateAccountChip();
        AccountVM.PropertyChanged += (_, args) =>
        {
            switch (args.PropertyName)
            {
                case nameof(AccountManagerViewModel.ActiveAccount):
                    UpdateAccountChip();
                    break;
                case nameof(AccountManagerViewModel.HasExpiredActiveAccount):
                    WarningBanner.Visibility = AccountVM.HasExpiredActiveAccount
                        ? Visibility.Visible
                        : Visibility.Collapsed;
                    break;
            }
        };

        UpdateModeToggleVisual();
        NavigateTo(new Views.Home.HomePage());
        SetNavActive(NavHome);

        _ = ApplyWindowSettingsAsync();
    }

    // ── Public navigation entry points (used by HomePage hooks) ──────────────
    public void ShowInstancesPage()
    {
        NavigateTo(new Views.Instances.InstancesPage());
        SetNavActive(NavInstances);
    }

    public void OpenAccountOverlay() => AccountOverlay.Open();

    /// <summary>Applies UI scale + minimize-to-tray from persisted settings.</summary>
    private async System.Threading.Tasks.Task ApplyWindowSettingsAsync()
    {
        try
        {
            var s = await Services.Storage.LauncherStorageService.LoadGlobalSettingsAsync();
            ApplyUiScale(s.UiScalePercent);
            SetMinimizeToTray(s.MinimizeToTray);

            if (s.CheckUpdatesOnStartup)
                _ = CheckForUpdatesOnStartupAsync();
        }
        catch (System.Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MainWindow] ApplyWindowSettings failed: {ex}");
        }
    }

    private string? _lastPromptedUpdate;
    private bool _updateDialogOpen;
    private System.Windows.Threading.DispatcherTimer? _updateTimer;

    /// <summary>Checks for updates at startup, then keeps polling every 2 minutes so an update
    /// prompt appears live while the launcher is open — no restart needed.</summary>
    private async System.Threading.Tasks.Task CheckForUpdatesOnStartupAsync()
    {
        await CheckAndPromptUpdateAsync();

        _updateTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = System.TimeSpan.FromMinutes(2)
        };
        _updateTimer.Tick += async (_, _) => await CheckAndPromptUpdateAsync();
        _updateTimer.Start();
    }

    /// <summary>Polls the feed; shows the update dialog once per newer version. Silent when offline,
    /// already up to date, already prompted for this version, or a dialog is already showing.</summary>
    private async System.Threading.Tasks.Task CheckAndPromptUpdateAsync()
    {
        try
        {
            if (_updateDialogOpen) return;
            var result = await new Services.Platform.UpdateCheckService().CheckAsync();
            if (!result.UpdateAvailable || result.Latest == _lastPromptedUpdate) return;
            _lastPromptedUpdate = result.Latest;

            await Dispatcher.InvokeAsync(() =>
            {
                if (_updateDialogOpen) return;
                _updateDialogOpen = true;
                try
                {
                    var dlg = new Views.UpdateDialog(
                        result.Latest,
                        Services.Platform.UpdateCheckService.CurrentVersion,
                        result.Notes,
                        result.Url) { Owner = this };
                    dlg.ShowDialog();
                }
                finally { _updateDialogOpen = false; }
            });
        }
        catch (System.Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MainWindow] update check failed: {ex.Message}");
        }
    }

    /// <summary>Live UI scale (90/100/110/125) via a layout transform on the window content.</summary>
    public void ApplyUiScale(int percent)
    {
        var scale = System.Math.Clamp(percent, 75, 150) / 100.0;
        if (Content is FrameworkElement root)
            root.LayoutTransform = System.Math.Abs(scale - 1.0) > 0.001
                ? new System.Windows.Media.ScaleTransform(scale, scale)
                : System.Windows.Media.Transform.Identity;
    }

    /// <summary>Live minimize-to-tray toggle; ensures the tray icon exists when enabled.</summary>
    public void SetMinimizeToTray(bool enabled)
    {
        _minimizeToTray = enabled;
        if (enabled) EnsureTrayIcon();
    }

    private void EnsureTrayIcon()
    {
        if (_trayIcon != null) return;
        try
        {
            _trayIcon = new System.Windows.Forms.NotifyIcon
            {
                Text    = "Anchor Launcher",
                Visible = true
            };
            try
            {
                var exe = System.Environment.ProcessPath;
                if (exe != null) _trayIcon.Icon = System.Drawing.Icon.ExtractAssociatedIcon(exe);
            }
            catch { /* fall back to default icon */ }

            _trayIcon.DoubleClick += (_, _) => RestoreFromTray();

            var menu = new System.Windows.Forms.ContextMenuStrip();
            menu.Items.Add("Open Anchor Launcher", null, (_, _) => RestoreFromTray());
            menu.Items.Add("Exit", null, (_, _) => { _reallyClosing = true; Close(); });
            _trayIcon.ContextMenuStrip = menu;
        }
        catch (System.Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MainWindow] tray icon failed: {ex}");
        }
    }

    private void RestoreFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        // Minimize-to-tray: the X hides to tray instead of exiting (tray → Exit really closes)
        if (_minimizeToTray && !_reallyClosing)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        _trayIcon?.Dispose();
        _trayIcon = null;
    }

    /// <summary>Navigates to the marketplace with a preset content-type filter (e.g. Shaders).</summary>
    public void NavigateToMarketplace(Models.Marketplace.ProjectType type)
    {
        MarketplaceViewModel.PendingPresetType = type;
        NavigateTo(new Views.Instances.DownloadsPage());
        SetNavActive(NavDownloads);
    }

    private void UpdateModeToggleVisual()
    {
        var accent  = FindResource("AccentBrush") as System.Windows.Media.Brush;
        var muted   = FindResource("TextSecondaryBrush") as System.Windows.Media.Brush;
        var white   = System.Windows.Media.Brushes.White;
        var clear   = System.Windows.Media.Brushes.Transparent;
        var isJava  = ShellVM.CurrentMode == Models.LauncherMode.Java;

        BtnModeJava.Background    = isJava ? accent : clear;
        BtnModeJava.Foreground    = isJava ? white : muted;
        BtnModeBedrock.Background = isJava ? clear : accent;
        BtnModeBedrock.Foreground = isJava ? muted : white;
    }

    private void BtnModeJava_Click(object sender, RoutedEventArgs e) =>
        ShellVM.SwitchModeCommand.Execute(Models.LauncherMode.Java);

    private void BtnModeBedrock_Click(object sender, RoutedEventArgs e) =>
        ShellVM.SwitchModeCommand.Execute(Models.LauncherMode.Bedrock);

    private void UpdateAccountChip()
    {
        BtnAccountText.Text = AccountVM.ActiveAccount?.Username ?? "Sign In";
    }

    private void NavigateTo(System.Windows.Controls.Page page)
    {
        // Page transition: fade in (0→1) + slide up (Y 10→0) over 200ms, CubicEase EaseOut
        page.Opacity = 0;
        var slide = new System.Windows.Media.TranslateTransform(0, 10);
        page.RenderTransform = slide;
        page.Loaded += (_, _) =>
        {
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            var dur  = Services.Platform.AnimationHelper.Get(200);   // honors the Animation-Speed setting
            page.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, dur) { EasingFunction = ease });
            slide.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty,
                new DoubleAnimation(12, 0, dur) { EasingFunction = ease });
        };
        ContentFrame.Navigate(page);
    }

    private void SetNavActive(System.Windows.Controls.Button active)
    {
        var muted  = FindResource("TextSecondaryBrush") as System.Windows.Media.SolidColorBrush;
        var accent = FindResource("AccentBrush") as System.Windows.Media.SolidColorBrush;

        var map = new (System.Windows.Controls.Button Btn, System.Windows.Controls.Border Bar)[]
        {
            (NavHome,      NavHomeBar),
            (NavInstances, NavInstancesBar),
            (NavDownloads, NavDownloadsBar),
            (NavSkins,     NavSkinsBar),
            (NavSettings,  NavSettingsBar),
        };

        foreach (var (btn, bar) in map)
        {
            var isActive = ReferenceEquals(btn, active);

            // Icon recolor — stroke-based icons (Home, Downloads) recolor their Stroke,
            // fill-based icons (Instances, Skins, Settings) recolor their Fill.
            foreach (var path in FindPaths(btn))
            {
                if (path.Stroke != null)
                    path.Stroke = isActive ? accent : muted;
                else
                    path.Fill = isActive ? accent : muted;
            }

            // Animated active indicator bar (grow in)
            if (isActive)
            {
                bar.Visibility = System.Windows.Visibility.Visible;
                var grow = new DoubleAnimation(0.2, 1.0, new Duration(TimeSpan.FromMilliseconds(180)))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                bar.RenderTransformOrigin = new Point(0.5, 0.5);
                bar.RenderTransform = new System.Windows.Media.ScaleTransform(1, 1);
                ((System.Windows.Media.ScaleTransform)bar.RenderTransform)
                    .BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, grow);
            }
            else
            {
                bar.Visibility = System.Windows.Visibility.Collapsed;
            }
        }
    }

    private static IEnumerable<System.Windows.Shapes.Path> FindPaths(DependencyObject root)
    {
        if (root is System.Windows.Shapes.Path p) { yield return p; yield break; }
        int count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
            foreach (var found in FindPaths(System.Windows.Media.VisualTreeHelper.GetChild(root, i)))
                yield return found;
    }

    // ── Chrome buttons ────────────────────────────────────────────────────
    private void BtnMinimize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState.Minimized;

    private void BtnMaximize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;

    private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

    private void OnStateChanged(object? sender, EventArgs e)
    {
        if (MaximizePath == null) return;
        // Small 10×10 icon space: single square (maximize) vs. overlapping squares (restore)
        MaximizePath.Data = System.Windows.Media.Geometry.Parse(
            WindowState == WindowState.Maximized
                ? "M 0,3 H 7 V 10 H 0 Z M 3,3 V 0 H 10 V 7 H 7"
                : "M 0,0 H 10 V 10 H 0 Z");
    }

    // ── Account overlay ───────────────────────────────────────────────────
    private void BtnAccount_Click(object sender, RoutedEventArgs e) =>
        AccountOverlay.Toggle();

    private void BtnSignInAgain_Click(object sender, RoutedEventArgs e) =>
        AccountOverlay.Open();

    // ── Nav handlers ──────────────────────────────────────────────────────
    private void NavHome_Click(object sender, RoutedEventArgs e)
    {
        NavigateTo(new Views.Home.HomePage());
        SetNavActive(NavHome);
    }

    private void NavInstances_Click(object sender, RoutedEventArgs e)
    {
        NavigateTo(new Views.Instances.InstancesPage());
        SetNavActive(NavInstances);
    }

    private void NavDownloads_Click(object sender, RoutedEventArgs e)
    {
        NavigateTo(new Views.Instances.DownloadsPage());
        SetNavActive(NavDownloads);
    }

    private void NavSkins_Click(object sender, RoutedEventArgs e)
    {
        NavigateTo(new Views.Instances.SkinsPage());
        SetNavActive(NavSkins);
    }

    private void NavSettings_Click(object sender, RoutedEventArgs e)
    {
        NavigateTo(new Views.Settings.SettingsPage());
    }
}
