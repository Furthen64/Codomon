using Codomon.Desktop.Models.SystemMap;
using System.Text.Json.Serialization;

namespace Codomon.Desktop.Models.ArchitectureHypothesis;

/// <summary>
/// An LLM suggestion for a top-level deployable system.
/// </summary>
public class HypothesisSystemModel
{
    /// <summary>Suggested system name.</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>The kind of system the LLM believes this to be.</summary>
    [JsonPropertyName("kind")]
    [JsonConverter(typeof(LenientStringEnumConverter<SystemKind>))]
    public SystemKind Kind { get; set; } = SystemKind.Unknown;

    /// <summary>Confidence level the LLM assigned to this suggestion.</summary>
    [JsonPropertyName("confidence")]
    [JsonConverter(typeof(LenientStringEnumConverter<ConfidenceLevel>))]
    public ConfidenceLevel Confidence { get; set; } = ConfidenceLevel.Possible;

    /// <summary>Pieces of evidence from the summaries that support this suggestion.</summary>
    [JsonPropertyName("evidence")]
    public List<string> Evidence { get; set; } = new();

    /// <summary>Suggested modules that belong to this system.</summary>
    [JsonPropertyName("modules")]
    public List<HypothesisModuleModel> Modules { get; set; } = new();

    /// <summary>Whether the user has accepted this system suggestion into the System Map.</summary>
    [JsonIgnore]
    public bool IsAccepted { get; set; }
}
