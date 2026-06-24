using Packman.Models;
using Packman.Services;
using System.IO;
using System.Windows;

namespace Packman.ViewModels;

/// <summary>
/// Drives the final wizard step: builds the .intunewin from the package produced by
/// Create/Upgrade and uploads it to Intune using the interactive sign-in token.
/// </summary>
public class UploadStepViewModel : ObservableObject
{
    private readonly CreatePackageViewModel _create;
    private readonly SettingsService _settingsService;
    private readonly IntuneAuthService _auth;

    private string _appSummaryName = "";
    private string _appSummaryDetail = "";
    private string _detectionSummary = "";
    private string _statusText = "";
    private int _progressValue;
    private bool _isUploading;

    public UploadStepViewModel(CreatePackageViewModel create, SettingsService settingsService, IntuneAuthService auth)
    {
        _create = create;
        _settingsService = settingsService;
        _auth = auth;
    }

    public string AppSummaryName { get => _appSummaryName; set => Set(ref _appSummaryName, value); }
    public string AppSummaryDetail { get => _appSummaryDetail; set => Set(ref _appSummaryDetail, value); }
    public string DetectionSummary { get => _detectionSummary; set => Set(ref _detectionSummary, value); }
    public string StatusText { get => _statusText; set => Set(ref _statusText, value); }
    public int ProgressValue { get => _progressValue; set => Set(ref _progressValue, value); }
    public bool IsUploading { get => _isUploading; set => Set(ref _isUploading, value); }

    public bool IsSignedIn => _auth.IsSignedIn;
    public bool IsNotSignedIn => !_auth.IsSignedIn;
    public string SignedInUser => _auth.SignedInUser ?? "";

    /// <summary>
    /// Refreshes the displayed summary from the package produced earlier in the wizard.
    /// </summary>
    public void RefreshFromPackage()
    {
        OnPropertyChanged(nameof(IsSignedIn));
        OnPropertyChanged(nameof(IsNotSignedIn));
        OnPropertyChanged(nameof(SignedInUser));

        if (string.IsNullOrEmpty(_create.CurrentPackagePath))
        {
            AppSummaryName = "No package generated yet";
            AppSummaryDetail = "Complete the Generate step first.";
            DetectionSummary = "";
            return;
        }

        var appInfo = _create.BuildApplicationInfo();
        AppSummaryName = $"{appInfo.Manufacturer} {appInfo.Name}".Trim();
        AppSummaryDetail = $"v{appInfo.Version} · {appInfo.InstallContext} context · Win32";
        DetectionSummary = DescribeDetection(BuildDetectionRules(_create.CurrentPackagePath, appInfo));
    }

    public async Task UploadAsync()
    {
        var packagePath = _create.CurrentPackagePath;
        if (string.IsNullOrEmpty(packagePath) || !Directory.Exists(packagePath))
        {
            StatusText = "Generate a package first.";
            return;
        }

        if (!_auth.IsSignedIn)
        {
            StatusText = "Sign in to Intune on the Settings page first.";
            return;
        }

        var settings = _settingsService.Settings;
        if (string.IsNullOrWhiteSpace(settings.NetworkPaths.IntuneWinAppUtil))
        {
            StatusText = "Set the IntuneWinAppUtil path on the Settings page first.";
            return;
        }

        var appInfo = _create.BuildApplicationInfo();
        var detectionRules = BuildDetectionRules(packagePath, appInfo);

        NativeCodeSigner? signer = null;
        if (settings.CodeSigning.Enabled)
            signer = new NativeCodeSigner(settings.CodeSigning.CertificateThumbprint, settings.CodeSigning.TimestampServer);

        IsUploading = true;
        ProgressValue = 0;
        StatusText = "Starting upload…";

        var progress = new DispatchedProgress(this);

        try
        {
            using var uploadService = new IntuneUploadService(
                _auth.GetAccessTokenAsync,
                signer,
                settings.NetworkPaths.IntuneWinAppUtil);

            var appId = await Task.Run(() => uploadService.UploadWin32ApplicationAsync(
                appInfo,
                packagePath,
                detectionRules,
                "Invoke-AppDeployToolkit.exe Install",
                "Invoke-AppDeployToolkit.exe Uninstall",
                $"{appInfo.Manufacturer} {appInfo.Name} {appInfo.Version}",
                appInfo.InstallContext,
                string.IsNullOrEmpty(_create.ExtractedIconPath) ? null : _create.ExtractedIconPath,
                progress,
                string.IsNullOrEmpty(_create.PredecessorAppId) ? null : _create.PredecessorAppId,
                settings.GroupAssignment));

            ProgressValue = 100;
            StatusText = $"Uploaded to Intune · App ID {appId}";
        }
        catch (Exception ex)
        {
            StatusText = $"Upload failed: {ex.Message}";
        }
        finally
        {
            IsUploading = false;
        }
    }

    private static List<DetectionRule> BuildDetectionRules(string packagePath, ApplicationInfo appInfo)
    {
        var rules = new List<DetectionRule>();

        try
        {
            var filesFolder = Path.Combine(packagePath, "Application", "Files");
            if (Directory.Exists(filesFolder))
            {
                var msiFiles = Directory.GetFiles(filesFolder, "*.msi", SearchOption.TopDirectoryOnly);
                if (msiFiles.Length > 0)
                {
                    var msiInfo = MsiInfoService.ExtractMsiInfo(msiFiles[0]);
                    if (msiInfo.IsValid)
                    {
                        rules.Add(new DetectionRule
                        {
                            Type = DetectionRuleType.File,
                            Path = "%ProgramFiles%",
                            FileOrFolderName = $"{appInfo.Name}.exe",
                            DetectionType = "version",
                            Operator = "greaterThanOrEqual",
                            DetectionValue = string.IsNullOrEmpty(msiInfo.ProductVersion) ? appInfo.Version : msiInfo.ProductVersion,
                            CheckVersion = true,
                            Check32BitOn64System = true
                        });
                    }
                }
            }
        }
        catch { /* fall through to default rule */ }

        if (rules.Count == 0)
        {
            rules.Add(new DetectionRule
            {
                Type = DetectionRuleType.File,
                Path = "%ProgramFiles%",
                FileOrFolderName = $"{appInfo.Name}.exe",
                DetectionType = "exists",
                Check32BitOn64System = true
            });
        }

        return rules;
    }

    private static string DescribeDetection(List<DetectionRule> rules)
        => rules.Count == 0 ? "No detection rule" : rules[0].Title;

    private sealed class DispatchedProgress : IUploadProgress
    {
        private readonly UploadStepViewModel _vm;
        public DispatchedProgress(UploadStepViewModel vm) => _vm = vm;

        public void UpdateProgress(int percentage, string message)
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                _vm.ProgressValue = percentage;
                _vm.StatusText = message;
            });
        }
    }
}
