using Codomon.Desktop.Models;
using Codomon.Desktop.Persistence;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
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
    private const string RuntimeStatsFileName = "runtime_stats.json";
    private const double DefaultEstimatedTokensPerSecond = 1200.0;
    private const int MaxTotalSummaryRequests = 6;
    private const string ContinuationPrompt = "Continue exactly where you left off. Do not repeat previous text. Return only the remaining summary content.";
    private static readonly TimeSpan ConnectivityProbeTimeout = TimeSpan.FromSeconds(10);

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
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(ConnectivityProbeTimeout);
        try
        {
            using var response = await Http.GetAsync(url, timeoutCts.Token);
            AppLogger.Debug($"[LLM] FetchModels ← {(int)response.StatusCode} {response.ReasonPhrase}");
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(timeoutCts.Token);
                var snippet = body.Length > 300 ? body[..300] + "…" : body;
                AppLogger.Warn($"[LLM] FetchModels non-success body: {snippet}");
                return new List<string>();
            }

            var result = await response.Content.ReadFromJsonAsync<ModelsResponse>(JsonOptions, timeoutCts.Token);
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
            if (!cancellationToken.IsCancellationRequested)
            {
                AppLogger.Warn($"[LLM] FetchModels timed out after {ConnectivityProbeTimeout.TotalSeconds:0}s.");
                return new List<string>();
            }
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
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(ConnectivityProbeTimeout);
        try
        {
            var payload = new ChatRequest
            {
                Model = modelName,
                Messages = new[] { new ChatMessage { Role = "user", Content = "Hello" } },
                MaxTokens = 5
            };

            using var response = await Http.PostAsJsonAsync(url, payload, JsonOptions, timeoutCts.Token);
            AppLogger.Debug($"[LLM] TestConnection ← {(int)response.StatusCode} {response.ReasonPhrase}");
            if (response.IsSuccessStatusCode)
            {
                var msg = $"Connected successfully ({(int)response.StatusCode} {response.ReasonPhrase}).";
                AppLogger.Info($"[LLM] TestConnection OK: {msg}");
                return (true, msg);
            }

            var body = await response.Content.ReadAsStringAsync(timeoutCts.Token);
            var snippet = body.Length > 200 ? body[..200] + "…" : body;
            var errMsg = $"Server returned {(int)response.StatusCode} {response.ReasonPhrase}: {snippet}";
            AppLogger.Warn($"[LLM] TestConnection failed: {errMsg}");
            return (false, errMsg);
        }
        catch (OperationCanceledException oce)
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                var timeoutMsg = $"Connection test timed out after {ConnectivityProbeTimeout.TotalSeconds:0}s.";
                AppLogger.Warn($"[LLM] TestConnection timeout: {timeoutMsg}");
                return (false, timeoutMsg);
            }
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
        int maxOutputTokens,
        string workspaceFolderPath,
        string batchFolder,
        string sourceFilePath,
        string searchRoot,
        CancellationToken cancellationToken = default,
        IProgress<string>? streamingTokenProgress = null)
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
        AppLogger.Debug($"[LLM] dump prompt: {Environment.NewLine}--- PROMPT START ---{Environment.NewLine}{prompt}{Environment.NewLine}--- PROMPT END ---");

        var summary = await CallLlmAsync(
            apiEndpoint,
            modelName,
            prompt,
            maxOutputTokens,
            cancellationToken,
            streamingTokenProgress);

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
        Directory.CreateDirectory(workspaceFolderPath);

        var path = Path.Combine(workspaceFolderPath, PromptFileName);
        if (File.Exists(path))
            return await File.ReadAllTextAsync(path);

        var defaultPrompt = WorkspaceSerializer.GetDefaultSummaryPrompt();
        await File.WriteAllTextAsync(path, defaultPrompt);
        return defaultPrompt;
    }

    /// <summary>Saves <paramref name="content"/> to the workspace <c>summary_prompt.md</c>.</summary>
    public static async Task SavePromptTemplateAsync(string workspaceFolderPath, string content)
    {
        Directory.CreateDirectory(workspaceFolderPath);

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
    /// Looks up the most recent summary for <paramref name="relativeSourcePath"/> and returns
    /// the text of its first content paragraph (skipping the metadata comment and any Markdown
    /// headings that precede it).  Returns <c>null</c> when no summary exists for the file.
    /// </summary>
    public static string? GetSummaryFirstParagraph(string workspaceFolderPath, string relativeSourcePath)
    {
        if (string.IsNullOrWhiteSpace(workspaceFolderPath) || string.IsNullOrWhiteSpace(relativeSourcePath))
            return null;

        var summariesRoot = Path.Combine(workspaceFolderPath, SummariesFolder);
        if (!Directory.Exists(summariesRoot))
            return null;

        var safeName = RelativePathToSafeName(relativeSourcePath) + ".md";

        foreach (var batchDir in Directory.EnumerateDirectories(summariesRoot).OrderByDescending(d => d))
        {
            var candidate = Path.Combine(batchDir, safeName);
            if (!File.Exists(candidate))
                continue;

            try
            {
                return ExtractFirstParagraph(candidate);
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"[LLM] GetSummaryFirstParagraph failed reading '{candidate}': {ex.Message}");
                return null;
            }
        }

        return null;
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

    /// <summary>
    /// Loads learned runtime statistics for LLM summary generation for the given endpoint/model.
    /// Returns a default fallback profile when no recorded history exists.
    /// </summary>
    public static async Task<SummaryRuntimeProfile> LoadRuntimeProfileAsync(
        string workspaceFolderPath,
        string apiEndpoint,
        string modelName,
        CancellationToken cancellationToken = default)
    {
        var store = await LoadRuntimeStatsStoreAsync(workspaceFolderPath, cancellationToken);
        var entry = store.Entries.FirstOrDefault(e =>
            string.Equals(e.ApiEndpoint, NormalizeApiEndpoint(apiEndpoint), StringComparison.OrdinalIgnoreCase) &&
            string.Equals(e.ModelName, modelName ?? string.Empty, StringComparison.OrdinalIgnoreCase));

        if (entry == null || entry.TotalElapsedSeconds <= 0 || entry.TotalEstimatedTokens <= 0)
        {
            return new SummaryRuntimeProfile
            {
                SampleCount = 0,
                AverageTokensPerSecond = DefaultEstimatedTokensPerSecond,
                UsesFallback = true
            };
        }

        return new SummaryRuntimeProfile
        {
            SampleCount = entry.SampleCount,
            AverageTokensPerSecond = Math.Max(1.0, entry.TotalEstimatedTokens / entry.TotalElapsedSeconds),
            UsesFallback = false
        };
    }

    /// <summary>
    /// Records a completed summary-generation batch so future runtime estimates can use
    /// observed throughput for the same endpoint/model pair.
    /// </summary>
    public static async Task RecordRuntimeSampleAsync(
        string workspaceFolderPath,
        string apiEndpoint,
        string modelName,
        int estimatedTokens,
        TimeSpan elapsed,
        CancellationToken cancellationToken = default)
    {
        if (estimatedTokens <= 0 || elapsed.TotalSeconds <= 0)
            return;

        var store = await LoadRuntimeStatsStoreAsync(workspaceFolderPath, cancellationToken);
        var normalizedEndpoint = NormalizeApiEndpoint(apiEndpoint);
        var normalizedModelName = modelName ?? string.Empty;

        var entry = store.Entries.FirstOrDefault(e =>
            string.Equals(e.ApiEndpoint, normalizedEndpoint, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(e.ModelName, normalizedModelName, StringComparison.OrdinalIgnoreCase));

        if (entry == null)
        {
            entry = new SummaryRuntimeStatsEntry
            {
                ApiEndpoint = normalizedEndpoint,
                ModelName = normalizedModelName
            };
            store.Entries.Add(entry);
        }

        entry.SampleCount += 1;
        entry.TotalEstimatedTokens += estimatedTokens;
        entry.TotalElapsedSeconds += elapsed.TotalSeconds;
        entry.LastObservedTokensPerSecond = estimatedTokens / elapsed.TotalSeconds;
        entry.LastUpdatedUtc = DateTime.UtcNow;

        await SaveRuntimeStatsStoreAsync(workspaceFolderPath, store, cancellationToken);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string GetRuntimeStatsPath(string workspaceFolderPath)
        => Path.Combine(workspaceFolderPath, SummariesFolder, RuntimeStatsFileName);

    private static async Task<SummaryRuntimeStatsStore> LoadRuntimeStatsStoreAsync(
        string workspaceFolderPath,
        CancellationToken cancellationToken)
    {
        var path = GetRuntimeStatsPath(workspaceFolderPath);
        if (!File.Exists(path))
            return new SummaryRuntimeStatsStore();

        try
        {
            await using var stream = File.OpenRead(path);
            var store = await JsonSerializer.DeserializeAsync<SummaryRuntimeStatsStore>(stream, JsonOptions, cancellationToken);
            return store ?? new SummaryRuntimeStatsStore();
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"[LLM] Failed to load runtime stats: {ex.GetType().Name}: {ex.Message}");
            return new SummaryRuntimeStatsStore();
        }
    }

    private static async Task SaveRuntimeStatsStoreAsync(
        string workspaceFolderPath,
        SummaryRuntimeStatsStore store,
        CancellationToken cancellationToken)
    {
        var summariesRoot = Path.Combine(workspaceFolderPath, SummariesFolder);
        Directory.CreateDirectory(summariesRoot);

        var path = GetRuntimeStatsPath(workspaceFolderPath);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, store, JsonOptions, cancellationToken);
    }

    private static async Task<string> CallLlmAsync(
        string apiEndpoint,
        string modelName,
        string prompt,
        int maxOutputTokens,
        CancellationToken cancellationToken,
        IProgress<string>? streamingTokenProgress = null)
    {
        var url = BuildChatCompletionsUrl(apiEndpoint);
        AppLogger.Debug($"[LLM] CallLlm → POST {url}  model={modelName}  promptLength={prompt.Length}");

        var messages = new List<ChatMessage>
        {
            new() { Role = "user", Content = prompt }
        };
        var summaryBuilder = new StringBuilder();
        int? maxTokensPerRequest = maxOutputTokens > 0 ? maxOutputTokens : null;

        try
        {
            var requestCount = 0;
            while (true)
            {
                requestCount++;
                var payload = new ChatRequest
                {
                    Model = modelName,
                    Messages = messages.ToArray(),
                    MaxTokens = maxTokensPerRequest
                };

                var (content, finishReason) = streamingTokenProgress != null
                    ? await CallLlmStreamingAsync(url, payload, streamingTokenProgress, cancellationToken)
                    : await CallLlmNonStreamingAsync(url, payload, cancellationToken);

                AppLogger.Debug($"[LLM] CallLlm response: finish_reason={finishReason}  contentLength={content?.Length ?? 0}  attempt={requestCount}");

                if (string.IsNullOrWhiteSpace(content))
                {
                    AppLogger.Warn($"[LLM] CallLlm: response content is empty. finish_reason={finishReason}");
                    throw new InvalidOperationException(
                        $"LLM API returned no assistant text (finish_reason={finishReason}). " +
                        "The server response was successful but did not include usable output in standard content fields.");
                }

                summaryBuilder.Append(content);

                if (!string.Equals(finishReason, "length", StringComparison.OrdinalIgnoreCase))
                    return summaryBuilder.ToString();

                if (requestCount >= MaxTotalSummaryRequests)
                {
                    AppLogger.Warn("[LLM] Summary generation hit the continuation limit while finish_reason=length.");
                    throw new InvalidOperationException(
                        "The summary response repeatedly reached the output limit and could not be completed. " +
                        "Try increasing Summary output token cap or using a model with larger output capacity.");
                }

                var continuationCount = requestCount;
                AppLogger.Warn($"[LLM] Summary response hit output limit (finish_reason=length). Requesting continuation ({continuationCount}/{MaxTotalSummaryRequests - 1}).");
                messages.Add(new ChatMessage { Role = "assistant", Content = content });
                messages.Add(new ChatMessage
                {
                    Role = "user",
                    Content = ContinuationPrompt
                });
            }
        }
        catch (OperationCanceledException oce)
        {
            AppLogger.Warn($"[LLM] CallLlm cancelled. IsCancellationRequested={oce.CancellationToken.IsCancellationRequested}. Inner: {oce.InnerException?.GetType().Name}: {oce.InnerException?.Message}. HttpClient timeout={Http.Timeout}");
            throw;
        }
    }

    private static async Task<(string Content, string FinishReason)> CallLlmNonStreamingAsync(
        string url,
        ChatRequest payload,
        CancellationToken cancellationToken)
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
        var content = ExtractResponseText(firstChoice);
        return (content, finishReason);
    }

    private static async Task<(string Content, string FinishReason)> CallLlmStreamingAsync(
        string url,
        ChatRequest payload,
        IProgress<string> tokenProgress,
        CancellationToken cancellationToken)
    {
        var streamPayload = new ChatStreamRequest
        {
            Model = payload.Model,
            Messages = payload.Messages,
            MaxTokens = payload.MaxTokens,
            Stream = true
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(streamPayload, options: JsonOptions)
        };

        using var response = await Http.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        AppLogger.Debug($"[LLM] CallLlm(stream) ← {(int)response.StatusCode} {response.ReasonPhrase}");

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            var snippet = body.Length > 500 ? body[..500] + "…" : body;
            AppLogger.Error($"[LLM] CallLlm(stream) error body: {snippet}");
            throw new InvalidOperationException(
                $"LLM API returned {(int)response.StatusCode} {response.ReasonPhrase}: {body}");
        }

        var contentType = response.Content.Headers.ContentType?.MediaType;
        if (!string.Equals(contentType, "text/event-stream", StringComparison.OrdinalIgnoreCase))
        {
            var result = await response.Content.ReadFromJsonAsync<ChatResponse>(JsonOptions, cancellationToken)
                ?? throw new InvalidOperationException("LLM API returned an empty response.");

            var firstChoice = result.Choices?.FirstOrDefault();
            var finish = firstChoice?.FinishReason ?? "(null)";
            var content = ExtractResponseText(firstChoice);
            return (content, finish);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);
        var sb = new StringBuilder();
        string? finishReason = null;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync();
            if (line == null) break;
            if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;

            var data = line.Length > 5 && line[5] == ' '
                ? line[6..]
                : line[5..];
            if (string.IsNullOrWhiteSpace(data)) continue;
            if (data == "[DONE]") break;

            ChatStreamChunk? chunk;
            try { chunk = JsonSerializer.Deserialize<ChatStreamChunk>(data, JsonOptions); }
            catch (JsonException jex)
            {
                AppLogger.Debug($"[LLM] CallLlm(stream): skipped malformed SSE frame: {jex.Message}");
                continue;
            }

            var choice = chunk?.Choices?.FirstOrDefault();
            var token = ExtractStreamToken(choice);
            if (!string.IsNullOrEmpty(token))
            {
                sb.Append(token);
                tokenProgress.Report(token);
            }

            if (!string.IsNullOrWhiteSpace(choice?.FinishReason))
                finishReason = choice!.FinishReason;
        }

        return (sb.ToString(), finishReason ?? "stop");
    }

    private static string ExtractResponseText(ChatChoice? choice)
    {
        if (!string.IsNullOrWhiteSpace(choice?.Message?.Content))
            return choice.Message.Content;
        if (!string.IsNullOrWhiteSpace(choice?.Text))
            return choice.Text;
        if (!string.IsNullOrWhiteSpace(choice?.Message?.ReasoningContent))
            return choice.Message.ReasoningContent;
        return string.Empty;
    }

    private static string? ExtractStreamToken(ChatStreamChoice? choice)
    {
        if (!string.IsNullOrEmpty(choice?.Delta?.Content))
            return choice.Delta.Content;
        if (!string.IsNullOrEmpty(choice?.Text))
            return choice.Text;
        if (!string.IsNullOrEmpty(choice?.Delta?.ReasoningContent))
            return choice.Delta.ReasoningContent;
        return null;
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
        var base_ = NormalizeApiEndpoint(apiEndpoint);
        // Avoid appending twice when the caller already includes the path.
        if (base_.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
            return base_;
        return base_ + "/chat/completions";
    }

    private static string BuildModelsUrl(string apiEndpoint)
    {
        var base_ = NormalizeApiEndpoint(apiEndpoint);
        if (base_.EndsWith("/models", StringComparison.OrdinalIgnoreCase))
            return base_;
        // Strip any trailing API path so we always append to the base URL.
        if (base_.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
            base_ = base_[..^"/chat/completions".Length];
        return base_ + "/models";
    }

    private static string NormalizeApiEndpoint(string apiEndpoint)
    {
        var base_ = (apiEndpoint ?? string.Empty).Trim();
        if (!base_.Contains("://", StringComparison.Ordinal))
            base_ = "http://" + base_;
        return base_.TrimEnd('/');
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
    /// Opens <paramref name="mdFilePath"/>, skips the <c>&lt;!-- codomon-source: … --&gt;</c>
    /// metadata comment on the first line (if present), then skips blank lines and Markdown
    /// headings, and returns the first non-heading paragraph as a single space-joined string.
    /// Returns <c>null</c> when the file is empty or contains no paragraph text.
    /// </summary>
    private static string? ExtractFirstParagraph(string mdFilePath)
    {
        using var reader = new StreamReader(mdFilePath);

        // Read the first line.  If it is the metadata comment, discard it; otherwise treat it
        // as part of the document content.
        string? firstLine = reader.ReadLine();

        var lines = new List<string>();
        bool inParagraph = false;

        // Helper to process one line against our paragraph-extraction state machine.
        void ProcessLine(string line)
        {
            if (!inParagraph)
            {
                // Skip blank lines and Markdown headings (# …) before the first paragraph.
                if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith('#'))
                    return;
                inParagraph = true;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                // Blank line terminates the paragraph — stop reading.
                inParagraph = false;
                return;
            }

            lines.Add(line);
        }

        if (firstLine != null)
        {
            // If the first line is the codomon metadata comment, skip it; otherwise process it.
            if (!firstLine.StartsWith("<!-- codomon-source:", StringComparison.Ordinal))
                ProcessLine(firstLine);
        }

        if (lines.Count == 0 || inParagraph)
        {
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                ProcessLine(line);
                if (!inParagraph && lines.Count > 0)
                    break; // Paragraph ended — no more lines needed.
            }
        }

        return lines.Count > 0 ? string.Join(" ", lines) : null;
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

    private sealed class ChatStreamRequest
    {
        [JsonPropertyName("model")]
        public string Model { get; set; } = string.Empty;

        [JsonPropertyName("messages")]
        public ChatMessage[] Messages { get; set; } = Array.Empty<ChatMessage>();

        [JsonPropertyName("max_tokens")]
        public int? MaxTokens { get; set; }

        [JsonPropertyName("stream")]
        public bool Stream { get; set; } = true;
    }

    private sealed class ChatMessage
    {
        [JsonPropertyName("role")]
        public string Role { get; set; } = string.Empty;

        [JsonPropertyName("content")]
        public string Content { get; set; } = string.Empty;

        [JsonPropertyName("reasoning_content")]
        public string? ReasoningContent { get; set; }
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

        [JsonPropertyName("text")]
        public string? Text { get; set; }
    }

    private sealed class ChatStreamChunk
    {
        [JsonPropertyName("choices")]
        public ChatStreamChoice[]? Choices { get; set; }
    }

    private sealed class ChatStreamChoice
    {
        [JsonPropertyName("delta")]
        public ChatStreamDelta? Delta { get; set; }

        [JsonPropertyName("finish_reason")]
        public string? FinishReason { get; set; }

        [JsonPropertyName("text")]
        public string? Text { get; set; }
    }

    private sealed class ChatStreamDelta
    {
        [JsonPropertyName("content")]
        public string? Content { get; set; }

        [JsonPropertyName("reasoning_content")]
        public string? ReasoningContent { get; set; }
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

/// <summary>Learned runtime profile for summary generation estimates.</summary>
public class SummaryRuntimeProfile
{
    public int SampleCount { get; set; }
    public double AverageTokensPerSecond { get; set; } = 1200.0;
    public bool UsesFallback { get; set; }
}

internal sealed class SummaryRuntimeStatsStore
{
    public List<SummaryRuntimeStatsEntry> Entries { get; set; } = new();
}

internal sealed class SummaryRuntimeStatsEntry
{
    public string ApiEndpoint { get; set; } = string.Empty;
    public string ModelName { get; set; } = string.Empty;
    public int SampleCount { get; set; }
    public double TotalEstimatedTokens { get; set; }
    public double TotalElapsedSeconds { get; set; }
    public double LastObservedTokensPerSecond { get; set; }
    public DateTime LastUpdatedUtc { get; set; }
}
