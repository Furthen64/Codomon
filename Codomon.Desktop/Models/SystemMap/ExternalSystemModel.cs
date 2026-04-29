namespace Codomon.Desktop.Models.SystemMap;

/// <summary>
/// Something outside the analyzed codebase that it connects to, such as a database,
/// SMTP server, PLC, payment provider, or another product.
/// </summary>
public class ExternalSystemModel
{
    /// <summary>Unique identifier for this external system.</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>Display name (e.g. "SQL Server", "Stripe").</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Informal category string describing the kind of external system
    /// (e.g. "Database", "SMTP", "PaymentProvider", "PLC", "Product").
    /// </summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>Optional human-provided notes.</summary>
    public string Notes { get; set; } = string.Empty;

    /// <summary>Confidence that this external system has been correctly identified.</summary>
    public ConfidenceLevel Confidence { get; set; } = ConfidenceLevel.Unknown;

    /// <summary>Evidence that supports this external system's identification.</summary>
    public List<EvidenceModel> Evidence { get; set; } = new();
}
