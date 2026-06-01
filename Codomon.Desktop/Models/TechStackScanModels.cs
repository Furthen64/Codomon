namespace Codomon.Desktop.Models;

/// <summary>Evidence that explains why a technology was detected.</summary>
public class TechnologyEvidence
{
    public string Source { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string SourceRef { get; set; } = string.Empty;
}

/// <summary>A detected technology for a project or the workspace as a whole.</summary>
public class DetectedTechnology
{
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Confidence { get; set; } = "Likely";
    public string Version { get; set; } = string.Empty;
    public string ProjectName { get; set; } = string.Empty;
    public string ProjectFilePath { get; set; } = string.Empty;
    public List<TechnologyEvidence> Evidence { get; set; } = new();
}

/// <summary>A project discovered for the tech stack scan.</summary>
public class TechStackProject
{
    public string Name { get; set; } = string.Empty;
    public string ProjectFilePath { get; set; } = string.Empty;
    public string FolderPath { get; set; } = string.Empty;
}

/// <summary>Top-level result of a tech stack scan.</summary>
public class TechStackScanResult
{
    public string Schema { get; set; } = "codomon-techstack/1";
    public DateTime ScanTime { get; set; }
    public string SourcePath { get; set; } = string.Empty;
    public List<TechStackProject> Projects { get; set; } = new();
    public List<DetectedTechnology> Technologies { get; set; } = new();
}

/// <summary>Result of a tech stack preflight check.</summary>
public record TechStackAvailabilityResult(
    bool IsAvailable,
    string Message,
    int ProjectCount,
    int DetectionMarkerCount);
