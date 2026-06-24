using Packman.Models;
using Packman.Services;
using System.Collections.ObjectModel;
using System.Diagnostics;

namespace Packman.ViewModels;

/// <summary>
/// Backs the Application detail screen: loads full metadata, assignments, detection
/// rules and the install-status rollup for a single Intune app.
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
        };
        BuildActivity();
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

    // ── Tabs ──
    private string _tab = "overview";
    public string Tab
    {
        get => _tab;
        set { if (Set(ref _tab, value)) RaiseTabFlags(); }
    }
    public bool IsOverview => _tab == "overview";
    public bool IsAssignments => _tab == "assignments";
    public bool IsDetection => _tab == "detection";
    public bool IsActivity => _tab == "activity";

    private void RaiseTabFlags()
    {
        OnPropertyChanged(nameof(IsOverview));
        OnPropertyChanged(nameof(IsAssignments));
        OnPropertyChanged(nameof(IsDetection));
        OnPropertyChanged(nameof(IsActivity));
    }

    public bool HasInstall => !string.IsNullOrWhiteSpace(Detail.InstallCommand);
    public bool HasUninstall => !string.IsNullOrWhiteSpace(Detail.UninstallCommand);

    public ObservableCollection<AssignedGroup> RequiredAssignments { get; } = new();
    public ObservableCollection<AssignedGroup> AvailableAssignments { get; } = new();
    public ObservableCollection<AssignedGroup> UninstallAssignments { get; } = new();
    public ObservableCollection<DetectionRuleDisplay> DetectionDisplays { get; } = new();
    public bool HasDetectionRules => Detail.DetectionRules.Count > 0;
    public bool HasAssignments => Detail.AssignedGroups.Count > 0;

    public ObservableCollection<ActivityEntry> Activity { get; } = new();

    // ── Deployment status (fixed 252px track to avoid binding GridLengths) ──
    private const double BarWidth = 252;
    public bool HasSummary => Detail.Statistics is { TotalDevices: > 0 };
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
            RebuildAssignments();
            RebuildDetection();
            BuildActivity();
            RaiseDerived();
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
        OnPropertyChanged(nameof(HasSummary));
        OnPropertyChanged(nameof(SumInstalled));
        OnPropertyChanged(nameof(SumPending));
        OnPropertyChanged(nameof(SumFailed));
        OnPropertyChanged(nameof(SumNotInstalled));
        OnPropertyChanged(nameof(SumNotApplicable));
        OnPropertyChanged(nameof(BarInstalled));
        OnPropertyChanged(nameof(BarPending));
        OnPropertyChanged(nameof(BarFailed));
    }

    private void RebuildAssignments()
    {
        RequiredAssignments.Clear();
        AvailableAssignments.Clear();
        UninstallAssignments.Clear();
        foreach (var a in Detail.AssignedGroups)
        {
            switch (a.AssignmentType?.ToLowerInvariant())
            {
                case "required": RequiredAssignments.Add(a); break;
                case "available": case "availablewithoutenrollment": AvailableAssignments.Add(a); break;
                case "uninstall": UninstallAssignments.Add(a); break;
            }
        }
    }

    private void RebuildDetection()
    {
        DetectionDisplays.Clear();
        foreach (var r in Detail.DetectionRules)
            DetectionDisplays.Add(DetectionRuleDisplay.From(r));
    }

    private void BuildActivity()
    {
        Activity.Clear();
        if (string.Equals(Detail.PublishingState, "published", StringComparison.OrdinalIgnoreCase))
            Activity.Add(new ActivityEntry("Published to Intune", Detail.UpdatedText, "ok"));
        if (Detail.AssignedGroups.Count > 0)
            Activity.Add(new ActivityEntry($"Assigned to {Detail.AssignedGroups.Count} group{(Detail.AssignedGroups.Count > 1 ? "s" : "")}", Detail.UpdatedText, "ok"));
        Activity.Add(new ActivityEntry("Last updated", Detail.LastModifiedFormatted, "mut"));
        Activity.Add(new ActivityEntry("Package created", Detail.CreatedFormatted, "mut"));
    }
}

public sealed class ActivityEntry
{
    public ActivityEntry(string title, string when, string kind)
    {
        Title = title;
        When = when;
        Kind = kind;
    }
    public string Title { get; }
    public string When { get; }
    public string Kind { get; }   // ok | mut
    public bool IsOk => Kind == "ok";
}

/// <summary>A single detection rule rendered as the design's labelled key/value card.</summary>
public sealed class DetectionRuleDisplay
{
    public string RuleTypeLabel { get; private init; } = "";
    public List<DetectionField> Fields { get; } = new();

    public static DetectionRuleDisplay From(Packman.Models.DetectionRule r)
    {
        var d = new DetectionRuleDisplay { RuleTypeLabel = TypeLabel(r.Type) };
        d.Fields.Add(new DetectionField("Rule type", d.RuleTypeLabel));

        switch (r.Type)
        {
            case Packman.Models.DetectionRuleType.MSI:
                d.Fields.Add(new DetectionField("Product code", Dash(r.Path)));
                d.Fields.Add(new DetectionField("Version check",
                    r.CheckVersion ? $"{OperatorWords(r.Operator)} {r.FileOrFolderName}" : "Not checked"));
                d.Fields.Add(new DetectionField("Operator", r.CheckVersion ? OperatorSymbol(r.Operator) : "—"));
                break;
            case Packman.Models.DetectionRuleType.File:
                d.Fields.Add(new DetectionField("Path", Dash(r.Path)));
                d.Fields.Add(new DetectionField("File or folder", Dash(r.FileOrFolderName)));
                d.Fields.Add(new DetectionField("Detection", DetectionSummary(r)));
                break;
            case Packman.Models.DetectionRuleType.Registry:
                d.Fields.Add(new DetectionField("Key path", Dash(r.Path)));
                d.Fields.Add(new DetectionField("Value name", Dash(r.FileOrFolderName)));
                d.Fields.Add(new DetectionField("Detection", DetectionSummary(r)));
                break;
            case Packman.Models.DetectionRuleType.Script:
                d.Fields.Add(new DetectionField("Method", "PowerShell detection script"));
                break;
        }
        return d;
    }

    private static string TypeLabel(Packman.Models.DetectionRuleType t) => t switch
    {
        Packman.Models.DetectionRuleType.MSI => "MSI",
        Packman.Models.DetectionRuleType.File => "File",
        Packman.Models.DetectionRuleType.Registry => "Registry",
        Packman.Models.DetectionRuleType.Script => "PowerShell",
        _ => t.ToString(),
    };

    private static string DetectionSummary(Packman.Models.DetectionRule r) => r.DetectionType switch
    {
        "exists" => "Exists",
        "doesNotExist" => "Does not exist",
        "version" => $"Version {OperatorSymbol(r.Operator)} {r.DetectionValue}",
        "string" => $"String {OperatorSymbol(r.Operator)} \"{r.DetectionValue}\"",
        "integer" => $"Integer {OperatorSymbol(r.Operator)} {r.DetectionValue}",
        "sizeInMB" => $"Size {OperatorSymbol(r.Operator)} {r.DetectionValue} MB",
        "modifiedDate" => $"Modified {OperatorSymbol(r.Operator)} {r.DetectionValue}",
        _ => string.IsNullOrEmpty(r.DetectionType) ? "Exists" : r.DetectionType,
    };

    private static string OperatorWords(string op) => op switch
    {
        "greaterThanOrEqual" => "Greater than or equal to",
        "greaterThan" => "Greater than",
        "equal" => "Equal to",
        "notEqual" => "Not equal to",
        "lessThan" => "Less than",
        "lessThanOrEqual" => "Less than or equal to",
        _ => string.IsNullOrEmpty(op) ? "Equal to" : op,
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

public sealed class DetectionField
{
    public DetectionField(string label, string value) { Label = label; Value = value; }
    public string Label { get; }
    public string Value { get; }
}
