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
}
