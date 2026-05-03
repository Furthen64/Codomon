namespace Codomon.Desktop.Models;

/// <summary>
/// Stores the OpenAI-compatible LLM API configuration for this workspace.
/// These settings are persisted inside workspace.json.
/// </summary>
public class LlmSettingsModel
{
    /// <summary>Base URL of the OpenAI-compatible endpoint (e.g. http://localhost:8080/v1).</summary>
    public string ApiEndpoint { get; set; } = "http://localhost:8080/v1";

    /// <summary>Model name passed to the API (e.g. "llama3", "gpt-4o").</summary>
    public string ModelName { get; set; } = string.Empty;

    /// <summary>
    /// Maximum estimated token count allowed in a single hypothesis synthesis prompt.
    /// When the combined token count of all summaries exceeds this value the synthesis
    /// pass is automatically split into smaller batches and the results are merged.
    /// A value of 0 or less disables the threshold check (no splitting).
    /// </summary>
    public int HypothesisTokenThreshold { get; set; } = 60_000;

    /// <summary>
    /// Maximum completion tokens allowed for each generated summary.
    /// A value of 0 or less lets the server/model decide with no explicit cap.
    /// </summary>
    public int SummaryMaxOutputTokens { get; set; } = 512;

    /// <summary>
    /// Number of summaries to process concurrently.
    /// Set to 1 for strictly one-at-a-time processing.
    /// </summary>
    public int SummaryQueueSize { get; set; } = 1;
}
