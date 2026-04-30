using Codomon.Desktop.Models;
using Codomon.Desktop.Models.ArchitectureHypothesis;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Codomon.Desktop.Services;

/// <summary>
/// Manages the LLM architecture-hypothesis synthesis pass.
/// Reads stored Markdown summaries, sends them to the LLM, parses the structured JSON
/// response into an <see cref="ArchitectureHypothesisModel"/>, and saves/lists hypotheses
/// in the workspace <c>hypotheses/</c> folder.
/// </summary>
public static class ArchitectureHypothesisService
{
    private const string HypothesesFolder = "hypotheses";
    private const string PromptFileName = "hypothesis_prompt.md";

    private static readonly JsonSerializerOptions LlmJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly JsonSerializerOptions HypothesisJsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private static readonly JsonSerializerOptions ParseOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    // Shared HttpClient — intentionally not disposed (static lifetime).
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromMinutes(30)
    };

    // ── Prompt template ───────────────────────────────────────────────────────

    /// <summary>
    /// Loads the workspace <c>hypothesis_prompt.md</c> template.
    /// Returns the default text when the file does not exist.
    /// </summary>
    public static async Task<string> LoadPromptTemplateAsync(string workspaceFolderPath)
    {
        var path = Path.Combine(workspaceFolderPath, PromptFileName);
        return File.Exists(path)
            ? await File.ReadAllTextAsync(path)
            : string.Empty;
    }

    /// <summary>Saves <paramref name="content"/> to the workspace <c>hypothesis_prompt.md</c>.</summary>
    public static async Task SavePromptTemplateAsync(string workspaceFolderPath, string content)
    {
        var path = Path.Combine(workspaceFolderPath, PromptFileName);
        await File.WriteAllTextAsync(path, content);
    }

    // ── Synthesis ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Runs a full architecture-hypothesis synthesis pass using the stored Markdown summaries.
    /// Returns the populated <see cref="ArchitectureHypothesisModel"/> and saves it to disk.
    /// </summary>
    public static async Task<ArchitectureHypothesisModel> RunSynthesisAsync(
        string apiEndpoint,
        string modelName,
        string workspaceFolderPath,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        // Load summaries.
        var summaries = LlmSummaryService.ListSummaries(workspaceFolderPath);
        if (summaries.Count == 0)
            throw new InvalidOperationException(
                "No Markdown summaries found. Generate summaries in the LLM Summaries dialog first.");

        progress?.Report($"Loaded {summaries.Count} summary file(s).");
        AppLogger.Debug($"[Hypothesis] Synthesis start: {summaries.Count} summaries  model={modelName}");

        // Build the combined summaries block.
        var summariesBlock = await BuildSummariesBlockAsync(summaries, cancellationToken);
        progress?.Report("Combined summaries into synthesis input.");

        // Load and fill prompt template.
        var promptTemplate = await LoadPromptTemplateAsync(workspaceFolderPath);
        if (string.IsNullOrWhiteSpace(promptTemplate))
            throw new InvalidOperationException(
                "Hypothesis prompt template is empty. Open the Hypothesis dialog Setup tab and save a prompt first.");

        var prompt = promptTemplate.Replace("{Summaries}", summariesBlock);
        AppLogger.Debug($"[Hypothesis] Prompt length={prompt.Length} chars");

        progress?.Report("Calling LLM — this may take a while…");

        // Call the LLM.
        var rawResponse = await CallLlmAsync(apiEndpoint, modelName, prompt, cancellationToken);
        AppLogger.Debug($"[Hypothesis] LLM responded: {rawResponse.Length} chars");

        progress?.Report("Parsing LLM response…");

        // Extract and parse the JSON.
        var hypothesis = ParseHypothesis(rawResponse);
        hypothesis.ModelName = modelName;
        hypothesis.SummaryCount = summaries.Count;
        hypothesis.CreatedAt = DateTime.UtcNow;

        // Save to disk.
        var savedPath = await SaveHypothesisAsync(workspaceFolderPath, hypothesis);
        AppLogger.Info($"[Hypothesis] Saved: {savedPath}");
        progress?.Report($"Hypothesis saved: {Path.GetFileName(savedPath)}");

        return hypothesis;
    }

    // ── Storage ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns all saved hypothesis entries for the workspace, newest first.
    /// </summary>
    public static List<HypothesisEntry> ListHypotheses(string workspaceFolderPath)
    {
        var root = Path.Combine(workspaceFolderPath, HypothesesFolder);
        if (!Directory.Exists(root))
            return new List<HypothesisEntry>();

        var entries = new List<HypothesisEntry>();
        foreach (var file in Directory.EnumerateFiles(root, "hypothesis_*.json")
                     .OrderByDescending(f => f))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            if (!TryParseTimestamp(name, out var ts)) continue;
            entries.Add(new HypothesisEntry { FilePath = file, CreatedAt = ts });
        }

        return entries;
    }

    /// <summary>Loads an <see cref="ArchitectureHypothesisModel"/> from <paramref name="filePath"/>.</summary>
    public static async Task<ArchitectureHypothesisModel> LoadHypothesisAsync(string filePath)
    {
        var json = await File.ReadAllTextAsync(filePath);
        return JsonSerializer.Deserialize<ArchitectureHypothesisModel>(json, ParseOptions)
               ?? new ArchitectureHypothesisModel();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static async Task<string> BuildSummariesBlockAsync(
        List<SummaryEntry> summaries,
        CancellationToken cancellationToken)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var s in summaries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!File.Exists(s.SummaryFilePath)) continue;

            sb.AppendLine($"## {s.SourceRelativePath}");
            sb.AppendLine();
            var content = await File.ReadAllTextAsync(s.SummaryFilePath, cancellationToken);
            // Strip leading metadata comment line if present.
            var firstNewline = content.IndexOf('\n');
            if (firstNewline >= 0 && content.StartsWith("<!-- codomon-source:", StringComparison.Ordinal))
                content = content[(firstNewline + 1)..].TrimStart('\r', '\n');
            sb.AppendLine(content);
            sb.AppendLine();
        }
        return sb.ToString();
    }

    private static async Task<string> SaveHypothesisAsync(
        string workspaceFolderPath,
        ArchitectureHypothesisModel hypothesis)
    {
        var root = Path.Combine(workspaceFolderPath, HypothesesFolder);
        Directory.CreateDirectory(root);

        var timestamp = hypothesis.CreatedAt.ToString("yyyyMMdd_HHmmss");
        var filePath = Path.Combine(root, $"hypothesis_{timestamp}.json");
        var json = JsonSerializer.Serialize(hypothesis, HypothesisJsonOptions);
        await File.WriteAllTextAsync(filePath, json);
        return filePath;
    }

    private static async Task<string> CallLlmAsync(
        string apiEndpoint,
        string modelName,
        string prompt,
        CancellationToken cancellationToken)
    {
        var url = BuildChatCompletionsUrl(apiEndpoint);
        AppLogger.Debug($"[Hypothesis] CallLlm → POST {url}  model={modelName}  promptLength={prompt.Length}");

        var payload = new ChatRequest
        {
            Model = modelName,
            Messages = new[] { new ChatMessage { Role = "user", Content = prompt } }
        };

        try
        {
            using var response = await Http.PostAsJsonAsync(url, payload, LlmJsonOptions, cancellationToken);
            AppLogger.Debug($"[Hypothesis] CallLlm ← {(int)response.StatusCode} {response.ReasonPhrase}");

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                var snippet = body.Length > 500 ? body[..500] + "…" : body;
                AppLogger.Error($"[Hypothesis] CallLlm error body: {snippet}");
                throw new InvalidOperationException(
                    $"LLM API returned {(int)response.StatusCode} {response.ReasonPhrase}: {body}");
            }

            var result = await response.Content.ReadFromJsonAsync<ChatResponse>(LlmJsonOptions, cancellationToken)
                         ?? throw new InvalidOperationException("LLM API returned an empty response.");

            var content = result.Choices?.FirstOrDefault()?.Message?.Content;
            if (string.IsNullOrWhiteSpace(content))
                throw new InvalidOperationException("LLM API returned a response with empty content.");

            return content;
        }
        catch (OperationCanceledException oce)
        {
            AppLogger.Warn($"[Hypothesis] CallLlm cancelled. Inner: {oce.InnerException?.GetType().Name}: {oce.InnerException?.Message}");
            throw;
        }
    }

    /// <summary>
    /// Extracts and parses the JSON block from the LLM response.
    /// Strips surrounding Markdown code-fence markers if present.
    /// </summary>
    internal static ArchitectureHypothesisModel ParseHypothesis(string rawResponse)
    {
        var json = ExtractJson(rawResponse);
        AppLogger.Debug($"[Hypothesis] Parsing JSON: {Math.Min(json.Length, 300)} chars (truncated)");

        try
        {
            var model = JsonSerializer.Deserialize<ArchitectureHypothesisModel>(json, ParseOptions);
            if (model == null)
                throw new InvalidOperationException("JSON deserialized to null.");
            return model;
        }
        catch (JsonException ex)
        {
            AppLogger.Error($"[Hypothesis] JSON parse error: {ex.Message}");
            throw new InvalidOperationException(
                $"Failed to parse LLM response as valid hypothesis JSON: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Strips Markdown code fences and locates the outermost JSON object in the text.
    /// </summary>
    private static string ExtractJson(string text)
    {
        // Remove ```json ... ``` fences.
        var stripped = Regex.Replace(text, @"```json\s*", string.Empty, RegexOptions.IgnoreCase);
        stripped = Regex.Replace(stripped, @"```\s*", string.Empty);
        stripped = stripped.Trim();

        // Find the outermost { … } block.
        var start = stripped.IndexOf('{');
        var end = stripped.LastIndexOf('}');
        if (start >= 0 && end > start)
            return stripped[start..(end + 1)];

        return stripped;
    }

    private static bool TryParseTimestamp(string name, out DateTime result)
    {
        // Expect: hypothesis_yyyyMMdd_HHmmss
        var suffix = name.Length > "hypothesis_".Length
            ? name["hypothesis_".Length..]
            : string.Empty;
        return DateTime.TryParseExact(
            suffix, "yyyyMMdd_HHmmss",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None,
            out result);
    }

    private static string BuildChatCompletionsUrl(string apiEndpoint)
    {
        var base_ = apiEndpoint.TrimEnd('/');
        if (base_.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
            return base_;
        return base_ + "/chat/completions";
    }

    // ── OpenAI-compatible JSON DTOs ───────────────────────────────────────────

    private sealed class ChatRequest
    {
        [JsonPropertyName("model")]
        public string Model { get; set; } = string.Empty;

        [JsonPropertyName("messages")]
        public ChatMessage[] Messages { get; set; } = Array.Empty<ChatMessage>();
    }

    private sealed class ChatMessage
    {
        [JsonPropertyName("role")]
        public string Role { get; set; } = string.Empty;

        [JsonPropertyName("content")]
        public string Content { get; set; } = string.Empty;
    }

    private sealed class ChatResponse
    {
        [JsonPropertyName("choices")]
        public ChatChoice[]? Choices { get; set; }
    }

    private sealed class ChatChoice
    {
        [JsonPropertyName("message")]
        public ChatMessage? Message { get; set; }
    }
}

/// <summary>Represents a saved hypothesis snapshot entry.</summary>
public class HypothesisEntry
{
    /// <summary>Full path to the hypothesis JSON file.</summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>UTC timestamp embedded in the file name.</summary>
    public DateTime CreatedAt { get; set; }

    public string DisplayName => $"Hypothesis  {CreatedAt:yyyy-MM-dd HH:mm}";
}
