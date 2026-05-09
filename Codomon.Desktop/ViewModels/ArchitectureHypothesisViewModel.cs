using Codomon.Desktop.Models;
using Codomon.Desktop.Models.ArchitectureHypothesis;
using Codomon.Desktop.Models.SystemMap;
using Codomon.Desktop.Persistence;
using Codomon.Desktop.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;

namespace Codomon.Desktop.ViewModels;

/// <summary>Tracks the overall state of a synthesis run.</summary>
public enum SynthesisState
{
    Idle,
    Preparing,
    Running,
    Retrying,
    Completed,
    CompletedWithWarnings,
    Failed,
    Cancelled,
}

/// <summary>A single row in the structured batch log shown in the Run tab.</summary>
public sealed class BatchLogEntry : INotifyPropertyChanged
{
    private string _status = "Running";

    public string BatchLabel { get; set; } = string.Empty;
    public int SummaryCount { get; set; }
    public int PromptTokens { get; set; }
    public int OutputTokens { get; set; }
    public string Duration { get; set; } = string.Empty;
    public string FinishReason { get; set; } = string.Empty;
    public bool IsRetry { get; set; }

    public string Status
    {
        get => _status;
        set { _status = value; OnPropertyChanged(); OnPropertyChanged(nameof(StatusIcon)); }
    }

    public string StatusIcon => Status switch
    {
        "Completed" => "✔",
        "Warning"   => "⚠",
        "Failed"    => "✖",
        "Split"     => "⇢",
        "Retrying"  => "↺",
        _           => "…",
    };

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// ViewModel for the Architecture Hypothesis dialog.
/// Manages running an LLM synthesis pass, reviewing the resulting hypothesis,
/// and accepting individual suggestions into the System Map.
/// </summary>
public class ArchitectureHypothesisViewModel : INotifyPropertyChanged
{
    private const string DefaultWorkspaceEndpoint = "http://localhost:8080/v1";

    private readonly WorkspaceModel _workspace;
    private readonly string _workspaceFolderPath;
    private readonly string _apiEndpoint;
    private readonly string _modelName;
    private readonly int _hypothesisTokenThreshold;

    private string _promptTemplate = string.Empty;
    private bool _isRunning;
    private string _statusMessage = string.Empty;
    private ArchitectureHypothesisModel? _currentHypothesis;
    private CancellationTokenSource? _cts;
    private int _acceptedCount;
    private int _appliedSuggestionCount;
    private bool _hasCanvasChanges;

    // ── Telemetry / synthesis state ──────────────────────────────────────────
    private SynthesisState _synthesisState = SynthesisState.Idle;
    private int _currentBatch;
    private int _totalBatches;
    private int _processedSummaries;
    private int _totalSummaries;
    private int _totalPromptTokens;
    private int _totalGeneratedTokens;
    private DateTime _synthesisStartTime;
    private TimeSpan _averageBatchDuration;
    private string _liveOutput = string.Empty;
    private string _elapsedFormatted = string.Empty;
    private string _etaFormatted = string.Empty;
    private double _generationSpeed;

    public ArchitectureHypothesisViewModel(WorkspaceModel workspace, string workspaceFolderPath)
    {
        _workspace = workspace;
        _workspaceFolderPath = workspaceFolderPath;

        // Fall back to user-level defaults when workspace LLM settings are not configured yet.
        var userConfig = UserConfigService.Load();
        var hasExplicitWorkspaceEndpoint = !string.IsNullOrWhiteSpace(workspace.LlmSettings.ApiEndpoint)
            && (!string.Equals(workspace.LlmSettings.ApiEndpoint, DefaultWorkspaceEndpoint, StringComparison.OrdinalIgnoreCase)
                || !string.IsNullOrWhiteSpace(workspace.LlmSettings.ModelName));

        _apiEndpoint = hasExplicitWorkspaceEndpoint
            ? workspace.LlmSettings.ApiEndpoint
            : userConfig.DefaultLlmSettings.ApiEndpoint;
        _modelName = !string.IsNullOrWhiteSpace(workspace.LlmSettings.ModelName)
            ? workspace.LlmSettings.ModelName
            : userConfig.DefaultLlmSettings.ModelName;

        // If the workspace is still using the built-in threshold default, prefer the user-level default.
        var defaultThreshold = new LlmSettingsModel().HypothesisTokenThreshold;
        _hypothesisTokenThreshold = workspace.LlmSettings.HypothesisTokenThreshold != defaultThreshold
            ? workspace.LlmSettings.HypothesisTokenThreshold
            : userConfig.DefaultLlmSettings.HypothesisTokenThreshold;
    }

    // ── State ─────────────────────────────────────────────────────────────────

    public string PromptTemplate
    {
        get => _promptTemplate;
        set { _promptTemplate = value; OnPropertyChanged(); }
    }

    public bool IsRunning
    {
        get => _isRunning;
        private set { _isRunning = value; OnPropertyChanged(); }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set { _statusMessage = value; OnPropertyChanged(); }
    }

    // ── Synthesis state & telemetry ───────────────────────────────────────────

    /// <summary>High-level state of the current (or last completed) synthesis run.</summary>
    public SynthesisState SynthesisState
    {
        get => _synthesisState;
        private set { _synthesisState = value; OnPropertyChanged(); OnPropertyChanged(nameof(SynthesisStateLabel)); }
    }

    /// <summary>Human-readable synthesis state label for display.</summary>
    public string SynthesisStateLabel => SynthesisState switch
    {
        SynthesisState.Idle                  => "Idle",
        SynthesisState.Preparing             => "Preparing…",
        SynthesisState.Running               => "Running",
        SynthesisState.Retrying              => "Retrying",
        SynthesisState.Completed             => "Completed",
        SynthesisState.CompletedWithWarnings => "Completed with warnings",
        SynthesisState.Failed                => "Failed",
        SynthesisState.Cancelled             => "Cancelled",
        _                                    => string.Empty,
    };

    /// <summary>Current batch number (1-based) during synthesis.</summary>
    public int CurrentBatch
    {
        get => _currentBatch;
        private set { _currentBatch = value; OnPropertyChanged(); }
    }

    /// <summary>Total number of batches queued (may grow due to auto-split).</summary>
    public int TotalBatches
    {
        get => _totalBatches;
        private set { _totalBatches = value; OnPropertyChanged(); }
    }

    /// <summary>Number of summaries processed so far across all completed batches.</summary>
    public int ProcessedSummaries
    {
        get => _processedSummaries;
        private set { _processedSummaries = value; OnPropertyChanged(); }
    }

    /// <summary>Total number of summaries in the workspace.</summary>
    public int TotalSummaries
    {
        get => _totalSummaries;
        private set { _totalSummaries = value; OnPropertyChanged(); }
    }

    /// <summary>Cumulative estimated prompt tokens sent across all batches.</summary>
    public int TotalPromptTokens
    {
        get => _totalPromptTokens;
        private set { _totalPromptTokens = value; OnPropertyChanged(); }
    }

    /// <summary>Cumulative estimated generated tokens received across all batches.</summary>
    public int TotalGeneratedTokens
    {
        get => _totalGeneratedTokens;
        private set { _totalGeneratedTokens = value; OnPropertyChanged(); }
    }

    /// <summary>Elapsed time for the current synthesis run, formatted for display.</summary>
    public string ElapsedFormatted
    {
        get => _elapsedFormatted;
        private set { _elapsedFormatted = value; OnPropertyChanged(); }
    }

    /// <summary>Estimated time remaining for the current synthesis run, formatted for display.</summary>
    public string EtaFormatted
    {
        get => _etaFormatted;
        private set { _etaFormatted = value; OnPropertyChanged(); }
    }

    /// <summary>Estimated generation speed in tokens per second for the last completed batch.</summary>
    public double GenerationSpeed
    {
        get => _generationSpeed;
        private set { _generationSpeed = value; OnPropertyChanged(); }
    }

    /// <summary>Live output text streaming from the LLM during the current batch call.</summary>
    public string LiveOutput
    {
        get => _liveOutput;
        private set { _liveOutput = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Count of suggestions that have been accepted into the System Map during this session.
    /// Used by the caller to decide whether to mark the workspace dirty.
    /// </summary>
    public int AcceptedCount
    {
        get => _acceptedCount;
        private set { _acceptedCount = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Count of suggestions the user applied during this dialog session,
    /// including merges into existing entities.
    /// </summary>
    public int AppliedSuggestionCount
    {
        get => _appliedSuggestionCount;
        private set { _appliedSuggestionCount = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// True once the dialog session has mutated the System Map, including merges
    /// that may not change top-level entity counts.
    /// </summary>
    public bool HasCanvasChanges
    {
        get => _hasCanvasChanges;
        private set { _hasCanvasChanges = value; OnPropertyChanged(); }
    }

    /// <summary>The hypothesis most recently loaded or generated.</summary>
    public ArchitectureHypothesisModel? CurrentHypothesis
    {
        get => _currentHypothesis;
        private set
        {
            _currentHypothesis = value;
            OnPropertyChanged();
            RebuildCollections();
        }
    }

    /// <summary>Progress messages emitted during the synthesis pass.</summary>
    public ObservableCollection<string> ProgressMessages { get; } = new();

    /// <summary>Structured per-batch telemetry log for display in the batch log table.</summary>
    public ObservableCollection<BatchLogEntry> BatchLog { get; } = new();

    /// <summary>Suggested systems from the current hypothesis.</summary>
    public ObservableCollection<HypothesisSystemModel> Systems { get; } = new();

    /// <summary>High-value node suggestions from the current hypothesis.</summary>
    public ObservableCollection<HypothesisHighValueNodeModel> HighValueNodes { get; } = new();

    /// <summary>Startup suggestions from the current hypothesis.</summary>
    public ObservableCollection<HypothesisStartupModel> Startup { get; } = new();

    /// <summary>Uncertain areas from the current hypothesis.</summary>
    public ObservableCollection<string> UncertainAreas { get; } = new();

    /// <summary>Saved hypothesis snapshots in the workspace, newest first.</summary>
    public ObservableCollection<HypothesisEntry> SavedHypotheses { get; } = new();

    // ── Commands ──────────────────────────────────────────────────────────────

    /// <summary>Loads the workspace hypothesis prompt template.</summary>
    public async Task LoadPromptAsync()
    {
        PromptTemplate = await ArchitectureHypothesisService.LoadPromptTemplateAsync(_workspaceFolderPath);
    }

    /// <summary>Returns available preset prompt template files for this workspace.</summary>
    public async Task<IReadOnlyList<(string FileName, string Description)>> GetPromptTemplatePresetsAsync()
    {
        await ArchitectureHypothesisService.EnsurePromptTemplatesAsync(_workspaceFolderPath);
        return ArchitectureHypothesisService.ListPromptTemplatePresets();
    }

    /// <summary>Loads one preset template into <see cref="PromptTemplate"/>.</summary>
    public async Task LoadPromptPresetAsync(string fileName)
    {
        PromptTemplate = await ArchitectureHypothesisService.LoadPromptTemplatePresetAsync(_workspaceFolderPath, fileName);
    }

    /// <summary>Saves the edited prompt template.</summary>
    public async Task SavePromptAsync()
    {
        await ArchitectureHypothesisService.SavePromptTemplateAsync(_workspaceFolderPath, PromptTemplate);
    }

    /// <summary>
    /// Runs the LLM synthesis pass, builds a hypothesis, and sets it as
    /// <see cref="CurrentHypothesis"/>.
    /// </summary>
    public async Task RunSynthesisAsync()
    {
        if (IsRunning) return;

        if (string.IsNullOrWhiteSpace(_apiEndpoint) || string.IsNullOrWhiteSpace(_modelName))
        {
            StatusMessage = "Configure the LLM endpoint and model in the LLM Summaries dialog first.";
            return;
        }

        IsRunning = true;
        SynthesisState = SynthesisState.Preparing;
        ProgressMessages.Clear();
        BatchLog.Clear();
        LiveOutput = string.Empty;
        TotalPromptTokens = 0;
        TotalGeneratedTokens = 0;
        CurrentBatch = 0;
        TotalBatches = 0;
        ProcessedSummaries = 0;
        TotalSummaries = LlmSummaryService.ListSummaries(_workspaceFolderPath).Count;
        ElapsedFormatted = string.Empty;
        EtaFormatted = string.Empty;
        GenerationSpeed = 0;
        _synthesisStartTime = DateTime.UtcNow;
        _averageBatchDuration = TimeSpan.Zero;

        StatusMessage = "Running synthesis…";
        _cts = new CancellationTokenSource();

        // Elapsed-time ticker — must be stopped and disposed in the finally block.
        var elapsedTimer = new System.Timers.Timer(1000);
        elapsedTimer.Elapsed += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(UpdateElapsed);
        elapsedTimer.Start();

        try
        {
            var progress = new Progress<string>(msg =>
                Avalonia.Threading.Dispatcher.UIThread.Post(() => ProgressMessages.Add(msg)));

            // Streaming token progress — append tokens to LiveOutput.
            var liveOutputSb = new StringBuilder();
            var streamingProgress = new Progress<string>(token =>
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    liveOutputSb.Append(token);
                    LiveOutput = liveOutputSb.ToString();
                }));

            // Telemetry progress — update batch log and telemetry card.
            var telemetryProgress = new Progress<BatchTelemetry>(t =>
                Avalonia.Threading.Dispatcher.UIThread.Post(() => ApplyBatchTelemetry(t, liveOutputSb)));

            SynthesisState = SynthesisState.Running;

            var hypothesis = await ArchitectureHypothesisService.RunSynthesisAsync(
                _apiEndpoint, _modelName, _workspaceFolderPath,
                _hypothesisTokenThreshold,
                progress,
                streamingProgress,
                telemetryProgress,
                _cts.Token);

            CurrentHypothesis = hypothesis;
            RefreshSavedHypotheses();

            var hasWarnings = BatchLog.Any(b => b.Status is "Warning" or "Split");
            SynthesisState = hasWarnings
                ? SynthesisState.CompletedWithWarnings
                : SynthesisState.Completed;

            StatusMessage = $"Synthesis complete — {hypothesis.Systems.Count} system(s), " +
                            $"{hypothesis.HighValueNodes.Count} high-value node(s).";
            AppLogger.Info($"[Hypothesis] Synthesis done: {hypothesis.Systems.Count} systems, " +
                           $"{hypothesis.HighValueNodes.Count} hvn");
        }
        catch (OperationCanceledException)
        {
            SynthesisState = SynthesisState.Cancelled;
            StatusMessage = "Synthesis cancelled.";
            AppLogger.Warn("[Hypothesis] Synthesis cancelled by user.");
            ReportProgress("Cancelled.");
        }
        catch (Exception ex)
        {
            SynthesisState = SynthesisState.Failed;
            StatusMessage = $"Synthesis failed: {ex.Message}";
            AppLogger.Error($"[Hypothesis] Synthesis failed: {ex.GetType().Name}: {ex.Message}");
            ReportProgress($"✖ {ex.Message}");
        }
        finally
        {
            elapsedTimer.Stop();
            elapsedTimer.Dispose();
            IsRunning = false;
            _cts?.Dispose();
            _cts = null;
            UpdateElapsed();
        }
    }

    /// <summary>Applies a <see cref="BatchTelemetry"/> update to the batch log and telemetry card.</summary>
    private void ApplyBatchTelemetry(BatchTelemetry t, StringBuilder liveOutputSb)
    {
        // Reset live output for each new batch call.
        if (t.Status is "Running" or "Retrying")
        {
            liveOutputSb.Clear();
            LiveOutput = string.Empty;
        }

        // Update synthesis state label to reflect retrying.
        if (t.Status == "Retrying" && SynthesisState == SynthesisState.Running)
            SynthesisState = SynthesisState.Retrying;
        else if (t.Status is "Running" && SynthesisState == SynthesisState.Retrying)
            SynthesisState = SynthesisState.Running;

        // Update telemetry card counters.
        CurrentBatch = t.BatchNumber;
        TotalBatches = t.TotalBatches;

        // Accumulate token counts for completed batches.
        if (t.Status is "Completed" or "Warning" or "Failed")
        {
            TotalPromptTokens += t.PromptTokens;
            TotalGeneratedTokens += t.OutputTokens;
            ProcessedSummaries += t.SummaryCount;

            if (t.Duration > TimeSpan.Zero)
            {
                var speed = t.OutputTokens / t.Duration.TotalSeconds;
                GenerationSpeed = speed;

                _averageBatchDuration = _averageBatchDuration == TimeSpan.Zero
                    ? t.Duration
                    : TimeSpan.FromSeconds(
                        (_averageBatchDuration.TotalSeconds + t.Duration.TotalSeconds) / 2.0);

                UpdateEta();
            }
        }

        // Find or create the batch log entry.
        var existing = BatchLog.FirstOrDefault(b => b.BatchLabel == t.BatchLabel);
        if (existing == null)
        {
            existing = new BatchLogEntry
            {
                BatchLabel = t.BatchLabel,
                SummaryCount = t.SummaryCount,
                IsRetry = t.IsRetry,
            };
            BatchLog.Add(existing);
        }

        existing.PromptTokens = t.PromptTokens > 0 ? t.PromptTokens : existing.PromptTokens;
        existing.OutputTokens = t.OutputTokens > 0 ? t.OutputTokens : existing.OutputTokens;
        existing.Duration = t.Duration > TimeSpan.Zero
            ? $"{t.Duration.TotalSeconds:F0}s"
            : existing.Duration;
        existing.FinishReason = !string.IsNullOrEmpty(t.FinishReason)
            ? t.FinishReason
            : existing.FinishReason;
        existing.Status = t.Status;
    }

    private void UpdateElapsed()
    {
        if (!IsRunning && SynthesisState == SynthesisState.Idle) return;
        var elapsed = DateTime.UtcNow - _synthesisStartTime;
        ElapsedFormatted = $"{(int)elapsed.TotalMinutes:D2}m {elapsed.Seconds:D2}s";
    }

    private void UpdateEta()
    {
        if (_averageBatchDuration == TimeSpan.Zero || TotalBatches == 0) return;
        var remaining = TotalBatches - CurrentBatch;
        if (remaining <= 0)
        {
            EtaFormatted = "~done";
            return;
        }
        var etaSec = remaining * _averageBatchDuration.TotalSeconds;
        var eta = TimeSpan.FromSeconds(etaSec);
        EtaFormatted = $"~{(int)eta.TotalMinutes:D2}m {eta.Seconds:D2}s";
    }

    /// <summary>Cancels an in-progress synthesis pass.</summary>
    public void CancelSynthesis() => _cts?.Cancel();

    /// <summary>Reloads the list of saved hypothesis files.</summary>
    public void RefreshSavedHypotheses()
    {
        SavedHypotheses.Clear();
        foreach (var e in ArchitectureHypothesisService.ListHypotheses(_workspaceFolderPath))
            SavedHypotheses.Add(e);
    }

    /// <summary>
    /// Loads a previously saved hypothesis from <paramref name="entry"/> and sets it as
    /// <see cref="CurrentHypothesis"/>.
    /// </summary>
    public async Task LoadHypothesisAsync(HypothesisEntry entry)
    {
        try
        {
            CurrentHypothesis = await ArchitectureHypothesisService.LoadHypothesisAsync(entry.FilePath);
            StatusMessage = $"Loaded: {entry.DisplayName}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to load hypothesis: {ex.Message}";
            AppLogger.Error($"[Hypothesis] Load failed: {ex.Message}");
        }
    }

    // ── Accept suggestions into System Map ────────────────────────────────────

    /// <summary>
    /// Accepts a suggested system into <see cref="WorkspaceModel.SystemMap"/> using
    /// idempotent upsert logic. If a matching System already exists, merges into it.
    /// Returns the created or merged <see cref="SystemModel"/>.
    /// </summary>
    public (SystemModel System, bool IsNew) AcceptSystem(HypothesisSystemModel suggestion)
    {
        var before = DescribeSystemMap();
        AppLogger.Debug($"[Hypothesis] AcceptSystem starting for '{suggestion.Name}'. Before: {before}");

        var (system, isNew) = SystemMapUpsertService.UpsertSystem(_workspace.SystemMap, suggestion);

        // Upsert the modules the LLM suggested for this system so that the canvas reflects
        // the hypothesis-defined module structure rather than showing empty systems.
        foreach (var moduleSuggestion in suggestion.Modules)
            SystemMapUpsertService.UpsertModule(_workspace.SystemMap, moduleSuggestion, system);

        if (isNew) AcceptedCount++;
        AppliedSuggestionCount++;
        HasCanvasChanges = true;
        AppLogger.Info($"[Hypothesis] {(isNew ? "Accepted" : "Merged")} system: {system.Name}" +
                       (suggestion.Modules.Count > 0 ? $" (+{suggestion.Modules.Count} module suggestion(s))" : string.Empty));
        ReapplyManualOverrides();
        SystemMapValidator.Validate(_workspace.SystemMap);

        var after = DescribeSystemMap();
        AppLogger.Debug($"[Hypothesis] AcceptSystem completed for '{system.Name}'. IsNew={isNew}; AppliedSuggestionCount={AppliedSuggestionCount}; After: {after}");
        return (system, isNew);
    }

    /// <summary>
    /// Accepts a suggested module into the given <paramref name="targetSystem"/>
    /// using idempotent upsert logic. If a matching Module already exists, merges into it.
    /// </summary>
    public (ModuleModel Module, bool IsNew) AcceptModule(HypothesisModuleModel suggestion, SystemModel targetSystem)
    {
        var before = DescribeSystemMap();
        AppLogger.Debug($"[Hypothesis] AcceptModule starting for '{suggestion.Name}' into '{targetSystem.Name}'. Before: {before}");

        var (module, isNew) = SystemMapUpsertService.UpsertModule(_workspace.SystemMap, suggestion, targetSystem);

        if (isNew) AcceptedCount++;
        AppliedSuggestionCount++;
        HasCanvasChanges = true;
        AppLogger.Info($"[Hypothesis] {(isNew ? "Accepted" : "Merged")} module: {module.Name} → {targetSystem.Name}");
        ReapplyManualOverrides();

        var after = DescribeSystemMap();
        AppLogger.Debug($"[Hypothesis] AcceptModule completed for '{module.Name}'. IsNew={isNew}; AppliedSuggestionCount={AppliedSuggestionCount}; After: {after}");
        return (module, isNew);
    }

    /// <summary>
    /// Accepts a high-value node suggestion using idempotent upsert logic.
    /// If a matching Code Node already exists, merges the high-value metadata into it.
    /// Returns the created or merged <see cref="CodeNodeModel"/>.
    /// </summary>
    public (CodeNodeModel Node, bool IsNew) AcceptHighValueNode(HypothesisHighValueNodeModel suggestion)
    {
        var before = DescribeSystemMap();
        AppLogger.Debug($"[Hypothesis] AcceptHighValueNode starting for '{suggestion.Name}'. Before: {before}");

        var (node, isNew) = SystemMapUpsertService.UpsertHighValueNode(_workspace.SystemMap, suggestion);

        if (isNew) AcceptedCount++;
        AppliedSuggestionCount++;
        HasCanvasChanges = true;
        AppLogger.Info($"[Hypothesis] {(isNew ? "Accepted" : "Merged")} high-value node: {node.Name}");
        ReapplyManualOverrides();
        SystemMapValidator.Validate(_workspace.SystemMap);

        var after = DescribeSystemMap();
        AppLogger.Debug($"[Hypothesis] AcceptHighValueNode completed for '{node.Name}'. IsNew={isNew}; AppliedSuggestionCount={AppliedSuggestionCount}; After: {after}");
        return (node, isNew);
    }

    /// <summary>
    /// Clears all Systems, Modules, ExternalSystems, and Relationships from the
    /// workspace System Map, leaving ManualOverrides intact.
    /// Call this before applying new hypothesis results when the user wants a fresh canvas.
    /// </summary>
    public void ClearSystemMap()
    {
        var before = DescribeSystemMap();
        AppLogger.Debug($"[Hypothesis] ClearSystemMap starting. Before: {before}");

        _workspace.SystemMap.Systems.Clear();
        _workspace.SystemMap.Modules.Clear();
        _workspace.SystemMap.ExternalSystems.Clear();
        _workspace.SystemMap.Relationships.Clear();
        HasCanvasChanges = true;
        StatusMessage = "Canvas cleared.";
        AppLogger.Info("[Hypothesis] System Map cleared by user.");
        AppLogger.Debug($"[Hypothesis] ClearSystemMap completed. After: {DescribeSystemMap()}");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void RebuildCollections()
    {
        Systems.Clear();
        HighValueNodes.Clear();
        Startup.Clear();
        UncertainAreas.Clear();

        if (_currentHypothesis == null) return;

        foreach (var s in _currentHypothesis.Systems)         Systems.Add(s);
        foreach (var n in _currentHypothesis.HighValueNodes)   HighValueNodes.Add(n);
        foreach (var u in _currentHypothesis.Startup)          Startup.Add(u);
        foreach (var a in _currentHypothesis.UncertainAreas)   UncertainAreas.Add(a);
    }

    /// <summary>
    /// Re-applies all stored manual overrides after any analysis pass that may have
    /// added or modified entities in the System Map.
    /// </summary>
    private void ReapplyManualOverrides()
    {
        if (_workspace.SystemMap.ManualOverrides.Count == 0) return;
        ManualOverrideService.Apply(_workspace.SystemMap, _workspace.SystemMap.ManualOverrides);
    }

    private void ReportProgress(string message)
        => Avalonia.Threading.Dispatcher.UIThread.Post(() => ProgressMessages.Add(message));

    private string DescribeSystemMap()
        => $"Systems={_workspace.SystemMap.Systems.Count}, Modules={_workspace.SystemMap.AllModules.Count()}, CodeNodes={_workspace.SystemMap.AllCodeNodes.Count()}, ExternalSystems={_workspace.SystemMap.ExternalSystems.Count}, Relationships={_workspace.SystemMap.Relationships.Count}";

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
