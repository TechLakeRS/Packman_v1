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
}
