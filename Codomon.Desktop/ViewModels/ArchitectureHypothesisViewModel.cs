using Codomon.Desktop.Models;
using Codomon.Desktop.Models.ArchitectureHypothesis;
using Codomon.Desktop.Models.SystemMap;
using Codomon.Desktop.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Codomon.Desktop.ViewModels;

/// <summary>
/// ViewModel for the Architecture Hypothesis dialog.
/// Manages running an LLM synthesis pass, reviewing the resulting hypothesis,
/// and accepting individual suggestions into the System Map.
/// </summary>
public class ArchitectureHypothesisViewModel : INotifyPropertyChanged
{
    private readonly WorkspaceModel _workspace;
    private readonly string _workspaceFolderPath;

    private string _promptTemplate = string.Empty;
    private bool _isRunning;
    private string _statusMessage = string.Empty;
    private ArchitectureHypothesisModel? _currentHypothesis;
    private CancellationTokenSource? _cts;
    private int _acceptedCount;

    public ArchitectureHypothesisViewModel(WorkspaceModel workspace, string workspaceFolderPath)
    {
        _workspace = workspace;
        _workspaceFolderPath = workspaceFolderPath;
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

    /// <summary>
    /// Count of suggestions that have been accepted into the System Map during this session.
    /// Used by the caller to decide whether to mark the workspace dirty.
    /// </summary>
    public int AcceptedCount
    {
        get => _acceptedCount;
        private set { _acceptedCount = value; OnPropertyChanged(); }
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

        var apiEndpoint = _workspace.LlmSettings.ApiEndpoint;
        var modelName   = _workspace.LlmSettings.ModelName;

        if (string.IsNullOrWhiteSpace(apiEndpoint) || string.IsNullOrWhiteSpace(modelName))
        {
            StatusMessage = "Configure the LLM endpoint and model in the LLM Summaries dialog first.";
            return;
        }

        IsRunning = true;
        ProgressMessages.Clear();
        StatusMessage = "Running synthesis…";
        _cts = new CancellationTokenSource();

        try
        {
            var progress = new Progress<string>(msg =>
                Avalonia.Threading.Dispatcher.UIThread.Post(() => ProgressMessages.Add(msg)));

            var hypothesis = await ArchitectureHypothesisService.RunSynthesisAsync(
                apiEndpoint, modelName, _workspaceFolderPath, progress, _cts.Token);

            CurrentHypothesis = hypothesis;
            RefreshSavedHypotheses();
            StatusMessage = $"Synthesis complete — {hypothesis.Systems.Count} system(s), " +
                            $"{hypothesis.HighValueNodes.Count} high-value node(s).";
            AppLogger.Info($"[Hypothesis] Synthesis done: {hypothesis.Systems.Count} systems, " +
                           $"{hypothesis.HighValueNodes.Count} hvn");
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Synthesis cancelled.";
            AppLogger.Warn("[Hypothesis] Synthesis cancelled by user.");
            ReportProgress("Cancelled.");
        }
        catch (Exception ex)
        {
            StatusMessage = $"Synthesis failed: {ex.Message}";
            AppLogger.Error($"[Hypothesis] Synthesis failed: {ex.GetType().Name}: {ex.Message}");
            ReportProgress($"✖ {ex.Message}");
        }
        finally
        {
            IsRunning = false;
            _cts?.Dispose();
            _cts = null;
        }
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
    /// Accepts a suggested system into <see cref="WorkspaceModel.SystemMap"/>.
    /// Returns the created <see cref="SystemModel"/>.
    /// </summary>
    public SystemModel AcceptSystem(HypothesisSystemModel suggestion)
    {
        var system = new SystemModel
        {
            Name       = suggestion.Name,
            Kind       = suggestion.Kind,
            Confidence = suggestion.Confidence,
            Evidence   = suggestion.Evidence.Select(e => new EvidenceModel
            {
                Source      = "LLM",
                Description = e
            }).ToList()
        };

        _workspace.SystemMap.Systems.Add(system);
        suggestion.IsAccepted = true;
        AcceptedCount++;
        AppLogger.Info($"[Hypothesis] Accepted system: {system.Name}");
        ReapplyManualOverrides();
        return system;
    }

    /// <summary>
    /// Accepts a suggested module into the given <paramref name="targetSystem"/>
    /// in the System Map.
    /// </summary>
    public ModuleModel AcceptModule(HypothesisModuleModel suggestion, SystemModel targetSystem)
    {
        var module = new ModuleModel
        {
            Name       = suggestion.Name,
            Confidence = suggestion.Confidence,
            SystemIds  = new List<string> { targetSystem.Id }
        };

        targetSystem.Modules.Add(module);
        AcceptedCount++;
        AppLogger.Info($"[Hypothesis] Accepted module: {module.Name} → {targetSystem.Name}");
        ReapplyManualOverrides();
        return module;
    }

    /// <summary>
    /// Accepts a high-value node suggestion as a <see cref="CodeNodeModel"/> appended to
    /// the first available module in the System Map, or creates a holding module on the
    /// first system if no modules exist.
    /// </summary>
    public CodeNodeModel AcceptHighValueNode(HypothesisHighValueNodeModel suggestion)
    {
        var node = new CodeNodeModel
        {
            Name       = suggestion.Name,
            Confidence = suggestion.Confidence,
            Notes      = suggestion.Reason,
            Evidence   = new List<EvidenceModel>
            {
                new() { Source = "LLM", Description = suggestion.Reason }
            }
        };

        // Try to place in the first available module.
        var firstModule = _workspace.SystemMap.AllModules.FirstOrDefault();
        if (firstModule != null)
        {
            firstModule.CodeNodes.Add(node);
        }
        else
        {
            // No modules yet — create a holding module on the first accepted system.
            var firstSystem = _workspace.SystemMap.Systems.FirstOrDefault();
            if (firstSystem != null)
            {
                var holdingModule = new ModuleModel
                {
                    Name = "UnassignedHighValueNodes",
                    Confidence = ConfidenceLevel.Unknown
                };
                holdingModule.CodeNodes.Add(node);
                firstSystem.Modules.Add(holdingModule);
            }
            // If there are no systems either, the node cannot be placed.
        }

        suggestion.IsAccepted = true;
        AcceptedCount++;
        AppLogger.Info($"[Hypothesis] Accepted high-value node: {node.Name}");
        ReapplyManualOverrides();
        return node;
    }

    /// <summary>
    /// Clears all Systems, Modules, ExternalSystems, and Relationships from the
    /// workspace System Map, leaving ManualOverrides intact.
    /// Call this before applying new hypothesis results when the user wants a fresh canvas.
    /// </summary>
    public void ClearSystemMap()
    {
        _workspace.SystemMap.Systems.Clear();
        _workspace.SystemMap.Modules.Clear();
        _workspace.SystemMap.ExternalSystems.Clear();
        _workspace.SystemMap.Relationships.Clear();
        StatusMessage = "Canvas cleared.";
        AppLogger.Info("[Hypothesis] System Map cleared by user.");
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

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
