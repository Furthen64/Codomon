namespace Codomon.Desktop.Models.SystemMap;

/// <summary>
/// A human correction that takes precedence over automatic inference in the System Map.
/// </summary>
public class ManualOverrideModel
{
    /// <summary>Unique identifier for this override.</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>The ID of the System Map entity this override applies to.</summary>
    public string TargetId { get; set; } = string.Empty;

    /// <summary>The kind of correction being recorded.</summary>
    public ManualOverrideType Type { get; set; } = ManualOverrideType.Rename;

    /// <summary>
    /// The new value imposed by this override.
    /// Interpretation depends on <see cref="Type"/>; see <see cref="ManualOverrideType"/> for details.
    /// </summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>Optional human notes explaining why this override was applied.</summary>
    public string Notes { get; set; } = string.Empty;

    /// <summary>When the override was created.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
