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
}
