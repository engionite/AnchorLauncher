using System.Diagnostics;
using System.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AnchorLauncher.Models.Auth;
using AnchorLauncher.Services.Auth;

namespace AnchorLauncher.ViewModels;

public partial class LoginViewModel : ObservableObject
{
    [ObservableProperty] private bool   _isBusy;
    [ObservableProperty] private string _statusMessage      = string.Empty;
    [ObservableProperty] private bool   _isDeviceCodeVisible;
    [ObservableProperty] private string _deviceCode         = string.Empty;
    [ObservableProperty] private string _deviceCodeUrl      = string.Empty;

    // Ely.by direct (Yggdrasil) sign-in form
    [ObservableProperty] private bool   _isElyFormVisible;
    [ObservableProperty] private bool   _isTwoFactorRequired;
    [ObservableProperty] private string _elyUsername   = string.Empty;
    [ObservableProperty] private string _elyTwoFactor  = string.Empty;
    [ObservableProperty] private string _elyError      = string.Empty;

    private CancellationTokenSource? _authCts;

    public event EventHandler<ILauncherAccount?>? AuthCompleted;

    [RelayCommand]
    private async Task SignInMicrosoftAsync()
    {
        try
        {
            _authCts      = new CancellationTokenSource();
            IsBusy        = true;
            StatusMessage = "Connecting to Microsoft...";

            var svc     = new MicrosoftAuthService();
            var account = await svc.AuthenticateDeviceCodeAsync(
                (code, url) =>
                {
                    DeviceCode          = code;
                    DeviceCodeUrl       = url;
                    IsDeviceCodeVisible = true;
                    StatusMessage       = "Waiting for browser sign-in...";
                },
                _authCts.Token);

            IsDeviceCodeVisible = false;
            StatusMessage       = account.OwnsMinecraft
                ? $"Welcome, {account.Username}!"
                : $"Added {account.Username} — no Minecraft license detected; offline mode only.";

            await PersistAccountAsync(account);
            AuthCompleted?.Invoke(this, account);
        }
        catch (OperationCanceledException)
        {
            IsDeviceCodeVisible = false;
            StatusMessage       = string.Empty;
        }
        catch (MicrosoftAuthException ex)
        {
            Debug.WriteLine($"[LoginVM] Microsoft auth: {ex.Message}");
            IsDeviceCodeVisible = false;
            StatusMessage       = ex.Message;   // carries actionable guidance
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LoginVM] SignInMicrosoftAsync failed: {ex}");
            IsDeviceCodeVisible = false;
            StatusMessage       = "Sign-in failed. Please try again.";
        }
        finally
        {
            IsBusy = false;
            _authCts?.Dispose();
            _authCts = null;
        }
    }

    [RelayCommand]
    private void ShowElyForm()
    {
        IsElyFormVisible = true;
        ElyError         = string.Empty;
    }

    [RelayCommand]
    private void BackToProviders()
    {
        IsElyFormVisible    = false;
        IsTwoFactorRequired = false;
        ElyError            = string.Empty;
        IsBusy              = false;
    }

    /// <summary>
    /// Direct Ely.by (Yggdrasil) sign-in. Password is passed in from the PasswordBox; the
    /// username/2FA come from bound properties. Re-prompts for a 2FA code when required.
    /// </summary>
    public async Task SignInElyAsync(string password)
    {
        if (IsBusy) return;

        if (string.IsNullOrWhiteSpace(ElyUsername))
        {
            ElyError = "Enter your Ely.by email or username.";
            return;
        }

        try
        {
            IsBusy   = true;
            ElyError = string.Empty;

            var svc     = new ElyAuthService();
            var account = await svc.AuthenticateAsync(
                ElyUsername.Trim(), password,
                IsTwoFactorRequired ? ElyTwoFactor : null);

            await PersistAccountAsync(account);
            AuthCompleted?.Invoke(this, account);
        }
        catch (ElyTwoFactorRequiredException)
        {
            IsTwoFactorRequired = true;
            ElyError = "This account uses two-factor auth — enter your code to continue.";
        }
        catch (ElyAuthException ex)
        {
            Debug.WriteLine($"[LoginVM] Ely auth: {ex.Message}");
            ElyError = ex.Message;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LoginVM] SignInElyAsync failed: {ex}");
            ElyError = "Sign-in failed. Check your connection and try again.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void Skip()
    {
        try
        {
            _authCts?.Cancel();
            IsDeviceCodeVisible = false;
            AuthCompleted?.Invoke(this, null);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LoginVM] Skip failed: {ex}");
        }
    }

    [RelayCommand]
    private void CancelAuth()
    {
        try
        {
            _authCts?.Cancel();
            IsDeviceCodeVisible = false;
            IsBusy              = false;
            StatusMessage       = string.Empty;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LoginVM] CancelAuth failed: {ex}");
        }
    }

    private static async Task PersistAccountAsync(ILauncherAccount account)
    {
        var store = await TokenVaultService.LoadAccountsAsync() ?? new AccountsStore();

        if (account is MicrosoftAccount ms)
        {
            store.MicrosoftAccounts.RemoveAll(a => a.MsalAccountId == ms.MsalAccountId);
            store.MicrosoftAccounts.Add(ms);
            store.ActiveAccountId   = ms.Id;
            store.ActiveAccountType = AccountType.Microsoft;
        }
        else if (account is ElyAccount ely)
        {
            store.ElyAccounts.RemoveAll(a => a.Uuid == ely.Uuid);
            store.ElyAccounts.Add(ely);
            store.ActiveAccountId   = ely.Id;
            store.ActiveAccountType = AccountType.ElyBy;
        }

        await TokenVaultService.SaveAccountsAsync(store);
    }
}
