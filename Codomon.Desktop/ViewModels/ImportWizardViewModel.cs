using System.ComponentModel;
using System.Runtime.CompilerServices;
using Codomon.Desktop.Services;

namespace Codomon.Desktop.ViewModels;

/// <summary>
/// Backing state for the 4-step Import Wizard.
/// <list type="number">
///   <item>Step 1 — select file</item>
///   <item>Step 2 — configure delimiter + preview</item>
///   <item>Step 3 — timestamp column, format and timezone</item>
///   <item>Step 4 — summary / confirmation</item>
/// </list>
/// </summary>
public class ImportWizardViewModel : INotifyPropertyChanged
{
    public const int TotalSteps = 4;

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

    /// <summary>Preset timestamp format choices shown in Step 3.</summary>
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

    /// <summary>Timezone choices shown in Step 3 (IANA IDs work on all platforms in .NET 8).</summary>
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

    // — Step 3: timestamp & timezone ————————————————————————————————————————

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
        1 => "Step 1 of 4 — Select File",
        2 => "Step 2 of 4 — Configure Delimiter",
        3 => "Step 3 of 4 — Timestamp & Timezone",
        4 => "Step 4 of 4 — Ready to Import",
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
                if (IsCustomTimestampFormat && string.IsNullOrWhiteSpace(CustomTimestampFormat))
                { ValidationError = "Please enter a custom timestamp format string."; return false; }
                break;

            // Step 4 is confirmation only — always valid.
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
        TimeZoneId           = TimeZoneId
    };

    /// <summary>Human-readable summary of the chosen settings (shown on Step 4).</summary>
    public string BuildSummary()
    {
        var delim  = IsCustomDelimiter
            ? $"Custom{(DelimiterIsRegex ? " Regex" : "")}: \"{CustomDelimiter}\""
            : DelimiterOptions.FirstOrDefault(o => o.Key == DelimiterKey)?.Label ?? DelimiterKey;

        var tsCol  = TimestampColumnIndex < 0 ? "Auto-detect" : $"Column {TimestampColumnIndex}";

        var tsFrom = IsCustomTimestampFormat
            ? $"Custom: {CustomTimestampFormat}"
            : TimestampFormatOptions.FirstOrDefault(o => o.Key == TimestampFormatKey)?.Label ?? TimestampFormatKey;

        var tz = TimeZoneOptions.FirstOrDefault(o => o.Id == TimeZoneId)?.Label ?? TimeZoneId;

        return $"File:              {System.IO.Path.GetFileName(FilePath)}\n" +
               $"Lines:             {PreviewLineCount:N0}\n" +
               $"Delimiter:         {delim}\n" +
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
