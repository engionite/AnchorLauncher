using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using AnchorLauncher.Models.Auth;
using AnchorLauncher.ViewModels;

namespace AnchorLauncher.Views.Auth;

public partial class LoginWindow : Window
{
    private readonly LoginViewModel _vm = new();

    public LoginWindow()
    {
        InitializeComponent();
        DataContext        = _vm;
        _vm.AuthCompleted += OnAuthCompleted;
        Loaded            += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var fade = new DoubleAnimation(0.0, 1.0, new Duration(TimeSpan.FromMilliseconds(300)))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        BeginAnimation(OpacityProperty, fade);
    }

    private void OnAuthCompleted(object? sender, ILauncherAccount? account)
    {
        Dispatcher.Invoke(() =>
        {
            var main = new MainWindow();
            Application.Current.MainWindow = main;
            main.Show();

            var fade = new DoubleAnimation(1.0, 0.0, new Duration(TimeSpan.FromMilliseconds(250)))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            fade.Completed += (_, _) => Close();
            BeginAnimation(OpacityProperty, fade);
        });
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
        => _vm.SkipCommand.Execute(null);

    // Password lives in a PasswordBox (not bindable for security) — hand it to the VM here.
    private async void ElySignIn_Click(object sender, RoutedEventArgs e)
        => await _vm.SignInElyAsync(ElyPasswordBox.Password);

    private async void ElyPassword_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            await _vm.SignInElyAsync(ElyPasswordBox.Password);
    }
}
