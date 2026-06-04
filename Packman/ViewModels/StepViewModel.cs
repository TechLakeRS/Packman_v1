namespace Packman.ViewModels;

public sealed class StepViewModel : ObservableObject
{
    public int Index { get; }
    public string Title { get; }
    public string Subtitle { get; }
    public bool Optional { get; }
    public bool IsFirst { get; }
    public bool IsLast { get; }
    public string PrimaryLabel { get; }
    public string Number => (Index + 1).ToString();

    private bool _isCurrent;
    public bool IsCurrent
    {
        get => _isCurrent;
        set { if (Set(ref _isCurrent, value)) OnPropertyChanged(nameof(IsActive)); }
    }

    private bool _isDone;
    public bool IsDone
    {
        get => _isDone;
        set { if (Set(ref _isDone, value)) OnPropertyChanged(nameof(IsActive)); }
    }

    public bool IsActive => IsCurrent || IsDone;

    public StepViewModel(int index, string title, string subtitle, bool optional,
                        string primaryLabel, bool isFirst, bool isLast)
    {
        Index = index;
        Title = title;
        Subtitle = subtitle;
        Optional = optional;
        PrimaryLabel = primaryLabel;
        IsFirst = isFirst;
        IsLast = isLast;
    }
}
