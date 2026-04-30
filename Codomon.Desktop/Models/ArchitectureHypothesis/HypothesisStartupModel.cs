using Codomon.Desktop.Models.SystemMap;
using System.Text.Json.Serialization;

namespace Codomon.Desktop.Models.ArchitectureHypothesis;

/// <summary>
/// An LLM suggestion about how a particular system starts up.
/// </summary>
public class HypothesisStartupModel
{
    /// <summary>Name of the system this startup hypothesis applies to.</summary>
    [JsonPropertyName("system")]
    public string System { get; set; } = string.Empty;

    /// <summary>Suggested startup mechanism (e.g. "Avalonia Application", "ASP.NET Core").</summary>
    [JsonPropertyName("mechanism")]
    public string Mechanism { get; set; } = string.Empty;

    /// <summary>Relative file paths the LLM identifies as entry-point candidates.</summary>
    [JsonPropertyName("entryPointCandidates")]
    public List<string> EntryPointCandidates { get; set; } = new();

    /// <summary>Confidence level the LLM assigned to this suggestion.</summary>
    [JsonPropertyName("confidence")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ConfidenceLevel Confidence { get; set; } = ConfidenceLevel.Possible;
}
