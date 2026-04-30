using Codomon.Desktop.Models.SystemMap;
using System.Text.Json.Serialization;

namespace Codomon.Desktop.Models.ArchitectureHypothesis;

/// <summary>
/// An LLM suggestion that a particular code node is architecturally important.
/// </summary>
public class HypothesisHighValueNodeModel
{
    /// <summary>Display name of the suggested node (e.g. class or file name).</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Human-readable reason for this node being considered high-value.</summary>
    [JsonPropertyName("reason")]
    public string Reason { get; set; } = string.Empty;

    /// <summary>The primary architectural signal behind the suggestion.</summary>
    [JsonPropertyName("signal")]
    [JsonConverter(typeof(LenientStringEnumConverter<HighValueNodeSignal>))]
    public HighValueNodeSignal Signal { get; set; } = HighValueNodeSignal.Other;

    /// <summary>Confidence level the LLM assigned to this suggestion.</summary>
    [JsonPropertyName("confidence")]
    [JsonConverter(typeof(LenientStringEnumConverter<ConfidenceLevel>))]
    public ConfidenceLevel Confidence { get; set; } = ConfidenceLevel.Possible;

    /// <summary>Whether the user has accepted this node into the System Map.</summary>
    [JsonIgnore]
    public bool IsAccepted { get; set; }

    /// <summary>
    /// ID of the <see cref="SystemMap.CodeNodeModel"/> this suggestion was accepted into.
    /// Set on first acceptance and preserved across idempotent re-accepts.
    /// </summary>
    [JsonIgnore]
    public string? AcceptedIntoId { get; set; }

    /// <summary>UTC timestamp when this suggestion was first accepted.</summary>
    [JsonIgnore]
    public DateTimeOffset? AcceptedAt { get; set; }
}