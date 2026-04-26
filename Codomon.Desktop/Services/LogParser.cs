using System.Text.RegularExpressions;
using Codomon.Desktop.Models;

namespace Codomon.Desktop.Services;

/// <summary>
/// Attempts to parse a single log line into a <see cref="LogEntryModel"/>.
/// Four common formats are tried in order; unrecognised lines are stored as raw.
/// </summary>
public static class LogParser
{
    // Pattern 1 — bracketed:  [2024-01-15 12:34:56.789] [LEVEL] [Source] message
    private static readonly Regex BracketedPattern = new(
        @"^\[(?<ts>\d{4}-\d{2}-\d{2}[T ]?\d{2}:\d{2}:\d{2}(?:\.\d+)?(?:Z|[+-]\d{2}:?\d{2})?)\]\s*" +
        @"\[(?<level>[A-Z]+)\]\s*" +
        @"(?:\[(?<src>[^\]]+)\]\s*)?(?<msg>.*)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Pattern 2 — ISO + keyword:  2024-01-15T12:34:56 INFO  Source - message
    private static readonly Regex IsoWithLevelPattern = new(
        @"^(?<ts>\d{4}-\d{2}-\d{2}[T ]\d{2}:\d{2}:\d{2}(?:\.\d+)?(?:Z|[+-]\d{2}:?\d{2})?)\s+" +
        @"(?<level>TRACE|VERBOSE|DEBUG|INFO|INFORMATION|WARN|WARNING|ERROR|FATAL|CRITICAL)\s+" +
        @"(?:(?<src>[\w\.]+)\s*[:\-]\s*)?(?<msg>.*)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Pattern 3 — time only:  [12:34:56] [LEVEL] Source: message
    private static readonly Regex TimeOnlyPattern = new(
        @"^\[(?<ts>\d{2}:\d{2}:\d{2}(?:\.\d+)?)\]\s*" +
        @"\[(?<level>[A-Z]+)\]\s*" +
        @"(?:(?<src>[\w\.]+)\s*:\s*)?(?<msg>.*)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Pattern 4 — level first (no timestamp):  DEBUG  Source: message
    private static readonly Regex LevelFirstPattern = new(
        @"^(?<level>TRACE|VERBOSE|DEBUG|INFO|INFORMATION|WARN|WARNING|ERROR|FATAL|CRITICAL)\s+" +
        @"(?:(?<src>[\w\.]+)\s*:\s*)?(?<msg>.*)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static LogEntryModel Parse(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return new LogEntryModel { RawLine = line, IsParsed = false };

        Match m;

        m = BracketedPattern.Match(line);
        if (m.Success) return BuildEntry(line, m);

        m = IsoWithLevelPattern.Match(line);
        if (m.Success) return BuildEntry(line, m);

        m = TimeOnlyPattern.Match(line);
        if (m.Success) return BuildEntry(line, m);

        m = LevelFirstPattern.Match(line);
        if (m.Success) return BuildEntry(line, m);

        return new LogEntryModel { RawLine = line, IsParsed = false, Message = line };
    }

    private static LogEntryModel BuildEntry(string rawLine, Match m)
    {
        DateTimeOffset? ts = null;
        if (m.Groups["ts"].Success &&
            DateTimeOffset.TryParse(
                m.Groups["ts"].Value, null,
                System.Globalization.DateTimeStyles.AssumeLocal |
                System.Globalization.DateTimeStyles.AllowWhiteSpaces,
                out var parsed))
        {
            ts = parsed;
        }

        return new LogEntryModel
        {
            Timestamp = ts,
            Level     = NormalizeLevel(m.Groups["level"].Success ? m.Groups["level"].Value : string.Empty),
            Source    = m.Groups["src"].Success ? m.Groups["src"].Value.Trim() : string.Empty,
            Message   = m.Groups["msg"].Success ? m.Groups["msg"].Value.Trim() : string.Empty,
            RawLine   = rawLine,
            IsParsed  = true
        };
    }

    private static string NormalizeLevel(string raw) => raw.ToUpperInvariant() switch
    {
        "INFORMATION" => "INFO",
        "WARNING"     => "WARN",
        "VERBOSE"     => "TRACE",
        "FATAL" or "CRITICAL" => "ERROR",
        var v => v
    };

    // ── Delimited (CSV / TSV / custom separator) parsing ─────────────────────

    private static readonly HashSet<string> KnownLevels =
        new(StringComparer.OrdinalIgnoreCase)
        { "TRACE", "VERBOSE", "DEBUG", "INFO", "INFORMATION", "WARN", "WARNING", "ERROR", "FATAL", "CRITICAL" };

    /// <summary>
    /// Parses a single line from a delimiter-separated log file using
    /// <paramref name="options"/> to locate the timestamp, detect the level,
    /// and build the message from remaining columns.
    /// </summary>
    public static LogEntryModel ParseDelimited(string line, ImportOptions options)
    {
        if (string.IsNullOrWhiteSpace(line))
            return new LogEntryModel { RawLine = line, IsParsed = false };

        var parts = line.Split(options.Delimiter);

        DateTimeOffset? ts     = null;
        string          level  = string.Empty;
        var             msgBuf = new List<string>(parts.Length);

        for (int i = 0; i < parts.Length; i++)
        {
            var cell = parts[i].Trim();

            // Try timestamp —————————————————————————————————————————————————
            if (ts == null)
            {
                bool isCandidate = options.TimestampColumnIndex < 0   // auto-detect: try every column
                                || options.TimestampColumnIndex == i;  // or exact column
                if (isCandidate)
                {
                    var parsed = TryParseTimestamp(cell, options.TimestampFormat, options.TimeZoneId);
                    if (parsed != null)
                    {
                        ts = parsed;
                        continue;      // consumed as timestamp
                    }
                }
            }

            // Try log level (short cells only) ——————————————————————————————
            if (string.IsNullOrEmpty(level) && cell.Length <= 15 && KnownLevels.Contains(cell))
            {
                level = NormalizeLevel(cell);
                continue;
            }

            msgBuf.Add(cell);
        }

        return new LogEntryModel
        {
            Timestamp = ts,
            Level     = level,
            Source    = string.Empty,
            Message   = string.Join(" | ", msgBuf),
            RawLine   = line,
            IsParsed  = true
        };
    }

    /// <summary>
    /// Attempts to parse <paramref name="value"/> as a <see cref="DateTimeOffset"/>.
    /// When <paramref name="format"/> is non-null it is used with
    /// <c>TryParseExact</c>; otherwise <c>TryParse</c> is used for flexible matching.
    /// The returned value is adjusted to the offset described by
    /// <paramref name="timeZoneId"/>.
    /// </summary>
    public static DateTimeOffset? TryParseTimestamp(string value, string? format, string timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        TimeZoneInfo tz = ResolveTimeZone(timeZoneId);

        DateTimeOffset result;

        if (!string.IsNullOrEmpty(format))
        {
            // Exact format – try DateTimeOffset first, then DateTime.
            if (DateTimeOffset.TryParseExact(value, format, null,
                    System.Globalization.DateTimeStyles.None, out result))
                return AdjustToZone(result, tz);

            if (DateTime.TryParseExact(value, format, null,
                    System.Globalization.DateTimeStyles.None, out var dt))
                return new DateTimeOffset(dt, tz.GetUtcOffset(dt));

            return null;
        }

        // Auto-detect – flexible parse.
        var styles = System.Globalization.DateTimeStyles.AllowWhiteSpaces;
        if (DateTimeOffset.TryParse(value, null, styles, out result))
            return AdjustToZone(result, tz);

        return null;
    }

    private static TimeZoneInfo ResolveTimeZone(string id)
    {
        if (string.IsNullOrEmpty(id) || id.Equals("UTC", StringComparison.OrdinalIgnoreCase))
            return TimeZoneInfo.Utc;
        if (id.Equals("Local", StringComparison.OrdinalIgnoreCase))
            return TimeZoneInfo.Local;
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch { return TimeZoneInfo.Utc; }
    }

    /// <summary>
    /// When <paramref name="tz"/> has a fixed offset (UTC, fixed-offset zones) the
    /// value is already fine; for DST-aware zones we re-interpret the DateTime part
    /// in the target zone to get the correct UTC offset.
    /// </summary>
    private static DateTimeOffset AdjustToZone(DateTimeOffset dto, TimeZoneInfo tz)
    {
        if (dto.Offset == TimeSpan.Zero && tz == TimeZoneInfo.Utc) return dto;
        // Re-interpret as an unspecified local time in the target zone.
        var unspecified = DateTime.SpecifyKind(dto.DateTime, DateTimeKind.Unspecified);
        return new DateTimeOffset(unspecified, tz.GetUtcOffset(unspecified));
    }
}
