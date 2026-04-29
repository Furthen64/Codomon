namespace Codomon.Desktop.Models.SystemMap;

/// <summary>
/// A high-level startable/monitorable unit such as a Desktop App, Web App, backend
/// Service, Worker, Scheduled Job, or Maintenance Process.
/// </summary>
public class SystemModel
{
    /// <summary>Unique identifier for this system.</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>Display name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The kind of deployable unit this system represents.</summary>
    public SystemKind Kind { get; set; } = SystemKind.Other;

    /// <summary>Optional human-provided notes.</summary>
    public string Notes { get; set; } = string.Empty;

    /// <summary>Confidence that this system has been correctly identified and scoped.</summary>
    public ConfidenceLevel Confidence { get; set; } = ConfidenceLevel.Unknown;

    /// <summary>Functional modules that belong to this system.</summary>
    public List<ModuleModel> Modules { get; set; } = new();

    /// <summary>Evidence that supports this system's definition.</summary>
    public List<EvidenceModel> Evidence { get; set; } = new();
}
