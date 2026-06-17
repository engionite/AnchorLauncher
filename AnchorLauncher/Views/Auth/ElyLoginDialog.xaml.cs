using System.Windows;
using System.Windows.Input;
using AnchorLauncher.Models.Auth;
using AnchorLauncher.ViewModels;

namespace AnchorLauncher.Views.Auth;

/// <summary>
/// Small credential dialog for adding an Ely.by account from the account overlay.
/// Reuses <see cref="LoginViewModel"/> so the direct (Yggdrasil) auth logic stays in the VM.
/// </summary>
public partial class ElyLoginDialog : Window
{
    private readonly LoginViewModel _vm = new();

    public ILauncherAccount? ResultAccount { get; private set; }

    public ElyLoginDialog()
    {
        InitializeComponent();
        DataContext        = _vm;
        _vm.AuthCompleted += OnAuthCompleted;
    }

    private void OnAuthCompleted(object? sender, ILauncherAccount? account)
    {
        if (account == null) return;          // skip never happens here (no skip button)
        ResultAccount = account;
        DialogResult  = true;
        Close();
    }

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            DragMove();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private async void ElySignIn_Click(object sender, RoutedEventArgs e)
        => await _vm.SignInElyAsync(ElyPasswordBox.Password);

    private async void ElyPassword_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            await _vm.SignInElyAsync(ElyPasswordBox.Password);
    }
}
