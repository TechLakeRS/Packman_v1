using System.Collections.ObjectModel;

namespace Packman.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    public ObservableCollection<StepViewModel> Steps { get; }
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
        PrimaryCommand     = new RelayCommand(OnPrimary);
        GoToStepCommand    = new RelayCommand<int>(i => CurrentStepIndex = i);
        ThemeToggleCommand = new RelayCommand(() => IsDarkTheme = !IsDarkTheme);

        Steps[0].IsCurrent = true;
    }

    private void OnPrimary()
    {
        if (CurrentStepIndex < Steps.Count - 1) CurrentStepIndex++;
    }
}
