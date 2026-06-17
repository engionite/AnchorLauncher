using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using AnchorLauncher.Models.Auth;
using AnchorLauncher.ViewModels;

namespace AnchorLauncher.Views.Auth;

public partial class AccountManagerOverlay : UserControl
{
    private bool _isOpen;
    private AccountManagerViewModel? _vm;

    public AccountManagerOverlay()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    // ── Set-Active green success flash (FIX 7) ───────────────────────────────
    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_vm != null) _vm.SetActiveSuccess -= OnSetActiveSuccess;
        _vm = DataContext as AccountManagerViewModel;
        if (_vm != null) _vm.SetActiveSuccess += OnSetActiveSuccess;
    }

    private void OnSetActiveSuccess(object? sender, ILauncherAccount account)
    {
        try
        {
            var container = AccountsList.ItemContainerGenerator.ContainerFromItem(account) as DependencyObject;
            if (container == null) return;

            var btn   = FindByTag(container, "setActiveBtn");
            var flash = FindByTag(container, "setActiveFlash");
            if (btn == null || flash == null) return;

            btn.Visibility   = Visibility.Collapsed;
            flash.Visibility = Visibility.Visible;

            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                flash.Visibility = Visibility.Collapsed;
                btn.Visibility   = Visibility.Visible;
            };
            timer.Start();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AccountOverlay] SetActive flash failed: {ex}");
        }
    }

    private static FrameworkElement? FindByTag(DependencyObject root, string tag)
    {
        if (root is FrameworkElement fe && fe.Tag is string s && s == tag) return fe;
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var found = FindByTag(VisualTreeHelper.GetChild(root, i), tag);
            if (found != null) return found;
        }
        return null;
    }

    public void Toggle()
    {
        _isOpen = !_isOpen;
        AnimateSlide(_isOpen ? 0 : 340);
    }

    public void Open()
    {
        if (_isOpen) return;
        _isOpen = true;
        AnimateSlide(0);
    }

    public void Close()
    {
        if (!_isOpen) return;
        _isOpen = false;
        AnimateSlide(340);
    }

    private void AnimateSlide(double targetX)
    {
        var anim = new DoubleAnimation(targetX, new Duration(TimeSpan.FromMilliseconds(280)))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        SlideTransform.BeginAnimation(TranslateTransform.XProperty, anim);
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

    // Ely.by now uses direct credential auth — open the small dialog, then refresh the list.
    private async void BtnAddEly_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not AccountManagerViewModel vm) return;
        try
        {
            var dlg = new ElyLoginDialog { Owner = Window.GetWindow(this) };
            if (dlg.ShowDialog() == true && dlg.ResultAccount != null)
                await vm.ReloadAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AccountOverlay] BtnAddEly_Click failed: {ex}");
        }
    }
}

