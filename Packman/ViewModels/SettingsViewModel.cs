using Microsoft.Identity.Client;
using Packman.Models;
using Packman.Services;
using System.Collections.ObjectModel;
using System.Security.Cryptography.X509Certificates;

namespace Packman.ViewModels;

public class CertificateInfo
{
    public string FriendlyName { get; init; } = "";
    public string Subject { get; init; } = "";
    public string Thumbprint { get; init; } = "";
    public override string ToString() => string.IsNullOrEmpty(FriendlyName) ? Subject : FriendlyName;
}

public sealed class SettingsViewModel : ObservableObject
{
    private readonly SettingsService _svc;
    private readonly IntuneAuthService _auth;

    // ── Interactive sign-in state ──────────────────────────────────────
    private bool _isSignedIn;
    public bool IsSignedIn
    {
        get => _isSignedIn;
        private set { if (Set(ref _isSignedIn, value)) OnPropertyChanged(nameof(IsNotSignedIn)); }
    }
    public bool IsNotSignedIn => !IsSignedIn;

    private string _signedInUser = "";
    public string SignedInUser { get => _signedInUser; private set => Set(ref _signedInUser, value); }

    // ── Auth mode ──────────────────────────────────────────────────────
    private bool _isInteractive = true;
    public bool IsInteractive
    {
        get => _isInteractive;
        set { if (Set(ref _isInteractive, value)) OnPropertyChanged(nameof(IsAppRegistration)); }
    }
    public bool IsAppRegistration { get => !_isInteractive; set => IsInteractive = !value; }

    // ── App Registration fields ────────────────────────────────────────
    private string _tenantId = "";
    public string TenantId { get => _tenantId; set => Set(ref _tenantId, value); }

    private string _clientId = "";
    public string ClientId { get => _clientId; set => Set(ref _clientId, value); }

    // Auth cert source
    private bool _authUseStoreCert = true;
    public bool AuthUseStoreCert
    {
        get => _authUseStoreCert;
        set { if (Set(ref _authUseStoreCert, value)) OnPropertyChanged(nameof(AuthUseManualThumbprint)); }
    }
    public bool AuthUseManualThumbprint { get => !_authUseStoreCert; set => AuthUseStoreCert = !value; }

    private string _authThumbprint = "";
    public string AuthThumbprint { get => _authThumbprint; set => Set(ref _authThumbprint, value); }

    private CertificateInfo? _selectedAuthCert;
    public CertificateInfo? SelectedAuthCert
    {
        get => _selectedAuthCert;
        set { if (Set(ref _selectedAuthCert, value) && value != null) AuthThumbprint = value.Thumbprint; }
    }

    // ── Code signing ───────────────────────────────────────────────────
    private bool _codeSigningEnabled;
    public bool CodeSigningEnabled
    {
        get => _codeSigningEnabled;
        set { if (Set(ref _codeSigningEnabled, value)) OnPropertyChanged(nameof(CodeSigningDisabled)); }
    }
    public bool CodeSigningDisabled { get => !_codeSigningEnabled; set => CodeSigningEnabled = !value; }

    private bool _codeSignUseStoreCert = true;
    public bool CodeSignUseStoreCert
    {
        get => _codeSignUseStoreCert;
        set { if (Set(ref _codeSignUseStoreCert, value)) OnPropertyChanged(nameof(CodeSignUseManualThumbprint)); }
    }
    public bool CodeSignUseManualThumbprint { get => !_codeSignUseStoreCert; set => CodeSignUseStoreCert = !value; }

    private string _codeSignThumbprint = "";
    public string CodeSignThumbprint { get => _codeSignThumbprint; set => Set(ref _codeSignThumbprint, value); }

    private string _codeSignCertName = "";
    public string CodeSignCertName { get => _codeSignCertName; set => Set(ref _codeSignCertName, value); }

    private string _codeSignCertSubject = "";
    public string CodeSignCertSubject { get => _codeSignCertSubject; set => Set(ref _codeSignCertSubject, value); }

    private string _codeSignTimestampServer = "http://timestamp.digicert.com";
    public string CodeSignTimestampServer { get => _codeSignTimestampServer; set => Set(ref _codeSignTimestampServer, value); }

    private CertificateInfo? _selectedCodeSignCert;
    public CertificateInfo? SelectedCodeSignCert
    {
        get => _selectedCodeSignCert;
        set { if (Set(ref _selectedCodeSignCert, value) && value != null) CodeSignThumbprint = value.Thumbprint; }
    }

    // ── Network Paths ──────────────────────────────────────────────────
    private string _intuneApplicationsPath = "";
    public string IntuneApplicationsPath { get => _intuneApplicationsPath; set => Set(ref _intuneApplicationsPath, value); }

    private string _psadtTemplatePath = "";
    public string PSADTTemplatePath { get => _psadtTemplatePath; set => Set(ref _psadtTemplatePath, value); }

    private string _intuneWinAppUtilPath = "";
    public string IntuneWinAppUtilPath { get => _intuneWinAppUtilPath; set => Set(ref _intuneWinAppUtilPath, value); }

    // ── Save feedback ──────────────────────────────────────────────────
    private string _saveStatus = "";
    public string SaveStatus { get => _saveStatus; set => Set(ref _saveStatus, value); }

    public ObservableCollection<CertificateInfo> AvailableCertificates { get; } = new();

    public RelayCommand SaveCommand { get; }
    public RelayCommand ResetCommand { get; }
    public RelayCommand SignInCommand { get; }
    public RelayCommand SignOutCommand { get; }

    public SettingsViewModel(SettingsService svc, IntuneAuthService auth)
    {
        _svc = svc;
        _auth = auth;
        SaveCommand = new RelayCommand(Save);
        ResetCommand = new RelayCommand(Reset);
        SignInCommand = new RelayCommand(SignIn);
        SignOutCommand = new RelayCommand(SignOut);
        LoadFromSettings();
        LoadCertificatesFromStore();
    }

    private void LoadFromSettings()
    {
        var s = _svc.Settings;
        IsInteractive = s.AuthMode == AuthMode.Interactive;
        TenantId = s.Authentication.TenantId;
        ClientId = s.Authentication.ClientId;
        AuthThumbprint = s.Authentication.CertificateThumbprint;
        AuthUseStoreCert = !string.IsNullOrEmpty(AuthThumbprint) ? false : true;

        CodeSigningEnabled = s.CodeSigning.Enabled;
        CodeSignThumbprint = s.CodeSigning.CertificateThumbprint;
        CodeSignCertName = s.CodeSigning.CertificateName;
        CodeSignCertSubject = s.CodeSigning.CertificateSubject;
        CodeSignTimestampServer = s.CodeSigning.TimestampServer;
        CodeSignUseStoreCert = !string.IsNullOrEmpty(CodeSignThumbprint) ? false : true;

        IntuneApplicationsPath = s.NetworkPaths.IntuneApplications;
        PSADTTemplatePath = s.NetworkPaths.PSADTTemplate;
        IntuneWinAppUtilPath = s.NetworkPaths.IntuneWinAppUtil;
    }

    private void LoadCertificatesFromStore()
    {
        AvailableCertificates.Clear();
        try
        {
            using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
            store.Open(OpenFlags.ReadOnly);
            foreach (var cert in store.Certificates)
            {
                AvailableCertificates.Add(new CertificateInfo
                {
                    FriendlyName = cert.FriendlyName,
                    Subject = cert.Subject,
                    Thumbprint = cert.Thumbprint
                });
            }
        }
        catch { /* store not accessible in this environment */ }

        if (!string.IsNullOrEmpty(AuthThumbprint))
            SelectedAuthCert = AvailableCertificates.FirstOrDefault(c => c.Thumbprint == AuthThumbprint);
        if (!string.IsNullOrEmpty(CodeSignThumbprint))
            SelectedCodeSignCert = AvailableCertificates.FirstOrDefault(c => c.Thumbprint == CodeSignThumbprint);
    }

    private async void SignIn()
    {
        SaveStatus = "Signing in…";
        try
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(
                System.Windows.Application.Current.MainWindow).Handle;
            var mode = IsInteractive ? AuthMode.Interactive : AuthMode.AppRegistration;
            var cfg = new AppSettings.AuthConfig
            {
                TenantId = TenantId,
                ClientId = ClientId,
                CertificateThumbprint = IsAppRegistration ? AuthThumbprint : ""
            };
            await _auth.SignInAsync(mode, cfg, hwnd);
            IsSignedIn = true;
            SignedInUser = _auth.SignedInUser ?? "";
            SaveStatus = "";
        }
        catch (MsalClientException ex) when (ex.ErrorCode == "authentication_canceled")
        {
            SaveStatus = "";
        }
        catch (Exception ex)
        {
            SaveStatus = $"Sign-in failed: {ex.Message}";
        }
    }

    private async void SignOut()
    {
        await _auth.SignOutAsync();
        IsSignedIn = false;
        SignedInUser = "";
    }

    private void Reset()
    {
        LoadFromSettings();
        SaveStatus = "";
    }

    private void Save()
    {
        var s = _svc.Settings;
        s.AuthMode = IsInteractive ? AuthMode.Interactive : AuthMode.AppRegistration;
        s.Authentication.TenantId = TenantId;
        s.Authentication.ClientId = ClientId;
        s.Authentication.CertificateThumbprint = AuthThumbprint;

        s.CodeSigning.Enabled = CodeSigningEnabled;
        s.CodeSigning.CertificateThumbprint = CodeSignThumbprint;
        s.CodeSigning.TimestampServer = CodeSignTimestampServer;
        s.CodeSigning.CertificateName = _selectedCodeSignCert?.FriendlyName ?? CodeSignCertName;
        s.CodeSigning.CertificateSubject = _selectedCodeSignCert?.Subject ?? CodeSignCertSubject;

        s.NetworkPaths.IntuneApplications = IntuneApplicationsPath;
        s.NetworkPaths.PSADTTemplate = PSADTTemplatePath;
        s.NetworkPaths.IntuneWinAppUtil = IntuneWinAppUtilPath;

        _svc.Save();
        SaveStatus = "Settings saved.";
    }
}
