namespace Packman.Services;

/// <summary>
/// Shared, app-wide service instances. Packman has no DI container; this keeps a
/// single SettingsService and IntuneAuthService so the interactive sign-in done on
/// the Settings page can be reused by the upload flow.
/// </summary>
public static class AppServices
{
    public static SettingsService Settings { get; } = new();
    public static IntuneAuthService Auth { get; } = new();
    public static IntuneService Apps { get; } = new(() => Auth.GetAccessTokenAsync());
}
