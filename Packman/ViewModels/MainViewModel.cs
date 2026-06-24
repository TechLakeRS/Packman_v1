using Packman.Helpers;
using Packman.Services;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;

namespace Packman.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly SettingsService _settingsService = new();

    public ObservableCollection<StepViewModel> Steps { get; }
    public CreatePackageViewModel CreatePackage { get; } = new();

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
            OnPropertyChanged(nameof(PrimaryLabel));
            OnPropertyChanged(nameof(IsLastStep));
            OnPropertyChanged(nameof(SkipVisible));
            BackCommand.RaiseCanExecuteChanged();
            SkipCommand.RaiseCanExecuteChanged();
        }
    }

    public string PrimaryLabel => Steps[CurrentStepIndex].PrimaryLabel;
    public bool IsLastStep => CurrentStepIndex == Steps.Count - 1;
    public bool SkipVisible => Steps[CurrentStepIndex].Optional && !IsLastStep;

    private bool _isDarkTheme;
    public bool IsDarkTheme
    {
        get => _isDarkTheme;
        set { if (Set(ref _isDarkTheme, value)) App.ApplyTheme(value); }
    }

    public MainViewModel()
    {
        Steps = new ObservableCollection<StepViewModel>
        {
            new(0, "Generate",    "PSADT structure",   false, "Generate Package", isFirst: true,  isLast: false),
            new(1, "Edit Script", "Optional",          true,  "Continue",         isFirst: false, isLast: false),
            new(2, "Remote Test", "Optional",          true,  "Continue",         isFirst: false, isLast: false),
            new(3, "Upload",      "Deploy to Intune",  false, "Build & Upload",   isFirst: false, isLast: true),
        };

        BackCommand        = new RelayCommand(() => CurrentStepIndex--, () => CurrentStepIndex > 0);
        SkipCommand        = new RelayCommand(() => CurrentStepIndex++, () => SkipVisible);
        PrimaryCommand     = new RelayCommand(OnPrimary, () => !CreatePackage.IsGenerating);
        GoToStepCommand    = new RelayCommand<int>(i => CurrentStepIndex = i);
        ThemeToggleCommand = new RelayCommand(() => IsDarkTheme = !IsDarkTheme);

        Steps[0].IsCurrent = true;

        CreatePackage.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(CreatePackageViewModel.IsGenerating))
                PrimaryCommand.RaiseCanExecuteChanged();
        };
    }

    private async void OnPrimary()
    {
        if (CurrentStepIndex == 0)
        {
            var packagePath = await CreatePackage.GenerateAsync(_settingsService.Settings);
            if (!string.IsNullOrEmpty(packagePath))
                CurrentStepIndex = 1;
            else if (!string.IsNullOrEmpty(CreatePackage.StatusText))
                MessageBox.Show(CreatePackage.StatusText, "Package Generation", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        else if (CurrentStepIndex == 1)
        {
            OpenScriptInEditor();
            if (CurrentStepIndex < Steps.Count - 1) CurrentStepIndex++;
        }
        else if (CurrentStepIndex < Steps.Count - 1)
        {
            CurrentStepIndex++;
        }
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
            var psi = vsCode != null
                ? new ProcessStartInfo(vsCode, $"\"{scriptPath}\"") { UseShellExecute = true }
                : new ProcessStartInfo(scriptPath) { UseShellExecute = true };
            Process.Start(psi);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to open script: {ex.Message}");
        }
    }
}
