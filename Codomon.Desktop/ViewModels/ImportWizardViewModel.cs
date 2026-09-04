using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Codomon.Desktop.Services;

namespace Codomon.Desktop.ViewModels;

/// <summary>
/// Backing state for the 5-step Import Wizard.
/// <list type="number">
///   <item>Step 1 — select file</item>
///   <item>Step 2 — configure delimiter + preview</item>
///   <item>Step 3 — edit columns / headers (rename, reorder, include)</item>
///   <item>Step 4 — timestamp column, format and timezone</item>
///   <item>Step 5 — summary / confirmation</item>
/// </list>
/// </summary>
public class ImportWizardViewModel : INotifyPropertyChanged
{
    public const int TotalSteps = 5;

    private int    _currentStep      = 1;
    private string _filePath         = string.Empty;
    private int    _previewLineCount = 0;
    private string _delimiterKey     = "tab";
    private string _customDelimiter  = string.Empty;
    private int    _timestampColumnIndex  = -1;
    private string _timestampFormatKey    = "auto";
    private string _customTimestampFormat = string.Empty;
    private string _timeZoneId            = "UTC";
    private string _validationError       = string.Empty;
    private string _knownFormatKey        = "none";
    private bool   _firstRowIsHeader      = false;
    private bool   _headerAutoDetected    = false;

    // ── Static option lists ────────────────────────────────────────────────────

    /// <summary>Preset delimiter choices shown in Step 2.</summary>
    public static readonly IReadOnlyList<DelimiterOption> DelimiterOptions = new[]
    {
        new DelimiterOption("tab",        @"Tab (\t)",                          "\t"),
        new DelimiterOption("comma",      "Comma (,)",                          ","),
        new DelimiterOption("semicolon",  "Semicolon (;)",                      ";"),
        new DelimiterOption("pipe",       "Pipe (|)",                           "|"),
        new DelimiterOption("space",      "Space",                              " "),
        // Regex preset: handles [timestamp] [level]  message  (e.g. plasticity.log)
        new DelimiterOption("bracketrx", @"Regex — [ts] [level]  msg",         @"\]\s*\[|\]\s{2,}", IsRegex: true),
        new DelimiterOption("custom",    "Custom…",                            ""),
        new DelimiterOption("customrx",  "Custom Regex…",                      "", IsRegex: true),
    };

    /// <summary>Preset timestamp format choices shown in Step 4.</summary>
    public static readonly IReadOnlyList<TimestampFormatOption> TimestampFormatOptions = new[]
    {
        new TimestampFormatOption("auto",       "Auto-detect",                                null),
        new TimestampFormatOption("iso_ms",     "ISO 8601 ms  (yyyy-MM-ddTHH:mm:ss.fff)",     "yyyy-MM-ddTHH:mm:ss.fff"),
        new TimestampFormatOption("iso",        "ISO 8601     (yyyy-MM-ddTHH:mm:ss)",         "yyyy-MM-ddTHH:mm:ss"),
        new TimestampFormatOption("space_ms",   "Space ms     (yyyy-MM-dd HH:mm:ss.fff)",     "yyyy-MM-dd HH:mm:ss.fff"),
        new TimestampFormatOption("space",      "Space        (yyyy-MM-dd HH:mm:ss)",         "yyyy-MM-dd HH:mm:ss"),
        new TimestampFormatOption("bracket_ms", "Bracketed ms ([yyyy-MM-dd HH:mm:ss.fff])",   "[yyyy-MM-dd HH:mm:ss.fff]"),
        new TimestampFormatOption("bracket",    "Bracketed    ([yyyy-MM-dd HH:mm:ss])",       "[yyyy-MM-dd HH:mm:ss]"),
        new TimestampFormatOption("us",         "US date      (MM/dd/yyyy HH:mm:ss)",         "MM/dd/yyyy HH:mm:ss"),
        new TimestampFormatOption("eu",         "EU date      (dd/MM/yyyy HH:mm:ss)",         "dd/MM/yyyy HH:mm:ss"),
        new TimestampFormatOption("time",       "Time only    (HH:mm:ss)",                    "HH:mm:ss"),
        new TimestampFormatOption("custom",     "Custom…",                                    null),
    };

    /// <summary>Known application log format presets. Selecting one auto-fills Step 2 and Step 3 settings.</summary>
    public static readonly IReadOnlyList<KnownAppLogFormat> KnownAppLogFormats = new[]
    {
        new KnownAppLogFormat("none",        "— None (configure manually) —", "tab",  "",  -1, "auto",   ""),
        new KnownAppLogFormat("orcaslicer",  "OrcaSlicer",                    "tab",  "",   1, "custom", "yyyy-MM-dd HH:mm:ss.ffffff"),
    };

    /// <summary>Timezone choices shown in Step 4 (IANA IDs work on all platforms in .NET 8).</summary>
    public static readonly IReadOnlyList<TimeZoneOption> TimeZoneOptions = new[]
    {
        new TimeZoneOption("UTC",                 "UTC"),
        new TimeZoneOption("Local",               "Local (system timezone)"),
        new TimeZoneOption("America/New_York",    "Eastern  (America/New_York)"),
        new TimeZoneOption("America/Chicago",     "Central  (America/Chicago)"),
        new TimeZoneOption("America/Denver",      "Mountain (America/Denver)"),
        new TimeZoneOption("America/Los_Angeles", "Pacific  (America/Los_Angeles)"),
        new TimeZoneOption("Europe/London",       "London   (Europe/London)"),
        new TimeZoneOption("Europe/Paris",        "Paris / Berlin (Europe/Paris)"),
        new TimeZoneOption("Europe/Helsinki",     "Helsinki (Europe/Helsinki)"),
        new TimeZoneOption("Asia/Tokyo",          "Tokyo    (Asia/Tokyo)"),
        new TimeZoneOption("Asia/Shanghai",       "Shanghai (Asia/Shanghai)"),
        new TimeZoneOption("Australia/Sydney",    "Sydney   (Australia/Sydney)"),
    };

    // ── Wizard state ───────────────────────────────────────────────────────────

    public int CurrentStep
    {
        get => _currentStep;
        set { _currentStep = value; OnPropertyChanged(); OnPropertyChanged(nameof(StepTitle)); }
    }

    // — Step 1: file —————————————————————————————————————————————————————————

    public string FilePath
    {
        get => _filePath;
        set { _filePath = value; OnPropertyChanged(); ClearValidation(); }
    }

    /// <summary>Key of the selected known-application log format preset, or "none".</summary>
    public string KnownFormatKey
    {
        get => _knownFormatKey;
        set { _knownFormatKey = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Applies the settings from the named known-application preset,
    /// overwriting delimiter, timestamp column, and timestamp format fields.
    /// </summary>
    public void ApplyKnownFormat(string key)
    {
        var fmt = KnownAppLogFormats.FirstOrDefault(f => f.Key == key);
        if (fmt == null) return;
        KnownFormatKey           = key;
        DelimiterKey             = fmt.DelimiterKey;
        CustomDelimiter          = fmt.CustomDelimiter;
        TimestampColumnIndex     = fmt.TimestampColumnIndex;
        TimestampFormatKey       = fmt.TimestampFormatKey;
        CustomTimestampFormat    = fmt.CustomTimestampFormat;
    }

    /// <summary>Number of lines in the selected file (set by code-behind after browse).</summary>
    public int PreviewLineCount
    {
        get => _previewLineCount;
        set { _previewLineCount = value; OnPropertyChanged(); OnPropertyChanged(nameof(FileInfoText)); }
    }

    public string FileInfoText =>
        PreviewLineCount > 0
            ? $"{PreviewLineCount:N0} lines  —  {System.IO.Path.GetFileName(FilePath)}"
            : string.Empty;

    // — Step 2: delimiter ————————————————————————————————————————————————————

    public string DelimiterKey
    {
        get => _delimiterKey;
        set
        {
            _delimiterKey = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsCustomDelimiter));
            OnPropertyChanged(nameof(DelimiterIsRegex));
            OnPropertyChanged(nameof(EffectiveDelimiter));
            ClearValidation();
        }
    }

    public string CustomDelimiter
    {
        get => _customDelimiter;
        set
        {
            _customDelimiter = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(EffectiveDelimiter));
            ClearValidation();
        }
    }

    public bool IsCustomDelimiter => _delimiterKey is "custom" or "customrx";

    /// <summary>True when the effective delimiter should be used as a regex pattern.</summary>
    public bool DelimiterIsRegex =>
        DelimiterOptions.FirstOrDefault(o => o.Key == _delimiterKey)?.IsRegex == true;

    public string EffectiveDelimiter
    {
        get
        {
            if (_delimiterKey is "custom" or "customrx") return _customDelimiter;
            return DelimiterOptions.FirstOrDefault(o => o.Key == _delimiterKey)?.Value ?? "\t";
        }
    }

    // — Step 3: columns / headers ——————————————————————————————————————————

    /// <summary>
    /// Editable column layout in display/import order. Rebuilt when entering
    /// Step 3 (see <see cref="EnsureColumns"/>) and edited there.
    /// </summary>
    public System.Collections.ObjectModel.ObservableCollection<EditableColumn> Columns { get; } = new();

    /// <summary>
    /// When true, the first file row is treated as a header row: it supplies
    /// default column names and is skipped during import.
    /// Auto-detected when Step 3 is first opened; the user can override it.
    /// </summary>
    public bool FirstRowIsHeader
    {
        get => _firstRowIsHeader;
        set { _firstRowIsHeader = value; OnPropertyChanged(); ClearValidation(); }
    }

    /// <summary>True when <see cref="FirstRowIsHeader"/> was set by auto-detection.</summary>
    public bool HeaderAutoDetected
    {
        get => _headerAutoDetected;
        set { _headerAutoDetected = value; OnPropertyChanged(); OnPropertyChanged(nameof(HeaderDetectionNote)); }
    }

    public string HeaderDetectionNote =>
        HeaderAutoDetected && FirstRowIsHeader ? "Auto-detected: first row looks like headers."
        : HeaderAutoDetected ? "Auto-detected: first row looks like data."
        : string.Empty;

    /// <summary>
    /// Ensures <see cref="Columns"/> matches the detected column count.
    /// Existing edits are preserved when the count is unchanged; otherwise the
    /// list is rebuilt using <paramref name="firstRowCells"/> as default names
    /// when <paramref name="firstRowIsHeader"/> is true, else "Column N".
    /// Auto-detection is only applied when the column count changes (so the
    /// user's checkbox / renames are not clobbered on every visit).
    /// </summary>
    public void EnsureColumns(int count, string[]? firstRowCells, bool firstRowIsHeader)
    {
        if (count < 0) count = 0;
        if (Columns.Count == count && count > 0) return;

        var previousByIndex = Columns.ToDictionary(c => c.OriginalIndex);
        Columns.Clear();
        for (int i = 0; i < count; i++)
        {
            string defaultName;
            if (firstRowIsHeader && firstRowCells != null && i < firstRowCells.Length
                && !string.IsNullOrWhiteSpace(firstRowCells[i]))
                defaultName = firstRowCells[i].Trim();
            else
                defaultName = $"Column {i}";

            if (previousByIndex.TryGetValue(i, out var prev))
            {
                // Keep the user's rename/include when the layout is rebuilt
                // (e.g. delimiter changed but column count happens to match —
                // handled by early return above — or count changed).
                Columns.Add(new EditableColumn(i, prev.Name, prev.IsIncluded));
            }
            else
            {
                Columns.Add(new EditableColumn(i, defaultName, true));
            }
        }

        FirstRowIsHeader = firstRowIsHeader;
        HeaderAutoDetected = true;
        OnPropertyChanged(nameof(HeaderDetectionNote));
    }

    public void MoveColumnUp(EditableColumn col)
    {
        int idx = Columns.IndexOf(col);
        if (idx > 0) Columns.Move(idx, idx - 1);
    }

    public void MoveColumnDown(EditableColumn col)
    {
        int idx = Columns.IndexOf(col);
        if (idx >= 0 && idx < Columns.Count - 1) Columns.Move(idx, idx + 1);
    }

    /// <summary>Restores default names ("Column N" or header-row values) and includes all columns.</summary>
    public void ResetColumns(string[]? firstRowCells)
    {
        for (int displayIdx = 0; displayIdx < Columns.Count; displayIdx++)
        {
            var col = Columns[displayIdx];
            int orig = col.OriginalIndex;
            string defaultName;
            if (FirstRowIsHeader && firstRowCells != null && orig < firstRowCells.Length
                && !string.IsNullOrWhiteSpace(firstRowCells[orig]))
                defaultName = firstRowCells[orig].Trim();
            else
                defaultName = $"Column {orig}";
            col.Name = defaultName;
            col.IsIncluded = true;
        }
        // Restore file order.
        var ordered = Columns.OrderBy(c => c.OriginalIndex).ToList();
        Columns.Clear();
        foreach (var c in ordered) Columns.Add(c);
    }

    /// <summary>Display label for a column index, using the custom name when available.</summary>
    public string GetColumnDisplayName(int originalIndex)
    {
        var col = Columns.FirstOrDefault(c => c.OriginalIndex == originalIndex);
        return col != null && !string.IsNullOrWhiteSpace(col.Name)
            ? $"Column {originalIndex} ({col.Name})"
            : $"Column {originalIndex}";
    }

    // — Step 4: timestamp & timezone ————————————————————————————————————————

    /// <summary>Zero-based column index, or -1 for auto-detect.</summary>
    public int TimestampColumnIndex
    {
        get => _timestampColumnIndex;
        set { _timestampColumnIndex = value; OnPropertyChanged(); ClearValidation(); }
    }

    public string TimestampFormatKey
    {
        get => _timestampFormatKey;
        set
        {
            _timestampFormatKey = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsCustomTimestampFormat));
            OnPropertyChanged(nameof(EffectiveTimestampFormat));
            ClearValidation();
        }
    }

    public string CustomTimestampFormat
    {
        get => _customTimestampFormat;
        set
        {
            _customTimestampFormat = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(EffectiveTimestampFormat));
            ClearValidation();
        }
    }

    public bool IsCustomTimestampFormat => TimestampFormatKey == "custom";

    /// <summary>The format string to pass to the parser, or <c>null</c> for auto-detect.</summary>
    public string? EffectiveTimestampFormat
    {
        get
        {
            if (TimestampFormatKey == "custom") return _customTimestampFormat;
            if (TimestampFormatKey == "auto")   return null;
            return TimestampFormatOptions.FirstOrDefault(o => o.Key == TimestampFormatKey)?.Format;
        }
    }

    public string TimeZoneId
    {
        get => _timeZoneId;
        set { _timeZoneId = value; OnPropertyChanged(); ClearValidation(); }
    }

    // — Validation ────────────────────────────────────────────────────────────

    public string ValidationError
    {
        get => _validationError;
        private set { _validationError = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasValidationError)); }
    }

    public bool HasValidationError => !string.IsNullOrEmpty(ValidationError);

    // — Step title ─────────────────────────────────────────────────────────────

    public string StepTitle => CurrentStep switch
    {
        1 => "Step 1 of 5 — Select File",
        2 => "Step 2 of 5 — Configure Delimiter",
        3 => "Step 3 of 5 — Columns & Headers",
        4 => "Step 4 of 5 — Timestamp & Timezone",
        5 => "Step 5 of 5 — Ready to Import",
        _ => string.Empty
    };

    // — Helpers ────────────────────────────────────────────────────────────────

    public bool ValidateCurrentStep()
    {
        ValidationError = string.Empty;
        switch (CurrentStep)
        {
            case 1:
                if (string.IsNullOrWhiteSpace(FilePath))
                { ValidationError = "Please select a log file to import."; return false; }
                if (!System.IO.File.Exists(FilePath))
                { ValidationError = "The selected file does not exist."; return false; }
                break;

            case 2:
                if (string.IsNullOrEmpty(EffectiveDelimiter))
                {
                    ValidationError = DelimiterIsRegex
                        ? "Please enter a regex pattern."
                        : "Please enter a delimiter character.";
                    return false;
                }
                if (DelimiterIsRegex)
                {
                    try { _ = new System.Text.RegularExpressions.Regex(EffectiveDelimiter); }
                    catch { ValidationError = "The regex pattern is not valid."; return false; }
                }
                break;

            case 3:
                if (Columns.Count == 0)
                { ValidationError = "No columns detected — check the delimiter on Step 2."; return false; }
                if (!Columns.Any(c => c.IsIncluded))
                { ValidationError = "Include at least one column for import."; return false; }
                if (Columns.Where(c => c.IsIncluded).Any(c => string.IsNullOrWhiteSpace(c.Name)))
                { ValidationError = "Column names cannot be empty. Rename or exclude the column."; return false; }
                var dupes = Columns.Where(c => c.IsIncluded)
                    .GroupBy(c => c.Name.Trim().ToLowerInvariant())
                    .FirstOrDefault(g => g.Count() > 1);
                if (dupes != null)
                { ValidationError = $"Duplicate column name \"{dupes.First().Name}\" — names must be unique."; return false; }
                break;

            case 4:
                if (IsCustomTimestampFormat && string.IsNullOrWhiteSpace(CustomTimestampFormat))
                { ValidationError = "Please enter a custom timestamp format string."; return false; }
                break;

            // Step 5 is confirmation only — always valid.
        }
        return true;
    }

    /// <summary>Builds the <see cref="ImportOptions"/> from the current wizard settings.</summary>
    public ImportOptions BuildImportOptions() => new ImportOptions
    {
        Delimiter            = EffectiveDelimiter,
        DelimiterIsRegex     = DelimiterIsRegex,
        TimestampColumnIndex = TimestampColumnIndex,
        TimestampFormat      = EffectiveTimestampFormat,
        TimeZoneId           = TimeZoneId,
        FirstRowIsHeader     = FirstRowIsHeader,
        Columns              = Columns.Select(c => new ImportColumnMapping(c.OriginalIndex, c.Name?.Trim() ?? string.Empty, c.IsIncluded)).ToList()
    };

    /// <summary>Human-readable summary of the chosen settings (shown on Step 5).</summary>
    public string BuildSummary()
    {
        var delim  = IsCustomDelimiter
            ? $"Custom{(DelimiterIsRegex ? " Regex" : "")}: \"{CustomDelimiter}\""
            : DelimiterOptions.FirstOrDefault(o => o.Key == DelimiterKey)?.Label ?? DelimiterKey;

        var tsCol  = TimestampColumnIndex < 0 ? "Auto-detect" : GetColumnDisplayName(TimestampColumnIndex);

        var tsFrom = IsCustomTimestampFormat
            ? $"Custom: {CustomTimestampFormat}"
            : TimestampFormatOptions.FirstOrDefault(o => o.Key == TimestampFormatKey)?.Label ?? TimestampFormatKey;

        var tz = TimeZoneOptions.FirstOrDefault(o => o.Id == TimeZoneId)?.Label ?? TimeZoneId;

        string cols;
        if (Columns.Count == 0)
        {
            cols = "—";
        }
        else
        {
            var included = Columns.Where(c => c.IsIncluded).ToList();
            var excluded = Columns.Count - included.Count;
            var order = string.Join(", ", Columns.Select(c => $"{c.Name}{(c.IsIncluded ? "" : " (excluded)")}"));
            cols = $"{included.Count}/{Columns.Count} included{(FirstRowIsHeader ? ", header row skipped" : "")}\n" +
                   $"                     {order}";
            if (excluded > 0) { /* count already shown */ }
        }

        return $"File:              {System.IO.Path.GetFileName(FilePath)}\n" +
               $"Lines:             {PreviewLineCount:N0}\n" +
               $"Delimiter:         {delim}\n" +
               $"Columns:           {cols}\n" +
               $"Timestamp column:  {tsCol}\n" +
               $"Timestamp format:  {tsFrom}\n" +
               $"Timezone:          {tz}";
    }

    private void ClearValidation() => ValidationError = string.Empty;

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

// ── Simple record types for option lists ──────────────────────────────────────

/// <summary>A single editable column in Step 3 (Columns &amp; Headers).</summary>
public sealed class EditableColumn : INotifyPropertyChanged
{
    private string _name = string.Empty;
    private bool _isIncluded = true;
    private string _sampleValue = string.Empty;

    public EditableColumn() { }

    public EditableColumn(int originalIndex, string name, bool isIncluded = true)
    {
        OriginalIndex = originalIndex;
        _name = name;
        _isIncluded = isIncluded;
    }

    /// <summary>Zero-based position produced by splitting a line.</summary>
    public int OriginalIndex { get; set; }

    public string Name
    {
        get => _name;
        set { _name = value; OnPropertyChanged(); }
    }

    public bool IsIncluded
    {
        get => _isIncluded;
        set { _isIncluded = value; OnPropertyChanged(); }
    }

    /// <summary>Sample value from the preview (set by the dialog, not persisted).</summary>
    public string SampleValue
    {
        get => _sampleValue;
        set { _sampleValue = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed record DelimiterOption(string Key, string Label, string Value, bool IsRegex = false);

public sealed record TimestampFormatOption(string Key, string Label, string? Format);

public sealed record TimeZoneOption(string Id, string Label);

/// <summary>
/// Describes a known application's log format, so the user can apply it as a preset
/// instead of configuring the delimiter and timestamp settings by hand.
/// </summary>
public sealed record KnownAppLogFormat(
    string Key,
    string Label,
    string DelimiterKey,
    string CustomDelimiter,
    int    TimestampColumnIndex,
    string TimestampFormatKey,
    string CustomTimestampFormat);
