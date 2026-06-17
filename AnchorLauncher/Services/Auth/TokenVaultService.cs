using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AnchorLauncher.Models.Auth;
using AnchorLauncher.Services.Storage;

namespace AnchorLauncher.Services.Auth;

public static class TokenVaultService
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    // ── DPAPI wrappers ──────────────────────────────────────────────────────
    // Every token MUST pass through Protect() before being written to disk.

    public static string Protect(string plaintext)
    {
        var bytes     = Encoding.UTF8.GetBytes(plaintext);
        var encrypted = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(encrypted);
    }

    public static string Unprotect(string ciphertext)
    {
        var bytes     = Convert.FromBase64String(ciphertext);
        var decrypted = ProtectedData.Unprotect(bytes, null, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(decrypted);
    }

    // ── Account store persistence ───────────────────────────────────────────
    // The JSON on disk contains DPAPI-encrypted Base64 blobs for all token fields.
    // Non-sensitive metadata (username, uuid, skinUrl) is stored plaintext.

    public static async Task SaveAccountsAsync(AccountsStore store)
    {
        try
        {
            var json = JsonSerializer.Serialize(store, _jsonOptions);
            await File.WriteAllTextAsync(LauncherStorageService.AccountsPath, json);
            Debug.WriteLine("[Vault] Accounts saved.");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Vault] SaveAccountsAsync failed: {ex}");
        }
    }

    public static async Task<AccountsStore> LoadAccountsAsync()
    {
        try
        {
            var path = LauncherStorageService.AccountsPath;
            if (!File.Exists(path))
                return new AccountsStore();

            var json = await File.ReadAllTextAsync(path);
            return JsonSerializer.Deserialize<AccountsStore>(json, _jsonOptions) ?? new AccountsStore();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Vault] LoadAccountsAsync failed: {ex}");
            return new AccountsStore();
        }
    }
}
