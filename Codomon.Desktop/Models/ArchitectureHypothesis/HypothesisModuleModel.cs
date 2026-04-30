using Codomon.Desktop.Models.SystemMap;
using System.Text.Json.Serialization;

namespace Codomon.Desktop.Models.ArchitectureHypothesis;

/// <summary>
/// An LLM suggestion for a module within a hypothesised system.
/// </summary>
public class HypothesisModuleModel
{
    /// <summary>Suggested module name.</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Confidence level the LLM assigned to this suggestion.</summary>
    [JsonPropertyName("confidence")]
    [JsonConverter(typeof(LenientStringEnumConverter<ConfidenceLevel>))]
    public ConfidenceLevel Confidence { get; set; } = ConfidenceLevel.Possible;

    /// <summary>Names of code nodes the LLM considers important for this module.</summary>
    [JsonPropertyName("highValueNodes")]
    public List<string> HighValueNodes { get; set; } = new();
}
