using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Codomon.Desktop.Models;
using Codomon.Desktop.ViewModels;

namespace Codomon.Desktop.Views;

/// <summary>
/// Multi-step dialog for tech stack scanning.
/// Step 1 — Preflight check.
/// Step 2 — Scan progress.
/// Step 3 — Browse detected technologies.
/// </summary>
public partial class TechStackScanDialog : Window
{
    private readonly TechStackScanViewModel _vm;
    private bool _dialogResultSet;
    private string? _selectedCategory;
    private string? _selectedProject;

    public TechStackScanDialog()
        : this(new TechStackScanViewModel(string.Empty, string.Empty))
    {
    }

    public TechStackScanDialog(TechStackScanViewModel vm)
    {
        InitializeComponent();
        _vm = vm;

        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(TechStackScanViewModel.Step))
                ApplyStep();
            else if (e.PropertyName is nameof(TechStackScanViewModel.ScanFinished)
                                     or nameof(TechStackScanViewModel.IsRunning))
            {
                RefreshScanningState();
                RefreshButtons();
            }
            else if (e.PropertyName is nameof(TechStackScanViewModel.IsRestoredScanLoaded)
                                    or nameof(TechStackScanViewModel.RestoredScanLabel))
                RefreshRestoredScanBanner();
            else if (e.PropertyName == nameof(TechStackScanViewModel.ScanResult))
            {
                PopulateCategoryList();
                PopulateProjectFilter();
                PopulateTechnologyList();
                RefreshSummary();
            }
        };

        _vm.ProgressMessages.CollectionChanged += (_, _) =>
            Dispatcher.UIThread.Post(ScrollProgressToBottom);

        Opened += async (_, _) => await InitializeAsync();

        Closing += (_, args) =>
        {
            if (_dialogResultSet) return;

            if (_vm.IsRunning)
                _vm.CancelScan();
            _dialogResultSet = true;
            args.Cancel = true;
            Dispatcher.UIThread.Post(() => Close(_vm));
        };
    }

    private async Task InitializeAsync()
    {
        if (await _vm.TryRestoreLatestSavedScanAsync())
        {
            PopulateCategoryList();
            PopulateProjectFilter();
            PopulateTechnologyList();
            RefreshSummary();
            ApplyStep();
            return;
        }

        await RunPreflightAsync();
        ApplyStep();
    }

    private async Task RunPreflightAsync()
    {
        await _vm.RunPreflightAsync();

        var icon = this.FindControl<TextBlock>("PreflightStatusIcon");
        var msgText = this.FindControl<TextBlock>("PreflightMessageText");
        var detailsCard = this.FindControl<Border>("PreflightDetailsCard");
        var sourceText = this.FindControl<TextBlock>("PreflightSourcePathText");
        var projectCountText = this.FindControl<TextBlock>("PreflightProjectCountText");
        var markerCountText = this.FindControl<TextBlock>("PreflightMarkerCountText");
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
            if (projectCountText != null) projectCountText.Text = _vm.ProjectCount.ToString();
            if (markerCountText != null) markerCountText.Text = _vm.MarkerCount.ToString();
        }

        if (startBtn != null)
            startBtn.IsEnabled = _vm.PreflightOk;
    }

    private async void OnStartScanClick(object? sender, RoutedEventArgs e)
    {
        await _vm.StartScanAsync();
        RefreshButtons();
    }

    private async void OnRescanClick(object? sender, RoutedEventArgs e)
    {
        await _vm.StartScanAsync();
        RefreshButtons();
    }

    private void OnCancelScanClick(object? sender, RoutedEventArgs e)
        => _vm.CancelScan();

    private void OnViewResultsClick(object? sender, RoutedEventArgs e)
    {
        _vm.ShowResults();
        PopulateCategoryList();
        PopulateProjectFilter();
        PopulateTechnologyList();
        RefreshSummary();
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        if (_vm.IsRunning)
            _vm.CancelScan();
        _dialogResultSet = true;
        Close(_vm);
    }

    private void OnCategorySelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count == 0)
            return;

        _selectedCategory = e.AddedItems[0] as string;
        PopulateTechnologyList();
    }

    private void OnProjectFilterSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox comboBox)
            _selectedProject = comboBox.SelectedItem?.ToString();

        PopulateTechnologyList();
    }

    private void OnTechnologySelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count == 0 || e.AddedItems[0] is not ListBoxItem item)
            return;

        if (item.Tag is DetectedTechnology technology)
        {
            _vm.SelectedTechnology = technology;
            ShowTechnologyDetail(technology);
        }
    }

    private void ApplyStep()
    {
        var preflightPanel = this.FindControl<DockPanel>("PreflightPanel");
        var scanningPanel = this.FindControl<DockPanel>("ScanningPanel");
        var resultsPanel = this.FindControl<Grid>("ResultsPanel");
        var stepTitle = this.FindControl<TextBlock>("StepTitleText");
        var dot1 = this.FindControl<Avalonia.Controls.Shapes.Ellipse>("Dot1");
        var dot2 = this.FindControl<Avalonia.Controls.Shapes.Ellipse>("Dot2");
        var dot3 = this.FindControl<Avalonia.Controls.Shapes.Ellipse>("Dot3");

        var activeFill = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#3A8FBF"));
        var inactiveFill = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#2A3F5A"));
        var doneFill = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#2A7A4A"));

        switch (_vm.Step)
        {
            case TechStackScanDialogStep.Preflight:
                if (preflightPanel != null) preflightPanel.IsVisible = true;
                if (scanningPanel != null) scanningPanel.IsVisible = false;
                if (resultsPanel != null) resultsPanel.IsVisible = false;
                if (stepTitle != null) stepTitle.Text = "Step 1 of 3 — Preflight Check";
                if (dot1 != null) dot1.Fill = activeFill;
                if (dot2 != null) dot2.Fill = inactiveFill;
                if (dot3 != null) dot3.Fill = inactiveFill;
                break;

            case TechStackScanDialogStep.Scanning:
                if (preflightPanel != null) preflightPanel.IsVisible = false;
                if (scanningPanel != null) scanningPanel.IsVisible = true;
                if (resultsPanel != null) resultsPanel.IsVisible = false;
                if (stepTitle != null) stepTitle.Text = "Step 2 of 3 — Scanning…";
                if (dot1 != null) dot1.Fill = doneFill;
                if (dot2 != null) dot2.Fill = activeFill;
                if (dot3 != null) dot3.Fill = inactiveFill;
                var progressListBox = this.FindControl<ListBox>("ProgressListBox");
                if (progressListBox != null) progressListBox.ItemsSource ??= _vm.ProgressMessages;
                break;

            case TechStackScanDialogStep.Results:
                if (preflightPanel != null) preflightPanel.IsVisible = false;
                if (scanningPanel != null) scanningPanel.IsVisible = false;
                if (resultsPanel != null) resultsPanel.IsVisible = true;
                if (stepTitle != null) stepTitle.Text = "Step 3 of 3 — Browse Results";
                if (dot1 != null) dot1.Fill = doneFill;
                if (dot2 != null) dot2.Fill = doneFill;
                if (dot3 != null) dot3.Fill = activeFill;
                PopulateCategoryList();
                PopulateProjectFilter();
                PopulateTechnologyList();
                RefreshSummary();
                break;
        }

        RefreshButtons();
        RefreshScanningState();
        RefreshRestoredScanBanner();
    }

    private void RefreshButtons()
    {
        var startButton = this.FindControl<Button>("StartScanButton");
        var cancelButton = this.FindControl<Button>("CancelScanButton");
        var viewResultsButton = this.FindControl<Button>("ViewResultsButton");
        var rescanButton = this.FindControl<Button>("RescanButton");
        var okButton = this.FindControl<Button>("OkButton");

        if (startButton != null)
            startButton.IsVisible = _vm.Step == TechStackScanDialogStep.Preflight;

        if (cancelButton != null)
            cancelButton.IsVisible = _vm.Step == TechStackScanDialogStep.Scanning && _vm.IsRunning;

        if (viewResultsButton != null)
            viewResultsButton.IsVisible = _vm.Step == TechStackScanDialogStep.Scanning && _vm.ScanFinished;

        if (rescanButton != null)
            rescanButton.IsVisible = _vm.Step == TechStackScanDialogStep.Results;

        if (okButton != null)
            okButton.IsVisible = _vm.Step == TechStackScanDialogStep.Results;
    }

    private void RefreshScanningState()
    {
        var statusText = this.FindControl<TextBlock>("ScanningStatusText");
        var progressBar = this.FindControl<ProgressBar>("ScanningProgressBar");

        if (statusText != null)
        {
            statusText.Text = _vm.Step == TechStackScanDialogStep.Scanning && !_vm.IsRunning && _vm.ScanFinished
                ? "Tech stack scan complete. Review the log below or continue to the results view."
                : "Tech stack scan in progress — reading project files and known stack markers…";
        }

        if (progressBar != null)
        {
            var showProgress = _vm.Step == TechStackScanDialogStep.Scanning && _vm.IsRunning;
            progressBar.IsVisible = showProgress;
            progressBar.IsIndeterminate = showProgress;
        }
    }

    private void RefreshRestoredScanBanner()
    {
        var banner = this.FindControl<Border>("RestoredScanBanner");
        var bannerText = this.FindControl<TextBlock>("RestoredScanBannerText");

        if (banner != null)
            banner.IsVisible = _vm.Step == TechStackScanDialogStep.Results && _vm.IsRestoredScanLoaded;
        if (bannerText != null)
            bannerText.Text = _vm.RestoredScanLabel;
    }

    private void PopulateCategoryList()
    {
        var listBox = this.FindControl<ListBox>("CategoryListBox");
        if (listBox == null)
            return;

        var categories = new List<string> { "All categories" };
        categories.AddRange(_vm.CategoryNames);
        listBox.ItemsSource = categories;

        if (_selectedCategory == null || !categories.Contains(_selectedCategory))
            _selectedCategory = categories[0];

        listBox.SelectedItem = _selectedCategory;
    }

    private void PopulateProjectFilter()
    {
        var comboBox = this.FindControl<ComboBox>("ProjectFilterComboBox");
        if (comboBox == null)
            return;

        var projects = new List<string> { "All projects" };
        projects.AddRange(_vm.ProjectNames);
        comboBox.ItemsSource = projects;

        if (_selectedProject == null || !projects.Contains(_selectedProject))
            _selectedProject = projects[0];

        comboBox.SelectedItem = _selectedProject;
    }

    private void PopulateTechnologyList()
    {
        var listBox = this.FindControl<ListBox>("TechnologyListBox");
        if (listBox == null)
            return;

        listBox.Items.Clear();

        var technologies = (_vm.ScanResult?.Technologies ?? new List<DetectedTechnology>())
            .Where(technology => _selectedCategory == null ||
                                 _selectedCategory == "All categories" ||
                                 string.Equals(technology.Category, _selectedCategory, StringComparison.OrdinalIgnoreCase))
            .Where(technology => _selectedProject == null ||
                                 _selectedProject == "All projects" ||
                                 string.Equals(string.IsNullOrWhiteSpace(technology.ProjectName) ? "(workspace)" : technology.ProjectName,
                                     _selectedProject, StringComparison.OrdinalIgnoreCase))
            .OrderBy(technology => technology.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(technology => technology.ProjectName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var technology in technologies)
        {
            var projectLabel = string.IsNullOrWhiteSpace(technology.ProjectName) ? "(workspace)" : technology.ProjectName;
            listBox.Items.Add(new ListBoxItem
            {
                Tag = technology,
                Content = new StackPanel
                {
                    Spacing = 2,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = technology.Name,
                            Foreground = Avalonia.Media.Brushes.White,
                            FontWeight = Avalonia.Media.FontWeight.SemiBold
                        },
                        new TextBlock
                        {
                            Text = $"{technology.Category} · {projectLabel}",
                            Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#88AABB")),
                            FontSize = 11
                        }
                    }
                }
            });
        }

        if (technologies.Count == 0)
        {
            listBox.Items.Add(new ListBoxItem
            {
                Content = new TextBlock
                {
                    Text = "No technologies match the current filters.",
                    Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#778899")),
                    FontStyle = Avalonia.Media.FontStyle.Italic
                },
                IsEnabled = false
            });
            return;
        }

        if (_vm.SelectedTechnology != null)
        {
            var selectedItem = listBox.Items.OfType<ListBoxItem>()
                .FirstOrDefault(item => ReferenceEquals(item.Tag, _vm.SelectedTechnology));
            if (selectedItem != null)
                listBox.SelectedItem = selectedItem;
        }
        else if (listBox.Items.Count > 0)
        {
            listBox.SelectedIndex = 0;
        }
    }

    private void RefreshSummary()
    {
        var summaryText = this.FindControl<TextBlock>("ResultsSummaryText");
        if (summaryText == null)
            return;

        var result = _vm.ScanResult;
        if (result == null)
        {
            summaryText.Text = "No technologies loaded.";
            return;
        }

        summaryText.Text = $"{result.Technologies.Count} technology entries detected across {Math.Max(result.Projects.Count, 1)} scope(s).";
    }

    private void ShowTechnologyDetail(DetectedTechnology technology)
    {
        var titleText = this.FindControl<TextBlock>("DetailTitleText");
        var detailStack = this.FindControl<StackPanel>("DetailStackPanel");
        if (titleText == null || detailStack == null)
            return;

        var projectLabel = string.IsNullOrWhiteSpace(technology.ProjectName) ? "(workspace)" : technology.ProjectName;

        titleText.Text = technology.Name;
        detailStack.Children.Clear();
        detailStack.Children.Add(CreateDetailLine("Category", technology.Category));
        detailStack.Children.Add(CreateDetailLine("Confidence", technology.Confidence));
        detailStack.Children.Add(CreateDetailLine("Project", projectLabel));

        if (!string.IsNullOrWhiteSpace(technology.Version))
            detailStack.Children.Add(CreateDetailLine("Version", technology.Version));

        if (!string.IsNullOrWhiteSpace(technology.ProjectFilePath))
            detailStack.Children.Add(CreateDetailLine("Project file", technology.ProjectFilePath));

        detailStack.Children.Add(new TextBlock
        {
            Text = "Evidence",
            Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#AABBCC")),
            FontWeight = Avalonia.Media.FontWeight.SemiBold,
            Margin = new Avalonia.Thickness(0, 8, 0, 2)
        });

        foreach (var evidence in technology.Evidence.OrderBy(entry => entry.Source, StringComparer.OrdinalIgnoreCase))
        {
            detailStack.Children.Add(new Border
            {
                Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#0F141E")),
                BorderBrush = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#2A3F5A")),
                BorderThickness = new Avalonia.Thickness(1),
                CornerRadius = new Avalonia.CornerRadius(4),
                Padding = new Avalonia.Thickness(10, 8),
                Child = new StackPanel
                {
                    Spacing = 4,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = evidence.Source,
                            Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#88AABB")),
                            FontSize = 11,
                            FontWeight = Avalonia.Media.FontWeight.SemiBold
                        },
                        new TextBlock
                        {
                            Text = evidence.Description,
                            Foreground = Avalonia.Media.Brushes.White,
                            TextWrapping = Avalonia.Media.TextWrapping.Wrap
                        },
                        new TextBlock
                        {
                            Text = evidence.SourceRef,
                            Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#778899")),
                            FontSize = 11,
                            TextWrapping = Avalonia.Media.TextWrapping.Wrap
                        }
                    }
                }
            });
        }
    }

    private static Control CreateDetailLine(string label, string value)
    {
        return new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("120,*"),
            Children =
            {
                new TextBlock
                {
                    Text = $"{label}:",
                    Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#778899"))
                },
                new TextBlock
                {
                    Text = value,
                    Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#AABBCC")),
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                    [Grid.ColumnProperty] = 1
                }
            }
        };
    }

    private void ScrollProgressToBottom()
    {
        var progressListBox = this.FindControl<ListBox>("ProgressListBox");
        var lastItem = progressListBox?.Items.Cast<object?>().LastOrDefault();
        if (progressListBox != null && lastItem != null)
            progressListBox.ScrollIntoView(lastItem);
    }
}
