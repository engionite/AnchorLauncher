using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using AnchorSetup.ViewModels;

namespace AnchorSetup;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new InstallerViewModel();
        StartLogoPulse();
    }

    // Gentle breathing animation on the install-screen logo (only visible during install).
    private void StartLogoPulse()
    {
        var anim = new DoubleAnimation(0.94, 1.06, TimeSpan.FromSeconds(1.15))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        };
        LogoScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, anim);
        LogoScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, anim);
    }

    private void Chrome_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            try { DragMove(); } catch { /* ignore double-click race */ }
    }

    private void Min_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
}
