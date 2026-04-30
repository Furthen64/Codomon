using System.Text.Json.Serialization;

namespace Codomon.Desktop.Models.ArchitectureHypothesis;

/// <summary>
/// The result of one LLM architecture-synthesis pass over a set of Markdown summaries.
/// This is stored separately from the accepted <see cref="SystemMap.SystemMapModel"/> so that
/// suggestions remain distinct from manually confirmed data.
/// </summary>
public class ArchitectureHypothesisModel
{
    /// <summary>Unique identifier for this hypothesis snapshot.</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>UTC timestamp when this hypothesis was generated.</summary>
    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>The LLM model name used to generate this hypothesis.</summary>
    [JsonPropertyName("modelName")]
    public string ModelName { get; set; } = string.Empty;

    /// <summary>How many summary files were fed into this synthesis pass.</summary>
    [JsonPropertyName("summaryCount")]
    public int SummaryCount { get; set; }

    /// <summary>Suggested top-level systems inferred from the summaries.</summary>
    [JsonPropertyName("systems")]
    public List<HypothesisSystemModel> Systems { get; set; } = new();

    /// <summary>Code nodes the LLM flags as architecturally significant.</summary>
    [JsonPropertyName("highValueNodes")]
    public List<HypothesisHighValueNodeModel> HighValueNodes { get; set; } = new();

    /// <summary>Startup hypotheses per system.</summary>
    [JsonPropertyName("startup")]
    public List<HypothesisStartupModel> Startup { get; set; } = new();

    /// <summary>Free-text descriptions of areas the LLM found ambiguous or uncertain.</summary>
    [JsonPropertyName("uncertainAreas")]
    public List<string> UncertainAreas { get; set; } = new();
}
