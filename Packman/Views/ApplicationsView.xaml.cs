using Packman.Models;
using Packman.ViewModels;
using System;
using System.Windows;
using System.Windows.Controls;

namespace Packman.Views;

public partial class ApplicationsView : UserControl
{
    public ApplicationsViewModel ViewModel { get; }

    /// <summary>Raised when a row is opened; the host swaps in the detail screen.</summary>
    public event Action<IntuneApplication>? AppOpened;

    /// <summary>Raised by the "New Package" button; the host switches to the Create flow.</summary>
    public event Action? NewPackageRequested;

    /// <summary>Raised when the user asks to connect; the host switches to the Settings screen.</summary>
    public event Action? ConnectRequested;

    public ApplicationsView()
    {
        ViewModel = new ApplicationsViewModel();
        ViewModel.OpenRequested += a => AppOpened?.Invoke(a);
        ViewModel.ConnectRequested += () => ConnectRequested?.Invoke();
        DataContext = ViewModel;
        InitializeComponent();
    }

    /// <summary>Loads (or refreshes) the list. Called by the host when the screen is shown.</summary>
    public async void Load() => await ViewModel.LoadAsync();

    private void NewPackage_Click(object sender, RoutedEventArgs e) => NewPackageRequested?.Invoke();
}
