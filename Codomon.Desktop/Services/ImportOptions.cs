namespace Codomon.Desktop.Services;

/// <summary>Options that control how a delimited log file is parsed during import.</summary>
public class ImportOptions
{
    /// <summary>The field delimiter string (e.g. "\t", ",", "|") or a regex pattern.</summary>
    public string Delimiter { get; set; } = "\t";

    /// <summary>
    /// When <c>true</c>, <see cref="Delimiter"/> is treated as a regular expression
    /// and <c>Regex.Split</c> is used instead of a literal string split.
    /// </summary>
    public bool DelimiterIsRegex { get; set; } = false;

    /// <summary>
    /// Zero-based column index of the timestamp field, or -1 to auto-detect
    /// (first column that parses successfully as a date/time).
    /// </summary>
    public int TimestampColumnIndex { get; set; } = -1;

    /// <summary>
    /// Exact format string passed to <c>DateTime.TryParseExact</c>, or <c>null</c>
    /// to fall back on <c>DateTimeOffset.TryParse</c> (flexible / auto-detect).
    /// </summary>
    public string? TimestampFormat { get; set; }

    /// <summary>
    /// IANA or Windows timezone ID (e.g. "UTC", "Local", "America/New_York").
    /// "Local" means <see cref="TimeZoneInfo.Local"/>; "UTC" means <see cref="TimeZoneInfo.Utc"/>.
    /// </summary>
    public string TimeZoneId { get; set; } = "UTC";

    /// <summary>
    /// When <c>true</c>, the first line of the file is treated as a header row:
    /// it supplies default column names and is skipped during import.
    /// </summary>
    public bool FirstRowIsHeader { get; set; } = false;

    /// <summary>
    /// Column layout in display order. An empty list means "all columns in file
    /// order, all included, with default names". When non-empty, the parser
    /// reorders/filters split fields according to this list before detecting
    /// timestamp/level/message.
    /// </summary>
    public List<ImportColumnMapping> Columns { get; set; } = new();
}

/// <summary>
/// Describes one column in the Columns/Headers wizard step.
/// <see cref="OriginalIndex"/> is the zero-based position produced by splitting
/// a line; the position of the entry inside <see cref="ImportOptions.Columns"/>
/// defines the display/import order. Set <see cref="IsIncluded"/> to
/// <c>false</c> to skip the column during import.
/// </summary>
public sealed class ImportColumnMapping
{
    public int OriginalIndex { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsIncluded { get; set; } = true;

    public ImportColumnMapping() { }

    public ImportColumnMapping(int originalIndex, string name, bool isIncluded = true)
    {
        OriginalIndex = originalIndex;
        Name = name;
        IsIncluded = isIncluded;
    }
}
