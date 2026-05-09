using Codomon.Desktop.Models;
using Codomon.Desktop.Models.ArchitectureHypothesis;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Codomon.Desktop.Services;

/// <summary>
/// Handles LLM-based natural-language location queries over architecture notes and summaries.
/// The user types a plain-English question; the service builds a prompt that includes the latest
/// architecture hypothesis and (optionally) a selection of file summaries, then forwards the
/// question to the configured LLM endpoint.
/// </summary>
public static class LocatorService
{
    private const string SummariesFolder   = "summaries";
    private const string HypothesesFolder  = "hypotheses";

    /// <summary>Rough token budget for the summaries block in the prompt.</summary>
    private const int SummaryTokenBudget = 32_000;

    // Shared HttpClient — intentionally not disposed (static lifetime).
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(5) };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy        = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition      = JsonIgnoreCondition.WhenWritingNull
    };

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Submits <paramref name="question"/> to the LLM together with the architecture context
    /// (hypothesis overview and, optionally, file summaries) and returns the answer text.
    /// </summary>
    /// <param name="apiEndpoint">Base URL of the OpenAI-compatible endpoint.</param>
    /// <param name="modelName">Model identifier to send with the request.</param>
    /// <param name="workspaceFolderPath">Absolute path to the workspace folder.</param>
    /// <param name="question">The plain-English question from the user.</param>
    /// <param name="includeSummaries">When <c>true</c>, up to <see cref="SummaryTokenBudget"/> tokens of summaries are appended.</param>
    /// <param name="progress">Optional callback to report status strings during the operation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The LLM answer text.</returns>
    public static async Task<string> AskAsync(
        string apiEndpoint,
        string modelName,
        string workspaceFolderPath,
        string question,
        bool includeSummaries,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        progress?.Report("Loading architecture notes…");

        var archBlock = await BuildArchitectureBlockAsync(workspaceFolderPath, cancellationToken);
        var summaryBlock = string.Empty;

        if (includeSummaries)
        {
            progress?.Report("Loading summaries…");
            summaryBlock = await BuildSummaryBlockAsync(workspaceFolderPath, SummaryTokenBudget, cancellationToken);
        }

        var prompt = BuildPrompt(question, archBlock, summaryBlock);

        AppLogger.Debug($"[Locator] Sending prompt ({prompt.Length} chars) to {apiEndpoint}, model={modelName}");
        progress?.Report("Asking LLM…");

        var answer = await CallLlmAsync(apiEndpoint, modelName, prompt, cancellationToken);
        AppLogger.Debug($"[Locator] Answer received ({answer.Length} chars).");
        return answer;
    }

    // ── Requirement checks ────────────────────────────────────────────────────

    /// <summary>
    /// Returns whether the workspace has at least one generated summary file.
    /// </summary>
    public static bool HasSummaries(string workspaceFolderPath)
    {
        var root = Path.Combine(workspaceFolderPath, SummariesFolder);
        if (!Directory.Exists(root)) return false;
        return Directory.EnumerateDirectories(root)
                        .SelectMany(d => Directory.EnumerateFiles(d, "*.md"))
                        .Any();
    }

    /// <summary>
    /// Returns whether the workspace has at least one saved architecture hypothesis.
    /// </summary>
    public static bool HasArchitectureNotes(string workspaceFolderPath)
    {
        var root = Path.Combine(workspaceFolderPath, HypothesesFolder);
        if (!Directory.Exists(root)) return false;
        return Directory.EnumerateFiles(root, "hypothesis_*.json").Any();
    }

    // ── Prompt building ───────────────────────────────────────────────────────

    private static async Task<string> BuildArchitectureBlockAsync(
        string workspaceFolderPath,
        CancellationToken cancellationToken)
    {
        var root = Path.Combine(workspaceFolderPath, HypothesesFolder);
        if (!Directory.Exists(root)) return string.Empty;

        var latest = Directory.EnumerateFiles(root, "hypothesis_*.json")
                              .OrderByDescending(f => f)
                              .FirstOrDefault();

        if (latest == null) return string.Empty;

        try
        {
            var json = await File.ReadAllTextAsync(latest, cancellationToken);
            var hypothesis = JsonSerializer.Deserialize<ArchitectureHypothesisModel>(json,
                                 new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                             ?? new ArchitectureHypothesisModel();

            return RenderHypothesisAsMarkdown(hypothesis);
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"[Locator] Failed to load hypothesis: {ex.Message}");
            return string.Empty;
        }
    }

    private static string RenderHypothesisAsMarkdown(ArchitectureHypothesisModel h)
    {
        var sb = new StringBuilder();
        sb.AppendLine("## Architecture Overview");
        sb.AppendLine();

        if (h.Systems.Count > 0)
        {
            sb.AppendLine("### Systems");
            foreach (var sys in h.Systems)
            {
                sb.AppendLine($"- **{sys.Name}** ({sys.Kind}, confidence: {sys.Confidence})");
                if (sys.Modules?.Count > 0)
                {
                    foreach (var mod in sys.Modules)
                    {
                        sb.AppendLine($"  - Module: {mod.Name} (confidence: {mod.Confidence})");
                        if (mod.HighValueNodes?.Count > 0)
                            sb.AppendLine($"    - Key nodes: {string.Join(", ", mod.HighValueNodes)}");
                    }
                }
            }
            sb.AppendLine();
        }

        if (h.HighValueNodes.Count > 0)
        {
            sb.AppendLine("### Architecture Anchors (High-Value Nodes)");
            foreach (var node in h.HighValueNodes)
                sb.AppendLine($"- **{node.Name}** [{node.Signal}] — {node.Reason}");
            sb.AppendLine();
        }

        if (h.Startup.Count > 0)
        {
            sb.AppendLine("### Startup");
            foreach (var s in h.Startup)
            {
                sb.AppendLine($"- {s.System}: {s.Mechanism}");
                if (s.EntryPointCandidates?.Count > 0)
                    sb.AppendLine($"  - Entry points: {string.Join(", ", s.EntryPointCandidates)}");
            }
            sb.AppendLine();
        }

        if (h.UncertainAreas.Count > 0)
        {
            sb.AppendLine("### Uncertain Areas");
            foreach (var a in h.UncertainAreas)
                sb.AppendLine($"- {a}");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static async Task<string> BuildSummaryBlockAsync(
        string workspaceFolderPath,
        int tokenBudget,
        CancellationToken cancellationToken)
    {
        var root = Path.Combine(workspaceFolderPath, SummariesFolder);
        if (!Directory.Exists(root)) return string.Empty;

        // Collect all summary .md files from all batch directories, newest batches first.
        var summaryFiles = Directory.EnumerateDirectories(root)
                                    .OrderByDescending(d => d)
                                    .SelectMany(d => Directory.EnumerateFiles(d, "*.md"))
                                    .ToList();

        var sb = new StringBuilder();
        int tokensUsed = 0;

        foreach (var file in summaryFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var content = await File.ReadAllTextAsync(file, cancellationToken);
                var tokens  = LlmHelper.EstimateTokenCount(content);

                if (tokensUsed + tokens > tokenBudget)
                    break;

                var relPath = Path.GetRelativePath(workspaceFolderPath, file);
                sb.AppendLine($"## {relPath}");
                sb.AppendLine();
                sb.AppendLine(content);
                sb.AppendLine();
                tokensUsed += tokens;
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"[Locator] Skipping summary {file}: {ex.Message}");
            }
        }

        return sb.ToString();
    }

    private static string BuildPrompt(string question, string archBlock, string summaryBlock)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are an expert software architect assistant.");
        sb.AppendLine("A developer has a question about a codebase. Use the architecture notes and file summaries below to give a helpful, specific answer that points to relevant systems, modules, and code files.");
        sb.AppendLine();

        if (!string.IsNullOrWhiteSpace(archBlock))
        {
            sb.AppendLine("--- ARCHITECTURE NOTES ---");
            sb.AppendLine(archBlock);
        }

        if (!string.IsNullOrWhiteSpace(summaryBlock))
        {
            sb.AppendLine("--- FILE SUMMARIES ---");
            sb.AppendLine(summaryBlock);
        }

        sb.AppendLine("--- QUESTION ---");
        sb.AppendLine(question);
        sb.AppendLine();
        sb.AppendLine("Please answer in plain English. If you can identify specific systems, modules, or files, name them. If you are uncertain, say so.");

        return sb.ToString();
    }

    // ── LLM HTTP call ─────────────────────────────────────────────────────────

    private static async Task<string> CallLlmAsync(
        string apiEndpoint,
        string modelName,
        string prompt,
        CancellationToken cancellationToken)
    {
        var url = BuildChatCompletionsUrl(apiEndpoint);

        var payload = new ChatRequest
        {
            Model    = modelName,
            Messages = new[] { new ChatMessage { Role = "user", Content = prompt } }
        };

        try
        {
            using var response = await Http.PostAsJsonAsync(url, payload, JsonOptions, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                var snippet = body.Length > 500 ? body[..500] + "…" : body;
                throw new InvalidOperationException(
                    $"LLM API returned {(int)response.StatusCode} {response.ReasonPhrase}: {snippet}");
            }

            var result = await response.Content.ReadFromJsonAsync<ChatResponse>(JsonOptions, cancellationToken)
                         ?? throw new InvalidOperationException("LLM API returned an empty response.");

            var content = result.Choices?.FirstOrDefault()?.Message?.Content;
            if (string.IsNullOrWhiteSpace(content))
                throw new InvalidOperationException("LLM returned an empty answer.");

            return content;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            throw new InvalidOperationException($"LLM call failed: {ex.Message}", ex);
        }
    }

    private static string BuildChatCompletionsUrl(string apiEndpoint)
    {
        var trimmed = apiEndpoint.TrimEnd('/');
        return $"{trimmed}/chat/completions";
    }

    // ── DTO types (local copies to avoid coupling to other service internals) ─

    private sealed class ChatRequest
    {
        [JsonPropertyName("model")]    public string Model    { get; set; } = string.Empty;
        [JsonPropertyName("messages")] public ChatMessage[] Messages { get; set; } = Array.Empty<ChatMessage>();
    }

    private sealed class ChatMessage
    {
        [JsonPropertyName("role")]    public string Role    { get; set; } = string.Empty;
        [JsonPropertyName("content")] public string Content { get; set; } = string.Empty;
    }

    private sealed class ChatResponse
    {
        [JsonPropertyName("choices")] public ChatChoice[]? Choices { get; set; }
    }

    private sealed class ChatChoice
    {
        [JsonPropertyName("message")] public ChatMessage? Message { get; set; }
    }
}
