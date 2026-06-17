using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using AnchorLauncher.Services.Storage;

namespace AnchorLauncher.Views.Splash;

public partial class SplashWindow : Window
{
    private const int SplashDurationMs = 2800;
    private const int BreathTickMs     = 16;

    private readonly DispatcherTimer _breathTimer;
    private readonly System.Diagnostics.Stopwatch _elapsed = new();
    private bool _transitioning;
    private bool _textShown;

    /// <summary>
    /// Set by App.xaml.cs. Awaited before Close() so the next window
    /// is guaranteed to be visible before the splash disappears.
    /// </summary>
    public Func<Task>? SplashCompleted;

    public SplashWindow()
    {
        InitializeComponent();

        _breathTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(BreathTickMs)
        };
        _breathTimer.Tick += OnBreathTick;
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _elapsed.Start();
        _breathTimer.Start();
        _ = RunPreChecksAsync();
    }

    private void OnBreathTick(object? sender, EventArgs e)
    {
        double ms       = _elapsed.Elapsed.TotalMilliseconds;
        double progress = Math.Min(ms / SplashDurationMs, 1.0);

        // Scale up 0.85 → 1.0 (cubic ease-out)
        double scale = 0.85 + (0.15 * EaseCubicOut(progress));
        LogoScale.ScaleX = scale;
        LogoScale.ScaleY = scale;

        // Orange glow pulse: BlurRadius 0 → 20 → 0 over the splash, synced with the scale
        LogoGlow.BlurRadius = 20.0 * Math.Sin(progress * Math.PI);

        // Reveal the wordmark after 800ms with a fade + slide-up
        if (ms >= 800 && !_textShown)
        {
            _textShown = true;
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            var dur  = new Duration(TimeSpan.FromMilliseconds(450));
            TextPanel.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, dur) { EasingFunction = ease });
            TextSlide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(10, 0, dur) { EasingFunction = ease });
        }

        if (progress >= 1.0 && !_transitioning)
        {
            _transitioning = true;
            _breathTimer.Stop();
            BeginFadeOutTransition();
        }
    }

    private static double EaseCubicOut(double t) => 1.0 - Math.Pow(1.0 - t, 3.0);

    private async Task RunPreChecksAsync()
    {
        try
        {
            await LauncherStorageService.InitializeAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Splash] Pre-check error: {ex}");
        }
    }

    private void BeginFadeOutTransition()
    {
        var fade = new DoubleAnimation(1.0, 0.0, new Duration(TimeSpan.FromMilliseconds(300)))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        fade.Completed += OnFadeCompleted;
        RootGrid.BeginAnimation(OpacityProperty, fade);
    }

    private async void OnFadeCompleted(object? sender, EventArgs e)
    {
        try
        {
            // Await the routing callback — next window is shown BEFORE Close()
            if (SplashCompleted != null)
                await SplashCompleted();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Splash] Routing callback error: {ex}");
        }
        finally
        {
            Close();
        }
    }
}
