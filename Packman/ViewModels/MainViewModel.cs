using Packman.Helpers;
using Packman.Services;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;

namespace Packman.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly SettingsService _settingsService = AppServices.Settings;
    private readonly IntuneAuthService _auth = AppServices.Auth;

    public SettingsService SettingsService => _settingsService;

    public ObservableCollection<StepViewModel> Steps { get; }
    public CreatePackageViewModel CreatePackage { get; } = new();
    public UpgradePackageViewModel Upgrade { get; } = new();
    public UploadStepViewModel Upload { get; }

    private bool _isUpgradeMode;
    public bool IsUpgradeMode
    {
        get => _isUpgradeMode;
        set { if (Set(ref _isUpgradeMode, value)) OnPropertyChanged(nameof(PrimaryLabel)); }
    }

    public RelayCommand BackCommand { get; }
    public RelayCommand SkipCommand { get; }
    public RelayCommand PrimaryCommand { get; }
    public RelayCommand<int> GoToStepCommand { get; }
    public RelayCommand ThemeToggleCommand { get; }

    private int _currentStepIndex;
    public int CurrentStepIndex
    {
        get => _currentStepIndex;
        set
        {
            if (value < 0 || value >= Steps.Count) return;
            if (!Set(ref _currentStepIndex, value)) return;
            for (int i = 0; i < Steps.Count; i++)
            {
                Steps[i].IsCurrent = i == value;
                Steps[i].IsDone = i < value;
            }
            if (value == 3) Upload.RefreshFromPackage();
            OnPropertyChanged(nameof(PrimaryLabel));
            OnPropertyChanged(nameof(IsLastStep));
            OnPropertyChanged(nameof(SkipVisible));
            OnPropertyChanged(nameof(StepPosition));
            BackCommand.RaiseCanExecuteChanged();
            SkipCommand.RaiseCanExecuteChanged();
        }
    }

    public string PrimaryLabel =>
        CurrentStepIndex == 0 && IsUpgradeMode ? "Upgrade Package" : Steps[CurrentStepIndex].PrimaryLabel;
    public bool IsLastStep => CurrentStepIndex == Steps.Count - 1;
    public bool SkipVisible => Steps[CurrentStepIndex].Optional && !IsLastStep;
    public string StepPosition => $"Step {CurrentStepIndex + 1} of {Steps.Count}";

    private bool _isDarkTheme;
    public bool IsDarkTheme
    {
        get => _isDarkTheme;
        set { if (Set(ref _isDarkTheme, value)) App.ApplyTheme(value); }
    }

    // ── Intune connection status (footer) ──────────────────────────────
    public bool IsConnected => _auth.IsSignedIn;
    public string ConnectionStatusText => _auth.IsSignedIn
        ? $"Connected to Microsoft Intune · {_auth.SignedInUser}"
        : "Not connected — sign in on the Settings page";

    public MainViewModel()
    {
        Upload = new UploadStepViewModel(CreatePackage, _settingsService, _auth);

        Steps = new ObservableCollection<StepViewModel>
        {
            new(0, "Generate",    false, "Generate Package", isFirst: true,  isLast: false),
            new(1, "Edit Script", true,  "Continue",         isFirst: false, isLast: false),
            new(2, "Remote Test", true,  "Continue",         isFirst: false, isLast: false),
            new(3, "Upload",      false, "Build & Upload",   isFirst: false, isLast: true),
        };

        BackCommand        = new RelayCommand(() => CurrentStepIndex--, () => CurrentStepIndex > 0);
        SkipCommand        = new RelayCommand(() => CurrentStepIndex++, () => SkipVisible);
        PrimaryCommand     = new RelayCommand(OnPrimary, () => !CreatePackage.IsGenerating && !Upgrade.IsBusy && !Upload.IsPublishing);
        GoToStepCommand    = new RelayCommand<int>(i => CurrentStepIndex = i);
        ThemeToggleCommand = new RelayCommand(() => IsDarkTheme = !IsDarkTheme);

        Steps[0].IsCurrent = true;

        _auth.StateChanged += () =>
        {
            OnPropertyChanged(nameof(IsConnected));
            OnPropertyChanged(nameof(ConnectionStatusText));
        };

        CreatePackage.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(CreatePackageViewModel.IsGenerating))
                PrimaryCommand.RaiseCanExecuteChanged();
        };
        Upgrade.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(UpgradePackageViewModel.IsBusy))
                PrimaryCommand.RaiseCanExecuteChanged();
        };
        Upload.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(UploadStepViewModel.IsPublishing))
                PrimaryCommand.RaiseCanExecuteChanged();
        };
    }

    private async void OnPrimary()
    {
        if (CurrentStepIndex == 0)
        {
            if (IsUpgradeMode)
                await RunUpgradeAsync();
            else
                await RunCreateAsync();
        }
        else if (CurrentStepIndex == 1)
        {
            OpenScriptInEditor();
            if (CurrentStepIndex < Steps.Count - 1) CurrentStepIndex++;
        }
        else if (IsLastStep)
        {
            await Upload.UploadAsync();
        }
        else if (CurrentStepIndex < Steps.Count - 1)
        {
            CurrentStepIndex++;
        }
    }

    private async Task RunCreateAsync()
    {
        var packagePath = await CreatePackage.GenerateAsync(_settingsService.Settings);
        if (!string.IsNullOrEmpty(packagePath))
            CurrentStepIndex = 1;
        else if (!string.IsNullOrEmpty(CreatePackage.StatusText))
            MessageBox.Show(CreatePackage.StatusText, "Package Generation", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private async Task RunUpgradeAsync()
    {
        var newPackagePath = await Upgrade.UpgradeAsync(_settingsService.Settings);
        if (string.IsNullOrEmpty(newPackagePath))
        {
            if (!string.IsNullOrEmpty(Upgrade.StatusText))
                MessageBox.Show(Upgrade.StatusText, "Package Upgrade", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Carry upgraded metadata into the create model so Edit/Test/Upload steps work.
        var meta = Upgrade.LoadedMetadata;
        if (meta != null)
        {
            CreatePackage.AppName = meta.AppName;
            CreatePackage.Manufacturer = meta.Manufacturer;
            CreatePackage.UserInstall = meta.InstallContext.Equals("User", StringComparison.OrdinalIgnoreCase);
        }
        CreatePackage.Version = Upgrade.NewVersion;
        CreatePackage.SourcesPath = Upgrade.NewSourcePath;
        CreatePackage.CurrentPackagePath = newPackagePath;
        CreatePackage.PredecessorAppId = PackageMarker.GetMarkerAppId(Upgrade.ExistingPackagePath) ?? "";

        CurrentStepIndex = 1;
    }

    private void OpenScriptInEditor()
    {
        var packagePath = CreatePackage.CurrentPackagePath;
        if (string.IsNullOrEmpty(packagePath)) return;

        var scriptPath = Path.Combine(packagePath, "Application", "Invoke-AppDeployToolkit.ps1");
        if (!File.Exists(scriptPath)) return;

        try
        {
            var vsCode = EditorLocator.FindVSCodePath();
            var ise = vsCode == null ? EditorLocator.FindPowerShellISEPath() : null;

            ProcessStartInfo psi;
            if (vsCode != null)
                psi = new ProcessStartInfo(vsCode, $"\"{scriptPath}\"") { UseShellExecute = true };
            else if (ise != null)
                psi = new ProcessStartInfo(ise, $"\"{scriptPath}\"") { UseShellExecute = true };
            else
                psi = new ProcessStartInfo(scriptPath) { UseShellExecute = true };

            Process.Start(psi);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to open script: {ex.Message}");
        }
    }
}
