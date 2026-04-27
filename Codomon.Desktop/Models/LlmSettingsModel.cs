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
}
