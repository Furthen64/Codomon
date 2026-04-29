namespace Codomon.Desktop.Models.SystemMap;

/// <summary>
/// A piece of evidence that explains why Codomon placed an item in a particular position
/// in the System Map.
/// </summary>
public class EvidenceModel
{
    /// <summary>The tool or mechanism that generated this evidence (e.g. "Roslyn", "LogPattern", "Manual").</summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>Human-readable description of what was found.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Optional reference to the source location, such as a file path and line number,
    /// or a log pattern string.
    /// </summary>
    public string SourceRef { get; set; } = string.Empty;
}
