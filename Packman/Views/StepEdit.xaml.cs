using Microsoft.Web.WebView2.Core;
using Packman.Helpers;
using Packman.Services;
using Packman.ViewModels;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
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

    private static readonly HashSet<string> PowerShellExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".ps1", ".psm1", ".psd1"
    };

    private bool _editorReady;
    private bool _editorFailed;
    private bool _isDirty;
    private string? _currentFilePath;
    private string? _pendingShowPath;   // file waiting for the editor to become ready
    private string? _loadAfterSavePath; // file to open once a save round-trip completes

    public StepEdit()
    {
        InitializeComponent();
    }

    private MainViewModel? VM => DataContext as MainViewModel;

    private string ApplicationFolder =>
        Path.Combine(VM?.CreatePackage.CurrentPackagePath ?? "", "Application");

    private async void StepEdit_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsVisible)
        {
            await InitializeEditorAsync();
            LoadPackage();
        }
        else if (_isDirty && _currentFilePath != null)
        {
            PromptSaveIfDirty(null);
        }
    }

    // ═══════════ WebView2 / Monaco host ═══════════

    private async Task InitializeEditorAsync()
    {
        if (_editorReady || _editorFailed || EditorWebView.CoreWebView2 != null) return;

        try
        {
            var userDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Packman", "WebView2");
            var env = await CoreWebView2Environment.CreateAsync(userDataFolder: userDataFolder);
            await EditorWebView.EnsureCoreWebView2Async(env);

            var assetsFolder = Path.Combine(AppContext.BaseDirectory, "MonacoEditor");
            EditorWebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                "packman-editor", assetsFolder, CoreWebView2HostResourceAccessKind.Allow);
            EditorWebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            EditorWebView.CoreWebView2.Settings.AreDevToolsEnabled = false;
            EditorWebView.CoreWebView2.WebMessageReceived += Editor_WebMessageReceived;
            EditorWebView.CoreWebView2.Navigate("https://packman-editor/index.html");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WebView2 init failed: {ex.Message}");
            _editorFailed = true;
            EditorWebView.Visibility = Visibility.Collapsed;
            EditorFallbackText.Visibility = Visibility.Visible;
            EditorFallbackText.Text =
                "The in-app editor needs the Microsoft Edge WebView2 Runtime, which was not found on this machine.\n\n" +
                "Use \"Open in VS Code\" to edit the script externally.";
        }
    }

    private void Editor_WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        using var doc = JsonDocument.Parse(e.WebMessageAsJson);
        var type = doc.RootElement.GetProperty("type").GetString();

        switch (type)
        {
            case "ready":
                _editorReady = true;
                PostToEditor(new { type = "init", catalog = BuildCatalogPayload() });
                if (_pendingShowPath != null)
                {
                    var path = _pendingShowPath;
                    _pendingShowPath = null;
                    LoadFileIntoEditor(path);
                }
                break;

            case "dirty":
                _isDirty = doc.RootElement.GetProperty("dirty").GetBoolean();
                DirtyDot.Visibility = _isDirty ? Visibility.Visible : Visibility.Collapsed;
                EditActions.Visibility = _isDirty ? Visibility.Visible : Visibility.Collapsed;
                break;

            case "content":
                var content = doc.RootElement.GetProperty("content").GetString() ?? "";
                if (_currentFilePath != null)
                {
                    try
                    {
                        File.WriteAllText(_currentFilePath, content);
                        PostToEditor(new { type = "markSaved" });
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Could not save file: {ex.Message}", "Save failed",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
                if (_loadAfterSavePath != null)
                {
                    var next = _loadAfterSavePath;
                    _loadAfterSavePath = null;
                    LoadFileIntoEditor(next);
                }
                break;
        }
    }

    private void PostToEditor(object message) =>
        EditorWebView.CoreWebView2?.PostWebMessageAsJson(JsonSerializer.Serialize(message));

    private static object BuildCatalogPayload()
    {
        var functions = PSADTFunctionCatalog.LoadFromCsv(PSADTFunctionCatalog.GetCsvPath());
        return functions.Select(f => new
        {
            name = f.Name,
            synopsis = f.Synopsis,
            category = f.Category,
            snippet = f.GenerateCallWithPlaceholders(),
            @params = f.Parameters
                .Where(p => p.Name != "(none)")
                .Select(p => new
                {
                    name = p.Name,
                    type = p.Type,
                    mandatory = p.Mandatory,
                    isSwitch = p.IsSwitch,
                    description = p.Description
                })
        }).ToList();
    }

    // ═══════════ Package tree ═══════════

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

    private void ToggleTree_Click(object sender, RoutedEventArgs e)
    {
        var show = TreePanel.Visibility != Visibility.Visible;
        TreePanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        TreeRail.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
    }

    private void FileTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is TreeViewItem { Tag: string path } && File.Exists(path) && path != _currentFilePath)
            ShowFile(path);
    }

    // ═══════════ File load / save ═══════════

    private void ShowFile(string path)
    {
        if (!_editorReady)
        {
            _pendingShowPath = path;
            return;
        }

        if (_isDirty && _currentFilePath != null)
        {
            PromptSaveIfDirty(path);
            return;
        }

        LoadFileIntoEditor(path);
    }

    /// <summary>Asks whether to keep unsaved changes; then opens nextPath (if given).</summary>
    private void PromptSaveIfDirty(string? nextPath)
    {
        var save = MessageBox.Show(
            $"Save changes to {Path.GetFileName(_currentFilePath)}?", "Unsaved changes",
            MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

        if (save)
        {
            _loadAfterSavePath = nextPath;
            PostToEditor(new { type = "getContent" }); // save happens when content arrives
        }
        else
        {
            _isDirty = false;
            if (nextPath != null) LoadFileIntoEditor(nextPath);
        }
    }

    private void LoadFileIntoEditor(string path)
    {
        _currentFilePath = path;
        CodeFileName.Text = Path.GetFileName(path);
        var ext = Path.GetExtension(path);

        string content;
        bool readOnly = false;

        if (!TextExtensions.Contains(ext))
        {
            content = $"[Binary file — open in an external editor to view]\n\n{path}";
            readOnly = true;
        }
        else
        {
            try
            {
                content = File.ReadAllText(path);
            }
            catch (Exception ex)
            {
                content = $"Could not read file: {ex.Message}";
                readOnly = true;
            }
        }

        PostToEditor(new
        {
            type = "setContent",
            content,
            language = PowerShellExtensions.Contains(ext) ? "powershell" : "plaintext",
            readOnly
        });
    }

    private void Save_Click(object sender, RoutedEventArgs e) =>
        PostToEditor(new { type = "getContent" });

    private void Revert_Click(object sender, RoutedEventArgs e)
    {
        if (_currentFilePath == null) return;
        _isDirty = false;
        LoadFileIntoEditor(_currentFilePath);
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
