using System.ComponentModel;
using System.Windows;
using Packman.ViewModels;

namespace Packman;

public partial class MainWindow : Window
{
    private bool _closeConfirmed;

    public MainWindow()
    {
        DataContext = new MainViewModel();
        InitializeComponent();

        ApplicationsPage.AppOpened += app => { AppDetailPage.Show(app); ShowOnly(AppDetailPage); };
        ApplicationsPage.ConnectRequested += () => SettingsNavBtn.IsChecked = true;

        AppDetailPage.BackRequested += () => ShowOnly(ApplicationsPage);
        AppDetailPage.Deleted += () =>
        {
            ShowOnly(ApplicationsPage);
            _ = ApplicationsPage.ViewModel.LoadAsync(force: true);
        };
        AppDetailPage.UpdateRequested += _ => CreatePackageNavBtn.IsChecked = true;

        AdvancedPage.ConnectRequested += () => SettingsNavBtn.IsChecked = true;
    }

    /// <summary>Gives the script editor a chance to save before the app goes away.</summary>
    private async void MainWindow_Closing(object sender, CancelEventArgs e)
    {
        if (_closeConfirmed || !EditStep.HasUnsavedChanges) return;

        e.Cancel = true;
        if (!await EditStep.PromptSaveAllAsync()) return;

        _closeConfirmed = true;
        Close();
    }

    /// <summary>Shows exactly one content page and collapses the rest. Null-safe for load-time calls.</summary>
    private void ShowOnly(UIElement? page)
    {
        foreach (var p in new UIElement?[] { CreatePackagePage, SettingsPage, UploadIntunePage, ApplicationsPage, AppDetailPage, AdvancedPage })
            if (p != null) p.Visibility = ReferenceEquals(p, page) ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Switches to the Upload to Intune page (used by the wizard's cross-link).</summary>
    public void NavigateToUploadIntune() => UploadIntuneNavBtn.IsChecked = true;

    private void CreatePackageNavBtn_Checked(object sender, RoutedEventArgs e) => ShowOnly(CreatePackagePage);

    private void UploadIntuneNavBtn_Checked(object sender, RoutedEventArgs e)
    {
        ShowOnly(UploadIntunePage);
        UploadIntunePage.Refresh();
    }

    private void ApplicationsNavBtn_Checked(object sender, RoutedEventArgs e)
    {
        ShowOnly(ApplicationsPage);
        ApplicationsPage.Load();
    }

    private void AdvancedNavBtn_Checked(object sender, RoutedEventArgs e)
    {
        ShowOnly(AdvancedPage);
        AdvancedPage.Refresh();
    }

    private void SettingsNavBtn_Checked(object sender, RoutedEventArgs e) => ShowOnly(SettingsPage);
}
