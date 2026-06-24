using Packman.Models;
using Packman.ViewModels;
using System;
using System.Windows;
using System.Windows.Controls;

namespace Packman.Views;

public partial class ApplicationDetailView : UserControl
{
    private ApplicationDetailViewModel? _vm;

    /// <summary>Raised by the breadcrumb; the host returns to the list.</summary>
    public event Action? BackRequested;

    /// <summary>Raised after a successful retire; the host returns to the list and refreshes.</summary>
    public event Action? Deleted;

    /// <summary>Raised by "Update version"; the host switches to the Create/Upgrade flow.</summary>
    public event Action<IntuneApplication>? UpdateRequested;

    /// <summary>Raised by "Run test"; the host switches to the Remote Test flow.</summary>
    public event Action? TestRequested;

    public ApplicationDetailView()
    {
        InitializeComponent();
    }

    /// <summary>Shows the given app and kicks off the full detail load.</summary>
    public async void Show(IntuneApplication app)
    {
        _vm = new ApplicationDetailViewModel(app);
        DataContext = _vm;
        await _vm.LoadAsync();
    }

    private void Back_Click(object sender, RoutedEventArgs e) => BackRequested?.Invoke();

    private void ViewInIntune_Click(object sender, RoutedEventArgs e) => _vm?.OpenInIntune();

    private void Update_Click(object sender, RoutedEventArgs e)
    {
        if (_vm != null) UpdateRequested?.Invoke(_vm.Detail);
    }

    private void Test_Click(object sender, RoutedEventArgs e) => TestRequested?.Invoke();

    // In-app assignment editing isn't built yet; open the app in Intune where
    // assignments are managed.
    private void EditAssignments_Click(object sender, RoutedEventArgs e) => _vm?.OpenInIntune();

    private async void Retire_Click(object sender, RoutedEventArgs e)
    {
        if (_vm == null) return;

        var result = MessageBox.Show(
            $"Retire \"{_vm.Detail.DisplayName}\" from Intune?\n\nThis permanently removes the Win32 app from the tenant. This cannot be undone.",
            "Retire from Intune", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;

        if (await _vm.DeleteAsync())
            Deleted?.Invoke();
    }
}
