using Microsoft.Identity.Client;
using Microsoft.Identity.Client.Broker;

namespace Packman.Services;

public class IntuneAuthService
{
    // Well-known Microsoft Intune PowerShell public client
    private const string IntuneClientId = "d1ddf0e4-d672-4dae-b554-9d5bdfd93547";

    private static readonly string[] Scopes =
    [
        "User.Read",
        "DeviceManagementApps.ReadWrite.All",
    ];

    private IPublicClientApplication? _pca;
    private IAccount? _account;

    public string? SignedInUser { get; private set; }

    public async Task SignInAsync(string? tenantId, nint hwnd)
    {
        var authority = string.IsNullOrWhiteSpace(tenantId)
            ? "https://login.microsoftonline.com/organizations"
            : $"https://login.microsoftonline.com/{tenantId.Trim()}";

        _pca = PublicClientApplicationBuilder
            .Create(IntuneClientId)
            .WithAuthority(authority)
            .WithBroker(new BrokerOptions(BrokerOptions.OperatingSystems.Windows))
            .Build();

        AuthenticationResult result;
        try
        {
            var accounts = await _pca.GetAccountsAsync();
            result = await _pca.AcquireTokenSilent(Scopes, accounts.FirstOrDefault()).ExecuteAsync();
        }
        catch (MsalUiRequiredException)
        {
            result = await _pca.AcquireTokenInteractive(Scopes)
                .WithParentActivityOrWindow(hwnd)
                .ExecuteAsync();
        }

        _account = result.Account;
        SignedInUser = result.Account.Username;
    }

    public async Task SignOutAsync()
    {
        if (_pca != null && _account != null)
            await _pca.RemoveAsync(_account);
        _account = null;
        SignedInUser = null;
    }

    public bool IsSignedIn => _pca != null && _account != null;

    /// <summary>
    /// Returns a Graph access token for the signed-in account. Requires a prior
    /// successful SignInAsync; throws otherwise so the upload flow can prompt to sign in.
    /// </summary>
    public async Task<string> GetAccessTokenAsync()
    {
        if (_pca == null || _account == null)
            throw new InvalidOperationException("Not signed in. Sign in on the Settings page before uploading.");

        var result = await _pca.AcquireTokenSilent(Scopes, _account).ExecuteAsync();
        return result.AccessToken;
    }
}
