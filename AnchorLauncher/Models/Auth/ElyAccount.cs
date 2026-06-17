namespace AnchorLauncher.Models.Auth;

public class ElyAccount : ILauncherAccount
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public AccountType AccountType => AccountType.ElyBy;
    public string Username  { get; set; } = string.Empty;
    public string Uuid      { get; set; } = string.Empty;
    public string SkinUrl   { get; set; } = string.Empty;
    public DateTime TokenExpiry { get; set; }
    public bool OwnsMinecraft   { get; set; } = true;

    // DPAPI-encrypted Base64 strings — never plaintext on disk.
    // Yggdrasil refresh uses accessToken + clientToken (there is no OAuth refresh token).
    public string EncryptedAccessToken  { get; set; } = string.Empty;
    public string EncryptedClientToken  { get; set; } = string.Empty;
}
