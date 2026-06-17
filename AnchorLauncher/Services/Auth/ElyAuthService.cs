using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using AnchorLauncher.Models.Auth;
using AnchorLauncher.Services.Storage;

namespace AnchorLauncher.Services.Auth;

/// <summary>Thrown when Ely.by requires a TOTP code; the UI re-prompts and resubmits.</summary>
public class ElyTwoFactorRequiredException : Exception
{
    public ElyTwoFactorRequiredException() : base("Account protected with two factor auth.") { }
}

/// <summary>A user-presentable Ely.by authentication failure.</summary>
public class ElyAuthException : Exception
{
    public ElyAuthException(string message) : base(message) { }
}

/// <summary>
/// Ely.by authentication via the Yggdrasil-compatible authserver (the flow Ely.by
/// recommends for launchers), NOT OAuth2. See https://docs.ely.by/en/minecraft-auth.html
/// Tokens are DPAPI-encrypted before any disk write.
/// </summary>
public class ElyAuthService
{
    private const string AuthBase     = "https://authserver.ely.by";
    private const string MetaEndpoint = "https://elyprismlauncher.github.io/meta/v1";

    private static readonly HttpClient _http = new()
    {
        Timeout = TimeSpan.FromSeconds(30),
        DefaultRequestHeaders = { { "User-Agent", "AnchorLauncher/1.0" } }
    };

    private static readonly JsonSerializerOptions _json = new() { PropertyNameCaseInsensitive = true };

    // ── Authenticate ──────────────────────────────────────────────────────────

    /// <summary>
    /// POST /auth/authenticate with credentials. If <paramref name="twoFactorToken"/> is set
    /// it is appended as "password:token". Throws <see cref="ElyTwoFactorRequiredException"/>
    /// when 2FA is needed, or <see cref="ElyAuthException"/> for other auth errors.
    /// </summary>
    public async Task<ElyAccount> AuthenticateAsync(
        string login, string password, string? twoFactorToken = null, CancellationToken ct = default)
    {
        var clientToken       = Guid.NewGuid().ToString("N");
        var effectivePassword = string.IsNullOrWhiteSpace(twoFactorToken)
            ? password
            : $"{password}:{twoFactorToken.Trim()}";

        var payload = new
        {
            agent       = new { name = "Minecraft", version = 1 },
            username    = login,
            password    = effectivePassword,
            clientToken,
            requestUser = true
        };

        using var resp = await PostJsonAsync("/auth/authenticate", payload, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
            ThrowForError(resp.StatusCode, body);

        using var doc = JsonDocument.Parse(body);
        var root        = doc.RootElement;
        var accessToken = root.GetProperty("accessToken").GetString()!;
        var serverToken = root.TryGetProperty("clientToken", out var ctk) ? ctk.GetString()! : clientToken;
        var profile     = root.GetProperty("selectedProfile");
        var rawId       = profile.GetProperty("id").GetString()!;
        var name        = profile.GetProperty("name").GetString()!;

        // Make sure the authlib-injector JAR is present for launch
        _ = Task.Run(() => EnsureAuthlibInjectorAsync(CancellationToken.None));

        Debug.WriteLine($"[ElyAuth] Authenticated as {name} ({rawId}).");

        return new ElyAccount
        {
            Id                   = Guid.NewGuid(),
            Username             = name,
            Uuid                 = FormatUuid(rawId),
            SkinUrl              = SkinUrl(name),
            TokenExpiry          = DateTime.UtcNow.AddDays(2),
            EncryptedAccessToken = TokenVaultService.Protect(accessToken),
            EncryptedClientToken = TokenVaultService.Protect(serverToken)
        };
    }

    // ── Refresh ───────────────────────────────────────────────────────────────

    /// <summary>POST /auth/refresh with the stored accessToken + clientToken. Null on failure.</summary>
    public async Task<ElyAccount?> TryRefreshAsync(ElyAccount account, CancellationToken ct = default)
    {
        try
        {
            var accessToken = TokenVaultService.Unprotect(account.EncryptedAccessToken);
            var clientToken = TokenVaultService.Unprotect(account.EncryptedClientToken);

            var payload = new { accessToken, clientToken, requestUser = true };
            using var resp = await PostJsonAsync("/auth/refresh", payload, ct);
            if (!resp.IsSuccessStatusCode)
            {
                Debug.WriteLine($"[ElyAuth] refresh failed: HTTP {(int)resp.StatusCode}");
                return null;
            }

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            var root      = doc.RootElement;
            var newAccess = root.GetProperty("accessToken").GetString()!;
            var newClient = root.TryGetProperty("clientToken", out var ctk) ? ctk.GetString()! : clientToken;
            var profile   = root.GetProperty("selectedProfile");

            account.Uuid                 = FormatUuid(profile.GetProperty("id").GetString()!);
            account.Username             = profile.GetProperty("name").GetString()!;
            account.SkinUrl              = SkinUrl(account.Username);
            account.EncryptedAccessToken = TokenVaultService.Protect(newAccess);
            account.EncryptedClientToken = TokenVaultService.Protect(newClient);
            account.TokenExpiry          = DateTime.UtcNow.AddDays(2);

            Debug.WriteLine("[ElyAuth] Token refreshed.");
            return account;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ElyAuth] TryRefreshAsync failed: {ex.Message}");
            return null;
        }
    }

    // ── HTTP helpers ──────────────────────────────────────────────────────────

    private static Task<HttpResponseMessage> PostJsonAsync(string path, object payload, CancellationToken ct)
    {
        var json    = JsonSerializer.Serialize(payload);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        return _http.PostAsync(AuthBase + path, content, ct);
    }

    private static void ThrowForError(HttpStatusCode status, string body)
    {
        string message = string.Empty;
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("errorMessage", out var m))
                message = m.GetString() ?? string.Empty;
        }
        catch { /* non-JSON error body */ }

        if (message.Contains("two factor", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("2fa", StringComparison.OrdinalIgnoreCase))
            throw new ElyTwoFactorRequiredException();

        throw new ElyAuthException(string.IsNullOrEmpty(message)
            ? $"Ely.by sign-in failed (HTTP {(int)status})."
            : message);
    }

    private static string SkinUrl(string username)
        => $"https://skinsystem.ely.by/skins/{Uri.EscapeDataString(username)}.png";

    // ── Authlib-injector ──────────────────────────────────────────────────────

    public static async Task EnsureAuthlibInjectorAsync(CancellationToken ct = default)
    {
        try
        {
            var jarPath = LauncherStorageService.AuthlibInjectorPath;
            if (File.Exists(jarPath) && new FileInfo(jarPath).Length > 0)
            {
                Debug.WriteLine($"[ElyAuth] authlib-injector present: {jarPath}");
                return;
            }

            var jarUrl = await ResolveAuthlibInjectorUrlAsync(ct);
            if (string.IsNullOrEmpty(jarUrl))
            {
                Debug.WriteLine("[ElyAuth] could not resolve an authlib-injector download URL.");
                return;
            }

            Debug.WriteLine($"[ElyAuth] Downloading authlib-injector from {jarUrl}");
            var jarBytes = await _http.GetByteArrayAsync(jarUrl, ct);
            Directory.CreateDirectory(Path.GetDirectoryName(jarPath)!);
            await File.WriteAllBytesAsync(jarPath, jarBytes, ct);

            Debug.WriteLine($"[ElyAuth] authlib-injector saved: {jarPath} ({jarBytes.Length} bytes)");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ElyAuth] EnsureAuthlibInjectorAsync failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Resolves the authlib-injector .jar URL from the official GitHub latest release,
    /// falling back to the meta server. Returns null if neither responds.
    /// </summary>
    private static async Task<string?> ResolveAuthlibInjectorUrlAsync(CancellationToken ct)
    {
        const StringComparison OIC = StringComparison.OrdinalIgnoreCase;

        // 1) Official GitHub latest release
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get,
                "https://api.github.com/repos/yushijinhun/authlib-injector/releases/latest");
            req.Headers.UserAgent.ParseAdd("AnchorLauncher/1.0");      // GitHub API requires a UA
            req.Headers.Accept.ParseAdd("application/vnd.github+json");

            using var resp = await _http.SendAsync(req, ct);
            resp.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));

            if (doc.RootElement.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
            {
                foreach (var a in assets.EnumerateArray())
                {
                    var name = a.TryGetProperty("name", out var n) ? n.GetString() : null;
                    if (name == null || !name.EndsWith(".jar", OIC) ||
                        name.Contains("javadoc", OIC) || name.Contains("sources", OIC)) continue;

                    var url = a.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null;
                    if (!string.IsNullOrEmpty(url))
                    {
                        Debug.WriteLine($"[ElyAuth] authlib-injector release asset: {name}");
                        return url;
                    }
                }
            }
        }
        catch (Exception ex) { Debug.WriteLine($"[ElyAuth] GitHub release lookup failed: {ex.Message}"); }

        // 2) Fallback: the PineconeMC/ElyPrism meta server
        try
        {
            using var metaResp = await _http.GetAsync(MetaEndpoint, ct);
            metaResp.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await metaResp.Content.ReadAsStringAsync(ct));
            if (doc.RootElement.TryGetProperty("authlibInjectorUrl", out var u))
                return u.GetString();
        }
        catch (Exception ex) { Debug.WriteLine($"[ElyAuth] meta server lookup failed: {ex.Message}"); }

        return null;
    }

    // ── Misc ──────────────────────────────────────────────────────────────────

    private static string FormatUuid(string raw) =>
        raw.Length == 32
            ? $"{raw[..8]}-{raw[8..12]}-{raw[12..16]}-{raw[16..20]}-{raw[20..]}"
            : raw;
}
