using Packman.Helpers;
using Packman.Models;
using Packman.Services;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;

namespace Packman.ViewModels;

/// <summary>
/// Backs the Application detail screen: loads full metadata, assignments, detection
/// rules and the install-status rollup for a single Intune app, and locates the
/// package's source folder on the configured network share.
/// </summary>
public sealed class ApplicationDetailViewModel : ObservableObject
{
    private readonly IntuneService _apps = AppServices.Apps;

    public ApplicationDetailViewModel(IntuneApplication app)
    {
        // Seed from the list item so the header renders immediately; LoadAsync fills the rest.
        Detail = new ApplicationDetail
        {
            Id = app.Id,
            DisplayName = app.DisplayName,
            Version = app.Version,
            Publisher = app.Publisher,
            Category = app.Category,
            LastModified = app.LastModified,
            LastModifiedDateTime = app.LastModified,
            PublishingState = app.PublishingState,
        };
    }

    private ApplicationDetail _detail = null!;
    public ApplicationDetail Detail
    {
        get => _detail;
        private set
        {
            _detail = value;
            OnPropertyChanged(nameof(Detail));
            OnPropertyChanged(nameof(HasInstall));
            OnPropertyChanged(nameof(HasUninstall));
        }
    }

    // ── Tabs: Overview / Package / Deployment ──
    private string _tab = "overview";
    public string Tab
    {
        get => _tab;
        set
        {
            if (!Set(ref _tab, value)) return;
            OnPropertyChanged(nameof(IsOverview));
            OnPropertyChanged(nameof(IsPackage));
            OnPropertyChanged(nameof(IsDeployment));
        }
    }
    public bool IsOverview => _tab == "overview";
    public bool IsPackage => _tab == "package";
    public bool IsDeployment => _tab == "deployment";

    public bool HasInstall => !string.IsNullOrWhiteSpace(Detail.InstallCommand);
    public bool HasUninstall => !string.IsNullOrWhiteSpace(Detail.UninstallCommand);

    public ObservableCollection<DetectionRuleDisplay> DetectionDisplays { get; } = new();
    public bool HasDetectionRules => Detail.DetectionRules.Count > 0;
    public bool HasAssignments => Detail.AssignedGroups.Count > 0;
    public string DetectionRulesHint => Detail.DetectionRules.Count switch
    {
        0 => "No rules",
        1 => "1 rule",
        var n => $"{n} rules · all must match",
    };

    // ── Package source (network share) ──
    private string? _sourcePath;
    public string? SourcePath
    {
        get => _sourcePath;
        private set
        {
            if (!Set(ref _sourcePath, value)) return;
            OnPropertyChanged(nameof(HasSource));
            OnPropertyChanged(nameof(SourcePathDisplay));
            OnPropertyChanged(nameof(SourceHintText));
            OnPropertyChanged(nameof(SourceHintOk));
        }
    }
    public bool HasSource => !string.IsNullOrEmpty(_sourcePath);
    public string SourcePathDisplay => _sourcePath ?? "Package not found on the configured share";
    public string SourceHintText => HasSource
        ? "Package path validated — ready for updates"
        : "No matching folder on the share — check the Intune Applications path in Settings";
    public bool SourceHintOk => HasSource;

    /// <summary>Full path of the PSADT script inside the source package, when found.</summary>
    public string? SourceScriptPath { get; private set; }

    public ObservableCollection<SourceCheck> SourceChecks { get; } = new();

    // ── Deployment status (fixed 252px track to avoid binding GridLengths) ──
    private const double BarWidth = 252;
    public bool HasSummary => Detail.Statistics is { TotalDevices: > 0 };
    public string TargetedDevicesText => (Detail.Statistics?.TotalDevices ?? 0).ToString("N0");
    public int SumInstalled => Detail.Statistics?.SuccessfulInstalls ?? 0;
    public int SumPending => Detail.Statistics?.PendingInstalls ?? 0;
    public int SumFailed => Detail.Statistics?.FailedInstalls ?? 0;
    public int SumNotInstalled => Detail.Statistics?.NotInstalled ?? 0;
    public int SumNotApplicable => Detail.Statistics?.NotApplicable ?? 0;
    public double BarInstalled => Frac(SumInstalled);
    public double BarPending => Frac(SumPending);
    public double BarFailed => Frac(SumFailed);
    private double Frac(int count)
    {
        var total = Detail.Statistics?.TotalDevices ?? 0;
        return total > 0 ? BarWidth * count / total : 0;
    }

    private bool _isLoading;
    public bool IsLoading { get => _isLoading; private set => Set(ref _isLoading, value); }

    private string _statusText = "";
    public string StatusText { get => _statusText; private set => Set(ref _statusText, value); }

    public async Task LoadAsync()
    {
        IsLoading = true;
        StatusText = "";
        try
        {
            Detail = await _apps.GetApplicationDetailAsync(Detail.Id);
            RebuildDetection();
            RaiseDerived();
            await LocateSourceAsync();
        }
        catch (Exception ex)
        {
            StatusText = $"Could not load full details: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    public async Task<bool> DeleteAsync()
    {
        try
        {
            await _apps.DeleteApplicationAsync(Detail.Id);
            return true;
        }
        catch (Exception ex)
        {
            StatusText = $"Could not retire app: {ex.Message}";
            return false;
        }
    }

    public void OpenInIntune()
    {
        var url = $"https://intune.microsoft.com/#view/Microsoft_Intune_Apps/SettingsMenu/~/0/appId/{Detail.Id}";
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) { StatusText = $"Could not open browser: {ex.Message}"; }
    }

    private void RaiseDerived()
    {
        OnPropertyChanged(nameof(HasDetectionRules));
        OnPropertyChanged(nameof(HasAssignments));
        OnPropertyChanged(nameof(DetectionRulesHint));
        OnPropertyChanged(nameof(HasSummary));
        OnPropertyChanged(nameof(TargetedDevicesText));
        OnPropertyChanged(nameof(SumInstalled));
        OnPropertyChanged(nameof(SumPending));
        OnPropertyChanged(nameof(SumFailed));
        OnPropertyChanged(nameof(SumNotInstalled));
        OnPropertyChanged(nameof(SumNotApplicable));
        OnPropertyChanged(nameof(BarInstalled));
        OnPropertyChanged(nameof(BarPending));
        OnPropertyChanged(nameof(BarFailed));
    }

    private void RebuildDetection()
    {
        DetectionDisplays.Clear();
        foreach (var r in Detail.DetectionRules)
            DetectionDisplays.Add(DetectionRuleDisplay.From(r));
    }

    /// <summary>
    /// Finds the package's version folder on the share and runs the integrity checks.
    /// Share enumeration happens off the UI thread; failures just leave "not found".
    /// </summary>
    private async Task LocateSourceAsync()
    {
        var root = AppServices.Settings.Settings.NetworkPaths.IntuneApplications;
        var d = Detail;

        var (path, script, checks) = await Task.Run(() =>
        {
            var p = PackageSourceLocator.Locate(root, d.Publisher, d.DisplayName, d.Version);
            return p == null
                ? ((string?)null, (string?)null, new List<SourceCheck>())
                : (p, FindScript(p), BuildChecks(p, d.Size, d.SizeFormatted));
        });

        SourceScriptPath = script;
        SourceChecks.Clear();
        foreach (var c in checks) SourceChecks.Add(c);
        SourcePath = path;
    }

    private static string? FindScript(string packagePath) =>
        FolderBrowserHelper.GetPSADTScriptPath(Path.Combine(packagePath, "Application"))
        ?? FolderBrowserHelper.GetPSADTScriptPath(packagePath);

    private static List<SourceCheck> BuildChecks(string packagePath, long intuneSize, string intuneSizeText)
    {
        var checks = new List<SourceCheck>();

        var script = FindScript(packagePath);
        checks.Add(script != null
            ? new SourceCheck(Path.GetFileName(script), "deployment script present", ok: true)
            : new SourceCheck("Invoke-AppDeployToolkit.ps1", "deployment script not found", ok: false));

        var intuneDir = Path.Combine(packagePath, "Intune");
        var intunewin = Directory.Exists(intuneDir) ? Directory.GetFiles(intuneDir, "*.intunewin").FirstOrDefault() : null;
        if (intunewin == null)
        {
            checks.Add(new SourceCheck("*.intunewin", "package file not found", ok: false));
        }
        else
        {
            var size = new FileInfo(intunewin).Length;
            // The Graph size is the committed upload; allow slack for encryption overhead.
            var matches = intuneSize <= 0 || Math.Abs(size - intuneSize) <= intuneSize * 0.1;
            checks.Add(new SourceCheck(Path.GetFileName(intunewin), matches
                ? $"{FormatSize(size)} — matches the Intune upload"
                : $"{FormatSize(size)} on share vs {intuneSizeText} in Intune — re-upload?", ok: matches));
        }

        checks.Add(File.Exists(Path.Combine(intuneDir, "detection.xml"))
            ? new SourceCheck("detection.xml", "detection definition present", ok: true)
            : new SourceCheck("detection.xml", "not found", ok: false));

        var iconDir = Path.Combine(packagePath, "Icon");
        var hasIcon = Directory.Exists(iconDir) && Directory.EnumerateFiles(iconDir).Any();
        checks.Add(new SourceCheck(hasIcon ? @"Icon\" + Path.GetFileName(Directory.EnumerateFiles(iconDir).First()) : @"Icon\",
            hasIcon ? "icon present" : "no icon file", ok: hasIcon));

        return checks;
    }

    private static string FormatSize(long bytes) => bytes switch
    {
        > 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024 * 1024):F1} GB",
        > 1024L * 1024 => $"{bytes / (1024.0 * 1024):F1} MB",
        > 1024 => $"{bytes / 1024.0:F1} KB",
        _ => $"{bytes} B",
    };
}

/// <summary>One row of the Package tab's integrity checklist.</summary>
public sealed class SourceCheck
{
    public SourceCheck(string file, string note, bool ok)
    {
        File = file;
        Note = note;
        IsOk = ok;
    }
    public string File { get; }
    public string Note { get; }
    public bool IsOk { get; }
}

/// <summary>A detection rule rendered as the design's "type tag + summary" row.</summary>
public sealed class DetectionRuleDisplay
{
    public DetectionRule Rule { get; private init; } = null!;
    public string TypeTag { get; private init; } = "";
    public string Summary { get; private init; } = "";

    public static DetectionRuleDisplay From(DetectionRule r) => new()
    {
        Rule = r,
        TypeTag = r.Type switch
        {
            DetectionRuleType.MSI => "MSI",
            DetectionRuleType.File => "FILE",
            DetectionRuleType.Registry => "REG",
            DetectionRuleType.Script => "PS1",
            _ => r.Type.ToString().ToUpperInvariant(),
        },
        Summary = r.Type switch
        {
            DetectionRuleType.MSI => r.CheckVersion
                ? $"{Dash(r.Path)} · version {OperatorSymbol(r.Operator)} {r.FileOrFolderName}"
                : $"{Dash(r.Path)} · product code present",
            DetectionRuleType.File => $@"{Dash(r.Path)}\{r.FileOrFolderName} · {DetectionSummary(r)}",
            DetectionRuleType.Registry => $"{Dash(r.Path)} · {Dash(r.FileOrFolderName)} · {DetectionSummary(r)}",
            DetectionRuleType.Script => "PowerShell detection script",
            _ => "",
        },
    };

    private static string DetectionSummary(DetectionRule r) => r.DetectionType switch
    {
        "exists" => "exists",
        "doesNotExist" => "does not exist",
        "version" => $"version {OperatorSymbol(r.Operator)} {r.DetectionValue}",
        "string" => $"string {OperatorSymbol(r.Operator)} \"{r.DetectionValue}\"",
        "integer" => $"integer {OperatorSymbol(r.Operator)} {r.DetectionValue}",
        "sizeInMB" => $"size {OperatorSymbol(r.Operator)} {r.DetectionValue} MB",
        "modifiedDate" => $"modified {OperatorSymbol(r.Operator)} {r.DetectionValue}",
        _ => string.IsNullOrEmpty(r.DetectionType) ? "exists" : r.DetectionType,
    };

    private static string OperatorSymbol(string op) => op switch
    {
        "greaterThanOrEqual" => ">=",
        "greaterThan" => ">",
        "equal" => "=",
        "notEqual" => "!=",
        "lessThan" => "<",
        "lessThanOrEqual" => "<=",
        _ => string.IsNullOrEmpty(op) ? "=" : op,
    };

    private static string Dash(string s) => string.IsNullOrWhiteSpace(s) ? "—" : s;
}
