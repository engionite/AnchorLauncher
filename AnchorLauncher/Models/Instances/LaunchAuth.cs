namespace AnchorLauncher.Models.Instances;

/// <summary>
/// The minimal, already-decrypted auth context needed to build launch arguments.
/// Built by the ViewModel from the active account; tokens are decrypted via the vault
/// only at the moment of launch and never persisted in this shape.
/// </summary>
public record LaunchAuth(
    string PlayerName,
    string Uuid,
    string AccessToken,
    string UserType,   // "msa" for modern accounts
    bool   IsElyBy)
{
    /// <summary>An offline/no-account fallback for unauthenticated play.</summary>
    public static LaunchAuth Offline(string name = "Player") =>
        new(name, OfflineUuid(name), "0", "legacy", false);

    private static string OfflineUuid(string name)
    {
        // Deterministic offline UUID (name-based), matching the common "OfflinePlayer:" scheme
        var bytes = System.Security.Cryptography.MD5.HashData(
            System.Text.Encoding.UTF8.GetBytes("OfflinePlayer:" + name));
        bytes[6] = (byte)((bytes[6] & 0x0f) | 0x30); // version 3
        bytes[8] = (byte)((bytes[8] & 0x3f) | 0x80); // variant
        return new Guid(bytes).ToString("N");
    }
}
