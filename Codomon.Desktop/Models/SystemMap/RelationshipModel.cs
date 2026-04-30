namespace Codomon.Desktop.Models.SystemMap;

/// <summary>
/// A typed connection between any two entities in the System Map (Systems, Modules,
/// Code Nodes, External Systems, or config/log items).
/// </summary>
public class RelationshipModel
{
    /// <summary>Unique identifier for this relationship.</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Stable identity key used for duplicate detection and upsert/merge logic.
    /// Computed from source key + target key + relationship kind.
    /// </summary>
    public string IdentityKey { get; set; } = string.Empty;

    /// <summary>The nature of this relationship.</summary>
    public RelationshipKind Kind { get; set; } = RelationshipKind.Depends;

    /// <summary>ID of the source entity.</summary>
    public string FromId { get; set; } = string.Empty;

    /// <summary>ID of the target entity.</summary>
    public string ToId { get; set; } = string.Empty;

    /// <summary>Optional human-provided notes.</summary>
    public string Notes { get; set; } = string.Empty;

    /// <summary>Confidence that this relationship exists as described.</summary>
    public ConfidenceLevel Confidence { get; set; } = ConfidenceLevel.Unknown;

    /// <summary>Evidence that supports this relationship.</summary>
    public List<EvidenceModel> Evidence { get; set; } = new();
}
