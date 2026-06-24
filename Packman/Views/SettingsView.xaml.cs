using Packman.Services;
using Packman.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Packman.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
        DataContext = new SettingsViewModel(new SettingsService(), new IntuneAuthService());
    }

    private void NavAuth_Click(object sender, MouseButtonEventArgs e)
        => AuthCard.BringIntoView();

    private void NavCodeSign_Click(object sender, MouseButtonEventArgs e)
        => CodeSignCard.BringIntoView();

    private void NavNetworkPaths_Click(object sender, MouseButtonEventArgs e)
        => NetworkPathsCard.BringIntoView();
}
