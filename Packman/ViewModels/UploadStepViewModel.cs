using Packman.Models;
using Packman.Services;
using System.IO;
using System.Linq;
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

    private string _selectedDetectionMethod = "Auto (from package)";
    private string _detectionPath = "";
    private string _detectionName = "";
    private string _detectionValue = "";

    private string _selectedOperatingSystem = "Windows 10 1607";
    private string _minFreeDiskSpaceMB = "";
    private string _minMemoryMB = "";

    private string _reviewName = "";
    private string _reviewVendor = "";
    private string _reviewVersion = "";
    private string _reviewAuthor = "";
    private string _reviewArchitecture = "";
    private string _reviewContext = "";
    private string _reviewPackageType = "";
    private string _reviewSize = "";

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

    // ── Detection method ───────────────────────────────────────────────
    public List<string> DetectionMethods { get; } = new()
    {
        "Auto (from package)", "File exists", "File version", "Registry key exists", "MSI product code"
    };

    public string SelectedDetectionMethod
    {
        get => _selectedDetectionMethod;
        set
        {
            if (Set(ref _selectedDetectionMethod, value))
            {
                OnPropertyChanged(nameof(IsCustomDetection));
                RefreshDetectionSummary();
            }
        }
    }

    public bool IsCustomDetection => _selectedDetectionMethod != "Auto (from package)";

    public string DetectionPath
    {
        get => _detectionPath;
        set { if (Set(ref _detectionPath, value)) RefreshDetectionSummary(); }
    }

    public string DetectionName
    {
        get => _detectionName;
        set { if (Set(ref _detectionName, value)) RefreshDetectionSummary(); }
    }

    public string DetectionValue
    {
        get => _detectionValue;
        set { if (Set(ref _detectionValue, value)) RefreshDetectionSummary(); }
    }

    // ── Requirements (collapsed; defaults used when left unset) ─────────
    public List<string> OperatingSystems { get; } = new()
    {
        "Windows 10 1607", "Windows 10 1809", "Windows 10 1903", "Windows 10 2004",
        "Windows 10 21H2", "Windows 10 22H2", "Windows 11 21H2", "Windows 11 22H2"
    };

    public string SelectedOperatingSystem { get => _selectedOperatingSystem; set => Set(ref _selectedOperatingSystem, value); }
    public string MinFreeDiskSpaceMB { get => _minFreeDiskSpaceMB; set => Set(ref _minFreeDiskSpaceMB, value); }
    public string MinMemoryMB { get => _minMemoryMB; set => Set(ref _minMemoryMB, value); }

    // ── Review ─────────────────────────────────────────────────────────
    public string ReviewName { get => _reviewName; set => Set(ref _reviewName, value); }
    public string ReviewVendor { get => _reviewVendor; set => Set(ref _reviewVendor, value); }
    public string ReviewVersion { get => _reviewVersion; set => Set(ref _reviewVersion, value); }
    public string ReviewAuthor { get => _reviewAuthor; set => Set(ref _reviewAuthor, value); }
    public string ReviewArchitecture { get => _reviewArchitecture; set => Set(ref _reviewArchitecture, value); }
    public string ReviewContext { get => _reviewContext; set => Set(ref _reviewContext, value); }
    public string ReviewPackageType { get => _reviewPackageType; set => Set(ref _reviewPackageType, value); }
    public string ReviewSize { get => _reviewSize; set => Set(ref _reviewSize, value); }

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

        // Seed the editable detection fields from the auto-detected rule.
        var autoRule = BuildDetectionRules(_create.CurrentPackagePath, appInfo)[0];
        _detectionPath = string.IsNullOrEmpty(autoRule.Path) ? "%ProgramFiles%" : autoRule.Path;
        _detectionName = string.IsNullOrEmpty(autoRule.FileOrFolderName) ? $"{appInfo.Name}.exe" : autoRule.FileOrFolderName;
        _detectionValue = string.IsNullOrEmpty(autoRule.DetectionValue) ? appInfo.Version : autoRule.DetectionValue;
        OnPropertyChanged(nameof(DetectionPath));
        OnPropertyChanged(nameof(DetectionName));
        OnPropertyChanged(nameof(DetectionValue));
        RefreshDetectionSummary();

        // Review panel.
        ReviewName = appInfo.Name;
        ReviewVendor = appInfo.Manufacturer;
        ReviewVersion = appInfo.Version;
        ReviewAuthor = string.IsNullOrWhiteSpace(appInfo.Author) ? Environment.UserName : appInfo.Author;
        ReviewArchitecture = appInfo.Architecture;
        ReviewContext = appInfo.InstallContext;
        ReviewPackageType = appInfo.PackageType;
        ReviewSize = FormatSize(GetSourceSizeBytes(_create.CurrentPackagePath));
    }

    private void RefreshDetectionSummary()
    {
        if (string.IsNullOrEmpty(_create.CurrentPackagePath)) return;
        var appInfo = _create.BuildApplicationInfo();
        DetectionSummary = DescribeDetection(BuildSelectedDetectionRules(_create.CurrentPackagePath, appInfo));
    }

    private List<DetectionRule> BuildSelectedDetectionRules(string packagePath, ApplicationInfo appInfo)
    {
        if (!IsCustomDetection)
            return BuildDetectionRules(packagePath, appInfo);

        return SelectedDetectionMethod switch
        {
            "File exists" => new List<DetectionRule>
            {
                new() { Type = DetectionRuleType.File, Path = DetectionPath, FileOrFolderName = DetectionName,
                        DetectionType = "exists", Check32BitOn64System = true }
            },
            "File version" => new List<DetectionRule>
            {
                new() { Type = DetectionRuleType.File, Path = DetectionPath, FileOrFolderName = DetectionName,
                        DetectionType = "version", CheckVersion = true, Operator = "greaterThanOrEqual",
                        DetectionValue = DetectionValue, Check32BitOn64System = true }
            },
            "Registry key exists" => new List<DetectionRule>
            {
                new() { Type = DetectionRuleType.Registry, Path = DetectionPath, FileOrFolderName = DetectionName,
                        DetectionType = "exists" }
            },
            "MSI product code" => new List<DetectionRule>
            {
                new() { Type = DetectionRuleType.MSI, Path = DetectionPath }
            },
            _ => BuildDetectionRules(packagePath, appInfo)
        };
    }

    private RequirementInfo BuildRequirements()
    {
        var req = new RequirementInfo { MinimumOperatingSystem = SelectedOperatingSystem };
        if (int.TryParse(MinFreeDiskSpaceMB, out var disk) && disk > 0) req.MinimumFreeDiskSpaceMB = disk;
        if (int.TryParse(MinMemoryMB, out var mem) && mem > 0) req.MinimumMemoryMB = mem;
        return req;
    }

    private static long GetSourceSizeBytes(string packagePath)
    {
        try
        {
            var filesFolder = Path.Combine(packagePath, "Application", "Files");
            if (!Directory.Exists(filesFolder)) return 0;
            return Directory.GetFiles(filesFolder, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length);
        }
        catch { return 0; }
    }

    private static string FormatSize(long bytes)
    {
        if (bytes <= 0) return "—";
        string[] units = { "B", "KB", "MB", "GB" };
        double size = bytes;
        int unit = 0;
        while (size >= 1024 && unit < units.Length - 1) { size /= 1024; unit++; }
        return $"{size:0.#} {units[unit]}";
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
        var detectionRules = BuildSelectedDetectionRules(packagePath, appInfo);
        var requirements = BuildRequirements();

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
                settings.GroupAssignment,
                requirements));

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
