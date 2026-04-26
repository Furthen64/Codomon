namespace Codomon.Desktop.Models;

/// <summary>
/// Aggregated activity bucket for a single System over a fixed time slice of the day.
/// </summary>
public class TimelineBucket
{
    /// <summary>ID of the System this bucket belongs to.</summary>
    public string SystemId { get; set; } = string.Empty;

    /// <summary>Start of the time slice (relative to midnight of the log day).</summary>
    public TimeSpan StartTime { get; set; }

    /// <summary>End of the time slice (exclusive).</summary>
    public TimeSpan EndTime { get; set; }

    /// <summary>Number of log entries that fall within this bucket for this System.</summary>
    public int Count { get; set; }

    /// <summary>
    /// Indices into <c>LogReplayViewModel.Entries</c> for entries matched to this bucket,
    /// enabling drill-down to related log lines.
    /// </summary>
    public List<int> MatchingLogEntryIds { get; set; } = new();
}
