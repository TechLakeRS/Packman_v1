using Packman.Models;
using Packman.Services;
using System.Collections.ObjectModel;
using System.Management.Automation.Remoting;
using System.Windows;
using System.Windows.Threading;

namespace Packman.ViewModels;

public enum LineKind { Info, Good, Warn, Bad, Dim }

public sealed class RemoteTestLine
{
    public string Time { get; init; } = "";
    public string Text { get; init; } = "";
    public LineKind Kind { get; init; }
}

/// <summary>
/// Drives the Remote Test tool: stages the generated package on a test machine over
/// WinRM, runs PSADT there as SYSTEM or as the logged-on user, then discovers the
/// detection rule the install actually produced and offers it to the publish step.
/// </summary>
public sealed class RemoteTestViewModel : ObservableObject
{
    private const int MaxRecentComputers = 8;

    private readonly CreatePackageViewModel _create;
    private readonly UploadStepViewModel _upload;
    private readonly SettingsService _settingsService;

    // PSADT is chatty, so lines are buffered and flushed on a timer — appending each
    // one straight to the bound collection re-renders the console per line.
    private readonly List<RemoteTestLine> _pending = new();
    private readonly DispatcherTimer _flushTimer;

    private string _targetComputer = "";
    private bool _runAsUser;
    private bool _isRunning;
    private bool _isOnline;
    private string _statusText = "no target selected";
    private int? _copyPercent;
    private DetectionRule? _discoveredRule;
    private string _discoveredSummary = "";

    public ObservableCollection<RemoteTestLine> Lines { get; } = new();
    public ObservableCollection<string> RecentComputers { get; } = new();

    public RelayCommand InstallCommand { get; }
    public RelayCommand UninstallCommand { get; }
    public RelayCommand DetectCommand { get; }
    public RelayCommand CheckOnlineCommand { get; }
    public RelayCommand ApplyDetectionCommand { get; }
    public RelayCommand ClearLogCommand { get; }

    public RemoteTestViewModel(CreatePackageViewModel create, UploadStepViewModel upload, SettingsService settingsService)
    {
        _create = create;
        _upload = upload;
        _settingsService = settingsService;

        foreach (var name in _settingsService.Settings.RemoteTest.RecentComputers)
            RecentComputers.Add(name);
        _targetComputer = RecentComputers.FirstOrDefault() ?? "";

        _flushTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        _flushTimer.Tick += (_, _) => Flush();

        InstallCommand        = new RelayCommand(() => _ = RunAsync("Install"), CanRun);
        UninstallCommand      = new RelayCommand(() => _ = RunAsync("Uninstall"), CanRun);
        DetectCommand         = new RelayCommand(() => _ = DiscoverAsync(), () => !_isRunning && IsValidTarget);
        CheckOnlineCommand    = new RelayCommand(() => _ = CheckOnlineAsync(), () => !_isRunning && IsValidTarget);
        ApplyDetectionCommand = new RelayCommand(ApplyDetection, () => _discoveredRule != null);
        ClearLogCommand       = new RelayCommand(() => { Lines.Clear(); lock (_pending) _pending.Clear(); });

        _create.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(CreatePackageViewModel.CurrentPackagePath)) return;
            OnPropertyChanged(nameof(NeedsPackage));
            RaiseCommandStates();
        };
    }

    // ── Target ─────────────────────────────────────────────────────────
    public string TargetComputer
    {
        get => _targetComputer;
        set
        {
            if (!Set(ref _targetComputer, value)) return;
            IsOnline = false;
            StatusText = IsValidTarget ? "not checked" : "enter a computer name";
            RaiseCommandStates();
        }
    }

    public bool IsValidTarget => RemoteTestService.IsValidComputerName(_targetComputer);

    public bool IsOnline
    {
        get => _isOnline;
        private set { if (Set(ref _isOnline, value)) RaiseCommandStates(); }
    }

    public string StatusText { get => _statusText; private set => Set(ref _statusText, value); }

    // ── Run context ────────────────────────────────────────────────────
    /// <summary>False runs as NT AUTHORITY\SYSTEM, matching what Intune does.</summary>
    public bool RunAsUser
    {
        get => _runAsUser;
        set { if (Set(ref _runAsUser, value)) { OnPropertyChanged(nameof(IsSystemContext)); OnPropertyChanged(nameof(ContextHelpText)); } }
    }

    public bool IsSystemContext
    {
        get => !_runAsUser;
        set => RunAsUser = !value;
    }

    public string ContextHelpText => _runAsUser
        ? "Runs in the logged-on user's session — their profile and HKCU, PSADT dialogs visible."
        : "Runs as NT AUTHORITY\\SYSTEM via a scheduled task, the same identity the Intune Management Extension uses.";

    // ── Run state ──────────────────────────────────────────────────────
    public bool IsRunning
    {
        get => _isRunning;
        private set { if (Set(ref _isRunning, value)) { OnPropertyChanged(nameof(IsIdle)); RaiseCommandStates(); } }
    }

    public bool IsIdle => !_isRunning;

    public int? CopyPercent
    {
        get => _copyPercent;
        private set { if (Set(ref _copyPercent, value)) OnPropertyChanged(nameof(IsCopying)); }
    }

    public bool IsCopying => _copyPercent.HasValue;

    // ── Discovered detection ───────────────────────────────────────────
    public bool HasDiscoveredRule => _discoveredRule != null;
    public string DiscoveredSummary { get => _discoveredSummary; private set => Set(ref _discoveredSummary, value); }

    /// <summary>Only install and uninstall need a package; check and discover run on the target alone.</summary>
    public bool NeedsPackage => string.IsNullOrEmpty(_create.CurrentPackagePath);

    private bool CanRun() => !_isRunning && IsValidTarget && !NeedsPackage;

    private void RaiseCommandStates()
    {
        InstallCommand.RaiseCanExecuteChanged();
        UninstallCommand.RaiseCanExecuteChanged();
        DetectCommand.RaiseCanExecuteChanged();
        CheckOnlineCommand.RaiseCanExecuteChanged();
        ApplyDetectionCommand.RaiseCanExecuteChanged();
        OnPropertyChanged(nameof(IsValidTarget));
    }

    // ── Actions ────────────────────────────────────────────────────────
    private async Task CheckOnlineAsync()
    {
        StatusText = "checking…";
        bool online = await Task.Run(() =>
        {
            try
            {
                using var ping = new System.Net.NetworkInformation.Ping();
                return ping.Send(_targetComputer, 2000).Status == System.Net.NetworkInformation.IPStatus.Success;
            }
            catch { return false; }
        });

        IsOnline = online;
        StatusText = online ? "online" : "unreachable";
        if (online) RememberComputer(_targetComputer);
    }

    private async Task RunAsync(string deploymentType)
    {
        Lines.Clear();
        lock (_pending) _pending.Clear();
        _discoveredRule = null;
        DiscoveredSummary = "";
        OnPropertyChanged(nameof(HasDiscoveredRule));

        IsRunning = true;
        StatusText = $"{deploymentType.ToLowerInvariant()} running…";
        _flushTimer.Start();
        RememberComputer(_targetComputer);

        string packagePath = _create.CurrentPackagePath;
        bool runAsUser = _runAsUser;
        bool cleanup = _settingsService.Settings.RemoteTest.CleanupAfterRun;

        int exitCode = -1;
        try
        {
            exitCode = await Task.Run(() => new RemoteTestService().Deploy(
                _targetComputer, packagePath, deploymentType, cleanup, runAsUser,
                Append, percent => CopyPercent = percent));
        }
        catch (PSRemotingTransportException ex)
        {
            Append($"ERROR: WinRM connection failed: {ex.Message}");
            Append("Enable WinRM on the target (Enable-PSRemoting) and check the firewall.");
        }
        catch (Exception ex)
        {
            Append($"ERROR: {ex.Message}");
        }
        finally
        {
            CopyPercent = null;
            _flushTimer.Stop();
            Flush();
            IsRunning = false;
        }

        bool success = RemoteTestService.IsSuccess(exitCode);
        Append("========================================");
        Append(success ? $"✓ {deploymentType.ToUpperInvariant()} SUCCEEDED" : $"✗ {deploymentType.ToUpperInvariant()} FAILED");
        Append("========================================");
        Flush();
        StatusText = success ? $"{deploymentType.ToLowerInvariant()} succeeded (exit {exitCode})" : $"{deploymentType.ToLowerInvariant()} failed (exit {exitCode})";

        // Detection only means anything once the app is actually installed.
        if (success && deploymentType == "Install")
        {
            Append("Waiting for the registry to settle…");
            Flush();
            await Task.Delay(3000);
            await DiscoverAsync();
        }
    }

    private async Task DiscoverAsync()
    {
        IsRunning = true;
        StatusText = "discovering detection rule…";
        _flushTimer.Start();

        try
        {
            var appInfo = _create.BuildApplicationInfo();
            var result = await new DetectionDiscoveryService()
                .DiscoverAsync(_targetComputer, appInfo.Name, appInfo.Version, _create.SourcesPath);

            foreach (var message in result.Messages) Append(message);

            if (result.Success && result.SuggestedRules.Count > 0)
            {
                // Prefer the version rule; it survives an upgrade check the exists rule can't make.
                _discoveredRule = result.SuggestedRules.LastOrDefault(r => r.CheckVersion) ?? result.SuggestedRules[0];
                DiscoveredSummary = _discoveredRule.Title;
                Append($"[OK] Detection rule found: {_discoveredRule.Title}");
                StatusText = "detection rule found";
            }
            else
            {
                _discoveredRule = null;
                DiscoveredSummary = "";
                Append($"WARNING: {result.ErrorMessage}");
                StatusText = "no detection rule found";
            }
        }
        catch (Exception ex)
        {
            _discoveredRule = null;
            Append($"ERROR: {ex.Message}");
            StatusText = "detection failed";
        }
        finally
        {
            OnPropertyChanged(nameof(HasDiscoveredRule));
            _flushTimer.Stop();
            Flush();
            IsRunning = false;
        }
    }

    /// <summary>Pushes the discovered rule into the publish step's detection fields.</summary>
    private void ApplyDetection()
    {
        if (_discoveredRule == null) return;

        _upload.SelectedDetectionMethod = _discoveredRule.CheckVersion ? "File version" : "File exists";
        _upload.DetectionPath = _discoveredRule.Path;
        _upload.DetectionName = _discoveredRule.FileOrFolderName;
        _upload.DetectionValue = _discoveredRule.DetectionValue;

        Append($"[OK] Applied to the publish step: {_discoveredRule.Title}");
        Flush();
        StatusText = "detection rule applied to publish";
    }

    // ── Recent computers ───────────────────────────────────────────────
    private void RememberComputer(string name)
    {
        if (!RemoteTestService.IsValidComputerName(name)) return;

        var existing = RecentComputers.FirstOrDefault(c => c.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (existing != null) RecentComputers.Remove(existing);
        RecentComputers.Insert(0, name);
        while (RecentComputers.Count > MaxRecentComputers) RecentComputers.RemoveAt(RecentComputers.Count - 1);

        _settingsService.Settings.RemoteTest.RecentComputers = RecentComputers.ToList();
        _settingsService.Save();
    }

    // ── Console ────────────────────────────────────────────────────────
    private void Append(string text)
    {
        var line = new RemoteTestLine { Time = DateTime.Now.ToString("HH:mm:ss"), Text = text, Kind = Classify(text) };
        lock (_pending) _pending.Add(line);
    }

    private void Flush()
    {
        RemoteTestLine[] batch;
        lock (_pending)
        {
            if (_pending.Count == 0) return;
            batch = _pending.ToArray();
            _pending.Clear();
        }

        if (Application.Current?.Dispatcher is { } dispatcher && !dispatcher.CheckAccess())
            dispatcher.Invoke(() => { foreach (var line in batch) Lines.Add(line); });
        else
            foreach (var line in batch) Lines.Add(line);
    }

    private static LineKind Classify(string text)
    {
        if (text.StartsWith("ERROR") || text.Contains('✗')) return LineKind.Bad;
        if (text.StartsWith("WARNING")) return LineKind.Warn;
        if (text.StartsWith("[OK]") || text.Contains('✓')) return LineKind.Good;
        if (text.StartsWith("====")) return LineKind.Dim;
        return LineKind.Info;
    }
}
