using Packman.Helpers;
using Packman.ViewModels;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;

namespace Packman.Views;

public partial class StepEdit : UserControl
{
    public StepEdit()
    {
        InitializeComponent();
    }

    private void OpenVSCode_Click(object sender, RoutedEventArgs e)
    {
        var vm = DataContext as MainViewModel;
        var packagePath = vm?.CreatePackage.CurrentPackagePath;
        if (string.IsNullOrEmpty(packagePath)) return;

        var scriptPath = Path.Combine(packagePath, "Application", "Invoke-AppDeployToolkit.ps1");
        if (!File.Exists(scriptPath))
        {
            MessageBox.Show("Script not found. Generate the package first.", "Not Found",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            var vsCode = EditorLocator.FindVSCodePath();
            var psi = vsCode != null
                ? new ProcessStartInfo(vsCode, $"\"{scriptPath}\"") { UseShellExecute = true }
                : new ProcessStartInfo(scriptPath) { UseShellExecute = true };
            Process.Start(psi);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not open script: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
