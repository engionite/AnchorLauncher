namespace AnchorLauncher.Models.Auth;

public interface ILauncherAccount
{
    Guid Id           { get; }
    AccountType AccountType { get; }
    string Username   { get; }
    string Uuid       { get; }
    string SkinUrl    { get; }
    DateTime TokenExpiry { get; }
    bool OwnsMinecraft   { get; }
}
