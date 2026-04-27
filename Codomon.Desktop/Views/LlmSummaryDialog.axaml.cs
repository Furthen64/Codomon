using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Codomon.Desktop.Services;
using Codomon.Desktop.ViewModels;

namespace Codomon.Desktop.Views;

/// <summary>
/// LLM Summaries dialog.
/// Three tabs: Setup (endpoint/model/prompt), Generate (file selection + progress), Browse (view stored summaries).
/// </summary>
public partial class LlmSummaryDialog : Window
{
    private readonly LlmSummaryViewModel _vm;

    public LlmSummaryDialog(LlmSummaryViewModel vm)
    {
        InitializeComponent();
        _vm = vm;

        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(LlmSummaryViewModel.StatusMessage))
                SyncStatusText();
            else if (e.PropertyName is nameof(LlmSummaryViewModel.ConnectionOk)
                                    or nameof(LlmSummaryViewModel.ConnectionStatus)
                                    or nameof(LlmSummaryViewModel.IsTestingConnection))
                SyncConnectionStatus();
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
        // Populate settings fields.
        var endpointBox = this.FindControl<TextBox>("ApiEndpointBox");
        var modelBox = this.FindControl<TextBox>("ModelNameBox");
        if (endpointBox != null) endpointBox.Text = _vm.ApiEndpoint;
        if (modelBox != null) modelBox.Text = _vm.ModelName;

        await _vm.LoadPromptAsync();
        var promptBox = this.FindControl<TextBox>("PromptBox");
        if (promptBox != null) promptBox.Text = _vm.PromptTemplate;

        // Load file list and existing summaries in the background.
        await _vm.LoadCsFilesAsync();
        RebuildFileList();

        _vm.RefreshSummaries();
        SyncStatusText();
    }

    // ── Setup tab ─────────────────────────────────────────────────────────────

    private void OnApiEndpointChanged(object? sender, TextChangedEventArgs e)
    {
        if (sender is TextBox tb)
            _vm.ApiEndpoint = tb.Text ?? string.Empty;
    }

    private void OnModelNameChanged(object? sender, TextChangedEventArgs e)
    {
        if (sender is TextBox tb)
            _vm.ModelName = tb.Text ?? string.Empty;
    }

    private async void OnTestConnectionClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var btn = this.FindControl<Button>("TestConnectionButton");
        if (btn != null) btn.IsEnabled = false;

        try
        {
            await _vm.TestConnectionAsync();
        }
        finally
        {
            if (btn != null) btn.IsEnabled = true;
        }
    }

    private async void OnProbeModelsClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var btn = this.FindControl<Button>("ProbeModelsButton");
        var probeText = this.FindControl<TextBlock>("ProbeStatusText");
        var picker = this.FindControl<ComboBox>("ModelPickerComboBox");

        if (btn != null) btn.IsEnabled = false;
        if (probeText != null)
        {
            probeText.Text = "Probing…";
            probeText.Foreground = Avalonia.Media.Brushes.Gray;
        }

        try
        {
            await _vm.FetchModelsAsync();

            var count = _vm.AvailableModels.Count;
            if (picker != null)
            {
                picker.ItemsSource = _vm.AvailableModels;
                picker.IsEnabled = count > 0;
            }

            if (probeText != null)
            {
                if (count > 0)
                {
                    probeText.Text = $"Found {count} model(s).";
                    probeText.Foreground = Avalonia.Media.Brushes.LightGreen;
                }
                else
                {
                    probeText.Text = "No models returned — check endpoint and try Test Connection first.";
                    probeText.Foreground = new Avalonia.Media.SolidColorBrush(
                        Avalonia.Media.Color.Parse("#FF8888"));
                }
            }
        }
        catch (Exception ex)
        {
            if (probeText != null)
            {
                probeText.Text = $"Probe failed: {ex.Message}";
                probeText.Foreground = new Avalonia.Media.SolidColorBrush(
                    Avalonia.Media.Color.Parse("#FF8888"));
            }
        }
        finally
        {
            if (btn != null) btn.IsEnabled = true;
        }
    }

    private void OnModelPickerSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox cb && cb.SelectedItem is string modelId)
        {
            var modelBox = this.FindControl<TextBox>("ModelNameBox");
            if (modelBox != null) modelBox.Text = modelId;
        }
    }

    private void SyncConnectionStatus()
    {
        var text = this.FindControl<TextBlock>("ConnectionStatusText");
        if (text == null) return;

        if (_vm.IsTestingConnection)
        {
            text.Text = "Testing…";
            text.Foreground = Avalonia.Media.Brushes.Gray;
        }
        else if (_vm.ConnectionOk)
        {
            text.Text = "✔  " + _vm.ConnectionStatus;
            text.Foreground = Avalonia.Media.Brushes.LightGreen;
        }
        else if (!string.IsNullOrEmpty(_vm.ConnectionStatus))
        {
            text.Text = "✖  " + _vm.ConnectionStatus;
            text.Foreground = new Avalonia.Media.SolidColorBrush(
                Avalonia.Media.Color.Parse("#FF8888"));
        }
        else
        {
            text.Text = string.Empty;
        }
    }

    private async void OnSavePromptClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var promptBox = this.FindControl<TextBox>("PromptBox");
        if (promptBox != null)
            _vm.PromptTemplate = promptBox.Text ?? string.Empty;

        await _vm.SavePromptAsync();
        _vm.StatusMessage = "Prompt saved.";
    }

    private void OnSaveSettingsClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _vm.SaveSettings();
        _vm.StatusMessage = "Settings saved.";
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
        _vm.SelectAll(true);
        RebuildFileList();
    }

    private void OnDeselectAllClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _vm.SelectAll(false);
        RebuildFileList();
    }

    private async void OnGenerateClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        await _vm.GenerateSummariesAsync();
        SyncStatusText();
    }

    private void OnCancelGenerateClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => _vm.CancelGeneration();

    private void SyncGenerateButtons()
    {
        var genBtn    = this.FindControl<Button>("GenerateButton");
        var cancelBtn = this.FindControl<Button>("CancelGenerateButton");

        if (genBtn    != null) genBtn.IsEnabled    = !_vm.IsGenerating;
        if (cancelBtn != null) cancelBtn.IsEnabled =  _vm.IsGenerating;
    }

    private void RebuildFileList()
    {
        var listBox = this.FindControl<ListBox>("CsFilesListBox");
        if (listBox == null) return;

        listBox.Items.Clear();

        foreach (var file in _vm.CsFiles)
        {
            var checkBox = new CheckBox
            {
                IsChecked = file.IsSelected,
                Content = new TextBlock
                {
                    Text = file.RelativePath,
                    FontSize = 11,
                    Foreground = Avalonia.Media.Brushes.LightGray,
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
                },
                Tag = file,
                Padding = new Avalonia.Thickness(4, 2)
            };

            checkBox.IsCheckedChanged += (_, _) =>
            {
                if (checkBox.Tag is CsFileItem f)
                    f.IsSelected = checkBox.IsChecked == true;
            };

            listBox.Items.Add(new ListBoxItem
            {
                Content = checkBox,
                Padding = new Avalonia.Thickness(2, 1)
            });
        }

        var countText = this.FindControl<TextBlock>("FileCountText");
        if (countText != null)
            countText.Text = $"C# Files ({_vm.CsFiles.Count})";
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
