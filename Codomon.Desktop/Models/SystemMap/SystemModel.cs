namespace Codomon.Desktop.Models.SystemMap;

/// <summary>
/// A high-level startable/monitorable unit such as a Desktop App, Web App, backend
/// Service, Worker, Scheduled Job, or Maintenance Process.
/// </summary>
public class SystemModel
{
    /// <summary>Unique identifier for this system.</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Stable identity key used for duplicate detection and upsert/merge logic.
    /// Computed from normalized name + kind on creation; persisted to survive reloads.
    /// </summary>
    public string IdentityKey { get; set; } = string.Empty;

    /// <summary>Display name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The kind of deployable unit this system represents.</summary>
    public SystemKind Kind { get; set; } = SystemKind.Unknown;

    /// <summary>Optional human-provided notes.</summary>
    public string Notes { get; set; } = string.Empty;

    /// <summary>Confidence that this system has been correctly identified and scoped.</summary>
    public ConfidenceLevel Confidence { get; set; } = ConfidenceLevel.Unknown;

    /// <summary>
    /// How this system starts, e.g. "ASP.NET Core", "Generic Host", "Windows Service",
    /// "WPF Application", "Avalonia Application", or "Class Library".
    /// Empty when the startup mechanism has not been determined.
    /// </summary>
    public string StartupMechanism { get; set; } = string.Empty;

    /// <summary>Relative paths to known entry-point files (Program.cs, App.xaml, etc.).</summary>
    public List<string> EntryPointCandidates { get; set; } = new();

    /// <summary>Relative paths to configuration files (appsettings.json, etc.).</summary>
    public List<string> ConfigFileCandidates { get; set; } = new();

    /// <summary>Relative paths to logging configuration files (NLog.config, etc.).</summary>
    public List<string> LogFileCandidates { get; set; } = new();

    /// <summary>Functional modules that belong to this system.</summary>
    public List<ModuleModel> Modules { get; set; } = new();

    /// <summary>Evidence that supports this system's definition.</summary>
    public List<EvidenceModel> Evidence { get; set; } = new();
}
