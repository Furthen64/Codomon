using System.Text.RegularExpressions;
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
        PopulateKnownFormatComboBox();
        PopulateTimestampFormatComboBox();
        PopulateTimeZoneComboBox();
        ApplyImportDefaults();
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
        _vm.CurrentStep = Math.Min(_vm.CurrentStep + 1, ImportWizardViewModel.TotalSteps);
        SyncStepUi();
    }

    private void OnBackClick(object? sender, RoutedEventArgs e)
    {
        HideError();
        _vm.CurrentStep = Math.Max(_vm.CurrentStep - 1, 1);
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
        SetPanelVisible("Step5Panel", step == 5);

        this.FindControl<Button>("BackButton")!.IsEnabled  = step > 1;
        this.FindControl<Button>("NextButton")!.IsVisible  = step < ImportWizardViewModel.TotalSteps;
        this.FindControl<Button>("ImportButton")!.IsVisible = step == ImportWizardViewModel.TotalSteps;

        this.FindControl<TextBlock>("StepTitleText")!.Text = _vm.StepTitle;

        UpdateStepDots(step);

        if (step == 2)
        {
            SyncDelimiterComboBoxToVm();
            RefreshPreviewGrid();
        }

        if (step == 3)
            RefreshColumnsStep();

        if (step == 4)
        {
            SyncTimestampComboBoxesToVm();
            RefreshTimestampColumnComboBox();
        }

        if (step == 5)
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
        combo.SelectedIndex = 0; // default overridden by ApplyImportDefaults()
    }

    private void PopulateTimestampFormatComboBox()
    {
        var combo = this.FindControl<ComboBox>("TimestampFormatComboBox")!;
        combo.Items.Clear();
        foreach (var opt in ImportWizardViewModel.TimestampFormatOptions)
            combo.Items.Add(new ComboBoxItem { Content = opt.Label, Tag = opt.Key });
        combo.SelectedIndex = 0; // default overridden by ApplyImportDefaults()
    }

    private void PopulateTimeZoneComboBox()
    {
        var combo = this.FindControl<ComboBox>("TimeZoneComboBox")!;
        combo.Items.Clear();
        foreach (var opt in ImportWizardViewModel.TimeZoneOptions)
            combo.Items.Add(new ComboBoxItem { Content = opt.Label, Tag = opt.Id });
        combo.SelectedIndex = 0; // default overridden by ApplyImportDefaults()
    }

    private void PopulateKnownFormatComboBox()
    {
        var combo = this.FindControl<ComboBox>("KnownFormatComboBox")!;
        combo.Items.Clear();
        foreach (var fmt in ImportWizardViewModel.KnownAppLogFormats)
            combo.Items.Add(new ComboBoxItem { Content = fmt.Label, Tag = fmt.Key });
        combo.SelectedIndex = 0; // default overridden by ApplyImportDefaults()
    }

    /// <summary>
    /// Applies user-configured import defaults to the combo boxes after they have been populated.
    /// </summary>
    private void ApplyImportDefaults()
    {
        var cfg = Codomon.Desktop.Persistence.UserConfigService.Load();
        SelectComboByTag(this.FindControl<ComboBox>("DelimiterComboBox")!,       cfg.DefaultImportDelimiterKey);
        SelectComboByTag(this.FindControl<ComboBox>("TimestampFormatComboBox")!, cfg.DefaultImportTimestampFormatKey);
        SelectComboByTag(this.FindControl<ComboBox>("TimeZoneComboBox")!,        cfg.DefaultImportTimeZoneId);
        SelectComboByTag(this.FindControl<ComboBox>("KnownFormatComboBox")!,     cfg.DefaultImportKnownFormatKey);
    }

    /// <summary>
    /// Selects the ComboBoxItem matching <paramref name="key"/> in <paramref name="combo"/>.
    /// Returns the matched index, or -1 if not found (selection is not changed).
    /// </summary>
    private static int SelectComboByTag(ComboBox combo, string key)
    {
        for (int i = 0; i < combo.Items.Count; i++)
        {
            if (combo.Items[i] is ComboBoxItem item && item.Tag?.ToString() == key)
            {
                combo.SelectedIndex = i;
                return i;
            }
        }
        return -1;
    }

    /// <summary>Synchronises the Delimiter ComboBox (and custom row) to the current VM state.</summary>
    private void SyncDelimiterComboBoxToVm()
    {
        var combo = this.FindControl<ComboBox>("DelimiterComboBox")!;
        SelectComboByTag(combo, _vm.DelimiterKey);

        var customRow = this.FindControl<Grid>("CustomDelimiterRow");
        if (customRow != null) customRow.IsVisible = _vm.IsCustomDelimiter;

        if (_vm.IsCustomDelimiter)
        {
            var box = this.FindControl<TextBox>("CustomDelimiterBox");
            if (box != null) box.Text = _vm.CustomDelimiter;
        }
    }

    /// <summary>Synchronises the Timestamp Format and Timezone ComboBoxes to the current VM state.</summary>
    private void SyncTimestampComboBoxesToVm()
    {
        var fmtCombo = this.FindControl<ComboBox>("TimestampFormatComboBox")!;
        SelectComboByTag(fmtCombo, _vm.TimestampFormatKey);

        var customRow = this.FindControl<Grid>("CustomFormatRow");
        if (customRow != null) customRow.IsVisible = _vm.IsCustomTimestampFormat;

        if (_vm.IsCustomTimestampFormat)
        {
            var box = this.FindControl<TextBox>("CustomFormatBox");
            if (box != null) box.Text = _vm.CustomTimestampFormat;
        }

        var tzCombo = this.FindControl<ComboBox>("TimeZoneComboBox")!;
        SelectComboByTag(tzCombo, _vm.TimeZoneId);
    }

    /// <summary>
    /// Rebuilds the "Timestamp column" ComboBox from the Step 3 column layout
    /// (called when entering Step 4), showing custom header names.
    /// </summary>
    private void RefreshTimestampColumnComboBox()
    {
        var combo = this.FindControl<ComboBox>("TimestampColumnComboBox")!;
        combo.Items.Clear();
        combo.Items.Add(new ComboBoxItem { Content = "Auto-detect", Tag = "-1" });

        // Prefer the edited column layout; fall back to detection when empty.
        IReadOnlyList<(int Index, string Label)> cols;
        if (_vm.Columns.Count > 0)
        {
            cols = _vm.Columns
                .OrderBy(c => c.OriginalIndex)
                .Select(c => (c.OriginalIndex, _vm.GetColumnDisplayName(c.OriginalIndex)))
                .ToList();
        }
        else
        {
            int count = EstimateColumnCount();
            cols = Enumerable.Range(0, count).Select(i => (i, $"Column {i}")).ToList();
        }

        foreach (var (idx, label) in cols)
            combo.Items.Add(new ComboBoxItem { Content = label, Tag = idx.ToString() });

        // Restore previous selection if valid.
        int target = _vm.TimestampColumnIndex;
        int selectedIdx = 0;
        for (int i = 0; i < combo.Items.Count; i++)
        {
            if (combo.Items[i] is ComboBoxItem item && item.Tag?.ToString() == target.ToString())
            { selectedIdx = i; break; }
        }
        combo.SelectedIndex = selectedIdx;

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
            return SplitLine(line).Length;
        }
        catch { return 0; }
    }

    /// <summary>
    /// Splits a log line using the current delimiter (regex or literal).
    /// Empty segments produced at the boundary of a regex match are filtered out
    /// to avoid phantom columns in the preview.
    /// </summary>
    private string[] SplitLine(string line)
    {
        var delimiter = _vm.EffectiveDelimiter;
        if (string.IsNullOrEmpty(delimiter)) return new[] { line };

        if (_vm.DelimiterIsRegex)
        {
            try
            {
                return Regex.Split(line, delimiter)
                            .Where(s => s.Length > 0)
                            .ToArray();
            }
            catch { return new[] { line }; }
        }

        return line.Split(new[] { delimiter }, StringSplitOptions.None);
    }

    // ── Step 3: columns & headers ────────────────────────────────────────────

    private bool _refreshingColumnsUi;

    /// <summary>
    /// (Re)builds the Step 3 column editor from the current delimiter settings.
    /// Detects whether the first row looks like headers and initialises the
    /// ViewModel column list (preserving user edits when the count matches).
    /// </summary>
    private void RefreshColumnsStep()
    {
        _refreshingColumnsUi = true;
        try
        {
            var firstCells = GetFirstRowCells();
            int count = firstCells?.Length ?? EstimateColumnCount();
            bool autoHeader = firstCells != null && DetectFirstRowIsHeader(firstCells);

            _vm.EnsureColumns(count, firstCells, autoHeader);
            // Refresh sample values from the first data row (skip header row).
            var sampleCells = GetSampleDataRowCells();
            foreach (var col in _vm.Columns)
            {
                if (sampleCells != null && col.OriginalIndex < sampleCells.Length)
                    col.SampleValue = sampleCells[col.OriginalIndex].Trim();
                else
                    col.SampleValue = string.Empty;
            }

            var headerCheck = this.FindControl<CheckBox>("HeaderRowCheckBox");
            if (headerCheck != null) headerCheck.IsChecked = _vm.FirstRowIsHeader;

            var headerNote = this.FindControl<TextBlock>("HeaderDetectionText");
            if (headerNote != null) headerNote.Text = _vm.HeaderDetectionNote;

            RenderColumnRows();
        }
        finally
        {
            _refreshingColumnsUi = false;
        }
    }

    private string[]? GetFirstRowCells()
    {
        if (!System.IO.File.Exists(_vm.FilePath)) return null;
        try
        {
            var line = System.IO.File
                .ReadLines(_vm.FilePath)
                .FirstOrDefault(l => !string.IsNullOrWhiteSpace(l));
            return line == null ? null : SplitLine(line);
        }
        catch { return null; }
    }

    private string[]? GetSampleDataRowCells()
    {
        if (!System.IO.File.Exists(_vm.FilePath)) return null;
        try
        {
            var lines = System.IO.File
                .ReadLines(_vm.FilePath)
                .Where(l => !string.IsNullOrWhiteSpace(l));
            if (_vm.FirstRowIsHeader)
                lines = lines.Skip(1);
            var line = lines.FirstOrDefault();
            return line == null ? null : SplitLine(line);
        }
        catch { return null; }
    }

    /// <summary>
    /// Heuristic: the first row looks like headers when no cell parses as a
    /// timestamp/level and most cells are alphabetic text (e.g. "timestamp",
    /// "level", "message") rather than numeric data.
    /// </summary>
    private bool DetectFirstRowIsHeader(string[] cells)
    {
        if (cells.Length == 0) return false;
        int textCells = 0;
        foreach (var raw in cells)
        {
            var cell = raw.Trim().TrimStart('[').TrimEnd(']').Trim();
            if (string.IsNullOrEmpty(cell)) continue;
            // Data signals: parses as timestamp or known level.
            if (LogParser.TryParseTimestamp(cell, _vm.EffectiveTimestampFormat, _vm.TimeZoneId) != null)
                return false;
            if (cell.Length <= 15 && IsKnownLevel(cell))
                return false;
            // Header signals: contains letters but no digits.
            if (cell.Any(char.IsLetter) && !cell.Any(char.IsDigit))
                textCells++;
        }
        return textCells > 0 && textCells * 2 >= cells.Length;
    }

    private static readonly HashSet<string> KnownLevelsLookup =
        new(StringComparer.OrdinalIgnoreCase)
        { "TRACE", "VERBOSE", "DEBUG", "INFO", "INFORMATION", "WARN", "WARNING", "ERROR", "FATAL", "CRITICAL" };

    private static bool IsKnownLevel(string value)
        => KnownLevelsLookup.Contains(value.TrimStart('[').TrimEnd(']').Trim());

    private void RenderColumnRows()
    {
        var panel = this.FindControl<StackPanel>("ColumnsStackPanel");
        if (panel == null) return;
        panel.Children.Clear();

        foreach (var col in _vm.Columns.ToList())
        {
            var row = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto")
            };

            var include = new CheckBox
            {
                IsChecked = col.IsIncluded,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                Tag = col
            };
            include.IsCheckedChanged += OnColumnIncludeChanged;
            Grid.SetColumn(include, 0);

            var nameBox = new TextBox
            {
                Text = col.Name,
                Watermark = $"Column {col.OriginalIndex}",
                Margin = new Avalonia.Thickness(8, 0, 0, 0),
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                Tag = col
            };
            nameBox.TextChanged += OnColumnNameChanged;
            Grid.SetColumn(nameBox, 1);
            ToolTip.SetTip(nameBox, $"Sample: {col.SampleValue}");

            var up = new Button { Content = "↑", Padding = new Avalonia.Thickness(8, 2), Margin = new Avalonia.Thickness(6, 0, 0, 0), Tag = col };
            up.Click += OnColumnMoveUpClick;
            Grid.SetColumn(up, 2);

            var down = new Button { Content = "↓", Padding = new Avalonia.Thickness(8, 2), Margin = new Avalonia.Thickness(4, 0, 0, 0), Tag = col };
            down.Click += OnColumnMoveDownClick;
            Grid.SetColumn(down, 3);

            row.Children.Add(include);
            row.Children.Add(nameBox);
            row.Children.Add(up);
            row.Children.Add(down);

            var sample = new TextBlock
            {
                Text = $"e.g. {TruncateSample(col.SampleValue)}",
                Foreground = new SolidColorBrush(Color.Parse("#667788")),
                FontSize = 11,
                FontFamily = new FontFamily("Monospace"),
                Margin = new Avalonia.Thickness(24, -2, 0, 4)
            };

            var wrapper = new StackPanel { Spacing = 1 };
            wrapper.Children.Add(row);
            wrapper.Children.Add(sample);
            panel.Children.Add(wrapper);
        }
    }

    private static string TruncateSample(string value)
    {
        if (string.IsNullOrEmpty(value)) return "—";
        var v = value.Trim();
        return v.Length > 48 ? v.Substring(0, 48) + "…" : v;
    }

    private void OnColumnNameChanged(object? sender, TextChangedEventArgs e)
    {
        if (_refreshingColumnsUi) return;
        if (sender is TextBox tb && tb.Tag is EditableColumn col)
        {
            col.Name = tb.Text ?? string.Empty;
            HideError();
        }
    }

    private void OnColumnIncludeChanged(object? sender, RoutedEventArgs e)
    {
        if (_refreshingColumnsUi) return;
        if (sender is CheckBox cb && cb.Tag is EditableColumn col)
        {
            col.IsIncluded = cb.IsChecked == true;
            HideError();
        }
    }

    private void OnColumnMoveUpClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is EditableColumn col)
        {
            _vm.MoveColumnUp(col);
            RenderColumnRows();
        }
    }

    private void OnColumnMoveDownClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is EditableColumn col)
        {
            _vm.MoveColumnDown(col);
            RenderColumnRows();
        }
    }

    private void OnHeaderRowChanged(object? sender, RoutedEventArgs e)
    {
        if (_refreshingColumnsUi) return;
        if (sender is CheckBox cb)
        {
            _vm.FirstRowIsHeader = cb.IsChecked == true;
            _vm.HeaderAutoDetected = false;
            var headerNote = this.FindControl<TextBlock>("HeaderDetectionText");
            if (headerNote != null) headerNote.Text = _vm.HeaderDetectionNote;
            // Refresh sample values (data row changes when header is skipped).
            var sampleCells = GetSampleDataRowCells();
            foreach (var col in _vm.Columns)
            {
                if (sampleCells != null && col.OriginalIndex < sampleCells.Length)
                    col.SampleValue = sampleCells[col.OriginalIndex].Trim();
            }
            RenderColumnRows();
            HideError();
        }
    }

    private void OnResetColumnsClick(object? sender, RoutedEventArgs e)
    {
        _vm.ResetColumns(GetFirstRowCells());
        var headerCheck = this.FindControl<CheckBox>("HeaderRowCheckBox");
        if (headerCheck != null) headerCheck.IsChecked = _vm.FirstRowIsHeader;
        RenderColumnRows();
        HideError();
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

    private void OnKnownFormatSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox combo || combo.SelectedItem is not ComboBoxItem item) return;
        var key = item.Tag?.ToString() ?? "none";
        _vm.ApplyKnownFormat(key);
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
            // Stream the file line-by-line to avoid loading large files into memory.
            // Cap the count at 1,000,000 so the UI never freezes on huge files.
            const int maxCount = 1_000_000;
            int count = 0;
            bool capped = false;
            foreach (var _ in System.IO.File.ReadLines(path))
            {
                count++;
                if (count >= maxCount) { capped = true; break; }
            }
            _vm.PreviewLineCount = count;
            tb.Text = capped ? $">{count:N0} lines  —  {System.IO.Path.GetFileName(path)}" : _vm.FileInfoText;
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

        // Update the label and input hint to reflect regex vs literal mode.
        if (_vm.IsCustomDelimiter)
        {
            var label = this.FindControl<TextBlock>("CustomDelimiterLabel");
            var box   = this.FindControl<TextBox>("CustomDelimiterBox");
            bool isRx = _vm.DelimiterIsRegex;
            if (label != null) label.Text = isRx ? "Regex pattern:" : "Custom character:";
            if (box   != null)
            {
                box.Watermark = isRx
                    ? @"e.g.  \]\s*\[  or  \t|\s{2,}  …"
                    : "e.g.  |  or  ;  or  ::  …";
                // Limit literal delimiters to a short string; regex patterns are unrestricted.
                const int maxLiteralDelimiterLength = 8;
                box.MaxLength = isRx ? 0 : maxLiteralDelimiterLength;
            }
        }

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
        var rows = previewLines.Select(l => SplitLine(l)).ToArray();
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

    // ── Step 4: timestamp / timezone ─────────────────────────────────────────

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
    /// Applies the Step 3 column layout (display order + inclusion) to split
    /// parts. Mirrors the logic in <see cref="LogParser.ParseDelimited"/>.
    /// </summary>
    private string[] ApplyColumnLayout(string[] parts)
    {
        if (_vm.Columns.Count == 0) return parts;
        var result = new List<string>(_vm.Columns.Count);
        foreach (var col in _vm.Columns)
        {
            if (!col.IsIncluded) continue;
            result.Add(col.OriginalIndex >= 0 && col.OriginalIndex < parts.Length
                ? parts[col.OriginalIndex]
                : string.Empty);
        }
        int maxMapped = _vm.Columns.Count > 0 ? _vm.Columns.Max(c => c.OriginalIndex) : -1;
        for (int i = maxMapped + 1; i < parts.Length; i++)
            result.Add(parts[i]);
        return result.ToArray();
    }

    /// <summary>Translates an OriginalIndex timestamp column to the effective (post-layout) position.</summary>
    private int ToEffectiveIndex(int originalIndex)
    {
        if (originalIndex < 0 || _vm.Columns.Count == 0) return originalIndex;
        int effective = -1;
        int pos = 0;
        foreach (var col in _vm.Columns)
        {
            if (!col.IsIncluded) continue;
            if (col.OriginalIndex == originalIndex) { effective = pos; break; }
            pos++;
        }
        return effective;
    }

    /// <summary>
    /// Reads the first data line (skipping the header row when set), applies
    /// the Step 3 column layout, and tries to parse the timestamp cell —
    /// then updates the "Sample parse" TextBlock.
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
            var lines = System.IO.File
                .ReadLines(_vm.FilePath)
                .Where(l => !string.IsNullOrWhiteSpace(l));
            if (_vm.FirstRowIsHeader)
                lines = lines.Skip(1);
            sampleLine = lines.FirstOrDefault();
        }
        catch { tb.Text = "Could not read file."; return; }

        if (sampleLine == null) { tb.Text = "File is empty."; return; }

        var delimiter = _vm.EffectiveDelimiter;
        if (string.IsNullOrEmpty(delimiter)) { tb.Text = "—"; return; }

        var parts  = ApplyColumnLayout(SplitLine(sampleLine));
        int colIdx = ToEffectiveIndex(_vm.TimestampColumnIndex);

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

    // ── Step 5: summary ──────────────────────────────────────────────────────

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
