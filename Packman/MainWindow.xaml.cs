using System.Windows;
using Packman.ViewModels;

namespace Packman;

public partial class MainWindow : Window
{
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
        // Remote Test screen isn't built yet; "Run test" returns to the wizard's test step.
        AppDetailPage.TestRequested += () => CreatePackageNavBtn.IsChecked = true;
    }

    /// <summary>Shows exactly one content page and collapses the rest. Null-safe for load-time calls.</summary>
    private void ShowOnly(UIElement? page)
    {
        foreach (var p in new UIElement?[] { CreatePackagePage, SettingsPage, UploadIntunePage, ApplicationsPage, AppDetailPage })
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

    private void SettingsNavBtn_Checked(object sender, RoutedEventArgs e) => ShowOnly(SettingsPage);
}
