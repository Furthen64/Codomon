using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Codomon.Desktop.Models;
using Codomon.Desktop.Services;
using Codomon.Desktop.ViewModels;

namespace Codomon.Desktop.Views;

/// <summary>
/// LLM Summaries dialog.
/// Three tabs: Setup (currently blank), Generate (prompt + file selection + progress), Browse (view stored summaries).
/// </summary>
public partial class LlmSummaryDialog : Window
{
    private readonly LlmSummaryViewModel _vm;
    private readonly List<int> _visibleFileIndices = new();
    private string _fileFilter = string.Empty;
    private int _lastFileToggleIndex = -1;
    private int _pendingShiftClickIndex = -1;
    private bool _pendingShiftClick;
    private bool _isApplyingRangeSelection;
    private bool _hideSummarized;

    private DispatcherTimer? _spinnerTimer;
    private int _spinnerFrame;
    private static readonly string[] SpinnerFrames = ["⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏"];

    public LlmSummaryDialog()
        : this(new LlmSummaryViewModel(new WorkspaceModel(), string.Empty))
    {
    }

    public LlmSummaryDialog(LlmSummaryViewModel vm)
    {
        InitializeComponent();
        _vm = vm;

        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(LlmSummaryViewModel.StatusMessage))
                SyncStatusText();
            else if (e.PropertyName is nameof(LlmSummaryViewModel.IsGenerating))
                SyncGenerateButtons();
        };

        _vm.ProgressMessages.CollectionChanged += (_, _) =>
            Dispatcher.UIThread.Post(ScrollProgressToBottom);

        _vm.Summaries.CollectionChanged += (_, _) =>
            Dispatcher.UIThread.Post(RebuildSummariesList);

        Opened += async (_, _) => await OnDialogOpenedAsync();
    }

    // ── Initialisation ────────────────────────────────────────────────────────

    private async Task OnDialogOpenedAsync()
    {
        await _vm.LoadPromptAsync();
        var promptBox = this.FindControl<TextBox>("PromptBox");
        if (promptBox != null) promptBox.Text = _vm.PromptTemplate;

        // Load file list and existing summaries in the background.
        await _vm.LoadCsFilesAsync();
        RebuildFileList();

        _vm.RefreshSummaries();
        SyncStatusText();
    }

    private async void OnSavePromptClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var promptBox = this.FindControl<TextBox>("PromptBox");
        if (promptBox != null)
            _vm.PromptTemplate = promptBox.Text ?? string.Empty;

        await _vm.SavePromptAsync();
        _vm.StatusMessage = "Prompt saved.";
    }

    private async void OnOpenSettingsClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var dialog = new UserSettingsDialog();
        await dialog.ShowDialog(this);
        _vm.RefreshEffectiveSettingsFromConfig();
        _vm.StatusMessage = "Settings refreshed from main Settings.";
    }

    // ── Generate tab ──────────────────────────────────────────────────────────

    private async void OnReloadFilesClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        await _vm.LoadCsFilesAsync();
        RebuildFileList();
        SyncStatusText();
    }

    private void OnSelectAllClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        SetVisibleSelection(true);
        RebuildFileList();
    }

    private void OnDeselectAllClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        SetVisibleSelection(false);
        RebuildFileList();
    }

    private void OnSelectNonSummarizedClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _vm.SelectAll(false);
        foreach (var sourceIndex in _visibleFileIndices)
        {
            if (!_vm.CsFiles[sourceIndex].HasSummary)
                _vm.CsFiles[sourceIndex].IsSelected = true;
        }
        RebuildFileList();
    }

    private void OnHideSummarizedChanged(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is CheckBox cb)
        {
            _hideSummarized = cb.IsChecked == true;
            _lastFileToggleIndex = -1;
            _pendingShiftClick = false;
            _pendingShiftClickIndex = -1;
            RebuildFileList();
        }
    }

    private void OnFileFilterChanged(object? sender, TextChangedEventArgs e)
    {
        if (sender is not TextBox tb) return;

        _fileFilter = tb.Text ?? string.Empty;
        _lastFileToggleIndex = -1;
        _pendingShiftClick = false;
        _pendingShiftClickIndex = -1;
        RebuildFileList();
    }

    private async void OnGenerateClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        await _vm.GenerateSummariesAsync();
        SyncStatusText();
        RebuildFileList();
    }

    private void OnCancelGenerateClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => _vm.CancelGeneration();

    private void SyncGenerateButtons()
    {
        var genBtn    = this.FindControl<Button>("GenerateButton");
        var cancelBtn = this.FindControl<Button>("CancelGenerateButton");

        if (genBtn    != null) genBtn.IsEnabled    = !_vm.IsGenerating;
        if (cancelBtn != null) cancelBtn.IsEnabled =  _vm.IsGenerating;

        if (_vm.IsGenerating)
            StartSpinner();
        else
            StopSpinner();
    }

    private void StartSpinner()
    {
        if (_spinnerTimer != null) return;

        var spinner = this.FindControl<TextBlock>("SpinnerText");
        if (spinner != null) spinner.IsVisible = true;

        _spinnerFrame = 0;
        _spinnerTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
        _spinnerTimer.Tick += (_, _) =>
        {
            var s = this.FindControl<TextBlock>("SpinnerText");
            if (s != null) s.Text = SpinnerFrames[_spinnerFrame % SpinnerFrames.Length];
            _spinnerFrame++;
        };
        _spinnerTimer.Start();
    }

    private void StopSpinner()
    {
        _spinnerTimer?.Stop();
        _spinnerTimer = null;

        var spinner = this.FindControl<TextBlock>("SpinnerText");
        if (spinner != null) spinner.IsVisible = false;
    }

    private void RebuildFileList()
    {
        var listBox = this.FindControl<ListBox>("CsFilesListBox");
        if (listBox == null) return;

        listBox.Items.Clear();
        _visibleFileIndices.Clear();

        var eligibleCount = 0;

        for (int sourceIndex = 0; sourceIndex < _vm.CsFiles.Count; sourceIndex++)
        {
            if (_hideSummarized && _vm.CsFiles[sourceIndex].HasSummary)
                continue;

            eligibleCount++;

            if (!MatchesFileFilter(_vm.CsFiles[sourceIndex].RelativePath, _fileFilter))
                continue;

            _visibleFileIndices.Add(sourceIndex);
        }

        for (int visibleIndex = 0; visibleIndex < _visibleFileIndices.Count; visibleIndex++)
        {
            var sourceIndex = _visibleFileIndices[visibleIndex];
            var file = _vm.CsFiles[sourceIndex];
            var itemIndex = visibleIndex;
            var checkBox = new CheckBox
            {
                IsChecked = file.IsSelected,
                Content = BuildFileLabel(file),
                Tag = file,
                Padding = new Avalonia.Thickness(4, 2),
                HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Stretch
            };

            checkBox.PointerPressed += (_, args) =>
            {
                _pendingShiftClick = args.KeyModifiers.HasFlag(KeyModifiers.Shift);
                _pendingShiftClickIndex = itemIndex;
            };

            checkBox.IsCheckedChanged += (_, _) =>
            {
                if (checkBox.Tag is CsFileItem f)
                    f.IsSelected = checkBox.IsChecked == true;

                if (_isApplyingRangeSelection)
                    return;

                var currentState = checkBox.IsChecked == true;
                if (_pendingShiftClick &&
                    _pendingShiftClickIndex == itemIndex &&
                    _lastFileToggleIndex >= 0 &&
                    _lastFileToggleIndex != itemIndex)
                {
                    ApplySelectionRange(_lastFileToggleIndex, itemIndex, currentState);
                }

                _lastFileToggleIndex = itemIndex;
                _pendingShiftClick = false;
                _pendingShiftClickIndex = -1;
                UpdateSelectionEstimate();
            };

            listBox.Items.Add(new ListBoxItem
            {
                Content = checkBox,
                Padding = new Avalonia.Thickness(2, 1)
            });
        }

        var countText = this.FindControl<TextBlock>("FileCountText");
        if (countText != null)
            countText.Text = _hideSummarized || !string.IsNullOrWhiteSpace(_fileFilter)
                ? $"C# Files (shown {_visibleFileIndices.Count}, eligible {eligibleCount}, total {_vm.CsFiles.Count})"
                : $"C# Files ({_vm.CsFiles.Count})";

        UpdateSelectionEstimate();
    }

    private void UpdateSelectionEstimate()
    {
        var estimateText = this.FindControl<TextBlock>("SelectionEstimateText");
        if (estimateText == null) return;

        var selected = _vm.CsFiles.Where(f => f.IsSelected).ToList();
        if (selected.Count == 0)
        {
            estimateText.Text = string.Empty;
            return;
        }

        var totalTokens = selected.Sum(f => f.EstimatedTokenCount);
        const double tokensPerSecond = 1200.0;
        var estimatedSeconds = Math.Max(1, (int)Math.Ceiling(totalTokens / tokensPerSecond));
        var runtimeLabel = FormatEstimateDuration(TimeSpan.FromSeconds(estimatedSeconds));
        var fileWord = selected.Count == 1 ? "file" : "files";
        estimateText.Text = $"{selected.Count} {fileWord} · ~{totalTokens:N0} tokens · ~{runtimeLabel}";
    }

    private static string FormatEstimateDuration(TimeSpan elapsed)
    {
        if (elapsed.TotalHours >= 1)
            return $"{(int)elapsed.TotalHours}h{elapsed.Minutes:D2}m{elapsed.Seconds:D2}s";
        if (elapsed.TotalMinutes >= 1)
            return $"{(int)elapsed.TotalMinutes}m{elapsed.Seconds:D2}s";
        if (elapsed.TotalSeconds >= 1)
            return $"{(int)elapsed.TotalSeconds}s";
        return $"{elapsed.TotalMilliseconds:0}ms";
    }

    private static bool MatchesFileFilter(string relativePath, string filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
            return true;

        var path = NormalizeForMatch(relativePath);
        var query = NormalizeForMatch(filter.Trim());

        // Path-like queries imply prefix intent, e.g. "OpenRA.Game/Network".
        if (query.Contains('/'))
            return path.StartsWith(query, StringComparison.OrdinalIgnoreCase);

        // Non-path queries use contains matching, e.g. "Network".
        return path.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeForMatch(string value)
        => (value ?? string.Empty).Replace('\\', '/');

    private static Control BuildFileLabel(CsFileItem file)
    {
        var textPanel = new StackPanel
        {
            Spacing = 0,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        };

        textPanel.Children.Add(new TextBlock
        {
            Text = file.RelativePath,
            FontSize = 11,
            Foreground = Avalonia.Media.Brushes.LightGray,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        });

        if (file.HasSummary)
        {
            var generatedText = file.LastSummaryGeneratedAtUtc.HasValue
                ? $"Summary exists - generated {file.LastSummaryGeneratedAtUtc.Value:yyyy-MM-dd HH:mm} UTC"
                : "Summary exists";

            textPanel.Children.Add(new TextBlock
            {
                Text = generatedText,
                FontSize = 10,
                Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#6FCF97")),
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
            });
        }

        var indicator = new Border
        {
            Width = 10,
            Height = 10,
            CornerRadius = new Avalonia.CornerRadius(1),
            Background = GetComplexityBrush(file.EstimatedTokenCount),
            BorderBrush = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#334455")),
            BorderThickness = new Avalonia.Thickness(1),
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Margin = new Avalonia.Thickness(8, 0, 0, 0)
        };

        var label = GetComplexityLabel(file.EstimatedTokenCount);
        ToolTip.SetTip(indicator, $"{label} complexity (~{file.EstimatedTokenCount:N0} tokens)");

        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch
        };
        row.Children.Add(textPanel);
        Grid.SetColumn(textPanel, 0);
        row.Children.Add(indicator);
        Grid.SetColumn(indicator, 1);

        return row;
    }

    private static Avalonia.Media.IBrush GetComplexityBrush(int estimatedTokens)
    {
        if (estimatedTokens >= 3000)
            return new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#E74C3C")); // red
        if (estimatedTokens >= 1500)
            return new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#E67E22")); // orange
        if (estimatedTokens >= 700)
            return new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#F1C40F")); // yellow
        return new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#2ECC71")); // green
    }

    private static string GetComplexityLabel(int estimatedTokens)
    {
        if (estimatedTokens >= 3000) return "High";
        if (estimatedTokens >= 1500) return "Medium-high";
        if (estimatedTokens >= 700) return "Medium";
        return "Low";
    }

    private void ApplySelectionRange(int fromIndex, int toIndex, bool selected)
    {
        var listBox = this.FindControl<ListBox>("CsFilesListBox");
        if (listBox == null) return;

        var min = Math.Min(fromIndex, toIndex);
        var max = Math.Max(fromIndex, toIndex);

        _isApplyingRangeSelection = true;
        try
        {
            for (int i = min; i <= max; i++)
            {
                if (i < 0 || i >= _visibleFileIndices.Count)
                    continue;

                var sourceIndex = _visibleFileIndices[i];
                _vm.CsFiles[sourceIndex].IsSelected = selected;

                if (listBox.Items[i] is ListBoxItem item && item.Content is CheckBox cb)
                    cb.IsChecked = selected;
            }
        }
        finally
        {
            _isApplyingRangeSelection = false;
        }
    }

    private void SetVisibleSelection(bool selected)
    {
        if (!selected)
        {
            // "Deselect All" should always clear all file selections, including
            // files currently hidden by filters or the hide-summarized toggle.
            _vm.SelectAll(false);
            return;
        }

        if (_hideSummarized)
        {
            foreach (var sourceIndex in _visibleFileIndices)
                _vm.CsFiles[sourceIndex].IsSelected = selected;
        }
        else
        {
            _vm.SelectAll(selected);
        }
    }

    private void ScrollProgressToBottom()
    {
        var listBox = this.FindControl<ListBox>("ProgressListBox");
        if (listBox == null) return;

        // Sync the progress list items from ViewModel.
        listBox.ItemsSource = null;
        listBox.ItemsSource = _vm.ProgressMessages;

        if (listBox.ItemCount == 0) return;

        var last = listBox.Items[listBox.ItemCount - 1];
        if (last != null) listBox.ScrollIntoView(last);
    }

    // ── Browse tab ────────────────────────────────────────────────────────────

    private void OnRefreshSummariesClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _vm.RefreshSummaries();
    }

    private void RebuildSummariesList()
    {
        var listBox = this.FindControl<ListBox>("SummariesListBox");
        if (listBox == null) return;

        listBox.Items.Clear();

        if (_vm.Summaries.Count == 0)
        {
            listBox.Items.Add(new ListBoxItem
            {
                IsEnabled = false,
                Content = new TextBlock
                {
                    Text = "No summaries yet. Generate summaries in the Generate tab.",
                    Foreground = new Avalonia.Media.SolidColorBrush(
                        Avalonia.Media.Color.Parse("#556677")),
                    FontSize = 12,
                    Margin = new Avalonia.Thickness(8, 6)
                }
            });
            return;
        }

        foreach (var summary in _vm.Summaries)
        {
            listBox.Items.Add(new ListBoxItem
            {
                Tag = summary,
                Padding = new Avalonia.Thickness(10, 6),
                Content = new StackPanel
                {
                    Spacing = 2,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = summary.SourceRelativePath,
                            FontSize = 13,
                            FontWeight = Avalonia.Media.FontWeight.SemiBold,
                            Foreground = Avalonia.Media.Brushes.White
                        },
                        new TextBlock
                        {
                            Text = $"Generated: {summary.GeneratedAt:yyyy-MM-dd HH:mm}  •  {summary.SummaryFilePath}",
                            FontSize = 10,
                            Foreground = new Avalonia.Media.SolidColorBrush(
                                Avalonia.Media.Color.Parse("#778899"))
                        }
                    }
                }
            });
        }
    }

    private void OnSummaryDoubleTapped(object? sender, TappedEventArgs e)
    {
        var listBox = this.FindControl<ListBox>("SummariesListBox");
        if (listBox?.SelectedItem is not ListBoxItem item) return;
        if (item.Tag is not SummaryEntry entry) return;

        if (!File.Exists(entry.SummaryFilePath)) return;

        try
        {
            // Open with the OS default application for .md files.
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = entry.SummaryFilePath,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Models.AppLogger.Error($"Failed to open summary in editor: {ex.Message}");
        }
    }

    // ── Shared ────────────────────────────────────────────────────────────────

    private void SyncStatusText()
    {
        var text = this.FindControl<TextBlock>("StatusText");
        if (text != null)
            text.Text = _vm.StatusMessage;
    }

    private void OnCloseClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => Close();
}
