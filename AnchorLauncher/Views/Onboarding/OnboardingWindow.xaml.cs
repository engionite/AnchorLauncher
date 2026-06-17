using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using AnchorLauncher.Models;
using AnchorLauncher.ViewModels;

namespace AnchorLauncher.Views.Onboarding;

public partial class OnboardingWindow : Window
{
    private readonly OnboardingViewModel _vm = new();

    public OnboardingWindow()
    {
        InitializeComponent();
        DataContext        = _vm;
        _vm.EditionSelected += OnEditionSelected;
        Loaded             += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Fade the window in from 0 → 1
        var fade = new DoubleAnimation(0.0, 1.0, new Duration(TimeSpan.FromMilliseconds(300)))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        BeginAnimation(OpacityProperty, fade);
    }

    // ── Hover animations ─────────────────────────────────────────────────────

    private void JavaCard_MouseEnter(object sender, MouseEventArgs e)
        => SetHoverState(javaActive: true);

    private void BedrockCard_MouseEnter(object sender, MouseEventArgs e)
        => SetHoverState(javaActive: false);

    private void AnyCard_MouseLeave(object sender, MouseEventArgs e)
    {
        // Only reset if the mouse genuinely left both cards
        var pos = e.GetPosition(this);
        if (pos.X < 0 || pos.X > ActualWidth || pos.Y < 0 || pos.Y > ActualHeight)
            SetHoverState(javaActive: null);
        else
            SetHoverState(javaActive: null);
    }

    private void SetHoverState(bool? javaActive)
    {
        const double ScaleHover  = 1.04;
        const double ScaleNormal = 1.00;
        const double DimOpacity  = 0.40;
        const double FullOpacity = 1.00;
        const int    Ms          = 200;

        switch (javaActive)
        {
            case true:
                AnimateScale(JavaCardScale,   ScaleHover,  Ms);
                AnimateScale(BedrockCardScale, ScaleNormal, Ms);
                AnimateOpacity(JavaCard,    FullOpacity, Ms);
                AnimateOpacity(BedrockCard, DimOpacity,  Ms);
                break;
            case false:
                AnimateScale(BedrockCardScale, ScaleHover,  Ms);
                AnimateScale(JavaCardScale,    ScaleNormal, Ms);
                AnimateOpacity(BedrockCard, FullOpacity, Ms);
                AnimateOpacity(JavaCard,    DimOpacity,  Ms);
                break;
            default:
                AnimateScale(JavaCardScale,    ScaleNormal, Ms);
                AnimateScale(BedrockCardScale, ScaleNormal, Ms);
                AnimateOpacity(JavaCard,    FullOpacity, Ms);
                AnimateOpacity(BedrockCard, FullOpacity, Ms);
                break;
        }
    }

    private static void AnimateScale(System.Windows.Media.ScaleTransform target, double to, int ms)
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var dur  = new Duration(TimeSpan.FromMilliseconds(ms));
        target.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty,
            new DoubleAnimation(to, dur) { EasingFunction = ease });
        target.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty,
            new DoubleAnimation(to, dur) { EasingFunction = ease });
    }

    private static void AnimateOpacity(UIElement target, double to, int ms)
    {
        target.BeginAnimation(OpacityProperty,
            new DoubleAnimation(to, new Duration(TimeSpan.FromMilliseconds(ms)))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });
    }

    // ── Click / selection handlers ────────────────────────────────────────────

    private void JavaCard_Click(object sender, RoutedEventArgs e)
        => SelectEdition(LauncherMode.Java);

    private void BedrockCard_Click(object sender, RoutedEventArgs e)
        => SelectEdition(LauncherMode.Bedrock);

    // Also handle direct MouseLeftButtonUp on the card Border (non-Button path)
    private void JavaCard_Click(object sender, MouseButtonEventArgs e)
        => SelectEdition(LauncherMode.Java);

    private void BedrockCard_Click(object sender, MouseButtonEventArgs e)
        => SelectEdition(LauncherMode.Bedrock);

    // Code-behind only triggers the command (persistence lives in the ViewModel)
    private void SelectEdition(LauncherMode mode)
    {
        if (_vm.SelectEditionCommand.CanExecute(mode))
            _vm.SelectEditionCommand.Execute(mode);
    }

    // Navigation runs once the edition has been persisted by the ViewModel
    private void OnEditionSelected(object? sender, LauncherMode mode)
        => TransitionToLoginWindow();

    private void TransitionToLoginWindow()
    {
        var login = new Views.Auth.LoginWindow();
        Application.Current.MainWindow = login;
        login.Show();

        var fade = new DoubleAnimation(1.0, 0.0, new Duration(TimeSpan.FromMilliseconds(250)))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        fade.Completed += (_, _) => Close();
        BeginAnimation(OpacityProperty, fade);
    }
}
