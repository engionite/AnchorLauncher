using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using AnchorLauncher.ViewModels;

namespace AnchorLauncher.Views.Home;

public partial class HomePage : Page
{
    private readonly HomeViewModel _vm = new();
    private readonly DispatcherTimer _heroTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private bool _heroShowingB;

    public HomePage()
    {
        InitializeComponent();
        DataContext = _vm;
        Loaded   += OnLoaded;
        Unloaded += (_, _) => _heroTimer.Stop();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Hero wallpaper crossfade
        _heroTimer.Tick -= HeroTimer_Tick;
        _heroTimer.Tick += HeroTimer_Tick;
        _heroTimer.Start();

        var shell = Window.GetWindow(this) as MainWindow;

        _vm.NavigateToInstances = () => shell?.ShowInstancesPage();

        _vm.OpenMarketplaceForMod = modName =>
        {
            MarketplaceViewModel.PendingSearchQuery = modName;
            shell?.NavigateToMarketplace(Models.Marketplace.ProjectType.Mod);
        };

        _vm.RequestSignIn = () => shell?.OpenAccountOverlay();

        _vm.CreateInstance = () =>
        {
            try
            {
                var dlg = new Instances.CreateInstanceDialog { Owner = Window.GetWindow(this) };
                if (dlg.ShowDialog() == true && dlg.ResultInstance != null)
                {
                    InstancesViewModel.Shared.AddInstance(dlg.ResultInstance);
                    shell?.ShowInstancesPage();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[HomePage] CreateInstance failed: {ex}");
            }
        };
    }

    private void HeroTimer_Tick(object? sender, EventArgs e)
    {
        // Crossfade between the two hero wallpapers
        var dur = Services.Platform.AnimationHelper.Get(1200);
        var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };
        _heroShowingB = !_heroShowingB;
        HeroWallA.BeginAnimation(OpacityProperty, new DoubleAnimation(_heroShowingB ? 0 : 0.5, dur) { EasingFunction = ease });
        HeroWallB.BeginAnimation(OpacityProperty, new DoubleAnimation(_heroShowingB ? 0.5 : 0, dur) { EasingFunction = ease });
    }

    private void NewsCard_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not Models.Home.NewsItem item) return;
        try { new NewsDetailDialog(item) { Owner = Window.GetWindow(this) }.ShowDialog(); }
        catch (Exception ex) { Debug.WriteLine($"[HomePage] news detail failed: {ex}"); }
    }
}
