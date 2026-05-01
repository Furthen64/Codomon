namespace Codomon.Desktop.Models.SystemMap;

/// <summary>
/// A meaningful functional building block used by one or more Systems.
/// </summary>
public class ModuleModel
{
    /// <summary>Unique identifier for this module.</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Stable identity key used for duplicate detection and upsert/merge logic.
    /// Computed from normalized system key + module name on creation.
    /// </summary>
    public string IdentityKey { get; set; } = string.Empty;

    /// <summary>Display name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The functional role this module fulfils.</summary>
    public ModuleKind Kind { get; set; } = ModuleKind.Other;

    /// <summary>Optional human-provided notes.</summary>
    public string Notes { get; set; } = string.Empty;

    /// <summary>Confidence that this module has been correctly identified and scoped.</summary>
    public ConfidenceLevel Confidence { get; set; } = ConfidenceLevel.Unknown;

    /// <summary>Code nodes that belong to this module.</summary>
    public List<CodeNodeModel> CodeNodes { get; set; } = new();

    /// <summary>
    /// IDs of <see cref="SystemModel"/> entries that use this module.
    /// A module may belong to more than one system; leave empty when the
    /// assignment has not yet been determined.
    /// </summary>
    public List<string> SystemIds { get; set; } = new();

    /// <summary>Evidence that supports this module's definition.</summary>
    public List<EvidenceModel> Evidence { get; set; } = new();
}
