using Packman.Services;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace Packman.Views;

public partial class PSADTConfigDialog : Window
{
    public PSADTOptions SelectedOptions { get; private set; }

    private string _packageType = "MSI";
    private string _appName = "AppName";
    private string _manufacturer = "Manufacturer";
    private string _version = "1.0.0";
    private string _sourceFileName = "";
    private string _msiProductCode = "";

    private List<PSADTFunction> _allFunctions = new();
    private PSADTFunction? _selectedFunction;

    // Per-phase ordered lists of function entries
    private readonly Dictionary<ScriptPhase, List<PSADTFunctionEntry>> _phaseEntries = new()
    {
        { ScriptPhase.PreInstallation, new() },
        { ScriptPhase.Installation, new() },
        { ScriptPhase.PostInstallation, new() },
        { ScriptPhase.PreUninstallation, new() },
        { ScriptPhase.Uninstallation, new() },
        { ScriptPhase.PostUninstallation, new() },
    };

    // Tab -> phase mapping
    private readonly Dictionary<string, ScriptPhase> _tabPhaseMap = new();

    public PSADTConfigDialog()
    {
        InitializeComponent();
        SelectedOptions = new PSADTOptions();

        Loaded += (s, e) =>
        {
            InitializeTabMapping();
            LoadFunctionCatalog();
            PopulateFunctionBrowser();
            UpdatePreview();
        };
    }

    private void InitializeTabMapping()
    {
        _tabPhaseMap["TabPreInstall"] = ScriptPhase.PreInstallation;
        _tabPhaseMap["TabInstall"] = ScriptPhase.Installation;
        _tabPhaseMap["TabPostInstall"] = ScriptPhase.PostInstallation;
        _tabPhaseMap["TabPreUninstall"] = ScriptPhase.PreUninstallation;
        _tabPhaseMap["TabUninstall"] = ScriptPhase.Uninstallation;
        _tabPhaseMap["TabPostUninstall"] = ScriptPhase.PostUninstallation;
    }

    private void LoadFunctionCatalog()
    {
        var csvPath = PSADTFunctionCatalog.GetCsvPath();
        _allFunctions = PSADTFunctionCatalog.LoadFromCsv(csvPath);
    }

    #region Function Browser

    private void PopulateFunctionBrowser(string? filter = null)
    {
        FunctionCategoriesPanel.Children.Clear();

        var filteredFunctions = _allFunctions;
        if (!string.IsNullOrWhiteSpace(filter))
        {
            var lowerFilter = filter.ToLower();
            filteredFunctions = _allFunctions
                .Where(f => f.Name.ToLower().Contains(lowerFilter) ||
                            f.Synopsis.ToLower().Contains(lowerFilter) ||
                            f.Category.ToLower().Contains(lowerFilter))
                .ToList();
        }

        var grouped = filteredFunctions
            .GroupBy(f => f.Category)
            .OrderBy(g => Array.IndexOf(PSADTFunctionCatalog.CategoryOrder, g.Key));

        foreach (var group in grouped)
        {
            var expander = new Expander
            {
                IsExpanded = !string.IsNullOrWhiteSpace(filter) || group.Key == "Application Management" || group.Key == "Process Execution",
                Style = (Style)FindResource("CategoryExpander")
            };

            var headerGrid = new Grid { Width = 280 };
            var headerPanel = new StackPanel { Orientation = Orientation.Horizontal };
            var headerText = new TextBlock
            {
                Text = group.Key,
                FontSize = 12.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)FindResource("InkBrush")
            };
            headerPanel.Children.Add(headerText);
            headerGrid.Children.Add(headerPanel);

            var countBadge = new Border
            {
                Background = (Brush)FindResource("PrimaryDimBrush"),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(8, 2, 8, 2),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };
            var countText = new TextBlock
            {
                Text = group.Count().ToString(),
                FontSize = 10.5,
                Foreground = (Brush)FindResource("PrimaryBrush"),
                FontWeight = FontWeights.Bold
            };
            countBadge.Child = countText;
            headerGrid.Children.Add(countBadge);
            expander.Header = headerGrid;

            var stackPanel = new StackPanel { Margin = new Thickness(6, 4, 6, 4) };

            // Noun colour follows the theme so names stay readable in light mode.
            var nounBrush = (Brush)FindResource("InkBrush");

            foreach (var func in group)
            {
                var border = new Border();
                border.SetResourceReference(StyleProperty, "FunctionRow");

                var dockPanel = new DockPanel();

                // Function name with verb-noun coloring
                var nameBlock = new TextBlock
                {
                    FontSize = 12,
                    FontFamily = CodeFont,
                    VerticalAlignment = VerticalAlignment.Center
                };
                var dashIdx = func.Name.IndexOf('-');
                if (dashIdx > 0)
                {
                    nameBlock.Inlines.Add(new Run(func.Name[..(dashIdx + 1)]) { Foreground = VerbBrush });
                    nameBlock.Inlines.Add(new Run(func.Name[(dashIdx + 1)..]) { Foreground = nounBrush });
                }
                else
                {
                    nameBlock.Inlines.Add(new Run(func.Name) { Foreground = nounBrush });
                }

                // Tooltip with syntax-highlighted parameter signature (dark surface so the
                // light syntax colours stay readable regardless of theme).
                border.ToolTip = new ToolTip
                {
                    Content = BuildSyntaxTooltip(func),
                    Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x2E)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x4E)),
                    Padding = new Thickness(10, 7, 10, 7)
                };

                dockPanel.Children.Add(nameBlock);
                border.Child = dockPanel;

                var capturedFunc = func;
                border.MouseLeftButtonDown += (s, e) =>
                {
                    if (e.ClickCount == 2)
                    {
                        // Double-click: add directly to active phase
                        SelectFunction(capturedFunc, border);
                        AddFunctionToActivePhase(capturedFunc);
                        e.Handled = true;
                    }
                    else
                    {
                        SelectFunction(capturedFunc, border);
                    }
                };

                stackPanel.Children.Add(border);
            }

            expander.Content = stackPanel;
            FunctionCategoriesPanel.Children.Add(expander);
        }
    }

    private Border? _selectedFunctionBorder;

    private void SelectFunction(PSADTFunction func, Border border)
    {
        // Deselect previous
        if (_selectedFunctionBorder != null)
        {
            _selectedFunctionBorder.Background = Brushes.Transparent;
            _selectedFunctionBorder.BorderBrush = Brushes.Transparent;
        }

        _selectedFunction = func;
        _selectedFunctionBorder = border;
        border.Background = new SolidColorBrush(Color.FromArgb(0x18, 0x00, 0x78, 0xD4));
        border.BorderBrush = (Brush)FindResource("PrimaryBrush");

        // Update synopsis
        SynopsisText.Text = func.Synopsis;
        SynopsisPanel.Visibility = Visibility.Visible;
    }

    #endregion

    #region Search

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var searchText = SearchBox.Text;
        SearchPlaceholder.Visibility = string.IsNullOrWhiteSpace(searchText) ? Visibility.Visible : Visibility.Collapsed;
        PopulateFunctionBrowser(searchText);
    }

    private void ClearSearch_Click(object sender, RoutedEventArgs e)
    {
        SearchBox.Text = "";
        SearchBox.Focus();
    }

    private void SearchBox_GotFocus(object sender, RoutedEventArgs e)
    {
        SearchPlaceholder.Visibility = Visibility.Collapsed;
    }

    private void SearchBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(SearchBox.Text))
            SearchPlaceholder.Visibility = Visibility.Visible;
    }

    #endregion

    #region Phase Tab Management

    private ScriptPhase GetActivePhase()
    {
        if (TabPreInstall.IsChecked == true) return ScriptPhase.PreInstallation;
        if (TabInstall.IsChecked == true) return ScriptPhase.Installation;
        if (TabPostInstall.IsChecked == true) return ScriptPhase.PostInstallation;
        if (TabPreUninstall.IsChecked == true) return ScriptPhase.PreUninstallation;
        if (TabUninstall.IsChecked == true) return ScriptPhase.Uninstallation;
        if (TabPostUninstall.IsChecked == true) return ScriptPhase.PostUninstallation;
        return ScriptPhase.PreInstallation;
    }

    private void PhaseTab_Click(object sender, RoutedEventArgs e)
    {
        UpdatePreview();
        UpdateTabBadges();
    }

    private void UpdateTabBadges()
    {
        UpdateTabContent(TabPreInstall, "Pre", ScriptPhase.PreInstallation);
        UpdateTabContent(TabInstall, "Install", ScriptPhase.Installation);
        UpdateTabContent(TabPostInstall, "Post", ScriptPhase.PostInstallation);
        UpdateTabContent(TabPreUninstall, "Pre", ScriptPhase.PreUninstallation);
        UpdateTabContent(TabUninstall, "Uninst", ScriptPhase.Uninstallation);
        UpdateTabContent(TabPostUninstall, "Post", ScriptPhase.PostUninstallation);
    }

    private void UpdateTabContent(RadioButton tab, string baseName, ScriptPhase phase)
    {
        int count = _phaseEntries[phase].Count;
        tab.Content = count > 0 ? $"{baseName} ({count})" : baseName;
    }

    #endregion

    #region Add / Remove / Reorder Functions

    private void AddFunctionToActivePhase(PSADTFunction func)
    {
        var phase = GetActivePhase();
        var code = "\t\t" + func.GenerateCallWithPlaceholders();
        var entry = new PSADTFunctionEntry(func.Name, code);

        _phaseEntries[phase].Add(entry);
        UpdatePreview();
        UpdateTabBadges();
        UpdateSelectedCount();
    }

    private void RemoveEntry(ScriptPhase phase, int index)
    {
        if (_phaseEntries[phase].Count > index)
        {
            _phaseEntries[phase].RemoveAt(index);
            UpdatePreview();
            UpdateTabBadges();
            UpdateSelectedCount();
        }
    }

    private void ClearPhase_Click(object sender, RoutedEventArgs e)
    {
        var phase = GetActivePhase();
        if (_phaseEntries[phase].Count == 0) return;

        var result = MessageBox.Show(
            $"Clear all functions from {GetPhaseDisplayName(phase)}?",
            "Clear Phase", MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            _phaseEntries[phase].Clear();
            UpdatePreview();
            UpdateTabBadges();
            UpdateSelectedCount();
        }
    }

    #endregion

    #region Template Phase Content

    /// <summary>
    /// Builds the template context for a given phase, using current package info
    /// for dynamic content (install/uninstall commands).
    /// </summary>
    private (string[] Before, string Marker, string[] After) GetTemplatePhaseContent(ScriptPhase phase)
    {
        return phase switch
        {
            ScriptPhase.PreInstallation => (
                Before: new[]
                {
                    "##================================================",
                    "## MARK: Pre-Install",
                    "##================================================",
                    "$adtSession.InstallPhase = \"Pre-$($adtSession.DeploymentType)\"",
                    "",
                    "## Show Welcome Message, close processes if specified, allow up to 3 deferrals,",
                    "## verify there is enough disk space to complete the install, and persist the prompt.",
                    "$saiwParams = @{",
                    "    AllowDefer = $true",
                    "    DeferTimes = 3",
                    "    CheckDiskSpace = $true",
                    "    PersistPrompt = $true",
                    "}",
                    "if ($adtSession.AppProcessesToClose.Count -gt 0)",
                    "{",
                    "    $saiwParams.Add('CloseProcesses', $adtSession.AppProcessesToClose)",
                    "}",
                    "Show-ADTInstallationWelcome @saiwParams",
                    "",
                    "## Show Progress Message (with the default message).",
                    "Show-ADTInstallationProgress",
                    "",
                },
                Marker: "## <Perform Pre-Installation tasks here>",
                After: Array.Empty<string>()
            ),
            ScriptPhase.Installation => (
                Before: new[]
                {
                    "##================================================",
                    "## MARK: Install",
                    "##================================================",
                    "$adtSession.InstallPhase = $adtSession.DeploymentType",
                    "",
                    "## Handle Zero-Config MSI installations.",
                    "",
                },
                Marker: "## <Perform Installation tasks here>",
                After: GetInstallCommandLines()
            ),
            ScriptPhase.PostInstallation => (
                Before: new[]
                {
                    "##================================================",
                    "## MARK: Post-Install",
                    "##================================================",
                    "$adtSession.InstallPhase = \"Post-$($adtSession.DeploymentType)\"",
                    "",
                },
                Marker: "## <Perform Post-Installation tasks here>",
                After: new[]
                {
                    "",
                    "## Display a message at the end of the install.",
                    "if (!$adtSession.UseDefaultMsi)",
                    "{",
                    "    Show-ADTInstallationPrompt -Message '...' -ButtonRightText 'OK' -Icon Information -NoWait",
                    "}",
                }
            ),
            ScriptPhase.PreUninstallation => (
                Before: new[]
                {
                    "##================================================",
                    "## MARK: Pre-Uninstall",
                    "##================================================",
                    "$adtSession.InstallPhase = \"Pre-$($adtSession.DeploymentType)\"",
                    "",
                    "## If there are processes to close, show Welcome Message with a 60 second countdown.",
                    "if ($adtSession.AppProcessesToClose.Count -gt 0)",
                    "{",
                    "    Show-ADTInstallationWelcome -CloseProcesses $adtSession.AppProcessesToClose -CloseProcessesCountdown 60",
                    "}",
                    "",
                    "## Show Progress Message (with the default message).",
                    "Show-ADTInstallationProgress",
                    "",
                },
                Marker: "## <Perform Pre-Uninstallation tasks here>",
                After: Array.Empty<string>()
            ),
            ScriptPhase.Uninstallation => (
                Before: new[]
                {
                    "##================================================",
                    "## MARK: Uninstall",
                    "##================================================",
                    "$adtSession.InstallPhase = $adtSession.DeploymentType",
                    "",
                },
                Marker: "## <Perform Uninstallation tasks here>",
                After: GetUninstallCommandLines()
            ),
            ScriptPhase.PostUninstallation => (
                Before: new[]
                {
                    "##================================================",
                    "## MARK: Post-Uninstallation",
                    "##================================================",
                    "$adtSession.InstallPhase = \"Post-$($adtSession.DeploymentType)\"",
                    "",
                },
                Marker: "## <Perform Post-Uninstallation tasks here>",
                After: Array.Empty<string>()
            ),
            _ => (Array.Empty<string>(), "", Array.Empty<string>())
        };
    }

    /// <summary>
    /// Returns the install command lines based on package type.
    /// </summary>
    private string[] GetInstallCommandLines()
    {
        if (_packageType == "MSI")
        {
            var msiFileName = !string.IsNullOrEmpty(_sourceFileName) ? _sourceFileName : $"{_appName}.msi";
            return new[]
            {
                "",
                "## MSI Installation",
                $"Start-ADTMsiProcess -Action 'Install' -FilePath \"$($adtSession.DirFiles)\\{msiFileName}\"",
            };
        }
        else
        {
            var exeFileName = !string.IsNullOrEmpty(_sourceFileName) ? _sourceFileName : "setup.exe";
            return new[]
            {
                "",
                "## EXE Installation",
                $"Start-ADTProcess -FilePath \"$($adtSession.DirFiles)\\{exeFileName}\" -ArgumentList '<silent flags>'",
            };
        }
    }

    /// <summary>
    /// Returns the uninstall command lines based on package type.
    /// </summary>
    private string[] GetUninstallCommandLines()
    {
        if (_packageType == "MSI")
        {
            var productCode = !string.IsNullOrEmpty(_msiProductCode) ? _msiProductCode : "{ProductCode}";
            return new[]
            {
                "",
                "## Uninstall MSI application by Product Code",
                $"Start-ADTMsiProcess -Action 'Uninstall' -FilePath '{productCode}'",
            };
        }
        else
        {
            var exeFileName = !string.IsNullOrEmpty(_sourceFileName) ? _sourceFileName : "setup.exe";
            return new[]
            {
                "",
                "## Uninstall EXE application",
                $"Start-ADTProcess -FilePath \"$($adtSession.DirFiles)\\{exeFileName}\" -ArgumentList '<uninstall flags>'",
            };
        }
    }

    #endregion

    #region Live Preview

    // Shared colors
    private static readonly SolidColorBrush DimmedTextBrush = new(Color.FromRgb(0x55, 0x5A, 0x66));
    private static readonly SolidColorBrush CommentBrush = new(Color.FromRgb(0x4A, 0x6E, 0x3A));
    private static readonly SolidColorBrush VariableBrush = new(Color.FromRgb(0x6E, 0x8A, 0xAE));
    private static readonly SolidColorBrush CmdletBrush = new(Color.FromRgb(0x8A, 0x8A, 0x70));
    private static readonly SolidColorBrush SeparatorBrush = new(Color.FromRgb(0x44, 0x4A, 0x55));
    private static readonly SolidColorBrush MarkerBrush = new(Color.FromRgb(0x6A, 0x99, 0x55));
    private static readonly SolidColorBrush BrightCmdletBrush = new(Color.FromRgb(0xDC, 0xDC, 0xAA));
    private static readonly SolidColorBrush BrightParamBrush = new(Color.FromRgb(0x9C, 0xDC, 0xFE));
    private static readonly SolidColorBrush BrightStringBrush = new(Color.FromRgb(0xCE, 0x91, 0x78));
    private static readonly SolidColorBrush DefaultTextBrush = new(Color.FromRgb(0xD4, 0xD4, 0xD4));
    private static readonly SolidColorBrush EntryBorderBrush = new(Color.FromRgb(0x2D, 0x2D, 0x3E));
    private static readonly SolidColorBrush EntryHighlightBrush = new(Color.FromArgb(0x30, 0x4E, 0x9A, 0x06));
    private static readonly FontFamily CodeFont = new("Cascadia Code, Consolas, Courier New");

    // Function browser colors
    private static readonly SolidColorBrush VerbBrush = new(Color.FromRgb(0x56, 0x9C, 0xD6));      // Blue for verb (Get-, Set-, etc.)
    // Tooltip syntax colors
    private static readonly SolidColorBrush TooltipFuncBrush = new(Color.FromRgb(0xE8, 0xEA, 0xF0));    // White - function name
    private static readonly SolidColorBrush TooltipParamBrush = new(Color.FromRgb(0x9C, 0xDC, 0xFE));   // Light blue - param names
    private static readonly SolidColorBrush TooltipTypeBrush = new(Color.FromRgb(0x4E, 0xC9, 0xB0));    // Teal - type names
    private static readonly SolidColorBrush TooltipBracketBrush = new(Color.FromRgb(0x80, 0x85, 0x90)); // Gray - brackets

    private void UpdatePreview()
    {
        var phase = GetActivePhase();
        var entries = _phaseEntries[phase];
        var template = GetTemplatePhaseContent(phase);

        PreviewEntriesPanel.Children.Clear();

        // 1. Render template code BEFORE the marker (dimmed)
        foreach (var line in template.Before)
        {
            PreviewEntriesPanel.Children.Add(CreateTemplateLine(line));
        }

        // 2. Render the marker line (green, highlighted)
        PreviewEntriesPanel.Children.Add(CreateMarkerLine(template.Marker));

        // 3. Render user-added function entries (bright, with action buttons)
        if (entries.Count == 0)
        {
            var hintText = new TextBlock
            {
                Text = "    # Select a function and click + Add to insert here",
                Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x5A, 0x66)),
                FontSize = 12,
                FontFamily = CodeFont,
                FontStyle = FontStyles.Italic,
                Margin = new Thickness(0, 4, 0, 4)
            };
            PreviewEntriesPanel.Children.Add(hintText);
        }
        else
        {
            for (int i = 0; i < entries.Count; i++)
            {
                PreviewEntriesPanel.Children.Add(CreateInteractiveEntry(entries[i], phase, i, entries.Count));
            }
        }

        // 4. Render template code AFTER the marker (dimmed)
        foreach (var line in template.After)
        {
            PreviewEntriesPanel.Children.Add(CreateTemplateLine(line));
        }
    }

    /// <summary>
    /// Creates a dimmed, read-only template line with basic syntax coloring.
    /// </summary>
    private UIElement CreateTemplateLine(string line)
    {
        var textBlock = new TextBlock
        {
            FontFamily = CodeFont,
            FontSize = 12,
            Margin = new Thickness(0, 0, 0, 0),
            TextWrapping = TextWrapping.Wrap
        };

        var trimmed = line.TrimStart();

        if (string.IsNullOrWhiteSpace(line))
        {
            textBlock.Inlines.Add(new Run(" ") { Foreground = DimmedTextBrush });
        }
        else if (trimmed.StartsWith("##"))
        {
            // Section headers and comments - dim green
            textBlock.Inlines.Add(new Run(line) { Foreground = CommentBrush });
        }
        else if (trimmed.StartsWith("$"))
        {
            // Variables - dim blue
            textBlock.Inlines.Add(new Run(line) { Foreground = VariableBrush });
        }
        else if (trimmed.StartsWith("if ") || trimmed.StartsWith("{") || trimmed.StartsWith("}") ||
                 trimmed.StartsWith("Show-") || trimmed.StartsWith("Close-"))
        {
            // Keywords and cmdlets - dim text
            textBlock.Inlines.Add(new Run(line) { Foreground = CmdletBrush });
        }
        else
        {
            textBlock.Inlines.Add(new Run(line) { Foreground = DimmedTextBrush });
        }

        return textBlock;
    }

    /// <summary>
    /// Creates the green injection marker line.
    /// </summary>
    private UIElement CreateMarkerLine(string marker)
    {
        var border = new Border
        {
            BorderBrush = SeparatorBrush,
            BorderThickness = new Thickness(0, 1, 0, 1),
            Padding = new Thickness(0, 6, 0, 6),
            Margin = new Thickness(0, 4, 0, 4),
        };

        var textBlock = new TextBlock
        {
            Text = marker,
            FontFamily = CodeFont,
            FontSize = 12,
            Foreground = MarkerBrush,
            FontWeight = FontWeights.SemiBold,
        };

        border.Child = textBlock;
        return border;
    }

    // Drag-and-drop state
    private int _dragStartIndex = -1;
    private ScriptPhase _dragPhase;
    private Border? _dragBorder;
    private static readonly SolidColorBrush DragOverBrush = new(Color.FromArgb(0x40, 0x00, 0x78, 0xD4));
    private static readonly SolidColorBrush DragGripBrush = new(Color.FromRgb(0x55, 0x5A, 0x66));

    /// <summary>
    /// Creates a bright, interactive function entry with drag-and-drop reordering and a remove button.
    /// </summary>
    private UIElement CreateInteractiveEntry(PSADTFunctionEntry entry, ScriptPhase phase, int index, int totalCount)
    {
        var entryBorder = new Border
        {
            Padding = new Thickness(8, 6, 8, 6),
            Margin = new Thickness(0, 2, 0, 2),
            CornerRadius = new CornerRadius(4),
            BorderBrush = EntryBorderBrush,
            BorderThickness = new Thickness(1),
            Background = EntryHighlightBrush,
            AllowDrop = true,
            Tag = index, // Store index for drop target resolution
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });  // Drag grip
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });  // Remove button

        // Drag grip handle
        var gripLabel = new TextBlock
        {
            Text = "☰", // Hamburger icon as drag handle
            FontSize = 14,
            Foreground = DragGripBrush,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
            Cursor = Cursors.SizeAll,
            ToolTip = "Drag to reorder"
        };
        Grid.SetColumn(gripLabel, 0);
        grid.Children.Add(gripLabel);

        // Code display with syntax highlighting
        var codeBlock = CreateSyntaxHighlightedBlock(entry);
        Grid.SetColumn(codeBlock, 1);
        grid.Children.Add(codeBlock);

        // Remove button only
        var capturedPhase = phase;
        var capturedIndex = index;

        var removeBtn = CreateSmallButton("✕", "Remove");
        removeBtn.Foreground = new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44));
        removeBtn.Click += (s, e) => RemoveEntry(capturedPhase, capturedIndex);
        Grid.SetColumn(removeBtn, 2);
        grid.Children.Add(removeBtn);

        entryBorder.Child = grid;

        // --- Drag-and-drop events ---
        var capturedBorder = entryBorder;

        gripLabel.MouseLeftButtonDown += (s, e) =>
        {
            _dragStartIndex = capturedIndex;
            _dragPhase = capturedPhase;
            _dragBorder = capturedBorder;
            var data = new DataObject("EntryIndex", capturedIndex);
            DragDrop.DoDragDrop(capturedBorder, data, DragDropEffects.Move);
            e.Handled = true;
        };

        entryBorder.DragEnter += (s, e) =>
        {
            if (e.Data.GetDataPresent("EntryIndex"))
            {
                capturedBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(0x00, 0x78, 0xD4));
                capturedBorder.Background = DragOverBrush;
            }
        };

        entryBorder.DragLeave += (s, e) =>
        {
            capturedBorder.BorderBrush = EntryBorderBrush;
            capturedBorder.Background = EntryHighlightBrush;
        };

        entryBorder.DragOver += (s, e) =>
        {
            e.Effects = e.Data.GetDataPresent("EntryIndex") ? DragDropEffects.Move : DragDropEffects.None;
            e.Handled = true;
        };

        entryBorder.Drop += (s, e) =>
        {
            if (!e.Data.GetDataPresent("EntryIndex")) return;

            var fromIndex = (int)e.Data.GetData("EntryIndex");
            var toIndex = capturedIndex;

            if (fromIndex != toIndex)
            {
                var entries = _phaseEntries[capturedPhase];
                var item = entries[fromIndex];
                entries.RemoveAt(fromIndex);
                entries.Insert(toIndex, item);
                UpdatePreview();
            }

            e.Handled = true;
        };

        return entryBorder;
    }

    /// <summary>
    /// Regex to detect placeholder tokens like &lt;FILE_PATH&gt; inside quoted strings.
    /// </summary>
    private static readonly System.Text.RegularExpressions.Regex PlaceholderRegex =
        new(@"<[A-Z_]+>", System.Text.RegularExpressions.RegexOptions.Compiled);

    private UIElement CreateSyntaxHighlightedBlock(PSADTFunctionEntry entry)
    {
        var panel = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };

        var code = entry.GeneratedCode.TrimStart('\t', ' ');
        var parts = code.Split(' ');

        // Function name in yellow
        if (parts.Length > 0)
        {
            panel.Children.Add(CreateCodeLabel(parts[0], BrightCmdletBrush));
        }

        // Parameters
        for (int p = 1; p < parts.Length; p++)
        {
            panel.Children.Add(CreateCodeLabel(" ", Brushes.Transparent));

            var part = parts[p];

            // Check if this part contains an editable placeholder like '<FILE_PATH>'
            if (PlaceholderRegex.IsMatch(part))
            {
                AddEditablePlaceholder(panel, entry, part, p);
            }
            else if (part.StartsWith("-"))
            {
                panel.Children.Add(CreateCodeLabel(part, BrightParamBrush));
            }
            else if (part.StartsWith("'") || part.StartsWith("\""))
            {
                panel.Children.Add(CreateCodeLabel(part, BrightStringBrush));
            }
            else if (part.StartsWith("$") || part.StartsWith("("))
            {
                panel.Children.Add(CreateCodeLabel(part, BrightParamBrush));
            }
            else if (part.StartsWith("@"))
            {
                panel.Children.Add(CreateCodeLabel(part, BrightStringBrush));
            }
            else
            {
                panel.Children.Add(CreateCodeLabel(part, DefaultTextBrush));
            }
        }

        return panel;
    }

    private TextBlock CreateCodeLabel(string text, Brush foreground)
    {
        return new TextBlock
        {
            Text = text,
            FontFamily = CodeFont,
            FontSize = 12,
            Foreground = foreground,
            VerticalAlignment = VerticalAlignment.Center
        };
    }

    /// <summary>
    /// Adds an inline-editable placeholder. Shows as a clickable TextBlock that
    /// becomes a TextBox on click. Edits update the entry's GeneratedCode.
    /// </summary>
    private void AddEditablePlaceholder(WrapPanel panel, PSADTFunctionEntry entry, string fullPart, int partIndex)
    {
        // Split around the placeholder: e.g. "'<FILE_PATH>'" → prefix="'", placeholder="<FILE_PATH>", suffix="'"
        var match = PlaceholderRegex.Match(fullPart);
        var prefix = fullPart[..match.Index];
        var placeholder = match.Value;
        var suffix = fullPart[(match.Index + match.Length)..];

        if (prefix.Length > 0)
            panel.Children.Add(CreateCodeLabel(prefix, BrightStringBrush));

        // The editable placeholder - starts as a styled TextBlock
        var container = new Border
        {
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(2, 0, 2, 0),
            Background = new SolidColorBrush(Color.FromArgb(0x20, 0x9C, 0xDC, 0xFE)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x40, 0x9C, 0xDC, 0xFE)),
            BorderThickness = new Thickness(1),
            Cursor = Cursors.IBeam,
            ToolTip = "Click to edit",
            VerticalAlignment = VerticalAlignment.Center
        };

        var label = new TextBlock
        {
            Text = placeholder,
            FontFamily = CodeFont,
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(0x9C, 0xDC, 0xFE)),
            FontStyle = FontStyles.Italic,
            VerticalAlignment = VerticalAlignment.Center
        };
        container.Child = label;

        var capturedEntry = entry;
        var capturedPartIndex = partIndex;
        var capturedPrefix = prefix;
        var capturedSuffix = suffix;

        container.MouseLeftButtonDown += (s, e) =>
        {
            // Replace label with TextBox for editing
            var currentText = label.Text;
            var isPlaceholder = PlaceholderRegex.IsMatch(currentText);

            var textBox = new TextBox
            {
                Text = isPlaceholder ? "" : currentText,
                FontFamily = CodeFont,
                FontSize = 12,
                Foreground = BrightStringBrush,
                Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x2E)),
                BorderThickness = new Thickness(0),
                MinWidth = 80,
                Padding = new Thickness(0),
                VerticalAlignment = VerticalAlignment.Center,
                CaretBrush = Brushes.White
            };

            container.Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x2E));
            container.Child = textBox;
            textBox.Focus();
            textBox.SelectAll();

            void CommitEdit()
            {
                var newValue = textBox.Text.Trim();
                if (string.IsNullOrEmpty(newValue))
                    newValue = placeholder; // Revert to placeholder if empty

                label.Text = newValue;
                label.FontStyle = PlaceholderRegex.IsMatch(newValue) ? FontStyles.Italic : FontStyles.Normal;
                label.Foreground = PlaceholderRegex.IsMatch(newValue)
                    ? new SolidColorBrush(Color.FromRgb(0x9C, 0xDC, 0xFE))
                    : BrightStringBrush;

                container.Background = new SolidColorBrush(Color.FromArgb(0x20, 0x9C, 0xDC, 0xFE));
                container.Child = label;

                // Update the entry's generated code
                UpdateEntryCode(capturedEntry, capturedPartIndex, capturedPrefix + newValue + capturedSuffix);
            }

            textBox.LostFocus += (_, _) => CommitEdit();
            textBox.KeyDown += (_, ke) =>
            {
                if (ke.Key == Key.Enter || ke.Key == Key.Escape)
                {
                    if (ke.Key == Key.Escape)
                        textBox.Text = label.Text; // Revert
                    CommitEdit();
                    ke.Handled = true;
                }
            };

            e.Handled = true;
        };

        panel.Children.Add(container);

        if (suffix.Length > 0)
            panel.Children.Add(CreateCodeLabel(suffix, BrightStringBrush));
    }

    /// <summary>
    /// Updates a specific part (by space-split index) in the entry's generated code.
    /// </summary>
    private static void UpdateEntryCode(PSADTFunctionEntry entry, int partIndex, string newPart)
    {
        var trimmed = entry.GeneratedCode.TrimStart('\t', ' ');
        var leadingWhitespace = entry.GeneratedCode[..^trimmed.Length];
        var parts = trimmed.Split(' ');

        if (partIndex >= 0 && partIndex < parts.Length)
        {
            parts[partIndex] = newPart;
        }

        entry.GeneratedCode = leadingWhitespace + string.Join(" ", parts);
    }

    private Button CreateSmallButton(string content, string tooltip)
    {
        var btn = new Button
        {
            Content = content,
            ToolTip = tooltip,
        };
        btn.SetResourceReference(StyleProperty, "PreviewActionButton");
        return btn;
    }

    #endregion

    #region Copy Preview

    private void CopyPreview_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var phase = GetActivePhase();
            var entries = _phaseEntries[phase];
            if (entries.Count == 0) return;

            var code = string.Join("\n", entries.Select(en => en.GeneratedCode));
            if (!string.IsNullOrEmpty(code))
            {
                Clipboard.SetText(code);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to copy preview: {ex.Message}", "Copy Error",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    #endregion

    #region Package Info & Options

    public void SetPackageInfo(string manufacturer, string appName, string version, string packageType, string sourceFileName = "", string msiProductCode = "")
    {
        _packageType = packageType;
        _manufacturer = manufacturer;
        _appName = appName;
        _version = version;
        _sourceFileName = sourceFileName;
        _msiProductCode = msiProductCode;
        PackageInfoText.Text = $"Configure for: {manufacturer} {appName} v{version} ({packageType})";
    }

    public void LoadOptions(PSADTOptions options)
    {
        SelectedOptions = options;

        // Load phase entries from options
        foreach (var kvp in options.PhaseEntries)
        {
            _phaseEntries[kvp.Key] = new List<PSADTFunctionEntry>(kvp.Value);
        }

        UpdatePreview();
        UpdateTabBadges();
        UpdateSelectedCount();
    }

    private void UpdateSelectedCount()
    {
        int total = _phaseEntries.Values.Sum(e => e.Count);
        SelectedCountText.Text = total.ToString();
    }

    #endregion

    #region Apply / Cancel

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        SelectedOptions = new PSADTOptions
        {
            PackageType = _packageType,
            PhaseEntries = _phaseEntries.ToDictionary(
                kvp => kvp.Key,
                kvp => new List<PSADTFunctionEntry>(kvp.Value))
        };

        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    #endregion

    #region Helpers

    private static string GetPhaseDisplayName(ScriptPhase phase)
    {
        return phase switch
        {
            ScriptPhase.PreInstallation => "Pre-Installation",
            ScriptPhase.Installation => "Installation",
            ScriptPhase.PostInstallation => "Post-Installation",
            ScriptPhase.PreUninstallation => "Pre-Uninstallation",
            ScriptPhase.Uninstallation => "Uninstallation",
            ScriptPhase.PostUninstallation => "Post-Uninstallation",
            _ => phase.ToString()
        };
    }

    /// <summary>
    /// Builds a syntax-highlighted tooltip TextBlock for a function, e.g.:
    /// Copy-ADTFile [-Path] &lt;String[]&gt; [-Destination] &lt;String&gt; [-Recurse] [&lt;CommonParameters&gt;]
    /// </summary>
    private static TextBlock BuildSyntaxTooltip(PSADTFunction func)
    {
        var tb = new TextBlock
        {
            FontFamily = CodeFont,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 520
        };

        // Function name: verb in blue, noun in white
        var dashIdx = func.Name.IndexOf('-');
        if (dashIdx > 0)
        {
            tb.Inlines.Add(new Run(func.Name[..(dashIdx + 1)]) { Foreground = VerbBrush, FontWeight = FontWeights.SemiBold });
            tb.Inlines.Add(new Run(func.Name[(dashIdx + 1)..]) { Foreground = TooltipFuncBrush, FontWeight = FontWeights.SemiBold });
        }
        else
        {
            tb.Inlines.Add(new Run(func.Name) { Foreground = TooltipFuncBrush, FontWeight = FontWeights.SemiBold });
        }

        var realParams = func.Parameters
            .Where(p => p.Name != "(none)")
            .ToList();

        foreach (var param in realParams)
        {
            tb.Inlines.Add(new Run(" "));

            if (param.IsSwitch)
            {
                tb.Inlines.Add(new Run("[") { Foreground = TooltipBracketBrush });
                tb.Inlines.Add(new Run($"-{param.Name}") { Foreground = TooltipParamBrush });
                tb.Inlines.Add(new Run("]") { Foreground = TooltipBracketBrush });
            }
            else if (param.Mandatory)
            {
                tb.Inlines.Add(new Run("[") { Foreground = TooltipBracketBrush });
                tb.Inlines.Add(new Run($"-{param.Name}") { Foreground = TooltipParamBrush });
                tb.Inlines.Add(new Run("] ") { Foreground = TooltipBracketBrush });
                tb.Inlines.Add(new Run($"<{param.Type}>") { Foreground = TooltipTypeBrush });
            }
            else
            {
                tb.Inlines.Add(new Run("[[") { Foreground = TooltipBracketBrush });
                tb.Inlines.Add(new Run($"-{param.Name}") { Foreground = TooltipParamBrush });
                tb.Inlines.Add(new Run("] ") { Foreground = TooltipBracketBrush });
                tb.Inlines.Add(new Run($"<{param.Type}>") { Foreground = TooltipTypeBrush });
                tb.Inlines.Add(new Run("]") { Foreground = TooltipBracketBrush });
            }
        }

        tb.Inlines.Add(new Run(" "));
        tb.Inlines.Add(new Run("[") { Foreground = TooltipBracketBrush });
        tb.Inlines.Add(new Run("<CommonParameters>") { Foreground = TooltipTypeBrush });
        tb.Inlines.Add(new Run("]") { Foreground = TooltipBracketBrush });

        return tb;
    }

    #endregion
}
