using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AnchorLauncher.Models.Auth;
using AnchorLauncher.Models.Skins;
using AnchorLauncher.Services.Auth;
using AnchorLauncher.Services.Skins;

namespace AnchorLauncher.ViewModels;

public partial class SkinsViewModel : ObservableObject
{
    private readonly SkinService _skins = new();
    private ILauncherAccount? _account;
    private byte[]? _currentSkinBytes;

    [ObservableProperty] private bool   _hasAccount;
    [ObservableProperty] private string _username      = string.Empty;
    [ObservableProperty] private string _accountLabel  = string.Empty;
    [ObservableProperty] private ImageSource? _frontView;
    [ObservableProperty] private ImageSource? _backView;
    /// <summary>The raw 64×64 skin the 3D viewer renders (also bindable).</summary>
    [ObservableProperty] private BitmapSource? _currentSkinBitmap;
    /// <summary>True when the account's skin uses the slim (Alex) 3-wide-arm model.</summary>
    [ObservableProperty] private bool _isSlim;
    [ObservableProperty] private ObservableCollection<string> _history = new();
    [ObservableProperty] private bool   _isBusy;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private bool   _canUpload;

    /// <summary>Curated free skins (textures resolve from mc-heads.net by player name).</summary>
    [ObservableProperty] private ObservableCollection<SkinMarketItem> _marketplaceSkins = new();

    /// <summary>Raised with the raw 64×64 skin bytes whenever the previewed skin changes,
    /// so the view can feed the live 3D viewer. Null bytes → show the placeholder model.</summary>
    public event EventHandler<byte[]?>? SkinChanged;

    public SkinsViewModel()
    {
        BuildMarketplace();
        _ = LoadAsync(forceFresh: true);   // always show the latest skin when the page opens
    }

    private void BuildMarketplace()
    {
        // (display name, Minecraft username for mc-heads.net)
        (string Display, string User)[] skins =
        {
            ("Notch", "Notch"), ("jeb_", "jeb_"), ("Dream", "Dream"), ("Technoblade", "Technoblade"),
            ("Grian", "Grian"), ("Dinnerbone", "Dinnerbone"), ("CaptainSparklez", "CaptainSparklez"),
            ("Tubbo", "Tubbo_"), ("Philza", "Ph1LzA"), ("Skeppy", "Skeppy"),
            ("BadBoyHalo", "BadBoyHalo"), ("Wilbur Soot", "WilburSoot"), ("TommyInnit", "Tommyinnit"),
            ("Ranboo", "Ranboo"), ("Quackity", "Quackity"), ("Karl Jacobs", "KarlJacobs"),
            ("Sapnap", "Sapnap"), ("GeorgeNotFound", "GeorgeNotFound"), ("Punz", "Punz"), ("Antfrost", "Antfrost")
        };
        MarketplaceSkins = new ObservableCollection<SkinMarketItem>(
            skins.Select(s => new SkinMarketItem
            {
                Name    = s.Display,
                SkinUrl = $"https://mc-heads.net/skin/{Uri.EscapeDataString(s.User)}",
                HeadUrl = $"https://mc-heads.net/head/{Uri.EscapeDataString(s.User)}/64"
            }));
    }

    private async Task LoadAsync(bool forceFresh = false)
    {
        try
        {
            IsBusy = true;

            var store = await TokenVaultService.LoadAccountsAsync();
            _account = store.ActiveAccountType switch
            {
                AccountType.Microsoft => store.MicrosoftAccounts.FirstOrDefault(a => a.Id == store.ActiveAccountId),
                AccountType.ElyBy     => store.ElyAccounts.FirstOrDefault(a => a.Id == store.ActiveAccountId),
                _                     => null
            };

            if (_account == null)
            {
                HasAccount    = false;
                StatusMessage = "Sign in to view and change your skin.";
                return;
            }

            HasAccount   = true;
            Username     = _account.Username;
            AccountLabel = _account.AccountType == AccountType.Microsoft ? "Microsoft Account" : "Ely.by Account";
            CanUpload    = _account.AccountType == AccountType.Microsoft;

            var url = _account.AccountType == AccountType.ElyBy
                ? $"https://skinsystem.ely.by/skins/{Uri.EscapeDataString(_account.Username)}.png"
                : _account.SkinUrl;

            if (string.IsNullOrEmpty(url))
            {
                StatusMessage = "No skin URL available for this account.";
                return;
            }

            // Slim (Alex) vs classic (Steve) — read from the account metadata where available
            IsSlim = _account.AccountType == AccountType.ElyBy
                ? await _skins.IsElySlimAsync(_account.Username)
                : false;   // the viewer also auto-detects from the texture for other sources

            // Refresh busts any CDN cache so a skin just changed on ely.by is picked up
            if (forceFresh)
                url += (url.Contains('?') ? "&" : "?") + "t=" + DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            _currentSkinBytes = await _skins.DownloadSkinAsync(url);
            if (_currentSkinBytes == null)
            {
                StatusMessage = "Could not load the current skin.";
                return;
            }

            PushPreview(_currentSkinBytes);
            _skins.RecordHistory(_currentSkinBytes);
            RefreshHistory();
            StatusMessage = string.Empty;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SkinsVM] LoadAsync failed: {ex}");
            StatusMessage = "Failed to load skin.";
        }
        finally { IsBusy = false; }
    }

    private void RefreshHistory()
        => History = new ObservableCollection<string>(_skins.ListHistory());

    /// <summary>Re-raises SkinChanged with the loaded skin so a late-subscribing view can sync.</summary>
    public void PushCurrentSkin() => SkinChanged?.Invoke(this, _currentSkinBytes);

    /// <summary>Decodes the bytes, updates the 2D + 3D previews, and notifies the viewer.</summary>
    private void PushPreview(byte[] bytes)
    {
        FrontView = _skins.ComposeView(bytes, front: true);
        BackView  = _skins.ComposeView(bytes, front: false);
        CurrentSkinBitmap = DecodeSkin(bytes);
        SkinChanged?.Invoke(this, bytes);
    }

    private static BitmapSource? DecodeSkin(byte[] bytes)
    {
        try
        {
            var bmp = new BitmapImage();
            using var ms = new MemoryStream(bytes);
            bmp.BeginInit();
            bmp.CacheOption  = BitmapCacheOption.OnLoad;
            bmp.StreamSource = ms;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch (Exception ex) { Debug.WriteLine($"[SkinsVM] decode failed: {ex.Message}"); return null; }
    }

    /// <summary>Refresh button: cache-bust the skin + force the account head to re-download.</summary>
    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (_account != null) SkinHeadService.InvalidateCache(_account.Username);
        await LoadAsync(forceFresh: true);
    }

    /// <summary>Applies a picked .png file (local preview + upload where supported).</summary>
    public async Task UploadSkinAsync(string pngPath)
    {
        try
        {
            var bytes = await File.ReadAllBytesAsync(pngPath);
            await ApplySkinBytesAsync(bytes);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SkinsVM] UploadSkinAsync failed: {ex}");
            StatusMessage = $"Could not read that file: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ApplyMarketplaceSkinAsync(SkinMarketItem? item)
    {
        if (item == null) return;
        try
        {
            IsBusy = true;
            StatusMessage = $"Fetching {item.Name}'s skin…";
            var bytes = await _skins.DownloadSkinAsync(item.SkinUrl);
            if (bytes == null) { StatusMessage = $"Could not fetch {item.Name}'s skin."; return; }
            await ApplySkinBytesAsync(bytes);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SkinsVM] ApplyMarketplaceSkin failed: {ex}");
            StatusMessage = "Could not apply that skin.";
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task ApplyHistoryAsync(string? path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
        try
        {
            var bytes = await File.ReadAllBytesAsync(path);
            await ApplySkinBytesAsync(bytes);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SkinsVM] ApplyHistory failed: {ex}");
            StatusMessage = "Could not re-apply that skin.";
        }
    }

    /// <summary>
    /// Central apply path: updates the live preview (2D + 3D), records history, and — where the
    /// account supports it — uploads. Microsoft uploads via the services API; Ely.by opens its
    /// site (no public upload API); signed-out previews locally only.
    /// </summary>
    private async Task ApplySkinBytesAsync(byte[] bytes)
    {
        // Live preview first so the change is instant regardless of upload outcome.
        _currentSkinBytes = bytes;
        IsSlim = false;   // a different skin → let the viewer auto-detect its model from the texture
        PushPreview(bytes);
        _skins.RecordHistory(bytes);
        RefreshHistory();

        if (_account is MicrosoftAccount ms)
        {
            if (ms.TokenExpiry < DateTime.UtcNow)
            {
                StatusMessage = "Previewed. Sign in again to upload it to your account.";
                return;
            }
            try
            {
                IsBusy = true;
                StatusMessage = "Uploading skin…";
                var tmp = Path.Combine(Path.GetTempPath(), $"anchor-skin-{Guid.NewGuid():N}.png");
                await File.WriteAllBytesAsync(tmp, bytes);
                var token = TokenVaultService.Unprotect(ms.EncryptedMinecraftToken);
                await _skins.UploadMicrosoftSkinAsync(token, tmp);
                try { File.Delete(tmp); } catch { }
                // The account skin changed → refresh the head avatars in the sidebar/overlay.
                SkinHeadService.InvalidateCache(ms.Username);
                StatusMessage = "Skin updated! It can take a minute to propagate.";
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SkinsVM] upload failed: {ex}");
                StatusMessage = $"Preview applied, but upload failed: {ex.Message}";
            }
            finally { IsBusy = false; }
        }
        else if (_account is ElyAccount)
        {
            Process.Start(new ProcessStartInfo("https://ely.by/skins") { UseShellExecute = true });
            StatusMessage = "Previewed. Ely.by skins are changed on ely.by — opened in your browser.";
        }
        else
        {
            StatusMessage = "Preview only — sign in to apply this skin to your account.";
        }
    }
}
