using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AnchorLauncher.Models.Auth;
using AnchorLauncher.Services.Storage;
using Microsoft.Identity.Client;

namespace AnchorLauncher.Services.Auth;

/// <summary>A user-presentable Microsoft sign-in failure (carries guidance for the UI).</summary>
public class MicrosoftAuthException : Exception
{
    public MicrosoftAuthException(string message, Exception? inner = null) : base(message, inner) { }
}

public class MicrosoftAuthService
{
    // Anchor Launcher's registered Azure AD application (Personal Microsoft accounts /
    // "consumers" authority, public client with device-code + native-client redirect enabled).
    // Registered by the Operator at https://portal.azure.com → App registrations.
    private const string ClientId = "2f5003b6-9576-4392-8e6a-2e9c03683ea4";
    private const string Authority = "https://login.microsoftonline.com/consumers";
    private static readonly string[] Scopes = { "XboxLive.signin", "offline_access" };

    private static readonly HttpClient _http = new()
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    private IPublicClientApplication? _msalApp;

    // ── MSAL app (lazy, cache attached on first use) ────────────────────────

    private IPublicClientApplication GetMsalApp()
    {
        if (_msalApp != null) return _msalApp;

        _msalApp = PublicClientApplicationBuilder
            .Create(ClientId)
            .WithAuthority(Authority)
            .WithDefaultRedirectUri()   // https://login.microsoftonline.com/common/oauth2/nativeclient
            .Build();

        Debug.WriteLine($"[MsAuth] MSAL app built — ClientId={ClientId} Authority={Authority} RedirectUri={_msalApp.AppConfig.RedirectUri}");

        _msalApp.UserTokenCache.SetBeforeAccess(OnBeforeCacheAccess);
        _msalApp.UserTokenCache.SetAfterAccess(OnAfterCacheAccess);

        return _msalApp;
    }

    private static void OnBeforeCacheAccess(TokenCacheNotificationArgs args)
    {
        var path = LauncherStorageService.MsalCachePath;
        if (!File.Exists(path)) return;
        try
        {
            var encrypted = File.ReadAllBytes(path);
            var decrypted = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
            args.TokenCache.DeserializeMsalV3(decrypted);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MsAuth] Cache load failed: {ex.Message}");
        }
    }

    private static void OnAfterCacheAccess(TokenCacheNotificationArgs args)
    {
        if (!args.HasStateChanged) return;
        try
        {
            var data      = args.TokenCache.SerializeMsalV3();
            var encrypted = ProtectedData.Protect(data, null, DataProtectionScope.CurrentUser);
            File.WriteAllBytes(LauncherStorageService.MsalCachePath, encrypted);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MsAuth] Cache save failed: {ex.Message}");
        }
    }

    // ── Public API ──────────────────────────────────────────────────────────

    public async Task<MicrosoftAccount> AuthenticateDeviceCodeAsync(
        Action<string, string> onCodeReady,
        CancellationToken ct = default)
    {
        Debug.WriteLine("[MsAuth] ─── Device-code flow START ───");
        Debug.WriteLine($"[MsAuth] ClientId={ClientId} Authority={Authority} Scopes={string.Join(' ', Scopes)}");
        var app = GetMsalApp();

        AuthenticationResult msalResult;
        try
        {
            msalResult = await app
                .AcquireTokenWithDeviceCode(Scopes, deviceCode =>
                {
                    Debug.WriteLine($"[MsAuth] STEP 1 device-code callback fired → code='{deviceCode.UserCode}' url='{deviceCode.VerificationUrl}' expires={deviceCode.ExpiresOn:u}");
                    onCodeReady(deviceCode.UserCode, deviceCode.VerificationUrl);
                    return Task.CompletedTask;
                })
                .ExecuteAsync(ct);

            Debug.WriteLine("[MsAuth] STEP 1 OK — MSA access token acquired.");
        }
        catch (MsalException ex) when (
            ex.ErrorCode is "unauthorized_client" or "invalid_client" or "invalid_request" ||
            (ex.Message?.Contains("AADSTS7000", StringComparison.OrdinalIgnoreCase) ?? false))
        {
            // The shared community client ID was rejected by Azure AD.
            Debug.WriteLine($"[MsAuth] STEP 1 FAILED (client rejected): {ex.ErrorCode} — {ex.Message}");
            throw new MicrosoftAuthException(
                "Microsoft rejected the launcher's sign-in request. This usually means the Azure AD " +
                "app registration needs its public-client / device-code flow enabled, or the " +
                "'consumers' (personal Microsoft accounts) audience isn't allowed.\n\n" +
                "In portal.azure.com → App registrations → Authentication: enable " +
                "'Allow public client flows', add the 'https://login.microsoftonline.com/common/oauth2/nativeclient' " +
                "redirect URI under Mobile and desktop applications, and set supported account types to " +
                "personal Microsoft accounts.\n\nEly.by sign-in works without this.", ex);
        }
        catch (MsalException ex)
        {
            Debug.WriteLine($"[MsAuth] STEP 1 FAILED: {ex.ErrorCode} — {ex.Message}");
            throw new MicrosoftAuthException($"Microsoft sign-in failed ({ex.ErrorCode}). {ex.Message}", ex);
        }

        return await BuildAccountFromMsaTokenAsync(msalResult);
    }

    public async Task<MicrosoftAccount?> TrySilentRefreshAsync(
        MicrosoftAccount account,
        CancellationToken ct = default)
    {
        try
        {
            var app         = GetMsalApp();
            var allAccounts = await app.GetAccountsAsync();
            var msalAccount = allAccounts.FirstOrDefault(
                a => a.HomeAccountId.Identifier == account.MsalAccountId);

            if (msalAccount == null)
            {
                Debug.WriteLine("[MsAuth] MSAL account not found — silent refresh skipped.");
                return null;
            }

            var msalResult = await app
                .AcquireTokenSilent(Scopes, msalAccount)
                .ExecuteAsync(ct);

            Debug.WriteLine("[MsAuth] Silent refresh succeeded.");
            return await BuildAccountFromMsaTokenAsync(msalResult);
        }
        catch (MsalUiRequiredException)
        {
            Debug.WriteLine("[MsAuth] Silent refresh requires interactive sign-in.");
            return null;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MsAuth] TrySilentRefreshAsync failed: {ex.Message}");
            return null;
        }
    }

    public async Task RemoveFromCacheAsync(MicrosoftAccount account)
    {
        try
        {
            var app         = GetMsalApp();
            var allAccounts = await app.GetAccountsAsync();
            var msalAccount = allAccounts.FirstOrDefault(
                a => a.HomeAccountId.Identifier == account.MsalAccountId);
            if (msalAccount != null)
                await app.RemoveAsync(msalAccount);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MsAuth] RemoveFromCacheAsync failed: {ex.Message}");
        }
    }

    // ── Token chain ─────────────────────────────────────────────────────────

    private async Task<MicrosoftAccount> BuildAccountFromMsaTokenAsync(AuthenticationResult msalResult)
    {
        string xblToken, userHash, xstsToken, mcToken;
        DateTime mcExpiry;
        try
        {
            Debug.WriteLine("[MsAuth] STEP 2 → Xbox Live token exchange…");
            (xblToken, userHash) = await GetXboxLiveTokenAsync(msalResult.AccessToken);

            Debug.WriteLine("[MsAuth] STEP 3 → XSTS token exchange…");
            xstsToken = await GetXstsTokenAsync(xblToken);

            Debug.WriteLine("[MsAuth] STEP 4 → Minecraft bearer token exchange…");
            (mcToken, mcExpiry) = await GetMinecraftTokenAsync(xstsToken, userHash);
        }
        catch (MicrosoftAuthException) { throw; }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MsAuth] token chain FAILED: {ex.Message}");
            throw new MicrosoftAuthException($"Microsoft sign-in failed during the Xbox/Minecraft token exchange. {ex.Message}", ex);
        }

        Debug.WriteLine("[MsAuth] STEP 5 → ownership + profile…");
        var ownsGame = await CheckGameOwnershipAsync(mcToken);

        var profile = await GetMinecraftProfileAsync(mcToken);
        if (profile == null)
        {
            // Xbox auth succeeded but there is no Minecraft Java profile on this account.
            Debug.WriteLine("[MsAuth] No Minecraft Java profile (account does not own the game).");
            throw new MicrosoftAuthException(
                "This Microsoft account does not own Minecraft Java Edition. " +
                "You can still use Ely.by for offline play.");
        }

        var (uuid, username, skinUrl) = profile.Value;
        Debug.WriteLine("[MsAuth] ─── Device-code flow COMPLETE ───");

        return new MicrosoftAccount
        {
            Id                    = Guid.NewGuid(),
            Username              = username,
            Uuid                  = uuid,
            SkinUrl               = skinUrl,
            TokenExpiry           = mcExpiry,
            OwnsMinecraft         = ownsGame,
            EncryptedMinecraftToken = TokenVaultService.Protect(mcToken),
            MsalAccountId         = msalResult.Account.HomeAccountId.Identifier
        };
    }

    private static async Task<(string Token, string UserHash)> GetXboxLiveTokenAsync(string msaToken)
    {
        var body = new
        {
            Properties = new
            {
                AuthMethod = "RPS",
                SiteName   = "user.auth.xboxlive.com",
                RpsTicket  = $"d={msaToken}"
            },
            RelyingParty = "http://auth.xboxlive.com",
            TokenType    = "JWT"
        };

        using var req = new HttpRequestMessage(HttpMethod.Post,
            "https://user.auth.xboxlive.com/user/authenticate");
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        using var resp = await _http.SendAsync(req);
        if (!resp.IsSuccessStatusCode)
            throw new MicrosoftAuthException(
                $"Xbox Live sign-in failed (HTTP {(int)resp.StatusCode}). {await SafeReadBodyAsync(resp)}");

        using var doc  = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var token      = doc.RootElement.GetProperty("Token").GetString()!;
        var hash       = doc.RootElement.GetProperty("DisplayClaims")
                                        .GetProperty("xui")[0]
                                        .GetProperty("uhs").GetString()!;
        Debug.WriteLine("[MsAuth] XBL token acquired.");
        return (token, hash);
    }

    private static async Task<string> GetXstsTokenAsync(string xblToken)
    {
        var body = new
        {
            Properties = new
            {
                SandboxId  = "RETAIL",
                UserTokens = new[] { xblToken }
            },
            RelyingParty = "rp://api.minecraftservices.com/",
            TokenType    = "JWT"
        };

        using var req = new HttpRequestMessage(HttpMethod.Post,
            "https://xsts.auth.xboxlive.com/xsts/authorize");
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        using var resp = await _http.SendAsync(req);
        if (!resp.IsSuccessStatusCode)
        {
            var errBody = await resp.Content.ReadAsStringAsync();
            // XSTS encodes account-state problems in an XErr code — translate the common ones
            long xErr = 0;
            try { using var ed = JsonDocument.Parse(errBody); if (ed.RootElement.TryGetProperty("XErr", out var xe)) xErr = xe.GetInt64(); } catch { }
            var friendly = xErr switch
            {
                2148916233 => "This Microsoft account has no Xbox profile. Visit xbox.com to create one, then try again.",
                2148916235 => "Xbox Live is not available in your account's country/region.",
                2148916236 or 2148916237 => "This account needs adult verification on Xbox before it can sign in.",
                2148916238 => "This is a child account. Add it to a Microsoft Family (with an adult) to use Xbox services.",
                _          => $"Xbox (XSTS) authorization failed (HTTP {(int)resp.StatusCode}, XErr {xErr})."
            };
            Debug.WriteLine($"[MsAuth] STEP 3 FAILED: {friendly} :: {errBody}");
            throw new MicrosoftAuthException(friendly);
        }

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var token     = doc.RootElement.GetProperty("Token").GetString()!;
        Debug.WriteLine("[MsAuth] STEP 3 OK — XSTS token acquired.");
        return token;
    }

    private static async Task<(string Token, DateTime Expiry)> GetMinecraftTokenAsync(
        string xstsToken, string userHash)
    {
        var body = new { identityToken = $"XBL3.0 x={userHash};{xstsToken}" };

        using var req = new HttpRequestMessage(HttpMethod.Post,
            "https://api.minecraftservices.com/authentication/login_with_xbox");
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        using var resp = await _http.SendAsync(req);

        // 403 here = the launcher's Azure app hasn't been granted Minecraft API access yet.
        // Xbox auth all succeeded; api.minecraftservices.com simply refuses the app. This is an
        // app-level approval (https://aka.ms/mce-reviewappid), not anything the player can fix —
        // so say so plainly and point them at Ely.by instead of showing a raw "403 Forbidden".
        if (resp.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            Debug.WriteLine($"[MsAuth] STEP 4 403 from login_with_xbox: {await SafeReadBodyAsync(resp)}");
            throw new MicrosoftAuthException(
                "Microsoft sign-in isn't available in this build yet. Anchor's Microsoft app is still " +
                "awaiting Minecraft API approval (Microsoft returns 403 — this affects every account, " +
                "not yours specifically). Please use Ely.by for now; Microsoft accounts will work once " +
                "approval comes through.");
        }
        if (!resp.IsSuccessStatusCode)
            throw new MicrosoftAuthException(
                $"Minecraft token exchange failed (HTTP {(int)resp.StatusCode}). {await SafeReadBodyAsync(resp)}");

        using var doc  = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var token      = doc.RootElement.GetProperty("access_token").GetString()!;
        var expiresIn  = doc.RootElement.GetProperty("expires_in").GetInt32();
        Debug.WriteLine("[MsAuth] Minecraft token acquired.");
        return (token, DateTime.UtcNow.AddSeconds(expiresIn));
    }

    /// <summary>Reads a (failed) response body for diagnostics, trimmed and length-capped; never throws.</summary>
    private static async Task<string> SafeReadBodyAsync(HttpResponseMessage resp)
    {
        try
        {
            var s = (await resp.Content.ReadAsStringAsync()).Trim();
            return s.Length > 300 ? s[..300] : s;
        }
        catch { return string.Empty; }
    }

    /// <summary>
    /// Checks /entitlements/mcstore for a Minecraft Java license. Looks specifically for a
    /// <c>product_minecraft</c> / <c>game_minecraft</c> entitlement rather than any item.
    /// </summary>
    private static async Task<bool> CheckGameOwnershipAsync(string mcToken)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get,
                "https://api.minecraftservices.com/entitlements/mcstore");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", mcToken);

            using var resp = await _http.SendAsync(req);
            resp.EnsureSuccessStatusCode();

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            if (!doc.RootElement.TryGetProperty("items", out var items) ||
                items.ValueKind != JsonValueKind.Array)
                return false;

            foreach (var item in items.EnumerateArray())
            {
                var name = item.TryGetProperty("name", out var n) ? n.GetString() : null;
                if (name is "product_minecraft" or "game_minecraft")
                    return true;
            }
            // Some accounts return entitlements without the named product but still own the game.
            return items.GetArrayLength() > 0;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MsAuth] Ownership check failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>Returns the Minecraft Java profile, or null when the account has none (404 = no game).</summary>
    private static async Task<(string Uuid, string Username, string SkinUrl)?> GetMinecraftProfileAsync(
        string mcToken)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get,
            "https://api.minecraftservices.com/minecraft/profile");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", mcToken);

        using var resp = await _http.SendAsync(req);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;   // no Minecraft Java profile on this account
        resp.EnsureSuccessStatusCode();

        using var doc  = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var uuid       = doc.RootElement.GetProperty("id").GetString()!;
        var name       = doc.RootElement.GetProperty("name").GetString()!;

        string skinUrl = string.Empty;
        if (doc.RootElement.TryGetProperty("skins", out var skins))
        {
            foreach (var skin in skins.EnumerateArray())
            {
                if (skin.TryGetProperty("state", out var state) && state.GetString() == "ACTIVE")
                {
                    skinUrl = skin.GetProperty("url").GetString() ?? string.Empty;
                    break;
                }
            }
        }

        Debug.WriteLine($"[MsAuth] Profile: {name} ({uuid})");
        return (FormatUuid(uuid), name, skinUrl);
    }

    private static string FormatUuid(string raw) =>
        raw.Length == 32
            ? $"{raw[..8]}-{raw[8..12]}-{raw[12..16]}-{raw[16..20]}-{raw[20..]}"
            : raw;
}
