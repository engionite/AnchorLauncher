using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AnchorLauncher.Models.Auth;
using AnchorLauncher.Services.Auth;
using AnchorLauncher.Services.Skins;

namespace AnchorLauncher.ViewModels;

public partial class AccountManagerViewModel : ObservableObject
{
    private readonly MicrosoftAuthService _msAuth = new();
    private readonly ElyAuthService       _elyAuth = new();
    private CancellationTokenSource?      _authCts;

    [ObservableProperty] private ObservableCollection<ILauncherAccount> _accounts = new();
    [ObservableProperty] private ILauncherAccount? _activeAccount;
    [ObservableProperty] private bool   _isBusy;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private bool   _isDeviceCodeVisible;
    [ObservableProperty] private string _deviceCode    = string.Empty;
    [ObservableProperty] private string _deviceCodeUrl = string.Empty;
    [ObservableProperty] private bool   _hasExpiredActiveAccount;

    /// <summary>Raised after an account is made active so the overlay can flash its row green.</summary>
    public event EventHandler<ILauncherAccount>? SetActiveSuccess;

    public AccountManagerViewModel()
    {
        // When a skin head cache is invalidated (e.g. Refresh on the Skins page), re-render avatars
        SkinHeadService.CacheInvalidated += OnSkinCacheInvalidated;
        _ = LoadAndValidateSessionAsync();
    }

    private void OnSkinCacheInvalidated()
    {
        try
        {
            Application.Current?.Dispatcher.Invoke(RefreshAvatars);
        }
        catch (Exception ex) { Debug.WriteLine($"[AccountVM] avatar refresh failed: {ex}"); }
    }

    /// <summary>
    /// Forces the skin-head bindings (sidebar + overlay list) to re-evaluate against the now-cleared
    /// head cache, so changed skins show without a restart. The converter re-runs and re-downloads.
    /// </summary>
    public void RefreshAvatars()
    {
        // Rebuild the list so each row's head converter re-runs (same account objects).
        var items = Accounts.ToList();
        Accounts = new ObservableCollection<ILauncherAccount>(items);

        // Re-trigger the active-account binding (sidebar head).
        var active = ActiveAccount;
        ActiveAccount = null;
        ActiveAccount = active;
    }

    // ── Session load + 3F silent refresh ──────────────────────────────────

    private async Task LoadAndValidateSessionAsync()
    {
        try
        {
            var store = await TokenVaultService.LoadAccountsAsync();
            Accounts.Clear();

            foreach (var acc in store.MicrosoftAccounts) Accounts.Add(acc);
            foreach (var acc in store.ElyAccounts)       Accounts.Add(acc);

            if (store.ActiveAccountId.HasValue)
                ActiveAccount = Accounts.FirstOrDefault(a => a.Id == store.ActiveAccountId.Value);

            Debug.WriteLine($"[AccountVM] Loaded {Accounts.Count} accounts.");

            // 3F: silent refresh for accounts near expiry (< 15 min)
            await RefreshNearExpiryAccountsAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AccountVM] LoadAndValidateSessionAsync failed: {ex}");
        }
    }

    private async Task RefreshNearExpiryAccountsAsync()
    {
        var threshold = DateTime.UtcNow.AddMinutes(15);
        bool anyChanged = false;

        foreach (var account in Accounts.ToList())
        {
            if (account.TokenExpiry > threshold) continue;

            if (account is MicrosoftAccount msAcc)
            {
                var refreshed = await _msAuth.TrySilentRefreshAsync(msAcc);
                if (refreshed != null)
                {
                    var idx = Accounts.IndexOf(account);
                    if (idx >= 0) Accounts[idx] = refreshed;
                    if (ActiveAccount?.Id == account.Id) ActiveAccount = refreshed;
                    anyChanged = true;
                    Debug.WriteLine($"[AccountVM] Silently refreshed: {refreshed.Username}");
                }
                else
                {
                    CheckExpiredActive(account);
                }
            }
            else if (account is ElyAccount elyAcc)
            {
                var refreshed = await _elyAuth.TryRefreshAsync(elyAcc);
                if (refreshed != null)
                {
                    anyChanged = true;
                    Debug.WriteLine($"[AccountVM] Ely token refreshed: {refreshed.Username}");
                }
                else
                {
                    CheckExpiredActive(account);
                }
            }
        }

        if (anyChanged) await PersistAccountsAsync();
    }

    private void CheckExpiredActive(ILauncherAccount account)
    {
        if (ActiveAccount?.Id == account.Id && account.TokenExpiry < DateTime.UtcNow)
        {
            HasExpiredActiveAccount = true;
            StatusMessage = "Session expired. Please sign in again.";
        }
    }

    // ── Add Microsoft account ──────────────────────────────────────────────

    [RelayCommand]
    private async Task AddMicrosoftAccountAsync()
    {
        try
        {
            IsBusy = true;
            StatusMessage = "Waiting for Microsoft sign-in…";

            _authCts = new CancellationTokenSource();

            var account = await _msAuth.AuthenticateDeviceCodeAsync(
                (code, url) =>
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        DeviceCode        = code;
                        DeviceCodeUrl     = url;
                        IsDeviceCodeVisible = true;
                    });
                },
                _authCts.Token);

            Accounts.Add(account);
            await SaveAndSetActiveAsync(account);
            HasExpiredActiveAccount = false;
            StatusMessage = account.OwnsMinecraft
                ? $"Signed in as {account.Username}"
                : $"Account added ({account.Username}). Note: no Minecraft license detected — you can still launch via offline mode.";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Sign-in cancelled.";
            Debug.WriteLine("[AccountVM] Microsoft sign-in cancelled.");
        }
        catch (MicrosoftAuthException ex)
        {
            StatusMessage = ex.Message;   // actionable guidance
            Debug.WriteLine($"[AccountVM] Microsoft auth: {ex.Message}");
        }
        catch (Exception ex)
        {
            StatusMessage = "Sign-in failed — see debug log.";
            Debug.WriteLine($"[AccountVM] AddMicrosoftAccountAsync failed: {ex}");
        }
        finally
        {
            IsBusy              = false;
            IsDeviceCodeVisible = false;
            _authCts?.Dispose();
            _authCts = null;
        }
    }

    // ── Add Ely.by account ─────────────────────────────────────────────────
    // Ely.by now uses direct (Yggdrasil) credential auth, shown in a small dialog the
    // overlay opens. After the dialog persists + activates the account, the overlay calls
    // ReloadAsync() to refresh this list.

    public async Task ReloadAsync()
    {
        try
        {
            var store = await TokenVaultService.LoadAccountsAsync();
            Accounts.Clear();
            foreach (var acc in store.MicrosoftAccounts) Accounts.Add(acc);
            foreach (var acc in store.ElyAccounts)       Accounts.Add(acc);

            ActiveAccount = store.ActiveAccountId.HasValue
                ? Accounts.FirstOrDefault(a => a.Id == store.ActiveAccountId.Value)
                : ActiveAccount;

            HasExpiredActiveAccount = false;
            StatusMessage = $"Signed in as {ActiveAccount?.Username}".TrimEnd();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AccountVM] ReloadAsync failed: {ex}");
        }
    }

    // ── Remove account ─────────────────────────────────────────────────────

    [RelayCommand]
    private async Task RemoveAccountAsync(ILauncherAccount account)
    {
        try
        {
            if (account is MicrosoftAccount msAcc)
                await _msAuth.RemoveFromCacheAsync(msAcc);

            Accounts.Remove(account);

            if (ActiveAccount?.Id == account.Id)
            {
                ActiveAccount = Accounts.FirstOrDefault();
                HasExpiredActiveAccount = false;
            }

            await PersistAccountsAsync();
            StatusMessage = $"Removed {account.Username}.";
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AccountVM] RemoveAccountAsync failed: {ex}");
        }
    }

    // ── Set active account ─────────────────────────────────────────────────

    [RelayCommand]
    private async Task SetActiveAccountAsync(ILauncherAccount account)
    {
        try
        {
            ActiveAccount           = account;
            HasExpiredActiveAccount = account.TokenExpiry < DateTime.UtcNow;
            StatusMessage           = $"{Services.Platform.Loc.I["acc_active"]} {account.Username}";
            await PersistAccountsAsync();
            SetActiveSuccess?.Invoke(this, account);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AccountVM] SetActiveAccountAsync failed: {ex}");
        }
    }

    // ── Device code helpers ────────────────────────────────────────────────

    [RelayCommand]
    private void CancelAuth()
    {
        try
        {
            _authCts?.Cancel();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AccountVM] CancelAuth failed: {ex}");
        }
    }

    [RelayCommand]
    private void CopyDeviceCode()
    {
        try
        {
            if (!string.IsNullOrEmpty(DeviceCode))
                Clipboard.SetText(DeviceCode);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AccountVM] CopyDeviceCode failed: {ex}");
        }
    }

    [RelayCommand]
    private void OpenDeviceCodeUrl()
    {
        try
        {
            if (!string.IsNullOrEmpty(DeviceCodeUrl))
                System.Diagnostics.Process.Start(
                    new System.Diagnostics.ProcessStartInfo(DeviceCodeUrl)
                    { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AccountVM] OpenDeviceCodeUrl failed: {ex}");
        }
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private async Task SaveAndSetActiveAsync(ILauncherAccount account)
    {
        ActiveAccount ??= account;
        await PersistAccountsAsync();
    }

    private async Task PersistAccountsAsync()
    {
        var store = new AccountsStore
        {
            MicrosoftAccounts = Accounts.OfType<MicrosoftAccount>().ToList(),
            ElyAccounts       = Accounts.OfType<ElyAccount>().ToList(),
            ActiveAccountId   = ActiveAccount?.Id,
            ActiveAccountType = ActiveAccount?.AccountType
        };
        await TokenVaultService.SaveAccountsAsync(store);
    }
}
