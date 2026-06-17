namespace AnchorLauncher.Models.Auth;

public class MicrosoftAccount : ILauncherAccount
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public AccountType AccountType => AccountType.Microsoft;
    public string Username  { get; set; } = string.Empty;
    public string Uuid      { get; set; } = string.Empty;
    public string SkinUrl   { get; set; } = string.Empty;
    public DateTime TokenExpiry  { get; set; }
    public bool OwnsMinecraft    { get; set; } = true;

    // DPAPI-encrypted Base64 strings — never plaintext on disk
    public string EncryptedMinecraftToken { get; set; } = string.Empty;

    // MSAL account identifier used for silent re-authentication
    public string MsalAccountId { get; set; } = string.Empty;
}
