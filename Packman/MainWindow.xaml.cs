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

        ApplicationsPage.AppOpened += app => { AppDetailPage.Show(app); ShowOnly(AppDetailPage, "Applications / Detail"); };
        ApplicationsPage.ConnectRequested += () => SettingsNavBtn.IsChecked = true;

        AppDetailPage.BackRequested += () => ShowOnly(ApplicationsPage, "Applications");
        AppDetailPage.Deleted += () =>
        {
            ShowOnly(ApplicationsPage, "Applications");
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
    private void ShowOnly(UIElement? page, string? screenTitle = null)
    {
        foreach (var p in new UIElement?[] { CreatePackagePage, SettingsPage, UploadIntunePage, ApplicationsPage, AppDetailPage, AdvancedPage })
            if (p != null) p.Visibility = ReferenceEquals(p, page) ? Visibility.Visible : Visibility.Collapsed;

        if (screenTitle != null && ScreenTitleText != null) ScreenTitleText.Text = screenTitle;
    }

    /// <summary>Switches to the Upload to Intune page (used by the wizard's cross-link).</summary>
    public void NavigateToUploadIntune() => UploadIntuneNavBtn.IsChecked = true;

    /// <summary>Both share the same page, so returning to the wizard has to close an open tool.</summary>
    private void CreatePackageNavBtn_Checked(object sender, RoutedEventArgs e)
    {
        ShowOnly(CreatePackagePage, "Create Package");
        (DataContext as MainViewModel)?.OpenTool(PackageTool.None);
    }

    private void UploadIntuneNavBtn_Checked(object sender, RoutedEventArgs e)
    {
        ShowOnly(UploadIntunePage, "Upload to Intune");
        UploadIntunePage.Refresh();
    }

    private void ApplicationsNavBtn_Checked(object sender, RoutedEventArgs e)
    {
        ShowOnly(ApplicationsPage, "Applications");
        ApplicationsPage.Load();
    }

    private void AdvancedNavBtn_Checked(object sender, RoutedEventArgs e)
    {
        ShowOnly(AdvancedPage, "Advanced");
        AdvancedPage.Refresh();
    }

    private void SettingsNavBtn_Checked(object sender, RoutedEventArgs e) => ShowOnly(SettingsPage, "Settings");

    /// <summary>The tool footer's action belongs to the tool page that is open.</summary>
    private void ToolAction_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel { IsEditToolOpen: true }) EditStep.OpenInExternalEditor();
    }

    /// <summary>
    /// Remote Test shares the wizard's page, but from the rail it stands alone: no package
    /// comes with it, so the user picks one on the page itself.
    /// </summary>
    private void RemoteTestNavBtn_Checked(object sender, RoutedEventArgs e)
    {
        ShowOnly(CreatePackagePage, "Remote Test");
        (DataContext as MainViewModel)?.OpenTool(PackageTool.RemoteTest, standalone: true);
    }
}
