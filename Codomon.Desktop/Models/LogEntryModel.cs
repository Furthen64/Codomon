namespace Codomon.Desktop.Models;

/// <summary>A single entry parsed from an imported log file.</summary>
public class LogEntryModel
{
    public DateTimeOffset? Timestamp { get; set; }
    public string Level { get; set; } = string.Empty;

    /// <summary>Class, namespace, or module name extracted from the log line.</summary>
    public string Source { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;
    public string RawLine { get; set; } = string.Empty;

    /// <summary>True when the line was successfully parsed into structured fields.</summary>
    public bool IsParsed { get; set; }

    /// <summary>Display color keyed on log level, matching AppLogger conventions.</summary>
    public string LevelColor => Level.ToUpperInvariant() switch
    {
        "ERROR"   => "#FF6666",
        "WARN"    => "#FFCC66",
        "DEBUG"   => "#AAAAAA",
        "TRACE"   => "#888888",
        _         => "#88CCAA"   // INFO and anything unrecognised
    };

    public string Formatted
    {
        get
        {
            if (!IsParsed) return RawLine;
            var ts = Timestamp?.ToString("HH:mm:ss.fff") ?? "??:??:??";
            var src = string.IsNullOrEmpty(Source) ? string.Empty : $"{Source}: ";
            var lvl = string.IsNullOrEmpty(Level) ? "?" : Level;
            return $"[{ts}] [{lvl,-5}]  {src}{Message}";
        }
    }
}
