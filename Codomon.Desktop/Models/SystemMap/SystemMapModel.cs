namespace Codomon.Desktop.Models.SystemMap;

/// <summary>
/// Codomon's interpreted model of the whole Codebase. It is not one diagram; it is
/// the model behind several views.
/// </summary>
public class SystemMapModel
{
    /// <summary>Unique identifier for this System Map instance.</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>When the System Map was first created.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>When the System Map was last modified.</summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Top-level systems discovered or defined in the codebase.</summary>
    public List<SystemModel> Systems { get; set; } = new();

    /// <summary>External systems that the codebase connects to.</summary>
    public List<ExternalSystemModel> ExternalSystems { get; set; } = new();

    /// <summary>
    /// Typed relationships between any two entities (Systems, Modules, Code Nodes,
    /// External Systems).
    /// </summary>
    public List<RelationshipModel> Relationships { get; set; } = new();

    /// <summary>Human corrections that take precedence over automatic inference.</summary>
    public List<ManualOverrideModel> ManualOverrides { get; set; } = new();

    /// <summary>Flat enumeration of all modules across all systems.</summary>
    public IEnumerable<ModuleModel> AllModules =>
        Systems.SelectMany(s => s.Modules);

    /// <summary>Flat enumeration of all code nodes across all modules in all systems.</summary>
    public IEnumerable<CodeNodeModel> AllCodeNodes =>
        AllModules.SelectMany(m => m.CodeNodes);
}
