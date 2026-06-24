using Packman.Models;
using Packman.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;

namespace Packman.ViewModels;

/// <summary>
/// Drives the Applications library screen: loads the lightweight Win32 app list from
/// Intune and exposes a searchable, category-filterable view over it.
/// </summary>
public sealed class ApplicationsViewModel : ObservableObject
{
    private const string AllCategories = "All Categories";

    private readonly IntuneService _apps = AppServices.Apps;
    private readonly IntuneAuthService _auth = AppServices.Auth;

    private readonly ObservableCollection<IntuneApplication> _all = new();
    public ICollectionView Apps { get; }
    public ObservableCollection<string> Categories { get; } = new() { AllCategories };

    public RelayCommand RefreshCommand { get; }
    public RelayCommand<IntuneApplication> OpenCommand { get; }

    /// <summary>Raised when a row is activated; the host swaps in the detail screen.</summary>
    public event Action<IntuneApplication>? OpenRequested;

    private bool _loadedOnce;

    public ApplicationsViewModel()
    {
        Apps = CollectionViewSource.GetDefaultView(_all);
        Apps.Filter = o => Matches((IntuneApplication)o);

        RefreshCommand = new RelayCommand(async () => await LoadAsync(force: true), () => !IsLoading);
        OpenCommand = new RelayCommand<IntuneApplication>(app => { if (app != null) OpenRequested?.Invoke(app); });
    }

    private string _search = "";
    public string Search
    {
        get => _search;
        set { if (Set(ref _search, value)) Apps.Refresh(); }
    }

    private string _selectedCategory = AllCategories;
    public string SelectedCategory
    {
        get => _selectedCategory;
        set { if (Set(ref _selectedCategory, value)) Apps.Refresh(); }
    }

    public int TotalCount => _all.Count;
    public int ShownCount => Apps.Cast<object>().Count();

    private bool _isLoading;
    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (!Set(ref _isLoading, value)) return;
            OnPropertyChanged(nameof(ShowEmpty));
            RefreshCommand.RaiseCanExecuteChanged();
        }
    }

    private string _statusText = "";
    public string StatusText
    {
        get => _statusText;
        private set { if (Set(ref _statusText, value)) OnPropertyChanged(nameof(HasStatus)); }
    }
    public bool HasStatus => !string.IsNullOrEmpty(_statusText);

    public bool ShowEmpty => !IsLoading && _all.Count == 0;

    public async Task LoadAsync(bool force = false)
    {
        if (IsLoading) return;
        if (_loadedOnce && !force) return;

        if (!_auth.IsSignedIn)
        {
            ResetList();
            StatusText = "Sign in on the Settings page to load applications from Intune.";
            OnPropertyChanged(nameof(ShowEmpty));
            return;
        }

        IsLoading = true;
        StatusText = "";
        try
        {
            var apps = await _apps.GetApplicationsAsync(force);
            _all.Clear();
            foreach (var a in apps) _all.Add(a);
            _loadedOnce = true;
            RebuildCategories();
            Apps.Refresh();
            OnPropertyChanged(nameof(TotalCount));
            OnPropertyChanged(nameof(ShownCount));
            if (_all.Count == 0)
                StatusText = "No Win32 applications found in this tenant.";
        }
        catch (Exception ex)
        {
            StatusText = $"Could not load applications: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(ShowEmpty));
        }
    }

    private void ResetList()
    {
        _all.Clear();
        Apps.Refresh();
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(ShownCount));
    }

    private void RebuildCategories()
    {
        var current = SelectedCategory;
        Categories.Clear();
        Categories.Add(AllCategories);
        foreach (var c in _all
                     .SelectMany(a => a.Category.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                     .Distinct()
                     .OrderBy(c => c))
            Categories.Add(c);

        if (!Categories.Contains(current)) _selectedCategory = AllCategories;
        OnPropertyChanged(nameof(SelectedCategory));
    }

    private bool Matches(IntuneApplication a)
    {
        if (_selectedCategory != AllCategories &&
            !a.Category.Split(',', StringSplitOptions.TrimEntries).Contains(_selectedCategory))
            return false;

        if (string.IsNullOrWhiteSpace(_search)) return true;
        var q = _search.Trim();
        return a.DisplayName.Contains(q, StringComparison.OrdinalIgnoreCase)
            || a.Publisher.Contains(q, StringComparison.OrdinalIgnoreCase);
    }
}
