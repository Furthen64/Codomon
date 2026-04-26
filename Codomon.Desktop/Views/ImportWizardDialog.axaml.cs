using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Codomon.Desktop.Services;
using Codomon.Desktop.ViewModels;

namespace Codomon.Desktop.Views;

public partial class ImportWizardDialog : Window
{
    private readonly ImportWizardViewModel _vm;

    /// <summary>Filled with the finished ViewModel when the user clicks Import.</summary>
    public ImportWizardViewModel? Result { get; private set; }

    public ImportWizardDialog()
    {
        _vm = new ImportWizardViewModel();
        InitializeComponent();
        DataContext = _vm;

        PopulateDelimiterComboBox();
        PopulateTimestampFormatComboBox();
        PopulateTimeZoneComboBox();
        SyncStepUi();
    }

    // ── Step navigation ──────────────────────────────────────────────────────

    private void OnNextClick(object? sender, RoutedEventArgs e)
    {
        if (!_vm.ValidateCurrentStep())
        {
            ShowError(_vm.ValidationError);
            return;
        }

        HideError();
        _vm.CurrentStep++;
        SyncStepUi();
    }

    private void OnBackClick(object? sender, RoutedEventArgs e)
    {
        HideError();
        _vm.CurrentStep--;
        SyncStepUi();
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(null);

    private void OnImportClick(object? sender, RoutedEventArgs e)
    {
        if (!_vm.ValidateCurrentStep())
        {
            ShowError(_vm.ValidationError);
            return;
        }

        Result = _vm;
        Close(_vm);
    }

    // ── Synchronise UI to current step ──────────────────────────────────────

    private void SyncStepUi()
    {
        var step = _vm.CurrentStep;

        SetPanelVisible("Step1Panel", step == 1);
        SetPanelVisible("Step2Panel", step == 2);
        SetPanelVisible("Step3Panel", step == 3);
        SetPanelVisible("Step4Panel", step == 4);

        this.FindControl<Button>("BackButton")!.IsEnabled  = step > 1;
        this.FindControl<Button>("NextButton")!.IsVisible  = step < ImportWizardViewModel.TotalSteps;
        this.FindControl<Button>("ImportButton")!.IsVisible = step == ImportWizardViewModel.TotalSteps;

        this.FindControl<TextBlock>("StepTitleText")!.Text = _vm.StepTitle;

        UpdateStepDots(step);

        if (step == 2)
            RefreshPreviewGrid();

        if (step == 3)
            RefreshTimestampColumnComboBox();

        if (step == 4)
            RefreshSummary();
    }

    private void SetPanelVisible(string name, bool visible)
    {
        var c = this.FindControl<Control>(name);
        if (c != null) c.IsVisible = visible;
    }

    private void UpdateStepDots(int step)
    {
        var active   = new SolidColorBrush(Color.Parse("#3A8FBF"));
        var inactive = new SolidColorBrush(Color.Parse("#2A3F5A"));
        for (int i = 1; i <= ImportWizardViewModel.TotalSteps; i++)
        {
            var dot = this.FindControl<Ellipse>($"Dot{i}");
            if (dot != null) dot.Fill = i <= step ? active : inactive;
        }
    }

    // ── ComboBox population ──────────────────────────────────────────────────

    private void PopulateDelimiterComboBox()
    {
        var combo = this.FindControl<ComboBox>("DelimiterComboBox")!;
        combo.Items.Clear();
        foreach (var opt in ImportWizardViewModel.DelimiterOptions)
            combo.Items.Add(new ComboBoxItem { Content = opt.Label, Tag = opt.Key });
        combo.SelectedIndex = 0; // Tab by default
    }

    private void PopulateTimestampFormatComboBox()
    {
        var combo = this.FindControl<ComboBox>("TimestampFormatComboBox")!;
        combo.Items.Clear();
        foreach (var opt in ImportWizardViewModel.TimestampFormatOptions)
            combo.Items.Add(new ComboBoxItem { Content = opt.Label, Tag = opt.Key });
        combo.SelectedIndex = 0; // Auto-detect by default
    }

    private void PopulateTimeZoneComboBox()
    {
        var combo = this.FindControl<ComboBox>("TimeZoneComboBox")!;
        combo.Items.Clear();
        foreach (var opt in ImportWizardViewModel.TimeZoneOptions)
            combo.Items.Add(new ComboBoxItem { Content = opt.Label, Tag = opt.Id });
        combo.SelectedIndex = 0; // UTC by default
    }

    /// <summary>
    /// Rebuilds the "Timestamp column" ComboBox based on the number of columns
    /// detected in the first preview line (called when entering Step 3).
    /// </summary>
    private void RefreshTimestampColumnComboBox()
    {
        var combo = this.FindControl<ComboBox>("TimestampColumnComboBox")!;
        combo.Items.Clear();
        combo.Items.Add(new ComboBoxItem { Content = "Auto-detect", Tag = "-1" });

        // Estimate column count from the first non-empty file line.
        int cols = EstimateColumnCount();
        for (int i = 0; i < cols; i++)
            combo.Items.Add(new ComboBoxItem { Content = $"Column {i}", Tag = i.ToString() });

        // Restore previous selection if valid.
        int target = _vm.TimestampColumnIndex;
        int idx = target < 0 ? 0 : Math.Min(target + 1, combo.Items.Count - 1);
        combo.SelectedIndex = idx;

        RefreshSampleParse();
    }

    private int EstimateColumnCount()
    {
        if (!System.IO.File.Exists(_vm.FilePath)) return 0;
        try
        {
            var line = System.IO.File
                .ReadLines(_vm.FilePath)
                .FirstOrDefault(l => !string.IsNullOrWhiteSpace(l));
            if (line == null) return 0;
            return line.Split(_vm.EffectiveDelimiter).Length;
        }
        catch { return 0; }
    }

    // ── Step 1: file picker ──────────────────────────────────────────────────

    private async void OnBrowseFileClick(object? sender, RoutedEventArgs e)
    {
        var sp = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (sp == null) return;

        var files = await sp.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select Log File",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Log / text files")
                    { Patterns = new[] { "*.log", "*.txt", "*.csv", "*.tsv", "*.out" } },
                new FilePickerFileType("All files") { Patterns = new[] { "*.*" } }
            }
        });

        if (files.Count == 0) return;

        var path = files[0].Path.LocalPath;
        _vm.FilePath = path;

        var box = this.FindControl<TextBox>("FilePathBox");
        if (box != null) box.Text = path;

        UpdateFileInfo(path);
        HideError();
    }

    private void OnFilePathChanged(object? sender, TextChangedEventArgs e)
    {
        if (sender is TextBox tb)
        {
            _vm.FilePath = tb.Text ?? string.Empty;
            UpdateFileInfo(_vm.FilePath);
        }
    }

    private void UpdateFileInfo(string path)
    {
        var tb = this.FindControl<TextBlock>("FileInfoText");
        if (tb == null) return;

        if (!System.IO.File.Exists(path))
        {
            _vm.PreviewLineCount = 0;
            tb.Text = string.Empty;
            return;
        }

        try
        {
            int count = System.IO.File.ReadLines(path).Count();
            _vm.PreviewLineCount = count;
            tb.Text = _vm.FileInfoText;
        }
        catch
        {
            _vm.PreviewLineCount = 0;
            tb.Text = "Could not read file.";
        }
    }

    // ── Step 2: delimiter + preview ──────────────────────────────────────────

    private void OnDelimiterSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox combo || combo.SelectedItem is not ComboBoxItem item) return;
        _vm.DelimiterKey = item.Tag?.ToString() ?? "tab";

        var customRow = this.FindControl<Grid>("CustomDelimiterRow");
        if (customRow != null) customRow.IsVisible = _vm.IsCustomDelimiter;

        RefreshPreviewGrid();
    }

    private void OnCustomDelimiterChanged(object? sender, TextChangedEventArgs e)
    {
        if (sender is TextBox tb)
        {
            _vm.CustomDelimiter = tb.Text ?? string.Empty;
            RefreshPreviewGrid();
        }
    }

    /// <summary>
    /// Rebuilds the preview grid (Step 2) to show the first 5 non-empty lines
    /// split by the current delimiter.
    /// </summary>
    private void RefreshPreviewGrid()
    {
        var panel = this.FindControl<StackPanel>("PreviewStackPanel");
        if (panel == null) return;
        panel.Children.Clear();

        if (!System.IO.File.Exists(_vm.FilePath)) return;

        string[] previewLines;
        try
        {
            previewLines = System.IO.File
                .ReadLines(_vm.FilePath)
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .Take(5)
                .ToArray();
        }
        catch { return; }

        if (previewLines.Length == 0) return;

        var delimiter = _vm.EffectiveDelimiter;
        if (string.IsNullOrEmpty(delimiter)) return;

        // Determine max column count across sample rows.
        var rows = previewLines.Select(l => l.Split(delimiter)).ToArray();
        int maxCols = rows.Max(r => r.Length);
        if (maxCols == 0) return;

        // Header row (column index labels).
        panel.Children.Add(BuildPreviewRow(
            Enumerable.Range(0, maxCols).Select(i => $"[{i}]").ToArray(),
            isHeader: true));

        // Data rows.
        foreach (var row in rows)
        {
            // Pad short rows to maxCols.
            var padded = row.Concat(Enumerable.Repeat(string.Empty, maxCols - row.Length)).ToArray();
            panel.Children.Add(BuildPreviewRow(padded, isHeader: false));
        }
    }

    private static StackPanel BuildPreviewRow(string[] cells, bool isHeader)
    {
        var row = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal };
        var headerBg = Color.Parse("#1E2A3A");
        var dataBg   = Color.Parse("#0F141E");
        var cellBg   = isHeader ? headerBg : dataBg;
        var cellFg   = isHeader
            ? new SolidColorBrush(Color.Parse("#667788"))
            : new SolidColorBrush(Color.Parse("#AABBCC"));

        foreach (var cell in cells)
        {
            var border = new Border
            {
                Background      = new SolidColorBrush(cellBg),
                BorderBrush     = new SolidColorBrush(Color.Parse("#2A3F5A")),
                BorderThickness = new Avalonia.Thickness(0, 0, 1, 1),
                Padding         = new Avalonia.Thickness(6, 3),
                MinWidth        = 80,
                MaxWidth        = 200
            };

            border.Child = new TextBlock
            {
                Text       = cell,
                Foreground = cellFg,
                FontFamily = new FontFamily("Monospace"),
                FontSize   = 11,
                FontWeight = isHeader ? FontWeight.Bold : FontWeight.Normal,
                TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis
            };

            row.Children.Add(border);
        }

        return row;
    }

    // ── Step 3: timestamp / timezone ─────────────────────────────────────────

    private void OnTimestampColumnChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox combo || combo.SelectedItem is not ComboBoxItem item) return;
        _vm.TimestampColumnIndex = int.TryParse(item.Tag?.ToString(), out var idx) ? idx : -1;
        RefreshSampleParse();
    }

    private void OnTimestampFormatChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox combo || combo.SelectedItem is not ComboBoxItem item) return;
        _vm.TimestampFormatKey = item.Tag?.ToString() ?? "auto";

        var customRow = this.FindControl<Grid>("CustomFormatRow");
        if (customRow != null) customRow.IsVisible = _vm.IsCustomTimestampFormat;

        RefreshSampleParse();
    }

    private void OnCustomFormatChanged(object? sender, TextChangedEventArgs e)
    {
        if (sender is TextBox tb)
        {
            _vm.CustomTimestampFormat = tb.Text ?? string.Empty;
            RefreshSampleParse();
        }
    }

    private void OnTimeZoneChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox combo || combo.SelectedItem is not ComboBoxItem item) return;
        _vm.TimeZoneId = item.Tag?.ToString() ?? "UTC";
        RefreshSampleParse();
    }

    /// <summary>
    /// Reads the first non-empty line from the file, splits it, and tries to
    /// parse the timestamp cell with the current settings — then updates the
    /// "Sample parse" TextBlock.
    /// </summary>
    private void RefreshSampleParse()
    {
        var tb = this.FindControl<TextBlock>("SampleParseText");
        if (tb == null) return;

        if (!System.IO.File.Exists(_vm.FilePath))
        {
            tb.Text = "—";
            return;
        }

        string? sampleLine;
        try
        {
            sampleLine = System.IO.File
                .ReadLines(_vm.FilePath)
                .FirstOrDefault(l => !string.IsNullOrWhiteSpace(l));
        }
        catch { tb.Text = "Could not read file."; return; }

        if (sampleLine == null) { tb.Text = "File is empty."; return; }

        var delimiter = _vm.EffectiveDelimiter;
        if (string.IsNullOrEmpty(delimiter)) { tb.Text = "—"; return; }

        var parts  = sampleLine.Split(delimiter);
        int colIdx = _vm.TimestampColumnIndex;

        string? candidateValue = null;
        if (colIdx >= 0 && colIdx < parts.Length)
        {
            candidateValue = parts[colIdx].Trim();
        }
        else
        {
            // Auto: try each column
            foreach (var p in parts)
            {
                var parsed = LogParser.TryParseTimestamp(p.Trim(), _vm.EffectiveTimestampFormat, _vm.TimeZoneId);
                if (parsed != null)
                {
                    candidateValue = p.Trim();
                    break;
                }
            }
        }

        if (candidateValue == null)
        {
            tb.Text = "No column matches the selected delimiter — check Step 2.";
            return;
        }

        var ts = LogParser.TryParseTimestamp(candidateValue, _vm.EffectiveTimestampFormat, _vm.TimeZoneId);
        if (ts == null)
        {
            tb.Foreground = new SolidColorBrush(Color.Parse("#FF8888"));
            tb.Text = $"Could not parse \"{candidateValue}\" with the selected format.";
        }
        else
        {
            tb.Foreground = new SolidColorBrush(Color.Parse("#88CCAA"));
            tb.Text = $"Raw:    {candidateValue}\nParsed: {ts:yyyy-MM-dd HH:mm:ss.fff zzz}";
        }
    }

    // ── Step 4: summary ──────────────────────────────────────────────────────

    private void RefreshSummary()
    {
        var tb = this.FindControl<TextBlock>("SummaryText");
        if (tb != null) tb.Text = _vm.BuildSummary();
    }

    // ── Validation banner ─────────────────────────────────────────────────────

    private void ShowError(string message)
    {
        var banner = this.FindControl<Border>("ErrorBanner");
        var text   = this.FindControl<TextBlock>("ErrorText");
        if (banner != null) banner.IsVisible = true;
        if (text   != null) text.Text        = message;
    }

    private void HideError()
    {
        var banner = this.FindControl<Border>("ErrorBanner");
        if (banner != null) banner.IsVisible = false;
    }
}
