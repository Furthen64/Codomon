using Codomon.Desktop.Models;
using Codomon.Desktop.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Codomon.Desktop.ViewModels;

/// <summary>
/// ViewModel for the LLM Summaries dialog.
/// Manages LLM settings, C# file enumeration, background summary generation, and summary browsing.
/// </summary>
public class LlmSummaryViewModel : INotifyPropertyChanged
{
    private readonly WorkspaceModel _workspace;
    private readonly string _workspaceFolderPath;

    private string _apiEndpoint = string.Empty;
    private string _modelName = string.Empty;
    private string _promptTemplate = string.Empty;
    private string _connectionStatus = string.Empty;
    private bool _connectionOk;
    private bool _isTestingConnection;
    private bool _isGenerating;
    private string _statusMessage = string.Empty;
    private CancellationTokenSource? _cts;

    public LlmSummaryViewModel(WorkspaceModel workspace, string workspaceFolderPath)
    {
        _workspace = workspace;
        _workspaceFolderPath = workspaceFolderPath;

        // Copy settings from model so edits are buffered until user saves.
        _apiEndpoint = workspace.LlmSettings.ApiEndpoint;
        _modelName = workspace.LlmSettings.ModelName;
    }

    // ── Settings ──────────────────────────────────────────────────────────────

    public string ApiEndpoint
    {
        get => _apiEndpoint;
        set { _apiEndpoint = value; OnPropertyChanged(); }
    }

    public string ModelName
    {
        get => _modelName;
        set { _modelName = value; OnPropertyChanged(); }
    }

    public string PromptTemplate
    {
        get => _promptTemplate;
        set { _promptTemplate = value; OnPropertyChanged(); }
    }

    public string ConnectionStatus
    {
        get => _connectionStatus;
        private set { _connectionStatus = value; OnPropertyChanged(); }
    }

    public bool ConnectionOk
    {
        get => _connectionOk;
        private set { _connectionOk = value; OnPropertyChanged(); }
    }

    public bool IsTestingConnection
    {
        get => _isTestingConnection;
        private set { _isTestingConnection = value; OnPropertyChanged(); }
    }

    private bool _isFetchingModels;

    public bool IsFetchingModels
    {
        get => _isFetchingModels;
        private set { _isFetchingModels = value; OnPropertyChanged(); }
    }

    /// <summary>Model IDs returned by the last successful probe of the endpoint.</summary>
    public ObservableCollection<string> AvailableModels { get; } = new();

    // ── Generation state ──────────────────────────────────────────────────────

    public bool IsGenerating
    {
        get => _isGenerating;
        private set { _isGenerating = value; OnPropertyChanged(); }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set { _statusMessage = value; OnPropertyChanged(); }
    }

    /// <summary>Progress messages emitted during background generation.</summary>
    public ObservableCollection<string> ProgressMessages { get; } = new();

    // ── File selection ────────────────────────────────────────────────────────

    /// <summary>All C# files discovered under the source project path.</summary>
    public ObservableCollection<CsFileItem> CsFiles { get; } = new();

    // ── Summaries browser ─────────────────────────────────────────────────────

    /// <summary>Stored summaries for the workspace, refreshed on demand.</summary>
    public ObservableCollection<SummaryEntry> Summaries { get; } = new();

    // ── Commands ──────────────────────────────────────────────────────────────

    /// <summary>Saves the current API endpoint and model name back to the workspace model.</summary>
    public void SaveSettings()
    {
        _workspace.LlmSettings.ApiEndpoint = ApiEndpoint;
        _workspace.LlmSettings.ModelName = ModelName;
    }

    /// <summary>Tests the LLM connection using the currently entered settings.</summary>
    public async Task TestConnectionAsync()
    {
        if (IsTestingConnection) return;

        IsTestingConnection = true;
        ConnectionStatus = "Testing…";
        ConnectionOk = false;

        try
        {
            var (ok, msg) = await LlmSummaryService.TestConnectionAsync(
                ApiEndpoint, ModelName, CancellationToken.None);
            ConnectionOk = ok;
            ConnectionStatus = msg;
        }
        finally
        {
            IsTestingConnection = false;
        }
    }

    /// <summary>
    /// Queries the host's <c>/models</c> endpoint and populates <see cref="AvailableModels"/>.
    /// Does nothing if a fetch is already in progress.
    /// </summary>
    public async Task FetchModelsAsync()
    {
        if (IsFetchingModels) return;

        IsFetchingModels = true;
        try
        {
            var models = await LlmSummaryService.FetchModelsAsync(ApiEndpoint, CancellationToken.None);
            AvailableModels.Clear();
            foreach (var m in models)
                AvailableModels.Add(m);
        }
        finally
        {
            IsFetchingModels = false;
        }
    }

    /// <summary>
    /// Loads the workspace <c>summary_prompt.md</c> template into <see cref="PromptTemplate"/>.
    /// </summary>
    public async Task LoadPromptAsync()
    {
        PromptTemplate = await LlmSummaryService.LoadPromptTemplateAsync(_workspaceFolderPath);
    }

    /// <summary>Saves the edited prompt template to <c>summary_prompt.md</c>.</summary>
    public async Task SavePromptAsync()
    {
        await LlmSummaryService.SavePromptTemplateAsync(_workspaceFolderPath, PromptTemplate);
    }

    /// <summary>
    /// Discovers C# source files under the workspace source project path and populates
    /// <see cref="CsFiles"/>.
    /// </summary>
    public async Task LoadCsFilesAsync()
    {
        CsFiles.Clear();

        var sourcePath = _workspace.SourceProjectPath;
        if (string.IsNullOrEmpty(sourcePath))
        {
            StatusMessage = "No source project path configured in workspace.";
            return;
        }

        var searchRoot = Directory.Exists(sourcePath)
            ? sourcePath
            : Path.GetDirectoryName(sourcePath) ?? sourcePath;

        if (!Directory.Exists(searchRoot))
        {
            StatusMessage = $"Source path not found: {searchRoot}";
            return;
        }

        var files = await Task.Run(() =>
            Directory.EnumerateFiles(searchRoot, "*.cs", SearchOption.AllDirectories)
                .Where(f => !IsExcluded(f))
                .OrderBy(f => f)
                .ToList());

        foreach (var f in files)
            CsFiles.Add(new CsFileItem
            {
                FullPath = f,
                RelativePath = Path.GetRelativePath(searchRoot, f),
                IsSelected = false
            });

        StatusMessage = $"Found {files.Count} C# file(s).";
    }

    /// <summary>Sets <see cref="CsFileItem.IsSelected"/> for all files.</summary>
    public void SelectAll(bool selected)
    {
        foreach (var f in CsFiles)
            f.IsSelected = selected;
    }

    /// <summary>
    /// Generates summaries for all currently selected files in the background.
    /// Stops on the first failure; failures are added to <see cref="ProgressMessages"/>.
    /// </summary>
    public async Task GenerateSummariesAsync()
    {
        if (IsGenerating) return;

        var selected = CsFiles.Where(f => f.IsSelected).ToList();
        if (selected.Count == 0)
        {
            StatusMessage = "No files selected.";
            return;
        }

        SaveSettings();

        IsGenerating = true;
        ProgressMessages.Clear();
        StatusMessage = "Generating summaries…";

        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        var sourcePath = _workspace.SourceProjectPath;
        var searchRoot = Directory.Exists(sourcePath)
            ? sourcePath
            : Path.GetDirectoryName(sourcePath) ?? sourcePath;

        try
        {
            var batchFolder = LlmSummaryService.CreateBatchFolder(_workspaceFolderPath);
            ReportProgress($"Batch folder: {Path.GetFileName(batchFolder)}");
            ReportProgress($"Generating summaries for {selected.Count} file(s)…");

            int done = 0;
            foreach (var file in selected)
            {
                ct.ThrowIfCancellationRequested();

                ReportProgress($"[{done + 1}/{selected.Count}] {file.RelativePath}");

                try
                {
                    await LlmSummaryService.GenerateAndSaveSummaryAsync(
                        ApiEndpoint, ModelName,
                        _workspaceFolderPath, batchFolder,
                        file.FullPath, searchRoot, ct);

                    done++;
                    ReportProgress($"  ✔ Done");
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    var msg = $"  ✖ Failed: {ex.Message}";
                    ReportProgress(msg);
                    Models.AppLogger.Error($"LLM summary failed for {file.RelativePath}: {ex.Message}");
                    // Requirement: stop batching on failure.
                    StatusMessage = $"Generation stopped after failure on {file.RelativePath}.";
                    return;
                }
            }

            StatusMessage = $"Done — {done} summary(s) generated.";
            ReportProgress($"Completed: {done} file(s) summarised.");

            // Refresh the browse list.
            RefreshSummaries();
        }
        catch (OperationCanceledException)
        {
            ReportProgress("Generation cancelled.");
            StatusMessage = "Cancelled.";
        }
        finally
        {
            IsGenerating = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    /// <summary>Cancels an in-progress generation batch.</summary>
    public void CancelGeneration() => _cts?.Cancel();

    /// <summary>Reloads stored summaries into <see cref="Summaries"/>.</summary>
    public void RefreshSummaries()
    {
        Summaries.Clear();
        var list = LlmSummaryService.ListSummaries(_workspaceFolderPath);
        foreach (var s in list)
            Summaries.Add(s);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void ReportProgress(string message)
        => Avalonia.Threading.Dispatcher.UIThread.Post(() => ProgressMessages.Add(message));

    private static bool IsExcluded(string filePath)
    {
        var norm = filePath.Replace('\\', '/');
        return norm.Contains("/obj/") || norm.Contains("/bin/") ||
               norm.Contains("/.vs/") || norm.Contains("/node_modules/") ||
               norm.EndsWith(".Designer.cs", StringComparison.OrdinalIgnoreCase) ||
               norm.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase) ||
               norm.EndsWith(".g.i.cs", StringComparison.OrdinalIgnoreCase);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>Represents a C# source file in the file-selection list.</summary>
public class CsFileItem : INotifyPropertyChanged
{
    private bool _isSelected;

    public string FullPath { get; set; } = string.Empty;
    public string RelativePath { get; set; } = string.Empty;

    public bool IsSelected
    {
        get => _isSelected;
        set { _isSelected = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
