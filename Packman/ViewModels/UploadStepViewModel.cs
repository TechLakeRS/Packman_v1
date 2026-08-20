using Packman.Helpers;
using Packman.Models;
using Packman.Services;
using System.Collections.ObjectModel;
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

    private string _selectedDetectionMethod = DetectionMethod.FileExists;
    private string _detectionPath = "";
    private string _detectionName = "";
    private string _detectionValue = "";
    private string _selectedRegistryHive = RegistryHiveNames.LocalMachine;
    private string _registryKeyPath = "";
    private string _registryValueName = "";
    private string _detectionProductCode = "";

    private string _selectedOperatingSystem = "Windows 10 1607";
    private string _minFreeDiskSpaceMB = "";
    private string _minMemoryMB = "";
    private string _minProcessors = "";
    private string _minCpuSpeedMHz = "";
    private string _newReturnCodeInput = "";
    private string _defaultsAppliedFor = "";

    private string _selectedDeployMode = DeployModeDefault;

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

        DoneCommand = new RelayCommand(() => { IsPublishing = false; IsComplete = false; });
        AddReturnCodeCommand = new RelayCommand(AddReturnCode);
        RestoreDefaultsCommand = new RelayCommand(ApplyIntuneDefaults);
        ApplyIntuneDefaults();

        PublishSteps = new ObservableCollection<PublishStepViewModel>
        {
            new(1, "Building .intunewin package"),
            new(2, "Signing with Authenticode"),
            new(3, "Uploading to tenant"),
            new(4, "Creating Win32 app"),
            new(5, "Assigning to groups"),
        };
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

    // ── Publishing overlay ─────────────────────────────────────────────
    public ObservableCollection<PublishStepViewModel> PublishSteps { get; }
    public RelayCommand DoneCommand { get; }

    private bool _isPublishing;
    public bool IsPublishing
    {
        get => _isPublishing;
        private set { if (Set(ref _isPublishing, value)) { OnPropertyChanged(nameof(IsNotPublishing)); OnPropertyChanged(nameof(IsRunning)); } }
    }
    public bool IsNotPublishing => !_isPublishing;

    /// <summary>True while a publish is in flight but not yet finished (drives the spinner).</summary>
    public bool IsRunning => _isPublishing && !_isComplete;

    private bool _isComplete;
    public bool IsComplete
    {
        get => _isComplete;
        private set { if (Set(ref _isComplete, value)) { OnPropertyChanged(nameof(IsRunning)); OnPropertyChanged(nameof(IsSucceeded)); OnPropertyChanged(nameof(IsFailed)); } }
    }

    private bool _succeeded;
    public bool IsSucceeded => _isComplete && _succeeded;
    public bool IsFailed => _isComplete && !_succeeded;

    private string _publishTitle = "";
    public string PublishTitle { get => _publishTitle; private set => Set(ref _publishTitle, value); }

    private string _resultText = "";
    public string ResultText { get => _resultText; private set => Set(ref _resultText, value); }

    /// <summary>Tenant label derived from the signed-in UPN domain (e.g. "contoso").</summary>
    public string TenantName
    {
        get
        {
            var upn = _auth.SignedInUser ?? "";
            var at = upn.IndexOf('@');
            if (at < 0 || at == upn.Length - 1) return "your";
            var domain = upn[(at + 1)..];
            var dot = domain.IndexOf('.');
            return dot > 0 ? domain[..dot] : domain;
        }
    }

    // ── Detection method ───────────────────────────────────────────────
    public List<string> DetectionMethods { get; } = DetectionMethod.All;
    public IReadOnlyList<string> RegistryHives { get; } = RegistryHiveNames.All;

    public string SelectedDetectionMethod
    {
        get => _selectedDetectionMethod;
        set
        {
            if (!Set(ref _selectedDetectionMethod, value)) return;
            if (IsMsiDetection && string.IsNullOrWhiteSpace(_detectionProductCode))
            {
                // Pull the product code straight off the MSI staged in the package.
                _detectionProductCode = FindMsiProductCode();
                OnPropertyChanged(nameof(DetectionProductCode));
            }
            OnPropertyChanged(nameof(IsFileDetection));
            OnPropertyChanged(nameof(IsFileVersionDetection));
            OnPropertyChanged(nameof(IsRegistryDetection));
            OnPropertyChanged(nameof(IsMsiDetection));
            OnPropertyChanged(nameof(HasNoMsiProductCode));
            RefreshDetectionSummary();
        }
    }

    public bool IsFileDetection => _selectedDetectionMethod is DetectionMethod.FileExists or DetectionMethod.FileVersion;
    public bool IsFileVersionDetection => _selectedDetectionMethod == DetectionMethod.FileVersion;
    public bool IsRegistryDetection => _selectedDetectionMethod == DetectionMethod.RegistryKey;
    public bool IsMsiDetection => _selectedDetectionMethod == DetectionMethod.MsiProductCode;

    /// <summary>True when MSI detection is selected but no product code could be read from the package.</summary>
    public bool HasNoMsiProductCode => IsMsiDetection && string.IsNullOrWhiteSpace(_detectionProductCode);

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

    public string SelectedRegistryHive
    {
        get => _selectedRegistryHive;
        set { if (Set(ref _selectedRegistryHive, value)) RefreshDetectionSummary(); }
    }

    public string RegistryKeyPath
    {
        get => _registryKeyPath;
        set { if (Set(ref _registryKeyPath, value)) RefreshDetectionSummary(); }
    }

    public string RegistryValueName
    {
        get => _registryValueName;
        set { if (Set(ref _registryValueName, value)) RefreshDetectionSummary(); }
    }

    public string DetectionProductCode
    {
        get => _detectionProductCode;
        set
        {
            if (!Set(ref _detectionProductCode, value)) return;
            OnPropertyChanged(nameof(HasNoMsiProductCode));
            RefreshDetectionSummary();
        }
    }

    // ── Requirements & return codes (seeded from Settings ▸ Intune Defaults) ──
    public IReadOnlyList<string> OperatingSystems { get; } = RequirementInfo.SupportedOperatingSystems;

    public string SelectedOperatingSystem { get => _selectedOperatingSystem; set => Set(ref _selectedOperatingSystem, value); }
    public string MinFreeDiskSpaceMB { get => _minFreeDiskSpaceMB; set => Set(ref _minFreeDiskSpaceMB, value); }
    public string MinMemoryMB { get => _minMemoryMB; set => Set(ref _minMemoryMB, value); }
    public string MinProcessors { get => _minProcessors; set => Set(ref _minProcessors, value); }
    public string MinCpuSpeedMHz { get => _minCpuSpeedMHz; set => Set(ref _minCpuSpeedMHz, value); }
    public string NewReturnCodeInput { get => _newReturnCodeInput; set => Set(ref _newReturnCodeInput, value); }

    public ObservableCollection<ReturnCodeRow> ReturnCodes { get; } = new();

    public RelayCommand AddReturnCodeCommand { get; }
    public RelayCommand RestoreDefaultsCommand { get; }

    /// <summary>Re-seeds the requirement fields and return codes from the saved Intune defaults.</summary>
    private void ApplyIntuneDefaults()
    {
        var defaults = _settingsService.Settings.IntuneDefaults;
        var req = defaults.Requirements;
        SelectedOperatingSystem = req.MinimumOperatingSystem;
        MinFreeDiskSpaceMB = req.MinimumFreeDiskSpaceMB?.ToString() ?? "";
        MinMemoryMB = req.MinimumMemoryMB?.ToString() ?? "";
        MinProcessors = req.MinimumNumberOfProcessors?.ToString() ?? "";
        MinCpuSpeedMHz = req.MinimumCpuSpeedMHz?.ToString() ?? "";

        ReturnCodes.Clear();
        foreach (var c in defaults.ReturnCodes)
            ReturnCodes.Add(new ReturnCodeRow(c.Code, c.Type, c.Description, r => ReturnCodes.Remove(r)));
    }

    private void AddReturnCode()
    {
        if (!int.TryParse(NewReturnCodeInput.Trim(), out var code)) return;
        if (ReturnCodes.Any(r => r.Code == code.ToString())) return;
        ReturnCodes.Add(new ReturnCodeRow(code, ReturnCodeType.Success, "", r => ReturnCodes.Remove(r)));
        NewReturnCodeInput = "";
    }

    // ── Deploy mode ────────────────────────────────────────────────────
    /// <summary>PSADT's own default; leaving it selected appends no -DeployMode switch.</summary>
    public const string DeployModeDefault = "Auto";

    public List<string> DeployModes { get; } = new() { "Auto", "Interactive", "NonInteractive", "Silent" };

    /// <summary>
    /// PSADT deploy mode baked into the Intune install/uninstall command lines.
    /// </summary>
    public string SelectedDeployMode
    {
        get => _selectedDeployMode;
        set
        {
            if (!Set(ref _selectedDeployMode, value)) return;
            OnPropertyChanged(nameof(DeployModeHint));
            OnPropertyChanged(nameof(InstallCommandPreview));
            OnPropertyChanged(nameof(UninstallCommandPreview));
        }
    }

    public string DeployModeHint => _selectedDeployMode switch
    {
        "Interactive" => "Always shows the PSADT dialogs.",
        "NonInteractive" => "Shows dialogs but never waits for the user.",
        "Silent" => "No dialogs at all.",
        _ => "PSADT decides: dialogs when a user is logged on, silent otherwise.",
    };

    public string InstallCommandPreview => WithDeployMode(_settingsService.Settings.IntuneDefaults.InstallCommand);
    public string UninstallCommandPreview => WithDeployMode(_settingsService.Settings.IntuneDefaults.UninstallCommand);

    /// <summary>
    /// Appends the chosen -DeployMode to a command line. Auto is PSADT's own default so
    /// it is left off, and a command that already sets the switch is used as written.
    /// </summary>
    private string WithDeployMode(string command)
    {
        command = (command ?? "").Trim();
        if (_selectedDeployMode == DeployModeDefault) return command;
        if (command.Contains("-DeployMode", StringComparison.OrdinalIgnoreCase)) return command;
        return $"{command} -DeployMode {_selectedDeployMode}";
    }

    // ── Assignment groups ──────────────────────────────────────────────
    /// <summary>Seeded from Settings ▸ Group Assignment, then editable for this package.</summary>
    public GroupPickerViewModel GroupPicker { get; } = new();

    // ── Review ─────────────────────────────────────────────────────────
    public string ReviewName { get => _reviewName; set => Set(ref _reviewName, value); }
    public string ReviewVendor { get => _reviewVendor; set => Set(ref _reviewVendor, value); }
    public string ReviewVersion { get => _reviewVersion; set => Set(ref _reviewVersion, value); }
    public string ReviewAuthor { get => _reviewAuthor; set => Set(ref _reviewAuthor, value); }
    public string ReviewArchitecture { get => _reviewArchitecture; set => Set(ref _reviewArchitecture, value); }
    public string ReviewContext { get => _reviewContext; set => Set(ref _reviewContext, value); }
    public string ReviewPackageType { get => _reviewPackageType; set => Set(ref _reviewPackageType, value); }
    public string ReviewSize { get => _reviewSize; set => Set(ref _reviewSize, value); }

    private string _intuneDisplayName = "";
    /// <summary>Title the app gets in Intune; seeded from the package metadata and editable before upload.</summary>
    public string IntuneDisplayName { get => _intuneDisplayName; set => Set(ref _intuneDisplayName, value); }

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

        var isNewPackage = _defaultsAppliedFor != _create.CurrentPackagePath;
        if (isNewPackage)
        {
            ApplyIntuneDefaults();
            SelectedDeployMode = DeployModeDefault;
            _defaultsAppliedFor = _create.CurrentPackagePath;
            _ = GroupPicker.SeedFromSettingsAsync(_settingsService.Settings.GroupAssignment);
        }

        var appInfo = _create.BuildApplicationInfo();
        if (isNewPackage)
            IntuneDisplayName = GroupAssignmentNamer.Build(
                _settingsService.Settings.IntuneDefaults.DisplayNameTemplate,
                appInfo.Manufacturer, appInfo.Name, appInfo.Version);
        AppSummaryName = $"{appInfo.Manufacturer} {appInfo.Name}".Trim();
        AppSummaryDetail = $"v{appInfo.Version} · {appInfo.InstallContext} context · Win32";

        // Seed the editable detection fields from the auto-detected rule. Only for a new
        // package - re-seeding would discard edits when stepping back from Review.
        if (isNewPackage)
        {
            var autoRule = BuildDetectionRules(_create.CurrentPackagePath, appInfo)[0];
            _detectionPath = string.IsNullOrEmpty(autoRule.Path) ? "%ProgramFiles%" : autoRule.Path;
            _detectionName = string.IsNullOrEmpty(autoRule.FileOrFolderName) ? $"{appInfo.Name}.exe" : autoRule.FileOrFolderName;
            _detectionValue = string.IsNullOrEmpty(autoRule.DetectionValue) ? appInfo.Version : autoRule.DetectionValue;
            OnPropertyChanged(nameof(DetectionPath));
            OnPropertyChanged(nameof(DetectionName));
            OnPropertyChanged(nameof(DetectionValue));
        }
      
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
        OnPropertyChanged(nameof(InstallCommandPreview));
        OnPropertyChanged(nameof(UninstallCommandPreview));
    }

    /// <summary>Refreshes what the Review step shows without touching the edited fields.</summary>
    public void RefreshReview()
    {
        OnPropertyChanged(nameof(IsSignedIn));
        OnPropertyChanged(nameof(IsNotSignedIn));
        OnPropertyChanged(nameof(SignedInUser));
        OnPropertyChanged(nameof(InstallCommandPreview));
        OnPropertyChanged(nameof(UninstallCommandPreview));
        RefreshDetectionSummary();
    }

    private void RefreshDetectionSummary()
    {
        if (string.IsNullOrEmpty(_create.CurrentPackagePath)) return;
        var appInfo = _create.BuildApplicationInfo();
        DetectionSummary = DescribeDetection(BuildSelectedDetectionRules(_create.CurrentPackagePath, appInfo));
    }

    private List<DetectionRule> BuildSelectedDetectionRules(string packagePath, ApplicationInfo appInfo)
        => SelectedDetectionMethod switch
        {
            DetectionMethod.FileExists => new List<DetectionRule>
            {
                new() { Type = DetectionRuleType.File, Path = DetectionPath, FileOrFolderName = DetectionName,
                        DetectionType = "exists", Check32BitOn64System = true }
            },
            DetectionMethod.FileVersion => new List<DetectionRule>
            {
                new() { Type = DetectionRuleType.File, Path = DetectionPath, FileOrFolderName = DetectionName,
                        DetectionType = "version", CheckVersion = true, Operator = "greaterThanOrEqual",
                        DetectionValue = DetectionValue, Check32BitOn64System = true }
            },
            DetectionMethod.RegistryKey => new List<DetectionRule>
            {
                new() { Type = DetectionRuleType.Registry,
                        Path = RegistryHiveNames.Combine(SelectedRegistryHive, RegistryKeyPath),
                        FileOrFolderName = RegistryValueName, DetectionType = "exists" }
            },
            DetectionMethod.MsiProductCode => new List<DetectionRule>
            {
                new() { Type = DetectionRuleType.MSI, Path = DetectionProductCode }
            },
            _ => BuildDetectionRules(packagePath, appInfo)
        };

    /// <summary>Reads the product code from the first MSI staged in the generated package, if there is one.</summary>
    private string FindMsiProductCode()
    {
        if (_create.CurrentMsiInfo?.IsValid == true)
            return _create.CurrentMsiInfo.ProductCode;

        try
        {
            var filesFolder = Path.Combine(_create.CurrentPackagePath, "Application", "Files");
            if (!Directory.Exists(filesFolder)) return "";
            var msi = Directory.GetFiles(filesFolder, "*.msi", SearchOption.TopDirectoryOnly).FirstOrDefault();
            return msi == null ? "" : MsiInfoService.ExtractMsiInfo(msi).ProductCode;
        }
        catch { return ""; }
    }

    private RequirementInfo BuildRequirements()
    {
        var req = new RequirementInfo { MinimumOperatingSystem = SelectedOperatingSystem };
        if (int.TryParse(MinFreeDiskSpaceMB, out var disk) && disk > 0) req.MinimumFreeDiskSpaceMB = disk;
        if (int.TryParse(MinMemoryMB, out var mem) && mem > 0) req.MinimumMemoryMB = mem;
        if (int.TryParse(MinProcessors, out var cpus) && cpus > 0) req.MinimumNumberOfProcessors = cpus;
        if (int.TryParse(MinCpuSpeedMHz, out var mhz) && mhz > 0) req.MinimumCpuSpeedMHz = mhz;
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
        appInfo.DisplayName = IntuneDisplayName;

        // The picker owns the named groups now, so only the per-package option is left
        // to the settings-driven assignment path.
        var groupAssignment = new AppSettings.GroupAssignmentConfig
        {
            CreateGroupPerPackage = settings.GroupAssignment.CreateGroupPerPackage,
            GroupNameTemplate = settings.GroupAssignment.GroupNameTemplate,
            NewGroupIntent = settings.GroupAssignment.NewGroupIntent,
        };

        var assignedGroups = GroupPicker.AssignableGroups;
        var detectionRules = BuildSelectedDetectionRules(packagePath, appInfo);
        var requirements = BuildRequirements();
        var returnCodes = ReturnCodes.Select(r => r.ToInfo()).OfType<ReturnCodeInfo>().ToList();

        NativeCodeSigner? signer = null;
        if (settings.CodeSigning.Enabled)
            signer = new NativeCodeSigner(settings.CodeSigning.CertificateThumbprint, settings.CodeSigning.TimestampServer);

        PublishSteps[2] = new PublishStepViewModel(3, $"Uploading to {TenantName} tenant");
        foreach (var s in PublishSteps) s.State = "pending";
        PublishSteps[0].State = "working";

        PublishTitle = $"Publishing {appInfo.Manufacturer} {appInfo.Name}…".Trim();
        ResultText = "";
        _succeeded = false;
        IsComplete = false;
        IsPublishing = true;
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
                InstallCommandPreview,
                UninstallCommandPreview,
                appInfo.DisplayName,
                appInfo.InstallContext,
                string.IsNullOrEmpty(_create.ExtractedIconPath) ? null : _create.ExtractedIconPath,
                progress,
                string.IsNullOrEmpty(_create.PredecessorAppId) ? null : _create.PredecessorAppId,
                groupAssignment,
                requirements,
                returnCodes,
                settings.IntuneDefaults.PrivacyUrl,
                settings.IntuneDefaults.InformationUrl,
                assignedGroups));

            ProgressValue = 100;
            foreach (var s in PublishSteps) s.State = "done";
            ResultText = assignedGroups.Count > 0
                ? $"Published and assigned to {assignedGroups.Count} group(s). App ID {appId}"
                : $"Published successfully. App ID {appId}";
            StatusText = $"Uploaded to Intune · App ID {appId}";
            _succeeded = true;
            IsComplete = true;
        }
        catch (Exception ex)
        {
            var working = PublishSteps.FirstOrDefault(s => s.State == "working");
            if (working != null) working.State = "error";
            ResultText = $"Upload failed: {ex.Message}";
            StatusText = $"Upload failed: {ex.Message}";
            _succeeded = false;
            IsComplete = true;
        }
        finally
        {
            IsUploading = false;
        }
    }

    /// <summary>Maps the upload service's 0–100 progress onto the five overlay steps.</summary>
    private void OnUploadProgress(int pct)
    {
        // 0 Building · 1 Signing · 2 Uploading · 3 Creating Win32 app · 4 Assigning
        int active =
            pct < 15 ? 0 :
            pct < 25 ? 1 :
            pct < 90 ? 2 :
            pct < 98 ? 3 : 4;

        for (int i = 0; i < PublishSteps.Count; i++)
        {
            if (i < active) { if (PublishSteps[i].State != "done") PublishSteps[i].State = "done"; }
            else if (i == active) { if (PublishSteps[i].State == "pending") PublishSteps[i].State = "working"; }
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
                _vm.OnUploadProgress(percentage);
            });
        }
    }
}
