using Codomon.Desktop.Models;
using Codomon.Desktop.Models.ArchitectureHypothesis;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
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
    private const int DefaultContextBudgetTokens = 65_535;
    private const double PreflightPromptBudgetFraction = 0.45;
    private const double ExpectedOutputFractionOfPrompt = 0.60;

    private const string HypothesesFolder = "hypotheses";
    private const string PromptFileName = "hypothesis_prompt.md";
        private const string PromptTemplatesFolder = "prompts/architecture-hypothesis";

        private static readonly (string FileName, string Description)[] PromptTemplatePresets =
        {
                ("DEFAULT.md", "Original default synthesis prompt."),
                ("LARGE_CODEBASE.md", "For large codebases with eco-system of systems, interconnected systems etc."),
                ("ONE_DESKTOP_APP.md", "For just one single desktop app"),
                ("ONE_WEB_APP.md", "For just one web app")
        };

        private const string LargeCodebasePrompt =
                """
                You are an expert enterprise software architect. Below are Markdown summaries of source files from a potentially large and interconnected codebase.

                Prioritize ecosystem-level structure:
                - Detect distinct systems, bounded contexts, integration seams, and data/contract boundaries.
                - Prefer fewer, high-confidence systems over many speculative systems.
                - Capture cross-system evidence explicitly (APIs, messaging, persistence, shared libraries, external integrations).
                - Mark uncertain areas where summaries are insufficient to infer architecture safely.

                Analyze these summaries and produce a JSON architecture hypothesis following the exact schema below.
                Only output the JSON object - no prose, no markdown fences.

                Schema:
                {
                    "systems": [
                        {
                            "name": "string",
                            "kind": "DesktopApp|WebApp|BackendService|WorkerService|ScheduledJob|CliTool|DatabaseProcess|LibraryOnly|Unknown",
                            "confidence": "Likely|Possible|Unknown",
                            "evidence": ["string"],
                            "modules": [
                                {
                                    "name": "string",
                                    "confidence": "Likely|Possible|Unknown",
                                    "highValueNodes": ["string"]
                                }
                            ]
                        }
                    ],
                    "highValueNodes": [
                        {
                            "name": "string",
                            "reason": "string",
                            "signal": "EntryPoint|Orchestrator|CentralStateModel|ServiceBoundary|SerializationBoundary|IntegrationBoundary|RuntimeHeavy|ErrorProne|BridgeBetweenClusters|Other",
                            "confidence": "Likely|Possible|Unknown"
                        }
                    ],
                    "startup": [
                        {
                            "system": "string",
                            "mechanism": "string",
                            "entryPointCandidates": ["string"],
                            "confidence": "Likely|Possible|Unknown"
                        }
                    ],
                    "uncertainAreas": ["string"]
                }

                --- SUMMARIES ---
                {Summaries}
                """;

        private const string OneDesktopAppPrompt =
                """
                You are an expert desktop application architect. Below are Markdown summaries of source files from a single desktop app.

                Prioritize one-system analysis:
                - Assume there is usually one primary DesktopApp unless strong evidence suggests otherwise.
                - Focus on startup flow, UI shell, state models, major services, persistence boundaries, and integrations.
                - Identify high-value nodes that orchestrate behavior or coordinate subsystems.

                Analyze these summaries and produce a JSON architecture hypothesis following the exact schema below.
                Only output the JSON object - no prose, no markdown fences.

                Schema:
                {
                    "systems": [
                        {
                            "name": "string",
                            "kind": "DesktopApp|WebApp|BackendService|WorkerService|ScheduledJob|CliTool|DatabaseProcess|LibraryOnly|Unknown",
                            "confidence": "Likely|Possible|Unknown",
                            "evidence": ["string"],
                            "modules": [
                                {
                                    "name": "string",
                                    "confidence": "Likely|Possible|Unknown",
                                    "highValueNodes": ["string"]
                                }
                            ]
                        }
                    ],
                    "highValueNodes": [
                        {
                            "name": "string",
                            "reason": "string",
                            "signal": "EntryPoint|Orchestrator|CentralStateModel|ServiceBoundary|SerializationBoundary|IntegrationBoundary|RuntimeHeavy|ErrorProne|BridgeBetweenClusters|Other",
                            "confidence": "Likely|Possible|Unknown"
                        }
                    ],
                    "startup": [
                        {
                            "system": "string",
                            "mechanism": "string",
                            "entryPointCandidates": ["string"],
                            "confidence": "Likely|Possible|Unknown"
                        }
                    ],
                    "uncertainAreas": ["string"]
                }

                --- SUMMARIES ---
                {Summaries}
                """;

        private const string OneWebAppPrompt =
                """
                You are an expert web application architect. Below are Markdown summaries of source files from a single web application.

                Prioritize one web-app architecture view:
                - Assume one primary WebApp unless evidence strongly indicates additional systems.
                - Highlight presentation/UI layer, API layer, domain/service layer, and persistence/external integrations.
                - Capture request/response boundaries, background work, and critical startup/bootstrapping nodes.

                Analyze these summaries and produce a JSON architecture hypothesis following the exact schema below.
                Only output the JSON object - no prose, no markdown fences.

                Schema:
                {
                    "systems": [
                        {
                            "name": "string",
                            "kind": "DesktopApp|WebApp|BackendService|WorkerService|ScheduledJob|CliTool|DatabaseProcess|LibraryOnly|Unknown",
                            "confidence": "Likely|Possible|Unknown",
                            "evidence": ["string"],
                            "modules": [
                                {
                                    "name": "string",
                                    "confidence": "Likely|Possible|Unknown",
                                    "highValueNodes": ["string"]
                                }
                            ]
                        }
                    ],
                    "highValueNodes": [
                        {
                            "name": "string",
                            "reason": "string",
                            "signal": "EntryPoint|Orchestrator|CentralStateModel|ServiceBoundary|SerializationBoundary|IntegrationBoundary|RuntimeHeavy|ErrorProne|BridgeBetweenClusters|Other",
                            "confidence": "Likely|Possible|Unknown"
                        }
                    ],
                    "startup": [
                        {
                            "system": "string",
                            "mechanism": "string",
                            "entryPointCandidates": ["string"],
                            "confidence": "Likely|Possible|Unknown"
                        }
                    ],
                    "uncertainAreas": ["string"]
                }

                --- SUMMARIES ---
                {Summaries}
                """;

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
        await EnsurePromptTemplatesAsync(workspaceFolderPath);

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

    /// <summary>
    /// Ensures built-in prompt template files exist in the workspace prompt presets folder.
    /// </summary>
    public static async Task EnsurePromptTemplatesAsync(string workspaceFolderPath)
    {
        var templatesFolder = Path.Combine(workspaceFolderPath, PromptTemplatesFolder);
        Directory.CreateDirectory(templatesFolder);

        var workspacePromptPath = Path.Combine(workspaceFolderPath, PromptFileName);
        var defaultTemplatePath = Path.Combine(templatesFolder, "DEFAULT.md");

        if (!File.Exists(defaultTemplatePath))
        {
            var defaultContent = File.Exists(workspacePromptPath)
                ? await File.ReadAllTextAsync(workspacePromptPath)
                : string.Empty;
            await File.WriteAllTextAsync(defaultTemplatePath, defaultContent);
        }

        var largeCodebasePath = Path.Combine(templatesFolder, "LARGE_CODEBASE.md");
        if (!File.Exists(largeCodebasePath))
            await File.WriteAllTextAsync(largeCodebasePath, LargeCodebasePrompt);

        var oneDesktopAppPath = Path.Combine(templatesFolder, "ONE_DESKTOP_APP.md");
        if (!File.Exists(oneDesktopAppPath))
            await File.WriteAllTextAsync(oneDesktopAppPath, OneDesktopAppPrompt);

        var oneWebAppPath = Path.Combine(templatesFolder, "ONE_WEB_APP.md");
        if (!File.Exists(oneWebAppPath))
            await File.WriteAllTextAsync(oneWebAppPath, OneWebAppPrompt);
    }

    /// <summary>Returns built-in prompt preset names and descriptions.</summary>
    public static IReadOnlyList<(string FileName, string Description)> ListPromptTemplatePresets()
        => PromptTemplatePresets;

    /// <summary>Loads one prompt template preset file from the workspace prompt presets folder.</summary>
    public static async Task<string> LoadPromptTemplatePresetAsync(string workspaceFolderPath, string fileName)
    {
        await EnsurePromptTemplatesAsync(workspaceFolderPath);

        var knownPreset = PromptTemplatePresets
            .FirstOrDefault(p => string.Equals(p.FileName, fileName, StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrWhiteSpace(knownPreset.FileName))
            throw new InvalidOperationException($"Unknown prompt preset: {fileName}");

        var path = Path.Combine(workspaceFolderPath, PromptTemplatesFolder, knownPreset.FileName);
        if (!File.Exists(path))
            throw new FileNotFoundException($"Prompt preset file not found: {knownPreset.FileName}", path);

        return await File.ReadAllTextAsync(path);
    }

    // ── Synthesis ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Runs a full architecture-hypothesis synthesis pass using the stored Markdown summaries.
    /// When the estimated token count of the combined summaries exceeds
    /// <paramref name="tokenThreshold"/>, the summaries are automatically split into
    /// smaller batches and the results are merged into a single hypothesis.
    /// Returns the populated <see cref="ArchitectureHypothesisModel"/> and saves it to disk.
    /// </summary>
    /// <param name="tokenThreshold">
    /// Maximum estimated token count per LLM call.
    /// Pass 0 or a negative value to disable batching (no split, single call).
    /// </param>
    /// <param name="streamingTokenProgress">
    /// Optional callback that receives individual tokens as they stream from the LLM.
    /// Used to populate the live output panel in the UI.
    /// </param>
    /// <param name="telemetryProgress">
    /// Optional callback that receives per-batch telemetry data after each LLM call completes.
    /// </param>
    public static async Task<ArchitectureHypothesisModel> RunSynthesisAsync(
        string apiEndpoint,
        string modelName,
        string workspaceFolderPath,
        int tokenThreshold = 0,
        IProgress<string>? progress = null,
        IProgress<string>? streamingTokenProgress = null,
        IProgress<BatchTelemetry>? telemetryProgress = null,
        CancellationToken cancellationToken = default)
    {
        // Load summaries.
        var summaries = LlmSummaryService.ListSummaries(workspaceFolderPath);
        if (summaries.Count == 0)
            throw new InvalidOperationException(
                "No Markdown summaries found. Generate summaries in the LLM Summaries dialog first.");

        progress?.Report($"Loaded {summaries.Count} summary file(s).");
        AppLogger.Debug($"[Hypothesis] Synthesis start: {summaries.Count} summaries  model={modelName}");

        // Load the prompt template up front — it is needed for every batch.
        var promptTemplate = await LoadPromptTemplateAsync(workspaceFolderPath);
        if (string.IsNullOrWhiteSpace(promptTemplate))
            throw new InvalidOperationException(
                "Hypothesis prompt template is empty. Open the Architecture dialog Setup tab and save a prompt first.");

        // Preflight: verify endpoint/model reachability before running many batched calls.
        progress?.Report("Checking LLM connectivity before synthesis...");
        var (llmReachable, connectionMessage) = await LlmSummaryService.TestConnectionAsync(
            apiEndpoint, modelName, cancellationToken);
        if (!llmReachable)
            throw new InvalidOperationException($"LLM preflight failed: {connectionMessage}");
        progress?.Report("LLM connectivity verified.");

        // Determine batches based on the token threshold.
        List<List<SummaryEntry>> initialBatches;
        if (tokenThreshold > 0)
        {
            var estimatedTokens = await LlmHelper.EstimateTokenCountAsync(summaries, cancellationToken);
            AppLogger.Debug($"[Hypothesis] Estimated token count: {estimatedTokens}  threshold={tokenThreshold}");

            if (estimatedTokens > tokenThreshold)
            {
                initialBatches = await LlmHelper.SplitIntoBatchesAsync(summaries, tokenThreshold, cancellationToken);
                AppLogger.Info($"[Hypothesis] Token threshold exceeded — splitting into {initialBatches.Count} batch(es).");
                progress?.Report($"Token budget exceeded ({estimatedTokens} estimated tokens). Splitting into {initialBatches.Count} batch(es).");
            }
            else
            {
                initialBatches = new List<List<SummaryEntry>> { summaries };
            }
        }
        else
        {
            initialBatches = new List<List<SummaryEntry>> { summaries };
        }

        // Use a queue to support automatic split-and-retry when the LLM hits its token limit.
        var processQueue = new Queue<(List<SummaryEntry> Batch, string Label, bool IsRetry)>();
        for (int i = 0; i < initialBatches.Count; i++)
        {
            var label = initialBatches.Count > 1
                ? $"batch {i + 1}/{initialBatches.Count}"
                : "all summaries";
            processQueue.Enqueue((initialBatches[i], label, false));
        }

        int totalQueued = processQueue.Count;
        int batchNumber = 0;

        ArchitectureHypothesisModel? merged = null;

        while (processQueue.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var (batch, batchLabel, isRetry) = processQueue.Dequeue();
            batchNumber++;

            // Emit "starting" telemetry so the UI can show "Running".
            telemetryProgress?.Report(new BatchTelemetry
            {
                BatchNumber = batchNumber,
                TotalBatches = totalQueued,
                BatchLabel = batchLabel,
                SummaryCount = batch.Count,
                Status = isRetry ? "Retrying" : "Running",
                IsRetry = isRetry,
            });

            var retryPrefix = isRetry ? "↺ [retry] " : string.Empty;
            progress?.Report($"{retryPrefix}Calling LLM for {batchLabel} ({batch.Count} summaries) — this may take a while…");

            // Build the combined summaries block for this batch.
            var summariesBlock = await BuildSummariesBlockAsync(batch, cancellationToken);
            var prompt = promptTemplate.Replace("{Summaries}", summariesBlock);
            var estimatedPromptTokens = LlmHelper.EstimateTokenCount(prompt);
            var contextBudgetTokens = tokenThreshold > 0 ? tokenThreshold : DefaultContextBudgetTokens;
            var remainingContextTokens = Math.Max(0, contextBudgetTokens - estimatedPromptTokens);
            var expectedOutputTokens = Math.Max(256, (int)Math.Ceiling(estimatedPromptTokens * ExpectedOutputFractionOfPrompt));
            var preflightWarning = string.Empty;
            var preflightThreshold = (int)Math.Floor(contextBudgetTokens * PreflightPromptBudgetFraction);
            if (estimatedPromptTokens > preflightThreshold)
            {
                preflightWarning =
                    $"Batch {batchLabel} is approaching model context limits. " +
                    $"Prompt ~{estimatedPromptTokens:N0}/{contextBudgetTokens:N0} tok, remaining ~{remainingContextTokens:N0}, expected output ~{expectedOutputTokens:N0}.";
                progress?.Report($"⚠ {preflightWarning}");
            }
            AppLogger.Debug($"[Hypothesis] {batchLabel}: Prompt length={prompt.Length} chars (~{estimatedPromptTokens} tokens)");

            if (estimatedPromptTokens > preflightThreshold && batch.Count > 1)
            {
                progress?.Report($"⚠ {batchLabel}: prompt exceeds {PreflightPromptBudgetFraction:P0} of context. Splitting before send.");
                telemetryProgress?.Report(new BatchTelemetry
                {
                    BatchNumber = batchNumber,
                    TotalBatches = totalQueued,
                    BatchLabel = batchLabel,
                    SummaryCount = batch.Count,
                    PromptTokens = estimatedPromptTokens,
                    Status = "Split",
                    IsRetry = isRetry,
                    WasSplit = true,
                    ContextBudgetTokens = contextBudgetTokens,
                    RemainingContextTokens = remainingContextTokens,
                    ExpectedOutputTokens = expectedOutputTokens,
                    PreflightWarning = preflightWarning,
                });

                var half = batch.Count / 2;
                var subA = batch.Take(half).ToList();
                var subB = batch.Skip(half).ToList();
                processQueue.Enqueue((subA, $"{batchLabel}A", true));
                processQueue.Enqueue((subB, $"{batchLabel}B", true));
                totalQueued += 2;
                continue;
            }

            // Call the LLM with streaming support.
            var batchStart = DateTime.UtcNow;
            LlmCallResult result;
            try
            {
                result = await CallLlmStreamingAsync(
                    apiEndpoint, modelName, prompt, streamingTokenProgress, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                var duration = DateTime.UtcNow - batchStart;
                AppLogger.Error($"[Hypothesis] {batchLabel}: LLM call failed: {ex.Message}");
                telemetryProgress?.Report(new BatchTelemetry
                {
                    BatchNumber = batchNumber,
                    TotalBatches = totalQueued,
                    BatchLabel = batchLabel,
                    SummaryCount = batch.Count,
                    PromptTokens = estimatedPromptTokens,
                    Duration = duration,
                    Status = "Failed",
                    FailureReason = ex.Message,
                    IsRetry = isRetry,
                    ContextBudgetTokens = contextBudgetTokens,
                    RemainingContextTokens = remainingContextTokens,
                    ExpectedOutputTokens = expectedOutputTokens,
                    PreflightWarning = preflightWarning,
                });
                throw;
            }

            var batchDuration = DateTime.UtcNow - batchStart;
            AppLogger.Debug($"[Hypothesis] {batchLabel}: LLM responded: {result.Content.Length} chars, finish={result.FinishReason}, dur={batchDuration.TotalSeconds:F1}s");

            // Auto-retry: when the LLM hit its token limit and we have multiple summaries, split.
            if (string.Equals(result.FinishReason, "length", StringComparison.OrdinalIgnoreCase)
                && batch.Count > 1)
            {
                AppLogger.Warn($"[Hypothesis] {batchLabel}: token limit hit (finish_reason=length). Splitting into sub-batches.");
                progress?.Report($"⚠ {batchLabel}: token limit hit. Splitting into sub-batches and retrying…");

                telemetryProgress?.Report(new BatchTelemetry
                {
                    BatchNumber = batchNumber,
                    TotalBatches = totalQueued,
                    BatchLabel = batchLabel,
                    SummaryCount = batch.Count,
                    PromptTokens = estimatedPromptTokens,
                    OutputTokens = result.OutputTokens,
                    Duration = batchDuration,
                    FinishReason = result.FinishReason,
                    Status = "Split",
                    IsRetry = isRetry,
                    WasSplit = true,
                    ContextBudgetTokens = contextBudgetTokens,
                    RemainingContextTokens = remainingContextTokens,
                    ExpectedOutputTokens = expectedOutputTokens,
                    PreflightWarning = preflightWarning,
                });

                var half = batch.Count / 2;
                var subA = batch.Take(half).ToList();
                var subB = batch.Skip(half).ToList();
                processQueue.Enqueue((subA, $"{batchLabel}A", true));
                processQueue.Enqueue((subB, $"{batchLabel}B", true));
                totalQueued += 2;
                continue;
            }

            AppLogger.Debug($"[Hypothesis] {batchLabel}: LLM raw output:\n{result.Content}");
            progress?.Report($"Parsing LLM response for {batchLabel}…");

            // Report completed batch telemetry.
            var batchStatus = string.Equals(result.FinishReason, "length", StringComparison.OrdinalIgnoreCase)
                ? "Warning"
                : "Completed";
            telemetryProgress?.Report(new BatchTelemetry
            {
                BatchNumber = batchNumber,
                TotalBatches = totalQueued,
                BatchLabel = batchLabel,
                SummaryCount = batch.Count,
                PromptTokens = estimatedPromptTokens,
                OutputTokens = result.OutputTokens,
                Duration = batchDuration,
                FinishReason = result.FinishReason,
                Status = batchStatus,
                IsRetry = isRetry,
                ContextBudgetTokens = contextBudgetTokens,
                RemainingContextTokens = remainingContextTokens,
                ExpectedOutputTokens = expectedOutputTokens,
                PreflightWarning = preflightWarning,
                RawResponse = result.Content,
            });

            progress?.Report($"✔ {batchLabel}: {batchDuration.TotalSeconds:F0}s  ~{estimatedPromptTokens}→{result.OutputTokens} tok  finish={result.FinishReason}");

            // Extract and parse the JSON.
            var batchHypothesis = ParseHypothesis(result.Content);

            if (merged == null)
            {
                merged = batchHypothesis;
            }
            else
            {
                MergeHypothesis(merged, batchHypothesis);
                AppLogger.Debug($"[Hypothesis] Merged {batchLabel} into combined hypothesis.");
            }
        }

        var hypothesis = merged!;
        hypothesis.ModelName = modelName;
        hypothesis.SummaryCount = summaries.Count;
        hypothesis.CreatedAt = DateTime.UtcNow;

        // Save to disk.
        var savedPath = await SaveHypothesisAsync(workspaceFolderPath, hypothesis);
        AppLogger.Info($"[Hypothesis] Saved: {savedPath}");
        progress?.Report($"Hypothesis saved: {Path.GetFileName(savedPath)}");

        return hypothesis;
    }

    /// <summary>
    /// Merges the content of <paramref name="source"/> into <paramref name="target"/> by
    /// appending Systems, HighValueNodes, Startup entries, and UncertainAreas that are not
    /// already present (matched by name, case-insensitive).
    /// </summary>
    private static void MergeHypothesis(ArchitectureHypothesisModel target, ArchitectureHypothesisModel source)
    {
        foreach (var s in source.Systems)
        {
            var existing = target.Systems.FirstOrDefault(x =>
                string.Equals(x.Name ?? string.Empty, s.Name ?? string.Empty, StringComparison.OrdinalIgnoreCase));
            if (existing == null)
            {
                target.Systems.Add(s);
                continue;
            }

            // Merge modules by module name.
            var existingModuleNames = new HashSet<string>(
                existing.Modules.Select(m => m.Name ?? string.Empty),
                StringComparer.OrdinalIgnoreCase);
            foreach (var module in s.Modules)
            {
                if (!existingModuleNames.Contains(module.Name ?? string.Empty))
                {
                    existing.Modules.Add(module);
                    existingModuleNames.Add(module.Name ?? string.Empty);
                }
            }

            // Merge evidence strings.
            existing.Evidence ??= new List<string>();
            var existingEvidence = new HashSet<string>(existing.Evidence, StringComparer.OrdinalIgnoreCase);
            foreach (var ev in s.Evidence ?? new List<string>())
            {
                if (existingEvidence.Add(ev))
                    existing.Evidence.Add(ev);
            }

            // Keep strongest confidence.
            if ((int)s.Confidence > (int)existing.Confidence)
                existing.Confidence = s.Confidence;
        }

        var existingHvnNames = new HashSet<string>(
            target.HighValueNodes.Select(n => n.Name ?? string.Empty),
            StringComparer.OrdinalIgnoreCase);

        foreach (var n in source.HighValueNodes)
        {
            if (!existingHvnNames.Contains(n.Name ?? string.Empty))
            {
                target.HighValueNodes.Add(n);
                existingHvnNames.Add(n.Name ?? string.Empty);
            }
        }

        var existingStartupSystems = new HashSet<string>(
            target.Startup.Select(s => s.System ?? string.Empty),
            StringComparer.OrdinalIgnoreCase);

        foreach (var s in source.Startup)
        {
            if (!existingStartupSystems.Contains(s.System ?? string.Empty))
            {
                target.Startup.Add(s);
                existingStartupSystems.Add(s.System ?? string.Empty);
            }
        }

        var existingUncertain = new HashSet<string>(target.UncertainAreas, StringComparer.OrdinalIgnoreCase);
        foreach (var a in source.UncertainAreas)
        {
            if (!existingUncertain.Contains(a))
            {
                target.UncertainAreas.Add(a);
                existingUncertain.Add(a);
            }
        }
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
        var sb = new StringBuilder();
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

    /// <summary>
    /// Calls the LLM using the OpenAI-compatible streaming endpoint (server-sent events).
    /// Appends individual tokens to <paramref name="tokenProgress"/> as they arrive so the
    /// UI can display live output. Returns the complete generated content and the
    /// <c>finish_reason</c> reported by the server.
    /// </summary>
    private static async Task<LlmCallResult> CallLlmStreamingAsync(
        string apiEndpoint,
        string modelName,
        string prompt,
        IProgress<string>? tokenProgress,
        CancellationToken cancellationToken)
    {
        var url = BuildChatCompletionsUrl(apiEndpoint);
        AppLogger.Debug($"[Hypothesis] CallLlm(stream) → POST {url}  model={modelName}  promptLength={prompt.Length}");

        var payload = new ChatStreamRequest
        {
            Model = modelName,
            Messages = new[] { new ChatMessage { Role = "user", Content = prompt } },
            Stream = true,
        };

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Content = JsonContent.Create(payload, options: LlmJsonOptions);

            using var response = await Http.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            AppLogger.Debug($"[Hypothesis] CallLlm(stream) ← {(int)response.StatusCode} {response.ReasonPhrase}");

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                var snippet = body.Length > 500 ? body[..500] + "…" : body;
                AppLogger.Error($"[Hypothesis] CallLlm(stream) error body: {snippet}");
                throw new InvalidOperationException(
                    $"LLM API returned {(int)response.StatusCode} {response.ReasonPhrase}: {body}");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var reader = new StreamReader(stream);

            var sb = new StringBuilder();
            string? finishReason = null;
            int promptTokens = 0;
            int outputTokens = 0;

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var line = await reader.ReadLineAsync();
                if (line == null) break;
                if (!line.StartsWith("data: ", StringComparison.Ordinal)) continue;

                var data = line[6..]; // skip "data: "
                if (data == "[DONE]") break;

                ChatStreamChunk? chunk;
                try { chunk = JsonSerializer.Deserialize<ChatStreamChunk>(data, LlmJsonOptions); }
                catch (JsonException jex)
                {
                    AppLogger.Debug($"[Hypothesis] CallLlm(stream): skipped malformed SSE frame: {jex.Message}");
                    continue;
                }

                var choice = chunk?.Choices?.FirstOrDefault();
                var token = choice?.Delta?.Content;
                if (token != null && token.Length > 0)
                {
                    sb.Append(token);
                    tokenProgress?.Report(token);
                }

                if (choice?.FinishReason is { Length: > 0 } fr)
                    finishReason = fr;

                if (chunk?.Usage is { } usage)
                {
                    if (usage.PromptTokens > 0) promptTokens = usage.PromptTokens;
                    if (usage.CompletionTokens > 0) outputTokens = usage.CompletionTokens;
                }
            }

            var content = sb.ToString();
            finishReason ??= "stop";

            // Fall back to estimation when the server did not report usage.
            if (outputTokens == 0)
            {
                outputTokens = LlmHelper.EstimateTokenCount(content);
                AppLogger.Debug($"[Hypothesis] CallLlm(stream): server did not report completion_tokens; using heuristic estimate={outputTokens}");
            }
            if (promptTokens == 0)
            {
                promptTokens = LlmHelper.EstimateTokenCount(prompt);
                AppLogger.Debug($"[Hypothesis] CallLlm(stream): server did not report prompt_tokens; using heuristic estimate={promptTokens}");
            }

            AppLogger.Debug($"[Hypothesis] CallLlm(stream) done: {content.Length} chars, finish={finishReason}, promptTok≈{promptTokens}, outTok≈{outputTokens}");

            if (string.IsNullOrWhiteSpace(content))
            {
                AppLogger.Warn($"[Hypothesis] CallLlm(stream): response content is empty. finish_reason={finishReason}");
                throw new InvalidOperationException("LLM API returned a response with empty content.");
            }

            return new LlmCallResult(content, finishReason, promptTokens, outputTokens);
        }
        catch (OperationCanceledException oce)
        {
            AppLogger.Warn($"[Hypothesis] CallLlm(stream) cancelled. Inner: {oce.InnerException?.GetType().Name}: {oce.InnerException?.Message}");
            throw;
        }
    }

    /// <summary>Result returned by the streaming LLM call.</summary>
    private readonly record struct LlmCallResult(
        string Content,
        string FinishReason,
        int PromptTokens = 0,
        int OutputTokens = 0);

    /// <summary>
    /// Extracts and parses the JSON block from the LLM response.
    /// Strips surrounding Markdown code-fence markers if present.
    /// Falls back to sanitisation of common LLM string-quoting mistakes, then to
    /// bracket-repair for truncated output, and finally combines both strategies.
    /// </summary>
    internal static ArchitectureHypothesisModel ParseHypothesis(string rawResponse)
    {
        var json = ExtractJson(rawResponse);
        AppLogger.Debug($"[Hypothesis] Parsing JSON ({json.Length} chars):\n{json}");

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

            // Step 1 — sanitise common LLM string-quoting mistakes, e.g.
            //   "Error" methods"  →  "Error methods"
            var sanitized = SanitizeLlmJson(json);
            if (sanitized != json)
            {
                AppLogger.Warn($"[Hypothesis] Attempting sanitized JSON parse ({sanitized.Length} chars after sanitization).");
                try
                {
                    var model = JsonSerializer.Deserialize<ArchitectureHypothesisModel>(sanitized, ParseOptions);
                    if (model != null)
                    {
                        AppLogger.Warn("[Hypothesis] Sanitized JSON parse succeeded.");
                        return model;
                    }
                }
                catch (JsonException sanitizeEx)
                {
                    AppLogger.Error($"[Hypothesis] Sanitized JSON parse failed: {sanitizeEx.Message}");
                }
            }

            // Step 2 — attempt to repair a truncated JSON response by closing unclosed
            // brackets/strings.  Apply to the sanitized version when it differs from the
            // original so both fixes are combined.
            var jsonToRepair = sanitized != json ? sanitized : json;
            if (TryRepairTruncatedJson(jsonToRepair, out var repaired))
            {
                AppLogger.Warn($"[Hypothesis] Attempting repair of truncated JSON ({repaired.Length} chars after repair).");
                try
                {
                    var model = JsonSerializer.Deserialize<ArchitectureHypothesisModel>(repaired, ParseOptions);
                    if (model != null)
                    {
                        AppLogger.Warn("[Hypothesis] JSON repair succeeded — hypothesis may be incomplete due to LLM token limit.");
                        return model;
                    }
                }
                catch (JsonException repairEx)
                {
                    AppLogger.Error($"[Hypothesis] JSON repair also failed: {repairEx.Message}");
                }
            }

            throw new InvalidOperationException(
                $"Failed to parse LLM response as valid hypothesis JSON: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Attempts to repair a truncated JSON string by closing any unclosed string literals,
    /// arrays, and objects. Returns <c>true</c> when the string was modified; <c>false</c>
    /// when the brackets were already balanced and no repair was needed.
    /// </summary>
    private static bool TryRepairTruncatedJson(string json, out string repaired)
    {
        var stack = new Stack<char>();
        bool inString = false;
        bool escaped = false;

        foreach (char c in json)
        {
            if (escaped) { escaped = false; continue; }

            if (inString)
            {
                if (c == '\\') escaped = true;
                else if (c == '"') inString = false;
                continue;
            }

            switch (c)
            {
                case '"': inString = true; break;
                case '{':
                case '[': stack.Push(c); break;
                case '}':
                    if (stack.Count > 0 && stack.Peek() == '{') stack.Pop();
                    else if (stack.Count > 0) { repaired = json; return false; } // mismatched — cannot repair
                    break;
                case ']':
                    if (stack.Count > 0 && stack.Peek() == '[') stack.Pop();
                    else if (stack.Count > 0) { repaired = json; return false; } // mismatched — cannot repair
                    break;
            }
        }

        if (stack.Count == 0 && !inString)
        {
            repaired = json;
            return false;
        }

        var sb = new StringBuilder(json.TrimEnd());

        // Close any unclosed string literal.
        if (inString)
            sb.Append('"');

        // Remove any trailing comma or whitespace that would make the closing bracket invalid.
        int lastValid = sb.Length - 1;
        while (lastValid >= 0 && (sb[lastValid] == ',' || char.IsWhiteSpace(sb[lastValid])))
            lastValid--;
        if (lastValid < sb.Length - 1)
            sb.Length = lastValid + 1;

        // Close all remaining open arrays and objects.
        while (stack.Count > 0)
            sb.Append(stack.Pop() == '{' ? '}' : ']');

        repaired = sb.ToString();
        return true;
    }

    // Compiled once for efficiency — used in SanitizeLlmJson on every failed parse.
    // Pattern anatomy:
    //   "((?:[^"\\]|\\.)*)"   — matches a complete JSON string value (group 1):
    //                           [^"\\]  any char that is not " or \
    //                           \\.     an escape sequence such as \" or \\
    //   \s+                   — one or more whitespace characters between the stray closer and the bare text
    //   ([^"\[\]{},:\r\n]+)   — the stray bare text (group 2): stops at any JSON structural
    //                           character or newline so we never cross token boundaries
    //   "                     — the unintended second closing quote
    private static readonly Regex SanitizePattern = new(
        @"""((?:[^""\\]|\\.)*)""\s+([^""\[\]{},:\r\n]+)""",
        RegexOptions.Compiled);

    /// <summary>
    /// Applies lightweight heuristic fixes for common LLM JSON string-quoting mistakes.
    /// Specifically, it collapses the pattern where a string's closing quote appears too
    /// early and bare text follows before the next <c>"</c>:
    /// <code>"Error" methods"</code> → <code>"Error methods"</code>
    /// The fix is applied repeatedly until no more occurrences remain.
    /// </summary>
    private static string SanitizeLlmJson(string json)
    {
        string current = json;
        string previous;
        do
        {
            previous = current;
            current = SanitizePattern.Replace(previous,
                m => $"\"{m.Groups[1].Value} {m.Groups[2].Value.TrimEnd()}\"");
        }
        while (current != previous);

        return current;
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
        var base_ = NormalizeApiEndpoint(apiEndpoint);
        if (base_.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
            return base_;
        return base_ + "/chat/completions";
    }

    private static string NormalizeApiEndpoint(string apiEndpoint)
    {
        var base_ = (apiEndpoint ?? string.Empty).Trim();
        if (!base_.Contains("://", StringComparison.Ordinal))
            base_ = "http://" + base_;
        return base_.TrimEnd('/');
    }

    // ── OpenAI-compatible JSON DTOs ───────────────────────────────────────────

    // Non-streaming request (kept for potential fallback use).
    private sealed class ChatRequest
    {
        [JsonPropertyName("model")]
        public string Model { get; set; } = string.Empty;

        [JsonPropertyName("messages")]
        public ChatMessage[] Messages { get; set; } = Array.Empty<ChatMessage>();
    }

    // Streaming request — identical but adds "stream": true.
    private sealed class ChatStreamRequest
    {
        [JsonPropertyName("model")]
        public string Model { get; set; } = string.Empty;

        [JsonPropertyName("messages")]
        public ChatMessage[] Messages { get; set; } = Array.Empty<ChatMessage>();

        [JsonPropertyName("stream")]
        public bool Stream { get; set; } = true;
    }

    private sealed class ChatMessage
    {
        [JsonPropertyName("role")]
        public string Role { get; set; } = string.Empty;

        [JsonPropertyName("content")]
        public string Content { get; set; } = string.Empty;
    }

    // Non-streaming response.
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

    // Streaming (SSE) response chunk.
    private sealed class ChatStreamChunk
    {
        [JsonPropertyName("choices")]
        public ChatStreamChoice[]? Choices { get; set; }

        [JsonPropertyName("usage")]
        public ChatUsage? Usage { get; set; }
    }

    private sealed class ChatStreamChoice
    {
        [JsonPropertyName("delta")]
        public ChatStreamDelta? Delta { get; set; }

        [JsonPropertyName("finish_reason")]
        public string? FinishReason { get; set; }
    }

    private sealed class ChatStreamDelta
    {
        [JsonPropertyName("content")]
        public string? Content { get; set; }
    }

    private sealed class ChatUsage
    {
        [JsonPropertyName("prompt_tokens")]
        public int PromptTokens { get; set; }

        [JsonPropertyName("completion_tokens")]
        public int CompletionTokens { get; set; }
    }
} // end ArchitectureHypothesisService

/// <summary>Represents a saved hypothesis snapshot entry.</summary>
public class HypothesisEntry
{
    /// <summary>Full path to the hypothesis JSON file.</summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>UTC timestamp embedded in the file name.</summary>
    public DateTime CreatedAt { get; set; }

    public string DisplayName => $"Hypothesis  {CreatedAt:yyyy-MM-dd HH:mm}";
}

/// <summary>
/// Telemetry data reported after (or during) each LLM batch call.
/// Consumed by the UI to populate the telemetry card and structured batch log.
/// </summary>
public sealed class BatchTelemetry
{
    /// <summary>Sequential number of this batch in the current run (1-based).</summary>
    public int BatchNumber { get; set; }

    /// <summary>Total number of batches queued (may grow if auto-split occurs).</summary>
    public int TotalBatches { get; set; }

    /// <summary>Human-readable label for this batch, e.g. "batch 2/5" or "all summaries".</summary>
    public string BatchLabel { get; set; } = string.Empty;

    /// <summary>Number of summaries included in this batch.</summary>
    public int SummaryCount { get; set; }

    /// <summary>Estimated number of tokens in the prompt sent to the LLM.</summary>
    public int PromptTokens { get; set; }

    /// <summary>Estimated or reported number of tokens generated by the LLM.</summary>
    public int OutputTokens { get; set; }

    /// <summary>Estimated context budget used for this batch preflight.</summary>
    public int ContextBudgetTokens { get; set; }

    /// <summary>Estimated remaining context tokens before sending this batch.</summary>
    public int RemainingContextTokens { get; set; }

    /// <summary>Estimated completion tokens expected for this batch call.</summary>
    public int ExpectedOutputTokens { get; set; }

    /// <summary>Wall-clock duration of the LLM call.</summary>
    public TimeSpan Duration { get; set; }

    /// <summary>The <c>finish_reason</c> reported by the LLM, e.g. "stop" or "length".</summary>
    public string FinishReason { get; set; } = string.Empty;

    /// <summary>
    /// Status string for display. One of: Running, Retrying, Completed, Warning, Failed, Split.
    /// </summary>
    public string Status { get; set; } = "Running";

    /// <summary>Human-readable failure reason, populated when <see cref="Status"/> is "Failed".</summary>
    public string? FailureReason { get; set; }

    /// <summary>Human-readable preflight warning, populated for near-budget batches.</summary>
    public string? PreflightWarning { get; set; }

    /// <summary>Raw LLM response content for this batch, when available.</summary>
    public string? RawResponse { get; set; }

    /// <summary>True when this batch was automatically retried due to a previous failure.</summary>
    public bool IsRetry { get; set; }

    /// <summary>True when this batch was split into sub-batches due to a token-limit hit.</summary>
    public bool WasSplit { get; set; }
}
