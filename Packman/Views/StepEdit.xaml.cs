using Packman.Helpers;
using Packman.ViewModels;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace Packman.Views;

public partial class StepEdit : UserControl
{
    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".ps1", ".psm1", ".psd1", ".txt", ".xml", ".json", ".cmd", ".bat",
        ".ini", ".md", ".config", ".log", ".reg", ".csv", ".yml", ".yaml"
    };

    public StepEdit()
    {
        InitializeComponent();
    }

    private MainViewModel? VM => DataContext as MainViewModel;

    private string ApplicationFolder =>
        Path.Combine(VM?.CreatePackage.CurrentPackagePath ?? "", "Application");

    private void StepEdit_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsVisible) LoadPackage();
    }

    private void LoadPackage()
    {
        var packagePath = VM?.CreatePackage.CurrentPackagePath;
        var appFolder = ApplicationFolder;

        if (string.IsNullOrEmpty(packagePath) || !Directory.Exists(appFolder))
        {
            EmptyState.Visibility = Visibility.Visible;
            EditorGrid.Visibility = Visibility.Collapsed;
            return;
        }

        EmptyState.Visibility = Visibility.Collapsed;
        EditorGrid.Visibility = Visibility.Visible;

        PackageNameText.Text = new DirectoryInfo(packagePath).Name;

        FileTree.Items.Clear();
        foreach (var dir in Directory.GetDirectories(appFolder).OrderBy(d => d))
            FileTree.Items.Add(BuildDirectoryNode(dir));
        foreach (var file in Directory.GetFiles(appFolder).OrderBy(f => f))
            FileTree.Items.Add(BuildFileNode(file));

        // Default to showing the main deployment script.
        var script = Path.Combine(appFolder, "Invoke-AppDeployToolkit.ps1");
        if (File.Exists(script))
            ShowFile(script);
    }

    private TreeViewItem BuildDirectoryNode(string dir)
    {
        var node = new TreeViewItem
        {
            Header = Path.GetFileName(dir),
            Tag = dir,
            IsExpanded = false
        };
        foreach (var sub in Directory.GetDirectories(dir).OrderBy(d => d))
            node.Items.Add(BuildDirectoryNode(sub));
        foreach (var file in Directory.GetFiles(dir).OrderBy(f => f))
            node.Items.Add(BuildFileNode(file));
        return node;
    }

    private static TreeViewItem BuildFileNode(string file) => new()
    {
        Header = Path.GetFileName(file),
        Tag = file
    };

    private void FileTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is TreeViewItem { Tag: string path } && File.Exists(path))
            ShowFile(path);
    }

    private void ShowFile(string path)
    {
        CodeFileName.Text = Path.GetFileName(path);
        var ext = Path.GetExtension(path);

        if (!TextExtensions.Contains(ext))
        {
            CodeBox.Text = $"[Binary file — open in an external editor to view]\n\n{path}";
            return;
        }

        try
        {
            CodeBox.Text = File.ReadAllText(path);
        }
        catch (Exception ex)
        {
            CodeBox.Text = $"Could not read file: {ex.Message}";
        }
    }

    private void OpenEditor_Click(object sender, RoutedEventArgs e)
    {
        var appFolder = ApplicationFolder;
        var scriptPath = Path.Combine(appFolder, "Invoke-AppDeployToolkit.ps1");

        // Prefer the file the user has selected in the tree, fall back to the script.
        var target = (FileTree.SelectedItem as TreeViewItem)?.Tag as string;
        if (string.IsNullOrEmpty(target) || !File.Exists(target))
            target = scriptPath;

        if (!File.Exists(target))
        {
            MessageBox.Show("Script not found. Generate the package first.", "Not Found",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            var vsCode = EditorLocator.FindVSCodePath();
            var ise = vsCode == null ? EditorLocator.FindPowerShellISEPath() : null;

            ProcessStartInfo psi;
            if (vsCode != null)
                psi = new ProcessStartInfo(vsCode, $"\"{target}\"") { UseShellExecute = true };
            else if (ise != null)
                psi = new ProcessStartInfo(ise, $"\"{target}\"") { UseShellExecute = true };
            else
                psi = new ProcessStartInfo(target) { UseShellExecute = true };

            Process.Start(psi);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not open script: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
