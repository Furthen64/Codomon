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

    /// <summary>
    /// Modules that have been classified independently of any specific system.
    /// A module here may be referenced by one or more <see cref="SystemModel"/> entries
    /// via <see cref="ModuleModel.SystemIds"/>.
    /// </summary>
    public List<ModuleModel> Modules { get; set; } = new();

    /// <summary>External systems that the codebase connects to.</summary>
    public List<ExternalSystemModel> ExternalSystems { get; set; } = new();

    /// <summary>
    /// Typed relationships between any two entities (Systems, Modules, Code Nodes,
    /// External Systems).
    /// </summary>
    public List<RelationshipModel> Relationships { get; set; } = new();

    /// <summary>Human corrections that take precedence over automatic inference.</summary>
    public List<ManualOverrideModel> ManualOverrides { get; set; } = new();

    /// <summary>
    /// Flat enumeration of all modules: top-level classified modules plus any modules
    /// embedded directly inside system entries, deduplicated by ID.
    /// </summary>
    public IEnumerable<ModuleModel> AllModules
    {
        get
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var m in Modules)
                if (seen.Add(m.Id)) yield return m;
            foreach (var m in Systems.SelectMany(s => s.Modules))
                if (seen.Add(m.Id)) yield return m;
        }
    }

    /// <summary>Flat enumeration of all code nodes across all modules.</summary>
    public IEnumerable<CodeNodeModel> AllCodeNodes =>
        AllModules.SelectMany(m => m.CodeNodes);
}
