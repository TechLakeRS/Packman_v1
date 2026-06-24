using System.Windows;
using Packman.ViewModels;

namespace Packman;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        DataContext = new MainViewModel();
        InitializeComponent();
    }

    private void SettingsNavBtn_Checked(object sender, RoutedEventArgs e)
    {
        MainScrollViewer.Visibility = Visibility.Collapsed;
        SettingsPage.Visibility = Visibility.Visible;
    }

    private void SettingsNavBtn_Unchecked(object sender, RoutedEventArgs e)
    {
        MainScrollViewer.Visibility = Visibility.Visible;
        SettingsPage.Visibility = Visibility.Collapsed;
    }
}
