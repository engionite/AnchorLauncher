using AnchorLauncher.Models.Auth;

namespace AnchorLauncher.Models.Auth;

public class AccountsStore
{
    public List<MicrosoftAccount> MicrosoftAccounts { get; set; } = new();
    public List<ElyAccount>       ElyAccounts       { get; set; } = new();
    public Guid?        ActiveAccountId   { get; set; }
    public AccountType? ActiveAccountType { get; set; }
}
