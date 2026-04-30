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
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public HighValueNodeSignal Signal { get; set; } = HighValueNodeSignal.Other;

    /// <summary>Confidence level the LLM assigned to this suggestion.</summary>
    [JsonPropertyName("confidence")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ConfidenceLevel Confidence { get; set; } = ConfidenceLevel.Possible;

    /// <summary>Whether the user has accepted this node into the System Map.</summary>
    [JsonIgnore]
    public bool IsAccepted { get; set; }
}