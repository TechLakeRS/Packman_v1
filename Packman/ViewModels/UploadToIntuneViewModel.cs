using Packman.Helpers;
using Packman.Models;
using Packman.Services;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;

namespace Packman.ViewModels;

/// <summary>
/// Drives the standalone "Upload to Intune" page: pick a built PSADT package folder,
/// review/edit its detection rules, choose Entra assignment groups, then publish to
/// Intune while a four-step overlay tracks progress.
/// </summary>
public sealed class UploadToIntuneViewModel : ObservableObject
{
    private readonly SettingsService _settings = AppServices.Settings;
    private readonly IntuneAuthService _auth = AppServices.Auth;
    private readonly IntuneService _apps = AppServices.Apps;

    public UploadToIntuneViewModel()
    {
        AddRuleCommand = new RelayCommand(AddDetectionRule, () => CanAddRule);
        RemoveRuleCommand = new RelayCommand<DetectionRule>(r => { if (r != null) DetectionRules.Remove(r); });
        SearchGroupsCommand = new RelayCommand(async () => await SearchGroupsAsync(), () => !IsSearchingGroups);
        AddGroupCommand = new RelayCommand<EntraGroup>(AddGroup);
        RemoveGroupCommand = new RelayCommand<AssignedGroup>(g => { if (g != null) SelectedGroups.Remove(g); });
        UploadCommand = new RelayCommand(async () => await UploadAsync(), () => UploadEnabled);
        DoneCommand = new RelayCommand(ResetAfterPublish);

        PublishSteps = new ObservableCollection<PublishStepViewModel>
        {
            new(1, "Validating .intunewin package"),
            new(2, "Uploading to tenant"),
            new(3, "Creating Win32 app"),
            new(4, "Assigning to groups"),
        };
    }

    // ── Sign-in state ───────────────────────────────────
    public bool IsSignedIn => _auth.IsSignedIn;
    public bool IsNotSignedIn => !_auth.IsSignedIn;
    public string SignedInUser => _auth.SignedInUser ?? "";

    /// <summary>Tenant label derived from the signed-in UPN domain (e.g. "contoso.com").</summary>
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

    /// <summary>Refreshes sign-in dependent text when the page is shown.</summary>
    public void Refresh()
    {
        OnPropertyChanged(nameof(IsSignedIn));
        OnPropertyChanged(nameof(IsNotSignedIn));
        OnPropertyChanged(nameof(SignedInUser));
        OnPropertyChanged(nameof(TenantName));
        UploadCommand.RaiseCanExecuteChanged();
    }

    // ── Package selection ───────────────────────────────
    private string _packageRoot = "";
    public string PackageRoot { get => _packageRoot; private set => Set(ref _packageRoot, value); }

    private string _packageFolderName = "";
    public string PackageFolderName { get => _packageFolderName; private set => Set(ref _packageFolderName, value); }

    private bool _isValidated;
    public bool IsValidated
    {
        get => _isValidated;
        private set { if (Set(ref _isValidated, value)) UploadCommand.RaiseCanExecuteChanged(); }
    }

    private string _validationError = "";
    public string ValidationError { get => _validationError; private set => Set(ref _validationError, value); }
    public bool HasValidationError => !string.IsNullOrEmpty(ValidationError);

    private string _appName = "";
    public string AppName { get => _appName; private set => Set(ref _appName, value); }

    private string _manufacturer = "";
    public string Manufacturer { get => _manufacturer; private set => Set(ref _manufacturer, value); }

    private string _version = "";
    public string Version { get => _version; private set => Set(ref _version, value); }

    private string _installContext = "System";
    public string InstallContext { get => _installContext; private set => Set(ref _installContext, value); }

    private string _sizeText = "";
    public string SizeText { get => _sizeText; private set => Set(ref _sizeText, value); }

    public string DisplayTitle => $"{Manufacturer} {AppName}".Trim();

    /// <summary>
    /// Validates a selected folder as a PSADT v4 package (root or its Application folder)
    /// and pulls metadata, install context and an MSI detection rule from it.
    /// </summary>
    public void ProcessSelectedFolder(string selectedPath)
    {
        ValidationError = "";
        try
        {
            var root = FolderBrowserHelper.GetPackageRootPath(selectedPath);
            if (!FolderBrowserHelper.ValidatePackageStructure(root))
            {
                Fail("Invoke-AppDeployToolkit.exe not found. Select the package folder that contains the Application folder.");
                return;
            }

            var applicationFolder = Path.Combine(root, "Application");
            if (!Directory.Exists(applicationFolder))
                applicationFolder = root;

            var scriptPath = FolderBrowserHelper.GetPSADTScriptPath(applicationFolder);
            if (string.IsNullOrEmpty(scriptPath))
            {
                Fail("Invoke-AppDeployToolkit.ps1 not found in the Application folder.");
                return;
            }

            var meta = MetadataExtractor.ExtractMetadataFromScript(scriptPath);
            Manufacturer = meta.GetValueOrDefault("Vendor", "");
            AppName = meta.GetValueOrDefault("AppName", "");
            Version = meta.GetValueOrDefault("Version", "");
            InstallContext = InstallContextParser.ExtractFromPackage(root);

            PackageRoot = root;
            PackageFolderName = new DirectoryInfo(root).Name;
            SizeText = FormatSize(DirectorySize(root));

            BuildDetectionRules(root);
            IsValidated = true;

            OnPropertyChanged(nameof(DisplayTitle));
            OnPropertyChanged(nameof(HasValidationError));
        }
        catch (Exception ex)
        {
            Fail($"Could not read package: {ex.Message}");
        }
    }

    private void Fail(string message)
    {
        IsValidated = false;
        ValidationError = message;
        OnPropertyChanged(nameof(HasValidationError));
    }

    // ── Detection rules ─────────────────────────────────
    public ObservableCollection<DetectionRule> DetectionRules { get; } = new();

    private void BuildDetectionRules(string root)
    {
        DetectionRules.Clear();
        try
        {
            var filesFolder = Path.Combine(root, "Application", "Files");
            if (Directory.Exists(filesFolder))
            {
                var msiFiles = Directory.GetFiles(filesFolder, "*.msi", SearchOption.TopDirectoryOnly);
                if (msiFiles.Length > 0)
                {
                    var msi = MsiInfoService.ExtractMsiInfo(msiFiles[0]);
                    if (msi.IsValid)
                    {
                        DetectionRules.Add(new DetectionRule
                        {
                            Type = DetectionRuleType.File,
                            Path = "%ProgramFiles%",
                            FileOrFolderName = $"{AppName}.exe",
                            DetectionType = "version",
                            Operator = "greaterThanOrEqual",
                            DetectionValue = string.IsNullOrEmpty(msi.ProductVersion) ? Version : msi.ProductVersion,
                            CheckVersion = true,
                            Check32BitOn64System = true,
                        });
                    }
                }
            }
        }
        catch { /* leave list empty; user can add a rule manually */ }
    }

    public ObservableCollection<string> RuleTypes { get; } = new() { "File", "Registry", "MSI" };
    public ObservableCollection<string> Operators { get; } =
        new() { "greaterThanOrEqual", "equal", "greaterThan", "lessThan", "lessThanOrEqual" };

    private string _newRuleType = "File";
    public string NewRuleType { get => _newRuleType; set { if (Set(ref _newRuleType, value)) AddRuleCommand.RaiseCanExecuteChanged(); } }

    private string _newRulePath = "";
    public string NewRulePath { get => _newRulePath; set { if (Set(ref _newRulePath, value)) AddRuleCommand.RaiseCanExecuteChanged(); } }

    private string _newRuleName = "";
    public string NewRuleName { get => _newRuleName; set => Set(ref _newRuleName, value); }

    private bool _newRuleCheckVersion;
    public bool NewRuleCheckVersion { get => _newRuleCheckVersion; set => Set(ref _newRuleCheckVersion, value); }

    private string _newRuleOperator = "greaterThanOrEqual";
    public string NewRuleOperator { get => _newRuleOperator; set => Set(ref _newRuleOperator, value); }

    private string _newRuleValue = "";
    public string NewRuleValue { get => _newRuleValue; set => Set(ref _newRuleValue, value); }

    public bool CanAddRule => !string.IsNullOrWhiteSpace(NewRulePath);

    private void AddDetectionRule()
    {
        var type = NewRuleType switch
        {
            "Registry" => DetectionRuleType.Registry,
            "MSI" => DetectionRuleType.MSI,
            _ => DetectionRuleType.File,
        };

        DetectionRules.Add(new DetectionRule
        {
            Type = type,
            Path = NewRulePath.Trim(),
            FileOrFolderName = NewRuleName.Trim(),
            CheckVersion = NewRuleCheckVersion,
            DetectionType = NewRuleCheckVersion ? "version" : "exists",
            Operator = NewRuleCheckVersion ? NewRuleOperator : "",
            DetectionValue = NewRuleCheckVersion ? NewRuleValue.Trim() : "",
            Check32BitOn64System = true,
        });

        NewRulePath = "";
        NewRuleName = "";
        NewRuleValue = "";
        NewRuleCheckVersion = false;
    }

    // ── Assignment groups ───────────────────────────────
    public ObservableCollection<EntraGroup> GroupResults { get; } = new();
    public ObservableCollection<AssignedGroup> SelectedGroups { get; } = new();

    private string _groupQuery = "";
    public string GroupQuery { get => _groupQuery; set => Set(ref _groupQuery, value); }

    private bool _isSearchingGroups;
    public bool IsSearchingGroups
    {
        get => _isSearchingGroups;
        private set { if (Set(ref _isSearchingGroups, value)) SearchGroupsCommand.RaiseCanExecuteChanged(); }
    }

    private string _groupSearchHint = "";
    public string GroupSearchHint { get => _groupSearchHint; private set => Set(ref _groupSearchHint, value); }

    private string _intent = "required"; // required | available | uninstall
    public string Intent { get => _intent; set { if (Set(ref _intent, value)) OnPropertyChanged(nameof(IntentLabel)); } }
    public string IntentLabel => char.ToUpper(Intent[0]) + Intent[1..];

    private async Task SearchGroupsAsync()
    {
        GroupResults.Clear();
        GroupSearchHint = "";
        if (string.IsNullOrWhiteSpace(GroupQuery))
            return;

        if (!_auth.IsSignedIn)
        {
            GroupSearchHint = "Sign in to Intune on the Settings page first.";
            return;
        }

        IsSearchingGroups = true;
        try
        {
            var found = await _apps.SearchGroupsAsync(GroupQuery);
            foreach (var g in found)
                GroupResults.Add(g);
            GroupSearchHint = found.Count == 0 ? "No groups match that name." : "";
        }
        catch (Exception ex)
        {
            GroupSearchHint = $"Search failed: {ex.Message}";
        }
        finally
        {
            IsSearchingGroups = false;
        }
    }

    private void AddGroup(EntraGroup? group)
    {
        if (group == null || string.IsNullOrEmpty(group.Id)) return;
        if (SelectedGroups.Any(g => g.GroupId == group.Id)) return;

        SelectedGroups.Add(new AssignedGroup
        {
            GroupId = group.Id,
            GroupName = group.DisplayName,
            AssignmentType = Intent,
        });

        GroupResults.Clear();
        GroupQuery = "";
    }

    // ── Publishing ──────────────────────────────────────
    public ObservableCollection<PublishStepViewModel> PublishSteps { get; }

    private bool _isPublishing;
    public bool IsPublishing
    {
        get => _isPublishing;
        private set { if (Set(ref _isPublishing, value)) { OnPropertyChanged(nameof(IsNotPublishing)); OnPropertyChanged(nameof(IsRunning)); } }
    }
    public bool IsNotPublishing => !_isPublishing;

    /// <summary>True while a publish is in flight but not yet finished (drives the spinner).</summary>
    public bool IsRunning => _isPublishing && !_isComplete;

    private string _publishTitle = "";
    public string PublishTitle { get => _publishTitle; private set => Set(ref _publishTitle, value); }

    private string _resultText = "";
    public string ResultText { get => _resultText; private set => Set(ref _resultText, value); }

    private bool _isComplete;
    public bool IsComplete
    {
        get => _isComplete;
        private set { if (Set(ref _isComplete, value)) { OnPropertyChanged(nameof(IsRunning)); OnPropertyChanged(nameof(IsSucceeded)); OnPropertyChanged(nameof(IsFailed)); } }
    }

    private bool _succeeded;
    public bool IsSucceeded => _isComplete && _succeeded;
    public bool IsFailed => _isComplete && !_succeeded;

    public bool UploadEnabled =>
        IsValidated && _auth.IsSignedIn && !IsPublishing &&
        !string.IsNullOrWhiteSpace(_settings.Settings.NetworkPaths.IntuneWinAppUtil);

    private async Task UploadAsync()
    {
        if (!UploadEnabled) return;

        var settings = _settings.Settings;
        var appInfo = new ApplicationInfo
        {
            Name = AppName.Trim(),
            Manufacturer = string.IsNullOrWhiteSpace(Manufacturer) ? "Unknown" : Manufacturer.Trim(),
            Version = string.IsNullOrWhiteSpace(Version) ? "1.0.0" : Version.Trim(),
            SourcesPath = PackageRoot,
            InstallContext = InstallContext,
        };

        NativeCodeSigner? signer = null;
        if (settings.CodeSigning.Enabled)
            signer = new NativeCodeSigner(settings.CodeSigning.CertificateThumbprint, settings.CodeSigning.TimestampServer);

        PublishSteps[1] = new PublishStepViewModel(2, $"Uploading to {TenantName} tenant");
        foreach (var s in PublishSteps) s.State = "pending";

        PublishTitle = $"Publishing {DisplayTitle}…";
        ResultText = "";
        _succeeded = false;
        IsComplete = false;
        IsPublishing = true;
        UploadCommand.RaiseCanExecuteChanged();
        PublishSteps[0].State = "working";

        var progress = new StepProgress(this);

        try
        {
            using var uploadService = new IntuneUploadService(
                _auth.GetAccessTokenAsync, signer, settings.NetworkPaths.IntuneWinAppUtil);

            var appId = await Task.Run(() => uploadService.UploadWin32ApplicationAsync(
                appInfo,
                PackageRoot,
                DetectionRules.ToList(),
                "Invoke-AppDeployToolkit.exe Install",
                "Invoke-AppDeployToolkit.exe Uninstall",
                $"{appInfo.Manufacturer} {appInfo.Name} {appInfo.Version}",
                appInfo.InstallContext,
                null,
                progress,
                requirements: settings.IntuneDefaults.Requirements,
                returnCodes: settings.IntuneDefaults.ReturnCodes));

            MarkDone(0); MarkDone(1); MarkDone(2);

            PublishSteps[3].State = "working";
            if (SelectedGroups.Count > 0)
                await uploadService.AssignAppToGroupsAsync(appId, SelectedGroups.Select(g => g.GroupId), Intent);
            PublishSteps[3].State = "done";

            ResultText = SelectedGroups.Count > 0
                ? $"Published and assigned to {SelectedGroups.Count} group(s). App ID {appId}"
                : $"Published successfully. App ID {appId}";
            _succeeded = true;
            IsComplete = true;
        }
        catch (Exception ex)
        {
            var working = PublishSteps.FirstOrDefault(s => s.State == "working");
            if (working != null) working.State = "error";
            ResultText = $"Upload failed: {ex.Message}";
            _succeeded = false;
            IsComplete = true;
        }
        finally
        {
            UploadCommand.RaiseCanExecuteChanged();
        }
    }

    private void MarkDone(int index)
    {
        if (PublishSteps[index].State != "done")
            PublishSteps[index].State = "done";
    }

    /// <summary>Maps the upload service's 0–100 progress onto the first three overlay steps.</summary>
    private void OnUploadProgress(int pct)
    {
        if (pct < 30)
        {
            PublishSteps[0].State = "working";
        }
        else if (pct < 90)
        {
            MarkDone(0);
            if (PublishSteps[1].State == "pending") PublishSteps[1].State = "working";
        }
        else
        {
            MarkDone(0); MarkDone(1);
            if (PublishSteps[2].State == "pending") PublishSteps[2].State = "working";
        }
    }

    private void ResetAfterPublish()
    {
        IsPublishing = false;
        IsComplete = false;
        if (_succeeded)
        {
            // Successful publish: clear the form for the next package.
            IsValidated = false;
            PackageRoot = "";
            PackageFolderName = "";
            AppName = Manufacturer = Version = SizeText = "";
            DetectionRules.Clear();
            SelectedGroups.Clear();
            OnPropertyChanged(nameof(DisplayTitle));
        }
        UploadCommand.RaiseCanExecuteChanged();
    }

    // ── Commands ────────────────────────────────────────
    public RelayCommand AddRuleCommand { get; }
    public RelayCommand<DetectionRule> RemoveRuleCommand { get; }
    public RelayCommand SearchGroupsCommand { get; }
    public RelayCommand<EntraGroup> AddGroupCommand { get; }
    public RelayCommand<AssignedGroup> RemoveGroupCommand { get; }
    public RelayCommand UploadCommand { get; }
    public RelayCommand DoneCommand { get; }

    // ── Helpers ─────────────────────────────────────────
    private static long DirectorySize(string path)
    {
        try
        {
            return new DirectoryInfo(path)
                .EnumerateFiles("*", SearchOption.AllDirectories)
                .Sum(f => f.Length);
        }
        catch { return 0; }
    }

    private static string FormatSize(long bytes) => bytes switch
    {
        > 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024 * 1024):F1} GB",
        > 1024L * 1024 => $"{bytes / (1024.0 * 1024):F1} MB",
        > 1024 => $"{bytes / 1024.0:F1} KB",
        _ => $"{bytes} B",
    };

    private sealed class StepProgress : IUploadProgress
    {
        private readonly UploadToIntuneViewModel _vm;
        public StepProgress(UploadToIntuneViewModel vm) => _vm = vm;

        public void UpdateProgress(int percentage, string message)
            => Application.Current?.Dispatcher.Invoke(() => _vm.OnUploadProgress(percentage));
    }
}
