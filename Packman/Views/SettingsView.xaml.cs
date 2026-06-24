using Packman.Services;
using Packman.ViewModels;
using System.Windows.Controls;
using System.Windows.Input;

namespace Packman.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
        DataContext = new SettingsViewModel(new SettingsService());
    }

    private void NavAuth_Click(object sender, MouseButtonEventArgs e)
        => AuthSection.BringIntoView();

    private void NavCodeSign_Click(object sender, MouseButtonEventArgs e)
        => CodeSignSection.BringIntoView();
}
