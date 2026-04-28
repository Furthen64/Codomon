using Codomon.Desktop.Models;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Codomon.Desktop.Services;

/// <summary>
/// Communicates with an OpenAI-compatible LLM HTTP API to generate Markdown summaries
/// for C# source files, and manages summary storage inside the workspace.
/// </summary>
public static class LlmSummaryService
{
    private const string SummariesFolder = "summaries";
    private const string PromptFileName = "summary_prompt.md";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    // Shared HttpClient — intentionally not disposed (static lifetime).
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromMinutes(30)
    };

    // ── Connection test ───────────────────────────────────────────────────────

    /// <summary>
    /// Queries the <c>/models</c> endpoint and returns the list of available model IDs.
    /// Returns an empty list when the endpoint is unreachable or returns no data.
    /// </summary>
    public static async Task<List<string>> FetchModelsAsync(
        string apiEndpoint,
        CancellationToken cancellationToken = default)
    {
        var url = BuildModelsUrl(apiEndpoint);
        AppLogger.Debug($"[LLM] FetchModels → GET {url}");
        try
        {
            using var response = await Http.GetAsync(url, cancellationToken);
            AppLogger.Debug($"[LLM] FetchModels ← {(int)response.StatusCode} {response.ReasonPhrase}");
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                var snippet = body.Length > 300 ? body[..300] + "…" : body;
                AppLogger.Warn($"[LLM] FetchModels non-success body: {snippet}");
                return new List<string>();
            }

            var result = await response.Content.ReadFromJsonAsync<ModelsResponse>(JsonOptions, cancellationToken);
            var models = result?.Data?
                .Select(m => m.Id)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id!)
                .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                .ToList()
                ?? new List<string>();

            var modelPreview = models.Count > 10
                ? string.Join(", ", models.Take(10)) + $"… and {models.Count - 10} more"
                : string.Join(", ", models);
            AppLogger.Debug($"[LLM] FetchModels found {models.Count} model(s): {modelPreview}");
            return models;
        }
        catch (OperationCanceledException oce)
        {
            AppLogger.Warn($"[LLM] FetchModels cancelled. IsCancellationRequested={oce.CancellationToken.IsCancellationRequested}. Inner: {oce.InnerException?.GetType().Name}: {oce.InnerException?.Message}");
            throw;
        }
        catch (Exception ex)
        {
            AppLogger.Error($"[LLM] FetchModels exception: {ex.GetType().Name}: {ex.Message}");
            return new List<string>();
        }
    }

    /// <summary>
    /// Sends a minimal chat completion request to verify the endpoint and model are reachable.
    /// Returns a human-readable result message.
    /// </summary>
    public static async Task<(bool Ok, string Message)> TestConnectionAsync(
        string apiEndpoint,
        string modelName,
        CancellationToken cancellationToken = default)
    {
        var url = BuildChatCompletionsUrl(apiEndpoint);
        AppLogger.Debug($"[LLM] TestConnection → POST {url}  model={modelName}");
        try
        {
            var payload = new ChatRequest
            {
                Model = modelName,
                Messages = new[] { new ChatMessage { Role = "user", Content = "Hello" } },
                MaxTokens = 5
            };

            using var response = await Http.PostAsJsonAsync(url, payload, JsonOptions, cancellationToken);
            AppLogger.Debug($"[LLM] TestConnection ← {(int)response.StatusCode} {response.ReasonPhrase}");
            if (response.IsSuccessStatusCode)
            {
                var msg = $"Connected successfully ({(int)response.StatusCode} {response.ReasonPhrase}).";
                AppLogger.Info($"[LLM] TestConnection OK: {msg}");
                return (true, msg);
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            var snippet = body.Length > 200 ? body[..200] + "…" : body;
            var errMsg = $"Server returned {(int)response.StatusCode} {response.ReasonPhrase}: {snippet}";
            AppLogger.Warn($"[LLM] TestConnection failed: {errMsg}");
            return (false, errMsg);
        }
        catch (OperationCanceledException oce)
        {
            AppLogger.Warn($"[LLM] TestConnection cancelled. IsCancellationRequested={oce.CancellationToken.IsCancellationRequested}. Inner: {oce.InnerException?.GetType().Name}: {oce.InnerException?.Message}");
            throw;
        }
        catch (Exception ex)
        {
            var errMsg = $"Connection failed: {ex.Message}";
            AppLogger.Error($"[LLM] TestConnection exception: {ex.GetType().Name}: {ex.Message}");
            return (false, errMsg);
        }
    }

    // ── Summary generation ────────────────────────────────────────────────────

    /// <summary>
    /// Generates a Markdown summary for <paramref name="sourceFilePath"/> using the prompt
    /// template stored in the workspace, then saves it.
    /// Throws on API or I/O failure.
    /// </summary>
    public static async Task<string> GenerateAndSaveSummaryAsync(
        string apiEndpoint,
        string modelName,
        string workspaceFolderPath,
        string batchFolder,
        string sourceFilePath,
        string searchRoot,
        CancellationToken cancellationToken = default)
    {
        var relPath = Path.GetRelativePath(searchRoot, sourceFilePath);
        AppLogger.Debug($"[LLM] GenerateSummary start: {relPath}  model={modelName}");

        // Build prompt from workspace template.
        var promptTemplate = await LoadPromptTemplateAsync(workspaceFolderPath);
        var sourceCode = await File.ReadAllTextAsync(sourceFilePath, cancellationToken);

        var prompt = promptTemplate
            .Replace("{FilePath}", relPath)
            .Replace("{SourceCode}", sourceCode);

        AppLogger.Debug($"[LLM] Prompt ready: template={promptTemplate.Length} chars, sourceCode={sourceCode.Length} chars, totalPrompt={prompt.Length} chars");

        // Call the LLM.
        var summary = await CallLlmAsync(apiEndpoint, modelName, prompt, cancellationToken);

        AppLogger.Debug($"[LLM] GenerateSummary complete: {relPath}  summary={summary.Length} chars");

        // Remove any previous summary for this file, then save new one.
        DeleteExistingSummary(workspaceFolderPath, relPath);
        var savedPath = await WriteSummaryFileAsync(batchFolder, relPath, summary);
        return savedPath;
    }

    // ── Prompt template ───────────────────────────────────────────────────────

    /// <summary>
    /// Loads the workspace <c>summary_prompt.md</c> template. Returns the default text when
    /// the file does not exist.
    /// </summary>
    public static async Task<string> LoadPromptTemplateAsync(string workspaceFolderPath)
    {
        var path = Path.Combine(workspaceFolderPath, PromptFileName);
        return File.Exists(path)
            ? await File.ReadAllTextAsync(path)
            : string.Empty;
    }

    /// <summary>Saves <paramref name="content"/> to the workspace <c>summary_prompt.md</c>.</summary>
    public static async Task SavePromptTemplateAsync(string workspaceFolderPath, string content)
    {
        var path = Path.Combine(workspaceFolderPath, PromptFileName);
        await File.WriteAllTextAsync(path, content);
    }

    // ── Summary storage ───────────────────────────────────────────────────────

    /// <summary>Creates a new timestamped batch folder under <c>summaries/</c> and returns its path.</summary>
    public static string CreateBatchFolder(string workspaceFolderPath)
    {
        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var folder = Path.Combine(workspaceFolderPath, SummariesFolder, timestamp);
        Directory.CreateDirectory(folder);
        return folder;
    }

    /// <summary>
    /// Returns all saved summaries for the workspace, newest first.
    /// Each entry carries the relative source path and the full path to the markdown file.
    /// </summary>
    public static List<SummaryEntry> ListSummaries(string workspaceFolderPath)
    {
        var summariesRoot = Path.Combine(workspaceFolderPath, SummariesFolder);
        if (!Directory.Exists(summariesRoot))
            return new List<SummaryEntry>();

        var entries = new List<SummaryEntry>();
        foreach (var batchDir in Directory.EnumerateDirectories(summariesRoot).OrderByDescending(d => d))
        {
            var batchName = Path.GetFileName(batchDir);
            if (!TryParseBatchTimestamp(batchName, out var ts)) continue;

            foreach (var mdFile in Directory.EnumerateFiles(batchDir, "*.md"))
            {
                var sourcePath = ReadSourcePathFromMetadata(mdFile);
                entries.Add(new SummaryEntry
                {
                    SummaryFilePath = mdFile,
                    SourceRelativePath = sourcePath ?? SafeNameToRelativePath(Path.GetFileNameWithoutExtension(mdFile)),
                    GeneratedAt = ts
                });
            }
        }

        // De-duplicate: keep only the newest summary per source file.
        return entries
            .GroupBy(e => e.SourceRelativePath, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(e => e.GeneratedAt).First())
            .OrderBy(e => e.SourceRelativePath, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static async Task<string> CallLlmAsync(
        string apiEndpoint,
        string modelName,
        string prompt,
        CancellationToken cancellationToken)
    {
        var url = BuildChatCompletionsUrl(apiEndpoint);
        AppLogger.Debug($"[LLM] CallLlm → POST {url}  model={modelName}  promptLength={prompt.Length}");

        var payload = new ChatRequest
        {
            Model = modelName,
            Messages = new[] { new ChatMessage { Role = "user", Content = prompt } }
        };

        try
        {
            using var response = await Http.PostAsJsonAsync(url, payload, JsonOptions, cancellationToken);
            AppLogger.Debug($"[LLM] CallLlm ← {(int)response.StatusCode} {response.ReasonPhrase}");

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                var snippet = body.Length > 500 ? body[..500] + "…" : body;
                AppLogger.Error($"[LLM] CallLlm error body: {snippet}");
                throw new InvalidOperationException(
                    $"LLM API returned {(int)response.StatusCode} {response.ReasonPhrase}: {body}");
            }

            var result = await response.Content.ReadFromJsonAsync<ChatResponse>(JsonOptions, cancellationToken)
                ?? throw new InvalidOperationException("LLM API returned an empty response.");

            var firstChoice = result.Choices?.FirstOrDefault();
            var finishReason = firstChoice?.FinishReason ?? "(null)";
            var content = firstChoice?.Message?.Content;

            AppLogger.Debug($"[LLM] CallLlm response: choices={result.Choices?.Length ?? 0}  finish_reason={finishReason}  contentLength={content?.Length ?? 0}");

            if (string.IsNullOrWhiteSpace(content))
            {
                AppLogger.Warn($"[LLM] CallLlm: response content is empty. finish_reason={finishReason}");
                throw new InvalidOperationException("LLM API returned a response with empty content.");
            }

            return content;
        }
        catch (OperationCanceledException oce)
        {
            AppLogger.Warn($"[LLM] CallLlm cancelled. IsCancellationRequested={oce.CancellationToken.IsCancellationRequested}. Inner: {oce.InnerException?.GetType().Name}: {oce.InnerException?.Message}. HttpClient timeout={Http.Timeout}");
            throw;
        }
    }

    private static async Task<string> WriteSummaryFileAsync(
        string batchFolder,
        string relativeSourcePath,
        string content)
    {
        var safeName = RelativePathToSafeName(relativeSourcePath) + ".md";
        var filePath = Path.Combine(batchFolder, safeName);

        // Ensure the batch folder exists — DeleteExistingSummary may have removed it if it was
        // still empty (no previous summaries had been written to it yet).
        Directory.CreateDirectory(batchFolder);

        // Prepend a hidden metadata comment so the original path can be recovered when browsing.
        var fileContent = $"<!-- codomon-source: {relativeSourcePath} -->{Environment.NewLine}{Environment.NewLine}{content}";
        await File.WriteAllTextAsync(filePath, fileContent);
        return filePath;
    }

    /// <summary>
    /// Deletes any existing summary <c>.md</c> file for <paramref name="relativeSourcePath"/>
    /// across all batch folders so each file has at most one summary at any time.
    /// </summary>
    private static void DeleteExistingSummary(string workspaceFolderPath, string relativeSourcePath)
    {
        var summariesRoot = Path.Combine(workspaceFolderPath, SummariesFolder);
        if (!Directory.Exists(summariesRoot)) return;

        var targetName = RelativePathToSafeName(relativeSourcePath) + ".md";

        foreach (var batchDir in Directory.EnumerateDirectories(summariesRoot))
        {
            var candidate = Path.Combine(batchDir, targetName);
            if (File.Exists(candidate))
                File.Delete(candidate);
        }

        // Remove now-empty batch folders.
        foreach (var batchDir in Directory.EnumerateDirectories(summariesRoot))
        {
            if (!Directory.EnumerateFiles(batchDir).Any())
                Directory.Delete(batchDir);
        }
    }

    private static string BuildChatCompletionsUrl(string apiEndpoint)
    {
        var base_ = apiEndpoint.TrimEnd('/');
        // Avoid appending twice when the caller already includes the path.
        if (base_.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
            return base_;
        return base_ + "/chat/completions";
    }

    private static string BuildModelsUrl(string apiEndpoint)
    {
        var base_ = apiEndpoint.TrimEnd('/');
        if (base_.EndsWith("/models", StringComparison.OrdinalIgnoreCase))
            return base_;
        // Strip any trailing API path so we always append to the base URL.
        if (base_.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
            base_ = base_[..^"/chat/completions".Length];
        return base_ + "/models";
    }

    /// <summary>
    /// Converts a relative path like <c>src/Foo/Bar.cs</c> to a safe file name
    /// <c>src_Foo_Bar_cs</c> by replacing path separators and dots with underscores.
    /// </summary>
    private static string RelativePathToSafeName(string relativePath)
        => relativePath.Replace('\\', '_').Replace('/', '_').Replace('.', '_');

    /// <summary>Inverse of <see cref="RelativePathToSafeName"/>: converts back to a display path.</summary>
    private static string SafeNameToRelativePath(string safeName)
    {
        // Best-effort reconstruction for display purposes only.
        // The last underscore-segment that ends with _cs → .cs
        return safeName.Replace('_', Path.DirectorySeparatorChar);
    }

    private static bool TryParseBatchTimestamp(string folderName, out DateTime result)
    {
        return DateTime.TryParseExact(
            folderName, "yyyyMMdd_HHmmss",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None,
            out result);
    }

    /// <summary>
    /// Reads the first line of a summary .md file and extracts the source path from the
    /// <c>&lt;!-- codomon-source: path --&gt;</c> metadata comment written by
    /// <see cref="WriteSummaryFileAsync"/>.
    /// Returns <c>null</c> if the metadata comment is absent (e.g. files written externally).
    /// </summary>
    private static string? ReadSourcePathFromMetadata(string mdFilePath)
    {
        try
        {
            using var reader = new StreamReader(mdFilePath);
            var firstLine = reader.ReadLine();
            if (firstLine == null) return null;

            const string prefix = "<!-- codomon-source: ";
            const string suffix = " -->";
            if (firstLine.StartsWith(prefix, StringComparison.Ordinal) &&
                firstLine.EndsWith(suffix, StringComparison.Ordinal))
            {
                return firstLine[prefix.Length..^suffix.Length];
            }
        }
        catch { /* Ignore — fall back to name-based display path. */ }

        return null;
    }

    // ── OpenAI-compatible JSON DTOs ───────────────────────────────────────────

    private sealed class ChatRequest
    {
        [JsonPropertyName("model")]
        public string Model { get; set; } = string.Empty;

        [JsonPropertyName("messages")]
        public ChatMessage[] Messages { get; set; } = Array.Empty<ChatMessage>();

        [JsonPropertyName("max_tokens")]
        public int? MaxTokens { get; set; }
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

        [JsonPropertyName("finish_reason")]
        public string? FinishReason { get; set; }
    }

    private sealed class ModelsResponse
    {
        [JsonPropertyName("data")]
        public ModelInfo[]? Data { get; set; }
    }

    private sealed class ModelInfo
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }
    }
}

/// <summary>Represents a stored summary for a single source file.</summary>
public class SummaryEntry
{
    /// <summary>Relative path of the source file (e.g. <c>src/Foo/Bar.cs</c>).</summary>
    public string SourceRelativePath { get; set; } = string.Empty;

    /// <summary>Full path to the generated Markdown file.</summary>
    public string SummaryFilePath { get; set; } = string.Empty;

    /// <summary>UTC timestamp of the batch that produced this summary.</summary>
    public DateTime GeneratedAt { get; set; }

    public string DisplayName =>
        $"{SourceRelativePath}  ({GeneratedAt:yyyy-MM-dd HH:mm})";
}
