using Packman.Helpers;
using Packman.Models;
using Packman.ViewModels;
using System;
using System.Diagnostics;
using System.IO;
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

    private void ManageAssignments_Click(object sender, RoutedEventArgs e)
    {
        if (_vm != null) _vm.Tab = "deployment";
    }

    // ── Clipboard ──

    private static void CopyText(string? text)
    {
        if (string.IsNullOrEmpty(text)) return;
        try { Clipboard.SetText(text); }
        catch { /* clipboard can be locked by another process; not worth surfacing */ }
    }

    private void CopyPath_Click(object sender, RoutedEventArgs e) => CopyText(_vm?.SourcePath);
    private void CopyAppId_Click(object sender, RoutedEventArgs e) => CopyText(_vm?.Detail.Id);
    private void CopyInstall_Click(object sender, RoutedEventArgs e) => CopyText(_vm?.Detail.InstallCommand);
    private void CopyUninstall_Click(object sender, RoutedEventArgs e) => CopyText(_vm?.Detail.UninstallCommand);

    // ── Package source ──

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        var path = _vm?.SourcePath;
        if (string.IsNullOrEmpty(path)) return;
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not open folder: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void EditScript_Click(object sender, RoutedEventArgs e)
    {
        var script = _vm?.SourceScriptPath;
        if (string.IsNullOrEmpty(script) || !File.Exists(script))
        {
            MessageBox.Show("No PSADT script found in the package's source folder.", "Not found",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            var vsCode = EditorLocator.FindVSCodePath();
            var ise = vsCode == null ? EditorLocator.FindPowerShellISEPath() : null;

            ProcessStartInfo psi;
            if (vsCode != null)
                psi = new ProcessStartInfo(vsCode, $"\"{script}\"") { UseShellExecute = true };
            else if (ise != null)
                psi = new ProcessStartInfo(ise, $"\"{script}\"") { UseShellExecute = true };
            else
                psi = new ProcessStartInfo(script) { UseShellExecute = true };

            Process.Start(psi);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not open script: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

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
