using Avalonia.Controls;
using Avalonia.Threading;
using Codomon.Desktop.Models;
using Codomon.Desktop.ViewModels;

namespace Codomon.Desktop.Views;

/// <summary>
/// Multi-step Roslyn scan dialog.
/// Step 1 — Preflight check: verifies source path and .cs file availability.
/// Step 2 — Scanning: background scan with live progress messages.
/// Step 3 — Results: code structure tree and suggested connections.
///
/// Closing the dialog returns the <see cref="RoslynScanViewModel"/> so the caller
/// can retrieve promoted connections.
/// </summary>
public partial class RoslynScanDialog : Window
{
    private readonly RoslynScanViewModel _vm;
    private bool _dialogResultSet;

    public RoslynScanDialog(RoslynScanViewModel vm)
    {
        InitializeComponent();
        _vm = vm;

        // Subscribe to VM property changes to drive UI state.
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(RoslynScanViewModel.Step))
                ApplyStep();
            else if (e.PropertyName is nameof(RoslynScanViewModel.ScanFinished)
                                    or nameof(RoslynScanViewModel.IsRunning))
                RefreshButtons();
            else if (e.PropertyName == nameof(RoslynScanViewModel.CanPromote))
                RefreshPromoteButton();
        };

        // Subscribe to progress messages collection so the ListBox auto-scrolls.
        _vm.ProgressMessages.CollectionChanged += (_, _) =>
            Dispatcher.UIThread.Post(ScrollProgressToBottom);

        Opened += async (_, _) => await RunPreflightAsync();

        // When the dialog is closed via the title-bar X button, Avalonia returns null from
        // ShowDialog<T> unless we supply the result explicitly here.
        // NOTE: _dialogResultSet is only set to true once (either here or in OnCloseClick).
        // Both handlers run on the UI thread so there is no actual race, but the flag
        // prevents the Closing event from calling Close(_vm) a second time after OnCloseClick
        // has already done so.
        Closing += (_, args) =>
        {
            if (_dialogResultSet) return;  // Already closed via the Close button — proceed normally.

            // Title-bar X was used: cancel this close and re-issue it with the VM as the result
            // so that ShowDialog<RoslynScanViewModel?> receives the promoted connections.
            if (_vm.IsRunning)
                _vm.CancelScan();
            _dialogResultSet = true;
            args.Cancel = true;
            Dispatcher.UIThread.Post(() => Close(_vm));
        };
    }

    // ── Preflight ─────────────────────────────────────────────────────────────

    private async Task RunPreflightAsync()
    {
        await _vm.RunPreflightAsync();

        var icon = this.FindControl<TextBlock>("PreflightStatusIcon");
        var msgText = this.FindControl<TextBlock>("PreflightMessageText");
        var detailsCard = this.FindControl<Border>("PreflightDetailsCard");
        var sourceText = this.FindControl<TextBlock>("PreflightSourcePathText");
        var fileCountText = this.FindControl<TextBlock>("PreflightFileCountText");
        var dotnetText = this.FindControl<TextBlock>("PreflightDotnetText");
        var startBtn = this.FindControl<Button>("StartScanButton");

        if (icon != null)
            icon.Text = _vm.PreflightOk ? "✔" : "✖";

        if (msgText != null)
        {
            msgText.Text = _vm.PreflightMessage;
            msgText.Foreground = _vm.PreflightOk
                ? Avalonia.Media.Brushes.LightGreen
                : new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#FF8888"));
        }

        if (_vm.PreflightOk && detailsCard != null)
        {
            detailsCard.IsVisible = true;
            if (sourceText != null) sourceText.Text = _vm.SourcePath;
            if (fileCountText != null) fileCountText.Text = _vm.CsFileCount.ToString();
            if (dotnetText != null) dotnetText.Text = _vm.DotnetVersion ?? "not detected";
        }

        if (startBtn != null)
            startBtn.IsEnabled = _vm.PreflightOk;
    }

    // ── Button handlers ───────────────────────────────────────────────────────

    private async void OnStartScanClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        await _vm.StartScanAsync();
        RefreshButtons();
    }

    private void OnCancelScanClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _vm.CancelScan();
    }

    private void OnViewResultsClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _vm.ShowResults();
        PopulateCodeTree();
        PopulateSuggestedConnectionsList();
    }

    private void OnCloseClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_vm.IsRunning)
            _vm.CancelScan();
        _dialogResultSet = true;
        Close(_vm);
    }

    private void OnPromoteClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_vm.SelectedSuggestedConnection == null) return;
        _vm.PromoteConnection(_vm.SelectedSuggestedConnection);
        RefreshPromoteButton();
        // Refresh the connections list to reflect the promoted state.
        PopulateSuggestedConnectionsList();
    }

    private void OnSuggestedConnectionSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count == 0) return;
        if (e.AddedItems[0] is not ListBoxItem item) return;
        _vm.SelectedSuggestedConnection = item.Tag as SuggestedConnection;
        RefreshPromoteButton();
    }

    private void OnCodeTreeSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count == 0 || e.AddedItems[0] is not TreeViewItem item) return;

        if (item.Tag is ScannedFile file)
        {
            _vm.SelectedFile = file;
            ShowFileDetail(file);
        }
        else if (item.Tag is ScannedClass cls)
        {
            _vm.SelectedClass = cls;
            ShowClassDetail(cls);
        }
        else if (item.Tag is ScannedMethod method)
        {
            ShowMethodDetail(method);
        }
    }

    // ── Step management ───────────────────────────────────────────────────────

    private void ApplyStep()
    {
        var preflightPanel = this.FindControl<DockPanel>("PreflightPanel");
        var scanningPanel  = this.FindControl<DockPanel>("ScanningPanel");
        var resultsPanel   = this.FindControl<Grid>("ResultsPanel");
        var stepTitle      = this.FindControl<TextBlock>("StepTitleText");
        var dot1 = this.FindControl<Avalonia.Controls.Shapes.Ellipse>("Dot1");
        var dot2 = this.FindControl<Avalonia.Controls.Shapes.Ellipse>("Dot2");
        var dot3 = this.FindControl<Avalonia.Controls.Shapes.Ellipse>("Dot3");

        var activeFill   = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#3A8FBF"));
        var inactiveFill = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#2A3F5A"));
        var doneFill     = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#2A7A4A"));

        switch (_vm.Step)
        {
            case ScanDialogStep.Preflight:
                if (preflightPanel != null) preflightPanel.IsVisible = true;
                if (scanningPanel  != null) scanningPanel.IsVisible  = false;
                if (resultsPanel   != null) resultsPanel.IsVisible   = false;
                if (stepTitle      != null) stepTitle.Text = "Step 1 of 3 — Preflight Check";
                if (dot1 != null) dot1.Fill = activeFill;
                if (dot2 != null) dot2.Fill = inactiveFill;
                if (dot3 != null) dot3.Fill = inactiveFill;
                break;

            case ScanDialogStep.Scanning:
                if (preflightPanel != null) preflightPanel.IsVisible = false;
                if (scanningPanel  != null) scanningPanel.IsVisible  = true;
                if (resultsPanel   != null) resultsPanel.IsVisible   = false;
                if (stepTitle      != null) stepTitle.Text = "Step 2 of 3 — Scanning…";
                if (dot1 != null) dot1.Fill = doneFill;
                if (dot2 != null) dot2.Fill = activeFill;
                if (dot3 != null) dot3.Fill = inactiveFill;
                // Bind progress list.
                var progressLb = this.FindControl<ListBox>("ProgressListBox");
                if (progressLb != null) progressLb.ItemsSource ??= _vm.ProgressMessages;
                break;

            case ScanDialogStep.Results:
                if (preflightPanel != null) preflightPanel.IsVisible = false;
                if (scanningPanel  != null) scanningPanel.IsVisible  = false;
                if (resultsPanel   != null) resultsPanel.IsVisible   = true;
                if (stepTitle      != null) stepTitle.Text = "Step 3 of 3 — Browse Results";
                if (dot1 != null) dot1.Fill = doneFill;
                if (dot2 != null) dot2.Fill = doneFill;
                if (dot3 != null) dot3.Fill = activeFill;
                break;
        }

        RefreshButtons();
    }

    private void RefreshButtons()
    {
        var startBtn       = this.FindControl<Button>("StartScanButton");
        var cancelScanBtn  = this.FindControl<Button>("CancelScanButton");
        var viewResultsBtn = this.FindControl<Button>("ViewResultsButton");
        var progressBar    = this.FindControl<ProgressBar>("ScanProgressBar");

        switch (_vm.Step)
        {
            case ScanDialogStep.Preflight:
                if (startBtn      != null) { startBtn.IsVisible      = true;  startBtn.IsEnabled = _vm.PreflightOk; }
                if (cancelScanBtn != null)   cancelScanBtn.IsVisible  = false;
                if (viewResultsBtn!= null)   viewResultsBtn.IsVisible = false;
                break;

            case ScanDialogStep.Scanning:
                if (startBtn      != null) startBtn.IsVisible       = false;
                if (cancelScanBtn != null) cancelScanBtn.IsVisible  = _vm.IsRunning;
                if (viewResultsBtn!= null) viewResultsBtn.IsVisible = _vm.ScanFinished;
                if (progressBar   != null) progressBar.IsIndeterminate = _vm.IsRunning;
                break;

            case ScanDialogStep.Results:
                if (startBtn      != null) startBtn.IsVisible       = false;
                if (cancelScanBtn != null) cancelScanBtn.IsVisible  = false;
                if (viewResultsBtn!= null) viewResultsBtn.IsVisible = false;
                break;
        }
    }

    private void RefreshPromoteButton()
    {
        var btn = this.FindControl<Button>("PromoteButton");
        if (btn != null) btn.IsEnabled = _vm.CanPromote;
    }

    // ── Code tree population ──────────────────────────────────────────────────

    private void PopulateCodeTree()
    {
        var tree = this.FindControl<TreeView>("CodeTreeView");
        if (tree == null || _vm.ScanResult == null) return;

        var items = new List<TreeViewItem>();

        foreach (var project in _vm.ScanResult.Projects)
        {
            var projectNode = new TreeViewItem
            {
                Header = BuildHeader("📁", project.Name, "#AABBCC"),
                IsExpanded = true
            };

            // Group files by relative folder.
            var projectFiles = _vm.ScanResult.Files
                .Where(f => project.FilePaths.Contains(f.FilePath))
                .OrderBy(f => f.RelativePath)
                .ToList();

            foreach (var file in projectFiles)
            {
                var fileNode = new TreeViewItem
                {
                    Header = BuildHeader("📄", Path.GetFileName(file.RelativePath), "#AABBCC"),
                    Tag = file
                };

                foreach (var cls in file.Classes)
                {
                    var icon = cls.Kind switch
                    {
                        "interface" => "⬡",
                        "record" or "record struct" => "◈",
                        _ => "◇"
                    };
                    var classNode = new TreeViewItem
                    {
                        Header = BuildHeader(icon, cls.SimpleName, "#88CCFF"),
                        Tag = cls
                    };

                    foreach (var method in cls.Methods)
                    {
                        classNode.Items.Add(new TreeViewItem
                        {
                            Header = BuildHeader("ƒ", method.Name, "#CCAAFF"),
                            Tag = method
                        });
                    }

                    fileNode.Items.Add(classNode);
                }

                projectNode.Items.Add(fileNode);
            }

            items.Add(projectNode);
        }

        tree.ItemsSource = items;
    }

    private void PopulateSuggestedConnectionsList()
    {
        var listBox = this.FindControl<ListBox>("SuggestedConnectionsListBox");
        if (listBox == null || _vm.ScanResult == null) return;

        listBox.Items.Clear();

        foreach (var conn in _vm.ScanResult.SuggestedConnections)
        {
            var promoted = conn.IsPromoted;
            var label = $"{ShortName(conn.FromClass)} → {ShortName(conn.ToClass)}  " +
                        $"({conn.CallCount} call{(conn.CallCount == 1 ? "" : "s")})";

            if (promoted) label = $"[promoted]  {label}";

            listBox.Items.Add(new ListBoxItem
            {
                Tag = conn,
                IsEnabled = !promoted,
                Content = new StackPanel
                {
                    Orientation = Avalonia.Layout.Orientation.Horizontal,
                    Spacing = 8,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = promoted ? "🔗" : "⚡",
                            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
                        },
                        new TextBlock
                        {
                            Text = label,
                            Foreground = promoted
                                ? new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#558855"))
                                : Avalonia.Media.Brushes.LightGray,
                            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                            FontSize = 12
                        },
                        new Border
                        {
                            Background = new Avalonia.Media.SolidColorBrush(
                                Avalonia.Media.Color.Parse("#1A3A5A")),
                            CornerRadius = new Avalonia.CornerRadius(3),
                            Padding = new Avalonia.Thickness(4, 1),
                            Child = new TextBlock
                            {
                                Text = "ROSLYN",
                                FontSize = 9,
                                Foreground = new Avalonia.Media.SolidColorBrush(
                                    Avalonia.Media.Color.Parse("#4A9FBF")),
                                FontWeight = Avalonia.Media.FontWeight.Bold,
                                LetterSpacing = 1
                            }
                        }
                    }
                }
            });
        }

        if (_vm.ScanResult.SuggestedConnections.Count == 0)
        {
            listBox.Items.Add(new ListBoxItem
            {
                IsEnabled = false,
                Content = new TextBlock
                {
                    Text = "No inter-class call relationships detected.",
                    Foreground = new Avalonia.Media.SolidColorBrush(
                        Avalonia.Media.Color.Parse("#556677")),
                    FontSize = 12,
                    Margin = new Avalonia.Thickness(8, 6)
                }
            });
        }
    }

    // ── Detail panels ─────────────────────────────────────────────────────────

    private void ShowFileDetail(ScannedFile file)
    {
        var title = this.FindControl<TextBlock>("DetailTitleText");
        var panel = this.FindControl<StackPanel>("DetailStackPanel");
        if (title == null || panel == null) return;

        title.Text = $"📄 {file.RelativePath}";
        panel.Children.Clear();

        panel.Children.Add(DetailRow("Classes", file.Classes.Count.ToString()));
        panel.Children.Add(DetailRow("Methods", file.Classes.Sum(c => c.Methods.Count).ToString()));

        var loggingCount = file.Classes
            .SelectMany(c => c.Methods)
            .Sum(m => m.LoggingCalls.Count);
        if (loggingCount > 0)
            panel.Children.Add(DetailRow("Logging calls", loggingCount.ToString()));
    }

    private void ShowClassDetail(ScannedClass cls)
    {
        var title = this.FindControl<TextBlock>("DetailTitleText");
        var panel = this.FindControl<StackPanel>("DetailStackPanel");
        if (title == null || panel == null) return;

        title.Text = $"◇ {cls.FullName}";
        panel.Children.Clear();

        panel.Children.Add(DetailRow("Kind", cls.Kind));
        panel.Children.Add(DetailRow("Namespace", string.IsNullOrEmpty(cls.Namespace) ? "(global)" : cls.Namespace));
        panel.Children.Add(DetailRow("Methods", cls.Methods.Count.ToString()));

        var loggingTotal = cls.Methods.Sum(m => m.LoggingCalls.Count);
        if (loggingTotal > 0)
            panel.Children.Add(DetailRow("Logging calls", loggingTotal.ToString()));

        if (cls.Methods.Count > 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = "Methods:",
                Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#778899")),
                FontSize = 11,
                Margin = new Avalonia.Thickness(0, 6, 0, 2)
            });

            foreach (var method in cls.Methods)
            {
                var loggingBadge = method.LoggingCalls.Count > 0
                    ? $"  [log×{method.LoggingCalls.Count}]" : string.Empty;
                panel.Children.Add(new TextBlock
                {
                    Text = $"  {method.Accessibility} {method.ReturnType} {method.Name}(){loggingBadge}  :{method.LineNumber}",
                    Foreground = Avalonia.Media.Brushes.LightGray,
                    FontFamily = new Avalonia.Media.FontFamily("Monospace"),
                    FontSize = 11
                });
            }
        }
    }

    private void ShowMethodDetail(ScannedMethod method)
    {
        var title = this.FindControl<TextBlock>("DetailTitleText");
        var panel = this.FindControl<StackPanel>("DetailStackPanel");
        if (title == null || panel == null) return;

        title.Text = $"ƒ {method.Name}";
        panel.Children.Clear();

        panel.Children.Add(DetailRow("Return type", method.ReturnType));
        panel.Children.Add(DetailRow("Accessibility", method.Accessibility));
        panel.Children.Add(DetailRow("Line", method.LineNumber.ToString()));

        if (method.CalledClasses.Count > 0)
        {
            panel.Children.Add(DetailRow("Calls classes", string.Join(", ", method.CalledClasses)));
        }

        if (method.LoggingCalls.Count > 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = $"Logging calls ({method.LoggingCalls.Count}):",
                Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#778899")),
                FontSize = 11,
                Margin = new Avalonia.Thickness(0, 6, 0, 2)
            });
            foreach (var log in method.LoggingCalls)
            {
                panel.Children.Add(new TextBlock
                {
                    Text = $"  {log.LoggerExpression}.{log.LogLevel}  :{log.LineNumber}",
                    Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#88CCAA")),
                    FontFamily = new Avalonia.Media.FontFamily("Monospace"),
                    FontSize = 11
                });
            }
        }
    }

    // ── Utility helpers ───────────────────────────────────────────────────────

    private static Control DetailRow(string label, string value)
    {
        return new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("140,*"),
            Margin = new Avalonia.Thickness(0, 1)
        }.Also(g =>
        {
            g.Children.Add(new TextBlock
            {
                Text = label + ":",
                Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#778899")),
                FontSize = 11,
                [Grid.ColumnProperty] = 0
            });
            g.Children.Add(new TextBlock
            {
                Text = value,
                Foreground = Avalonia.Media.Brushes.LightGray,
                FontSize = 11,
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                [Grid.ColumnProperty] = 1
            });
        });
    }

    private static StackPanel BuildHeader(string icon, string label, string colorHex)
    {
        return new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            Spacing = 5,
            Children =
            {
                new TextBlock { Text = icon, FontSize = 11,
                    Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#667788")) },
                new TextBlock { Text = label, FontSize = 12,
                    Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse(colorHex)) }
            }
        };
    }

    private void ScrollProgressToBottom()
    {
        var lb = this.FindControl<ListBox>("ProgressListBox");
        if (lb == null || _vm.ProgressMessages.Count == 0) return;
        var last = _vm.ProgressMessages[^1];
        lb.ItemsSource ??= _vm.ProgressMessages;
        lb.ScrollIntoView(last);
    }

    private static string ShortName(string fullName)
    {
        var idx = fullName.LastIndexOf('.');
        return idx >= 0 ? fullName[(idx + 1)..] : fullName;
    }
}

/// <summary>Fluent helper for inline object configuration.</summary>
internal static class FluentExtensions
{
    public static T Also<T>(this T obj, Action<T> configure)
    {
        configure(obj);
        return obj;
    }
}
