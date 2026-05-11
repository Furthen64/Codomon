using Avalonia.Controls;
using Avalonia.Platform.Storage;
using System.Text;
using System.Linq;
using Avalonia.Input;
using Avalonia.Threading;
using Codomon.Desktop.Models;
using Codomon.Desktop.Models.ArchitectureHypothesis;
using Codomon.Desktop.Services;
using Codomon.Desktop.ViewModels;

namespace Codomon.Desktop.Views;

/// <summary>
/// Architecture Hypothesis dialog.
/// Five tabs: Setup - Run - Systems - High-Value Nodes - Accept.
/// </summary>
public partial class ArchitectureHypothesisDialog : Window
{
    private readonly ArchitectureHypothesisViewModel _vm;
    private readonly Dictionary<string, string> _promptTemplateDescriptions =
        new(StringComparer.OrdinalIgnoreCase);

    public ArchitectureHypothesisDialog()
        : this(new ArchitectureHypothesisViewModel(new WorkspaceModel(), string.Empty))
    {
    }

    public ArchitectureHypothesisDialog(ArchitectureHypothesisViewModel vm)
    {
        InitializeComponent();
        _vm = vm;

        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(ArchitectureHypothesisViewModel.StatusMessage))
                SyncStatusText();
            else if (e.PropertyName is nameof(ArchitectureHypothesisViewModel.IsRunning))
                SyncRunButtons();
            else if (e.PropertyName is nameof(ArchitectureHypothesisViewModel.CurrentHypothesis))
                Dispatcher.UIThread.Post(RebuildResultTabs);
            else if (e.PropertyName is nameof(ArchitectureHypothesisViewModel.HasCanvasChanges))
                SyncApplyToCanvasButton();
            else if (e.PropertyName is nameof(ArchitectureHypothesisViewModel.SynthesisState))
            {
                SyncSynthesisState();
                SyncRunButtons();
                SyncApplyToCanvasButton();
                UpdateWizardButtons();
            }
            else if (e.PropertyName is nameof(ArchitectureHypothesisViewModel.LiveOutput))
                SyncLiveOutput();
            else if (e.PropertyName
                is nameof(ArchitectureHypothesisViewModel.CurrentBatch)
                or nameof(ArchitectureHypothesisViewModel.TotalBatches)
                or nameof(ArchitectureHypothesisViewModel.ProcessedSummaries)
                or nameof(ArchitectureHypothesisViewModel.TotalSummaries)
                or nameof(ArchitectureHypothesisViewModel.TotalPromptTokens)
                or nameof(ArchitectureHypothesisViewModel.TotalGeneratedTokens)
                or nameof(ArchitectureHypothesisViewModel.ElapsedFormatted)
                or nameof(ArchitectureHypothesisViewModel.EtaFormatted)
                or nameof(ArchitectureHypothesisViewModel.GenerationSpeed))
                SyncTelemetryCard();
            else if (e.PropertyName is nameof(ArchitectureHypothesisViewModel.LiveOutputTokenEstimate))
                SyncLiveOutput();
            else if (e.PropertyName is nameof(ArchitectureHypothesisViewModel.TokenBudgetWarning))
                SyncTokenBudgetWarning();
        };

        _vm.ProgressMessages.CollectionChanged += (_, _) =>
            Dispatcher.UIThread.Post(ScrollProgressToBottom);

        _vm.BatchLog.CollectionChanged += (_, _) =>
            Dispatcher.UIThread.Post(RebuildBatchLog);

        _vm.SavedHypotheses.CollectionChanged += (_, _) =>
            Dispatcher.UIThread.Post(RebuildHistoryList);

        Opened += async (_, _) => await OnDialogOpenedAsync();
        UpdateWizardButtons();
    }

    // ── Initialisation ────────────────────────────────────────────────────────

    private async Task OnDialogOpenedAsync()
    {
        // Show current LLM settings (read-only info).
        var infoText = this.FindControl<TextBlock>("LlmSettingsInfoText");
        if (infoText != null)
            infoText.Text = string.Empty;

        await PopulatePromptTemplatePickerAsync();

        await _vm.LoadPromptAsync();
        var promptBox = this.FindControl<TextBox>("PromptBox");
        if (promptBox != null) promptBox.Text = _vm.PromptTemplate;

        _vm.RefreshSavedHypotheses();
        RebuildHistoryList();
        SyncStatusText();
    }

    private async Task PopulatePromptTemplatePickerAsync()
    {
        var combo = this.FindControl<ComboBox>("PromptTemplateComboBox");
        if (combo == null) return;

        combo.Items.Clear();
        _promptTemplateDescriptions.Clear();

        var presets = await _vm.GetPromptTemplatePresetsAsync();
        foreach (var preset in presets)
        {
            _promptTemplateDescriptions[preset.FileName] = preset.Description;
            combo.Items.Add(new ComboBoxItem
            {
                Content = preset.FileName,
                Tag = preset.FileName
            });
        }

        if (combo.ItemCount > 0)
            combo.SelectedIndex = 0;

        SyncPromptTemplateDescription();
    }

    // ── Setup tab ─────────────────────────────────────────────────────────────

    private async void OnSavePromptClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var promptBox = this.FindControl<TextBox>("PromptBox");
        if (promptBox != null)
            _vm.PromptTemplate = promptBox.Text ?? string.Empty;

        await _vm.SavePromptAsync();
        _vm.StatusMessage = "Prompt saved.";
    }

    private async void OnPromptTemplateSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        var fileName = GetSelectedPromptTemplateFileName();
        SyncPromptTemplateDescription();
        if (string.IsNullOrWhiteSpace(fileName)) return;

        await _vm.LoadPromptPresetAsync(fileName);

        var promptBox = this.FindControl<TextBox>("PromptBox");
        if (promptBox != null)
            promptBox.Text = _vm.PromptTemplate;

        _vm.StatusMessage = $"Loaded preset: {fileName}. Click Save Prompt to persist it.";
        SyncStatusText();
    }

    private string GetSelectedPromptTemplateFileName()
    {
        var combo = this.FindControl<ComboBox>("PromptTemplateComboBox");
        if (combo?.SelectedItem is ComboBoxItem item)
            return item.Tag?.ToString() ?? string.Empty;
        return string.Empty;
    }

    private void SyncPromptTemplateDescription()
    {
        var descriptionText = this.FindControl<TextBlock>("PromptTemplateDescriptionText");
        if (descriptionText == null) return;

        var fileName = GetSelectedPromptTemplateFileName();
        if (!string.IsNullOrWhiteSpace(fileName)
            && _promptTemplateDescriptions.TryGetValue(fileName, out var description))
        {
            descriptionText.Text = description;
            return;
        }

        descriptionText.Text = "Select a preset to view its description.";
    }

    // ── Run tab ───────────────────────────────────────────────────────────────

    private async void OnRunSynthesisClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        await _vm.RunSynthesisAsync();
        if (_vm.CurrentHypothesis != null)
        {
            AutoAcceptRecommended();
            _vm.StatusMessage = BuildRecommendedStatus();
            MoveToTab(2);
        }
        SyncStatusText();
        UpdateWizardButtons();
    }

    private void OnCancelClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => _vm.CancelSynthesis();

    private void OnRefreshHistoryClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _vm.RefreshSavedHypotheses();
        RebuildHistoryList();
    }

    private async void OnHistoryDoubleTapped(object? sender, TappedEventArgs e)
    {
        var listBox = this.FindControl<ListBox>("HistoryListBox");
        if (listBox?.SelectedItem is not ListBoxItem item) return;
        if (item.Tag is not HypothesisEntry entry) return;

        await _vm.LoadHypothesisAsync(entry);
        SyncStatusText();
        RebuildResultTabs();
        UpdateWizardButtons();
    }

    private void OnHistorySelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        var listBox = this.FindControl<ListBox>("HistoryListBox");
        var loadBtn = this.FindControl<Button>("HistoryLoadButton");
        if (loadBtn != null)
            loadBtn.IsEnabled = listBox?.SelectedItem is ListBoxItem { Tag: HypothesisEntry };
    }

    private async void OnLoadSelectedHistoryClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var listBox = this.FindControl<ListBox>("HistoryListBox");
        if (listBox?.SelectedItem is not ListBoxItem item) return;
        if (item.Tag is not HypothesisEntry entry) return;

        await _vm.LoadHypothesisAsync(entry);
        SyncStatusText();
        RebuildResultTabs();
        UpdateWizardButtons();
    }

    private void SyncRunButtons()
    {
        var runBtn    = this.FindControl<Button>("RunButton");
        var cancelBtn = this.FindControl<Button>("CancelButton");
        if (runBtn != null)
        {
            runBtn.IsEnabled = !_vm.IsRunning;
            runBtn.Content = _vm.SynthesisState is SynthesisState.Failed or SynthesisState.Cancelled
                ? "↺ Retry Synthesis"
                : "▶ Run Synthesis";
        }
        if (cancelBtn != null) cancelBtn.IsEnabled =  _vm.IsRunning;

        if (_vm.IsRunning)
        {
            var card = this.FindControl<Border>("TelemetryCard");
            if (card != null) card.IsVisible = true;
        }
    }

    private void SyncSynthesisState()
    {
        var label = this.FindControl<TextBlock>("TelemetryStateLabel");
        if (label != null)
        {
            label.Text = _vm.SynthesisStateLabel;
            label.Foreground = _vm.SynthesisState switch
            {
                SynthesisState.Completed             => new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#88CC88")),
                SynthesisState.CompletedWithWarnings => new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#CCAA44")),
                SynthesisState.Failed                => new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#CC6666")),
                SynthesisState.Cancelled             => new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#AAAAAA")),
                _                                    => new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#AADDCC")),
            };
        }

        // Ensure the telemetry card is visible once synthesis has started.
        var card = this.FindControl<Border>("TelemetryCard");
        if (card != null && _vm.SynthesisState != SynthesisState.Idle)
            card.IsVisible = true;
    }

    private void SyncTelemetryCard()
    {
        var batchLbl   = this.FindControl<TextBlock>("TelemetryBatchLabel");
        var summLbl    = this.FindControl<TextBlock>("TelemetrySummariesLabel");
        var elapsedLbl = this.FindControl<TextBlock>("TelemetryElapsedLabel");
        var etaLbl     = this.FindControl<TextBlock>("TelemetryEtaLabel");
        var promptLbl  = this.FindControl<TextBlock>("TelemetryPromptTokLabel");
        var outputLbl  = this.FindControl<TextBlock>("TelemetryOutputTokLabel");
        var speedLbl   = this.FindControl<TextBlock>("TelemetrySpeedLabel");

        if (batchLbl   != null) batchLbl.Text   = $"Batch {_vm.CurrentBatch} / {_vm.TotalBatches}";
        if (summLbl    != null) summLbl.Text     = $"Summaries: {_vm.ProcessedSummaries} / {_vm.TotalSummaries}";
        if (elapsedLbl != null) elapsedLbl.Text  = $"Elapsed: {_vm.ElapsedFormatted}";
        if (etaLbl     != null) etaLbl.Text      = !string.IsNullOrEmpty(_vm.EtaFormatted) ? $"ETA: {_vm.EtaFormatted}" : "ETA: —";
        if (promptLbl  != null) promptLbl.Text   = $"Prompt tok: {_vm.TotalPromptTokens:N0}";
        if (outputLbl  != null) outputLbl.Text   = $"Output tok: {_vm.TotalGeneratedTokens:N0}";
        if (speedLbl   != null) speedLbl.Text    = _vm.GenerationSpeed > 0 ? $"Speed: {_vm.GenerationSpeed:F0} tok/s" : "Speed: —";
    }

    private void SyncTokenBudgetWarning()
    {
        var warningPanel = this.FindControl<Border>("TokenBudgetWarningPanel");
        var warningText = this.FindControl<TextBlock>("TokenBudgetWarningText");
        if (warningText != null)
            warningText.Text = _vm.TokenBudgetWarning;
        if (warningPanel != null)
            warningPanel.IsVisible = !string.IsNullOrWhiteSpace(_vm.TokenBudgetWarning);
    }

    private void SyncLiveOutput()
    {
        var textBlock = this.FindControl<TextBlock>("LiveOutputText");
        if (textBlock != null)
            textBlock.Text = _vm.LiveOutput;

        var countLabel = this.FindControl<TextBlock>("LiveOutputCharCountLabel");
        if (countLabel != null)
            countLabel.Text = $"{_vm.LiveOutput.Length:N0} chars";

        var tokenLabel = this.FindControl<TextBlock>("LiveOutputTokenCountLabel");
        if (tokenLabel != null)
            tokenLabel.Text = $"~{_vm.LiveOutputTokenEstimate:N0} tok";

        // Auto-scroll the live output.
        if (IsLiveOutputAutoScrollEnabled())
        {
            var scroll = this.FindControl<ScrollViewer>("LiveOutputScroll");
            scroll?.ScrollToEnd();
        }
    }

    private bool IsLiveOutputAutoScrollEnabled()
        => this.FindControl<CheckBox>("LiveOutputAutoScrollToggle")?.IsChecked != false;

    private void OnLiveOutputAutoScrollChanged(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (IsLiveOutputAutoScrollEnabled())
            this.FindControl<ScrollViewer>("LiveOutputScroll")?.ScrollToEnd();
    }

    private void RebuildBatchLog()
    {
        var listBox = this.FindControl<ListBox>("BatchLogListBox");
        if (listBox == null) return;

        listBox.Items.Clear();

        if (_vm.BatchLog.Count == 0)
        {
            listBox.Items.Add(new ListBoxItem
            {
                IsEnabled = false,
                Content = new TextBlock
                {
                    Text = "No batches yet.",
                    Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#556677")),
                    FontSize = 11,
                    Margin = new Avalonia.Thickness(6, 4)
                }
            });
            return;
        }

        foreach (var entry in _vm.BatchLog)
        {
            var statusColor = entry.Status switch
            {
                "Completed" => "#88BB88",
                "Warning"   => "#CCAA44",
                "Failed"    => "#CC6666",
                "Split"     => "#AABBCC",
                "Retrying"  => "#AABBDD",
                _           => "#778899",
            };

            var panel = new StackPanel
            {
                Spacing = 1,
                Margin = new Avalonia.Thickness(6, 3)
            };

            var line1 = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 8 };
            line1.Children.Add(new TextBlock
            {
                Text = entry.StatusIcon,
                Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse(statusColor)),
                FontSize = 11,
                Width = 14
            });
            line1.Children.Add(new TextBlock
            {
                Text = entry.BatchLabel,
                Foreground = Avalonia.Media.Brushes.LightGray,
                FontSize = 11,
                FontWeight = Avalonia.Media.FontWeight.SemiBold
            });
            if (!string.IsNullOrEmpty(entry.Duration))
                line1.Children.Add(new TextBlock
                {
                    Text = entry.Duration,
                    Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#667788")),
                    FontSize = 10
                });
            panel.Children.Add(line1);

            if (entry.PromptTokens > 0 || entry.OutputTokens > 0)
            {
                panel.Children.Add(new TextBlock
                {
                    Text = $"  {entry.SummaryCount} summaries · {entry.PromptTokens:N0}→{entry.OutputTokens:N0} tok",
                    Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#556677")),
                    FontSize = 10
                });
            }

            if (!string.IsNullOrWhiteSpace(entry.FailureReason))
            {
                panel.Children.Add(new TextBlock
                {
                    Text = $"  Reason: {entry.FailureReason}",
                    Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#CC8888")),
                    FontSize = 10,
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap
                });
            }

            listBox.Items.Add(new ListBoxItem
            {
                Tag = entry,
                Padding = new Avalonia.Thickness(2, 2),
                Content = panel
            });
        }
    }

    private void OnCopyLiveOutputClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_vm.LiveOutput))
            Clipboard?.SetTextAsync(_vm.LiveOutput);
    }

    private async void OnExportLogClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var storageProvider = StorageProvider;
        if (storageProvider == null) return;

        var file = await storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export Synthesis Log",
            SuggestedFileName = $"synthesis_log_{DateTime.UtcNow:yyyyMMdd_HHmmss}.txt"
        });
        if (file == null) return;

        var content = BuildSynthesisLogText();
        await using var stream = await file.OpenWriteAsync();
        await using var writer = new StreamWriter(stream);
        await writer.WriteAsync(content);

        _vm.StatusMessage = $"Exported synthesis log: {file.Name}";
        SyncStatusText();
    }

    private async void OnCopyDiagnosticsClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var text = BuildDiagnosticsText();
        await (Clipboard?.SetTextAsync(text) ?? Task.CompletedTask);
        _vm.StatusMessage = "Diagnostics copied to clipboard.";
        SyncStatusText();
    }

    private void OnOpenRawResponseClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_vm.LatestRawResponse))
        {
            _vm.StatusMessage = "No raw response captured yet.";
            SyncStatusText();
            return;
        }

        var viewer = new Window
        {
            Title = "Raw LLM Response",
            Width = 820,
            Height = 620,
            MinWidth = 500,
            MinHeight = 300,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#111820")),
            Content = new Border
            {
                Padding = new Avalonia.Thickness(10),
                Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#111820")),
                Child = new TextBox
                {
                    Text = _vm.LatestRawResponse,
                    IsReadOnly = true,
                    AcceptsReturn = true,
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                    FontFamily = "Monospace",
                    FontSize = 11,
                    Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#0F141E")),
                    Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#CCDDEE")),
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                }
            }
        };
        viewer.Show(this);
    }

    private void ScrollProgressToBottom()
    {
        var listBox = this.FindControl<ListBox>("ProgressListBox");
        if (listBox == null) return;
        listBox.ItemsSource = null;
        listBox.ItemsSource = _vm.ProgressMessages;
        if (listBox.ItemCount == 0) return;
        var last = listBox.Items[listBox.ItemCount - 1];
        if (last != null) listBox.ScrollIntoView(last);
    }

    private void RebuildHistoryList()
    {
        var listBox = this.FindControl<ListBox>("HistoryListBox");
        if (listBox == null) return;
        listBox.Items.Clear();

        if (_vm.SavedHypotheses.Count == 0)
        {
            listBox.Items.Add(new ListBoxItem
            {
                IsEnabled = false,
                Content = new TextBlock
                {
                    Text = "No hypotheses yet.",
                    Foreground = new Avalonia.Media.SolidColorBrush(
                        Avalonia.Media.Color.Parse("#556677")),
                    FontSize = 11,
                    Margin = new Avalonia.Thickness(6, 4)
                }
            });
            return;
        }

        foreach (var entry in _vm.SavedHypotheses)
        {
            listBox.Items.Add(new ListBoxItem
            {
                Tag = entry,
                Padding = new Avalonia.Thickness(8, 5),
                Content = new TextBlock
                {
                    Text = entry.DisplayName,
                    FontSize = 12,
                    Foreground = Avalonia.Media.Brushes.LightGray
                }
            });
        }
    }

    // ── Systems / HVN tabs ────────────────────────────────────────────────────

    private void RebuildResultTabs()
    {
        RebuildSystemsList();
        RebuildHvnList();
        RebuildAcceptTab();
    }

    private void RebuildSystemsList()
    {
        var listBox = this.FindControl<ListBox>("SystemsListBox");
        if (listBox == null) return;
        listBox.Items.Clear();

        var headerText = this.FindControl<TextBlock>("SystemsHeaderText");

        if (_vm.Systems.Count == 0)
        {
            if (headerText != null)
                headerText.Text = "No systems suggested — run or load a hypothesis first.";
            listBox.Items.Add(MakePlaceholderItem("No system suggestions in this hypothesis."));
            return;
        }

        if (headerText != null)
            headerText.Text = $"{_vm.Systems.Count} system(s) suggested";

        foreach (var sys in _vm.Systems)
        {
            var panel = new StackPanel { Spacing = 4, Margin = new Avalonia.Thickness(2, 4) };

            panel.Children.Add(new TextBlock
            {
                Text = $"{sys.Name}  [{sys.Kind}]  — {sys.Confidence}",
                FontSize = 13,
                FontWeight = Avalonia.Media.FontWeight.SemiBold,
                Foreground = sys.IsAccepted
                    ? new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#88BB88"))
                    : Avalonia.Media.Brushes.White
            });

            foreach (var ev in sys.Evidence)
                panel.Children.Add(new TextBlock
                {
                    Text = $"  • {ev}",
                    FontSize = 11,
                    Foreground = new Avalonia.Media.SolidColorBrush(
                        Avalonia.Media.Color.Parse("#778899")),
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap
                });

            foreach (var mod in sys.Modules)
                panel.Children.Add(new TextBlock
                {
                    Text = $"  ◦ Module: {mod.Name}  ({mod.Confidence})" +
                           (mod.HighValueNodes.Count > 0
                               ? $"  — HVN: {string.Join(", ", mod.HighValueNodes)}"
                               : string.Empty),
                    FontSize = 11,
                    Foreground = new Avalonia.Media.SolidColorBrush(
                        Avalonia.Media.Color.Parse("#AABBCC")),
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap
                });

            listBox.Items.Add(new ListBoxItem
            {
                Tag = sys,
                Padding = new Avalonia.Thickness(10, 6),
                Content = panel
            });
        }
    }

    private void RebuildHvnList()
    {
        var listBox = this.FindControl<ListBox>("HvnListBox");
        if (listBox == null) return;
        listBox.Items.Clear();

        var headerText = this.FindControl<TextBlock>("HvnHeaderText");

        if (_vm.HighValueNodes.Count == 0)
        {
            if (headerText != null)
                headerText.Text = "No high-value nodes suggested — run or load a hypothesis first.";
            listBox.Items.Add(MakePlaceholderItem("No high-value node suggestions."));
            return;
        }

        if (headerText != null)
            headerText.Text = $"{_vm.HighValueNodes.Count} high-value node(s) suggested";

        foreach (var node in _vm.HighValueNodes)
        {
            var panel = new StackPanel { Spacing = 2, Margin = new Avalonia.Thickness(2, 2) };

            panel.Children.Add(new TextBlock
            {
                Text = $"{node.Name}  [{node.Signal}]  — {node.Confidence}",
                FontSize = 13,
                FontWeight = Avalonia.Media.FontWeight.SemiBold,
                Foreground = node.IsAccepted
                    ? new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#88BB88"))
                    : Avalonia.Media.Brushes.White
            });

            panel.Children.Add(new TextBlock
            {
                Text = $"  {node.Reason}",
                FontSize = 11,
                Foreground = new Avalonia.Media.SolidColorBrush(
                    Avalonia.Media.Color.Parse("#778899")),
                TextWrapping = Avalonia.Media.TextWrapping.Wrap
            });

            listBox.Items.Add(new ListBoxItem
            {
                Tag = node,
                Padding = new Avalonia.Thickness(10, 5),
                Content = panel
            });
        }
    }

    // ── Systems / HVN tabs — inline accept handlers ───────────────────────────

    private void OnSystemsListSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        var listBox = this.FindControl<ListBox>("SystemsListBox");
        var btn = this.FindControl<Button>("AcceptSelectedSystemButton");
        if (btn != null)
            btn.IsEnabled = listBox?.SelectedItem is ListBoxItem { Tag: HypothesisSystemModel };
    }

    private void OnAcceptSelectedSystemFromTabClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var listBox = this.FindControl<ListBox>("SystemsListBox");
        if (listBox?.SelectedItem is not ListBoxItem item) return;
        if (item.Tag is not HypothesisSystemModel sys) return;

        var (_, isNew) = _vm.AcceptSystem(sys);
        _vm.StatusMessage = isNew ? $"Accepted system: {sys.Name}" : $"Merged system: {sys.Name}";
        RebuildSystemsList();
        RebuildAcceptSystemsList();
        UpdateFinishSummary();
    }

    private void OnAcceptAllLikelySystemsClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var count = 0;
        foreach (var sys in _vm.Systems.Where(s =>
                     s.Confidence == Models.SystemMap.ConfidenceLevel.Likely && !s.IsAccepted))
        {
            _vm.AcceptSystem(sys);
            count++;
        }
        _vm.StatusMessage = count > 0
            ? $"Accepted {count} likely system(s)."
            : "All likely systems already accepted.";
        RebuildSystemsList();
        RebuildAcceptSystemsList();
        UpdateFinishSummary();
    }

    private void OnHvnListSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        var listBox = this.FindControl<ListBox>("HvnListBox");
        var btn = this.FindControl<Button>("AcceptSelectedNodeButton");
        if (btn != null)
            btn.IsEnabled = listBox?.SelectedItem is ListBoxItem { Tag: HypothesisHighValueNodeModel };
    }

    private void OnAcceptSelectedNodeFromTabClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var listBox = this.FindControl<ListBox>("HvnListBox");
        if (listBox?.SelectedItem is not ListBoxItem item) return;
        if (item.Tag is not HypothesisHighValueNodeModel node) return;

        var (_, isNew) = _vm.AcceptHighValueNode(node);
        _vm.StatusMessage = isNew ? $"Accepted node: {node.Name}" : $"Merged node: {node.Name}";
        RebuildHvnList();
        RebuildAcceptHvnList();
        UpdateFinishSummary();
    }

    private void OnAcceptAllLikelyNodesClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var count = 0;
        foreach (var node in _vm.HighValueNodes.Where(n =>
                     n.Confidence == Models.SystemMap.ConfidenceLevel.Likely && !n.IsAccepted))
        {
            _vm.AcceptHighValueNode(node);
            count++;
        }
        _vm.StatusMessage = count > 0
            ? $"Accepted {count} likely node(s)."
            : "All likely nodes already accepted.";
        RebuildHvnList();
        RebuildAcceptHvnList();
        UpdateFinishSummary();
    }

    // ── Accept tab ────────────────────────────────────────────────────────────

    private void RebuildAcceptTab()
    {
        RebuildAcceptSystemsList();
        RebuildAcceptHvnList();
        RebuildUncertainList();
    }

    private void RebuildAcceptSystemsList()
    {
        var listBox = this.FindControl<ListBox>("AcceptSystemsListBox");
        if (listBox == null) return;
        listBox.Items.Clear();

        foreach (var sys in _vm.Systems)
        {
            var label = sys.IsAccepted
                ? $"✔ {sys.Name}  [{sys.Kind}]  — {sys.Confidence}  (Accepted)"
                : $"{sys.Name}  [{sys.Kind}]  — {sys.Confidence}";

            listBox.Items.Add(new ListBoxItem
            {
                Tag = sys,
                Padding = new Avalonia.Thickness(8, 4),
                Content = new TextBlock
                {
                    Text = label,
                    FontSize = 12,
                    Foreground = sys.IsAccepted
                        ? new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#88BB88"))
                        : Avalonia.Media.Brushes.LightGray
                }
            });
        }

        if (listBox.Items.Count == 0)
            listBox.Items.Add(MakePlaceholderItem("No systems — run or load a hypothesis first."));
    }

    private void RebuildAcceptHvnList()
    {
        var listBox = this.FindControl<ListBox>("AcceptHvnListBox");
        if (listBox == null) return;
        listBox.Items.Clear();

        foreach (var node in _vm.HighValueNodes)
        {
            var label = node.IsAccepted
                ? $"✔ {node.Name}  [{node.Signal}]  — {node.Confidence}  (Accepted)"
                : $"{node.Name}  [{node.Signal}]  — {node.Confidence}";

            listBox.Items.Add(new ListBoxItem
            {
                Tag = node,
                Padding = new Avalonia.Thickness(8, 4),
                Content = new TextBlock
                {
                    Text = label,
                    FontSize = 12,
                    Foreground = node.IsAccepted
                        ? new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#88BB88"))
                        : Avalonia.Media.Brushes.LightGray
                }
            });
        }

        if (listBox.Items.Count == 0)
            listBox.Items.Add(MakePlaceholderItem("No high-value nodes — run or load a hypothesis first."));
    }

    private void RebuildUncertainList()
    {
        var listBox = this.FindControl<ListBox>("UncertainListBox");
        if (listBox == null) return;
        listBox.Items.Clear();

        foreach (var area in _vm.UncertainAreas)
        {
            listBox.Items.Add(new ListBoxItem
            {
                Padding = new Avalonia.Thickness(8, 3),
                Content = new TextBlock
                {
                    Text = area,
                    FontSize = 11,
                    Foreground = new Avalonia.Media.SolidColorBrush(
                        Avalonia.Media.Color.Parse("#AABBCC")),
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap
                }
            });
        }

        if (listBox.Items.Count == 0)
            listBox.Items.Add(MakePlaceholderItem("No uncertain areas noted."));
    }

    private void OnAcceptSystemClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var listBox = this.FindControl<ListBox>("AcceptSystemsListBox");
        if (listBox?.SelectedItem is not ListBoxItem item) return;
        if (item.Tag is not HypothesisSystemModel sys) return;

        var (_, isNew) = _vm.AcceptSystem(sys);
        _vm.StatusMessage = isNew
            ? $"Accepted system: {sys.Name}"
            : $"Merged system: {sys.Name}";
        RebuildAcceptSystemsList();
        RebuildSystemsList();
        UpdateFinishSummary();
    }

    private void OnAcceptNodeClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var listBox = this.FindControl<ListBox>("AcceptHvnListBox");
        if (listBox?.SelectedItem is not ListBoxItem item) return;
        if (item.Tag is not HypothesisHighValueNodeModel node) return;

        var (_, isNew) = _vm.AcceptHighValueNode(node);
        _vm.StatusMessage = isNew
            ? $"Accepted high-value node: {node.Name}"
            : $"Merged high-value node: {node.Name}";
        RebuildAcceptHvnList();
        RebuildHvnList();
        UpdateFinishSummary();
    }

    // ── Finish tab ────────────────────────────────────────────────────────────

    private void UpdateFinishSummary()
    {
        var text = this.FindControl<TextBlock>("FinishSummaryText");
        if (text == null) return;

        if (_vm.AppliedSuggestionCount == 0 && !_vm.HasCanvasChanges)
        {
            text.Text = "No suggestions applied yet. Go to the Accept tab to accept or merge suggestions.";
            return;
        }

        var acceptedSystems = _vm.Systems.Count(s => s.IsAccepted);
        var acceptedNodes   = _vm.HighValueNodes.Count(n => n.IsAccepted);
        text.Text = $"{_vm.AppliedSuggestionCount} suggestion(s) applied in this session: " +
                    $"{acceptedSystems} system suggestion(s), {acceptedNodes} high-value node suggestion(s). " +
                    "Click 'Apply to Canvas' to close and refresh the System Map.";
    }

    private void OnClearCanvasClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _vm.ClearSystemMap();
        SyncStatusText();
        UpdateFinishSummary();
    }

    private void OnApplyToCanvasClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        AppLogger.Debug($"[Hypothesis] Apply to Canvas clicked. AcceptedCount={_vm.AcceptedCount}; AppliedSuggestionCount={_vm.AppliedSuggestionCount}; HasCanvasChanges={_vm.HasCanvasChanges}; Status='{_vm.StatusMessage}'. Closing Architecture dialog to hand off refresh to MainWindow.");
        Close();
    }


    private void AutoAcceptRecommended()
    {
        foreach (var sys in _vm.Systems.Where(s => s.Confidence == Models.SystemMap.ConfidenceLevel.Likely))
            if (!sys.IsAccepted) _vm.AcceptSystem(sys);
        foreach (var node in _vm.HighValueNodes.Where(n => n.Confidence == Models.SystemMap.ConfidenceLevel.Likely))
            if (!node.IsAccepted) _vm.AcceptHighValueNode(node);
    }

    private string BuildRecommendedStatus()
    {
        var acceptedSystems = _vm.Systems.Count(s => s.IsAccepted);
        var acceptedNodes = _vm.HighValueNodes.Count(n => n.IsAccepted);
        return $"Synthesis complete. {acceptedSystems} recommended systems and {acceptedNodes} recommended nodes are ready to review.";
    }

    private async void OnSaveMarkdownOverviewClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_vm.CurrentHypothesis == null)
        {
            _vm.StatusMessage = "Run or load a hypothesis first.";
            SyncStatusText();
            return;
        }

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save architecture overview",
            SuggestedFileName = $"architecture_overview_{DateTime.UtcNow:yyyyMMdd_HHmmss}.md"
        });
        if (file == null) return;

        var sb = new StringBuilder();
        sb.AppendLine("# Architecture Overview");
        sb.AppendLine();
        sb.AppendLine($"Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine();
        sb.AppendLine("## Systems");
        foreach (var s in _vm.Systems)
        {
            sb.AppendLine($"- **{s.Name}** ({s.Kind}, {s.Confidence})");
            foreach (var ev in s.Evidence.Take(5)) sb.AppendLine($"  - Evidence: `{ev}`");
        }
        sb.AppendLine();
        sb.AppendLine("## Architecture Anchors");
        foreach (var n in _vm.HighValueNodes)
            sb.AppendLine($"- **{n.Name}** ({n.Signal}, {n.Confidence}) — {n.Reason}");

        await using var stream = await file.OpenWriteAsync();
        await using var writer = new StreamWriter(stream);
        await writer.WriteAsync(sb.ToString());

        _vm.StatusMessage = $"Saved Markdown overview: {file.Name}";
        SyncStatusText();
    }

    private void OnBackStepClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => MoveStep(-1);
    private void OnNextStepClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => MoveStep(1);

    private void MoveStep(int delta)
    {
        var tabs = this.FindControl<TabControl>("WizardTabs");
        if (tabs == null) return;
        var target = Math.Clamp(tabs.SelectedIndex + delta, 0, tabs.ItemCount - 1);
        MoveToTab(target);
    }

    private void MoveToTab(int index)
    {
        var tabs = this.FindControl<TabControl>("WizardTabs");
        if (tabs == null) return;
        tabs.SelectedIndex = index;
        UpdateWizardButtons();
    }

    private void UpdateWizardButtons()
    {
        var tabs = this.FindControl<TabControl>("WizardTabs");
        var back = this.FindControl<Button>("BackStepButton");
        var next = this.FindControl<Button>("NextStepButton");
        if (tabs == null || back == null || next == null) return;
        back.IsEnabled = tabs.SelectedIndex > 0;
        next.IsEnabled = tabs.SelectedIndex < tabs.ItemCount - 1
            && _vm.SynthesisState != SynthesisState.Failed;
    }
    // ── Shared ────────────────────────────────────────────────────────────────

    private void SyncStatusText()
    {
        var text = this.FindControl<TextBlock>("StatusText");
        if (text != null)
            text.Text = _vm.StatusMessage;
    }

    private void SyncApplyToCanvasButton()
    {
        var btn = this.FindControl<Button>("ApplyToCanvasFooterButton");
        if (btn != null)
            btn.IsEnabled = _vm.HasCanvasChanges && _vm.SynthesisState != SynthesisState.Failed;
    }

    private string BuildDiagnosticsText()
    {
        var sb = new StringBuilder();
        sb.AppendLine("Codomon Architecture Synthesis Diagnostics");
        sb.AppendLine($"Timestamp (UTC): {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"State: {_vm.SynthesisStateLabel}");
        sb.AppendLine($"Batches: {_vm.CurrentBatch}/{_vm.TotalBatches}");
        sb.AppendLine($"Summaries: {_vm.ProcessedSummaries}/{_vm.TotalSummaries}");
        sb.AppendLine($"Prompt tokens: {_vm.TotalPromptTokens:N0}");
        sb.AppendLine($"Output tokens: {_vm.TotalGeneratedTokens:N0}");
        sb.AppendLine($"Elapsed: {_vm.ElapsedFormatted}");
        sb.AppendLine($"ETA: {_vm.EtaFormatted}");
        sb.AppendLine($"Generation speed: {(_vm.GenerationSpeed > 0 ? $"{_vm.GenerationSpeed:F0} tok/s" : "—")}");
        if (!string.IsNullOrWhiteSpace(_vm.TokenBudgetWarning))
            sb.AppendLine($"Token warning: {_vm.TokenBudgetWarning}");
        sb.AppendLine();
        sb.AppendLine("Batch log:");
        foreach (var batch in _vm.BatchLog)
        {
            sb.AppendLine($"- {batch.BatchLabel} [{batch.Status}] summaries={batch.SummaryCount}, prompt={batch.PromptTokens}, output={batch.OutputTokens}, finish={batch.FinishReason}, duration={batch.Duration}");
            if (!string.IsNullOrWhiteSpace(batch.FailureReason))
                sb.AppendLine($"  failure={batch.FailureReason}");
        }
        return sb.ToString();
    }

    private string BuildSynthesisLogText()
    {
        var sb = new StringBuilder();
        sb.AppendLine(BuildDiagnosticsText());
        sb.AppendLine();
        sb.AppendLine("Progress messages:");
        foreach (var msg in _vm.ProgressMessages)
            sb.AppendLine($"- {msg}");

        if (!string.IsNullOrWhiteSpace(_vm.LatestRawResponse))
        {
            sb.AppendLine();
            sb.AppendLine("Latest raw response:");
            sb.AppendLine(_vm.LatestRawResponse);
        }

        return sb.ToString();
    }

    private static ListBoxItem MakePlaceholderItem(string message) =>
        new()
        {
            IsEnabled = false,
            Content = new TextBlock
            {
                Text = message,
                Foreground = new Avalonia.Media.SolidColorBrush(
                    Avalonia.Media.Color.Parse("#556677")),
                FontSize = 12,
                Margin = new Avalonia.Thickness(8, 6)
            }
        };

    private void OnCloseClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => Close();
}
