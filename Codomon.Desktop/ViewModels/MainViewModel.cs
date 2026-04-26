using Codomon.Desktop.Models;
using Codomon.Desktop.Persistence;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Codomon.Desktop.ViewModels;

public class MainViewModel : INotifyPropertyChanged
{
    private WorkspaceModel _workspace = DemoData.Workspace;
    private string _workspaceFolderPath = string.Empty;
    private string _statusMessage = "Demo workspace loaded.";
    private bool _isDirty = false;

    /// <summary>Periodic autosave timer — fires every 5 minutes while a workspace is open.</summary>
    private System.Timers.Timer? _autosaveTimer;

    public WorkspaceModel Workspace
    {
        get => _workspace;
        private set { _workspace = value; OnPropertyChanged(); }
    }

    public string WorkspaceFolderPath
    {
        get => _workspaceFolderPath;
        private set { _workspaceFolderPath = value; OnPropertyChanged(); }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set { _statusMessage = value; OnPropertyChanged(); }
    }

    /// <summary>True when the workspace has unsaved changes.</summary>
    public bool IsDirty
    {
        get => _isDirty;
        set { _isDirty = value; OnPropertyChanged(); }
    }

    public SelectionStateModel Selection { get; } = new SelectionStateModel();

    public async Task NewWorkspaceAsync(
        string folderPath,
        string workspaceName,
        string sourceProjectPath = "",
        string profileName = "Default",
        IEnumerable<string>? initialSystemNames = null)
    {
        var workspace = await WorkspaceSerializer.CreateNewAsync(
            folderPath, workspaceName, sourceProjectPath, profileName, initialSystemNames);
        Workspace = workspace;
        WorkspaceFolderPath = folderPath;
        IsDirty = false;
        ClearSelection();
        StatusMessage = $"New workspace created: {folderPath}";
        StartAutosaveTimer();
        await TryCreateAutosaveAsync();
    }

    public async Task OpenWorkspaceAsync(string folderPath)
    {
        var workspace = await WorkspaceSerializer.LoadAsync(folderPath);
        Workspace = workspace;
        WorkspaceFolderPath = folderPath;
        IsDirty = false;
        ClearSelection();
        StatusMessage = $"Opened: {folderPath}";
        StartAutosaveTimer();
    }

    public async Task SaveWorkspaceAsync()
    {
        if (string.IsNullOrEmpty(WorkspaceFolderPath))
            throw new InvalidOperationException("No workspace folder is set. Use Save As.");

        await WorkspaceSerializer.SaveAsync(Workspace, WorkspaceFolderPath);
        IsDirty = false;
        StatusMessage = $"Saved: {WorkspaceFolderPath}";
        await TryCreateAutosaveAsync();
    }

    public async Task SaveWorkspaceAsAsync(string folderPath)
    {
        await WorkspaceSerializer.SaveAsync(Workspace, folderPath);
        WorkspaceFolderPath = folderPath;
        IsDirty = false;
        StatusMessage = $"Saved: {folderPath}";
        StartAutosaveTimer();
        await TryCreateAutosaveAsync();
    }

    /// <summary>
    /// Loads the autosave at <paramref name="autosavePath"/> after verifying its integrity,
    /// then reloads the workspace from disk.
    /// Throws <see cref="InvalidOperationException"/> if integrity validation fails.
    /// </summary>
    public async Task LoadAutosaveAsync(string autosavePath)
    {
        if (!await AutosaveService.ValidateAutosaveAsync(autosavePath))
            throw new InvalidOperationException(
                "Autosave integrity check failed. The snapshot may be corrupt and cannot be loaded.");

        await AutosaveService.RestoreAutosaveAsync(autosavePath, WorkspaceFolderPath);

        var workspace = await WorkspaceSerializer.LoadAsync(WorkspaceFolderPath);
        Workspace = workspace;
        IsDirty = false;
        ClearSelection();
        StatusMessage = $"Autosave restored: {Path.GetFileName(autosavePath)}";
    }

    /// <summary>Returns autosave entries for the active workspace, newest first.</summary>
    public List<AutosaveEntry> GetAutosaveEntries() =>
        string.IsNullOrEmpty(WorkspaceFolderPath)
            ? new List<AutosaveEntry>()
            : AutosaveService.GetAutosaveEntries(WorkspaceFolderPath);

    // ── Autosave timer ────────────────────────────────────────────────────────

    private void StartAutosaveTimer()
    {
        _autosaveTimer?.Dispose();
        if (string.IsNullOrEmpty(WorkspaceFolderPath)) return;

        _autosaveTimer = new System.Timers.Timer(TimeSpan.FromMinutes(5).TotalMilliseconds)
        {
            AutoReset = true
        };
        _autosaveTimer.Elapsed += async (_, _) => await TryCreateAutosaveAsync();
        _autosaveTimer.Start();
    }

    /// <summary>
    /// Attempts to create an autosave; logs any error but does not propagate it so a
    /// background timer failure never crashes the application.
    /// </summary>
    public async Task TryCreateAutosaveAsync()
    {
        if (string.IsNullOrEmpty(WorkspaceFolderPath)) return;
        try
        {
            await AutosaveService.CreateAutosaveAsync(WorkspaceFolderPath);
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"Autosave failed: {ex.Message}");
        }
    }

    private void ClearSelection()
    {
        Selection.SelectedId = string.Empty;
        Selection.SelectedType = string.Empty;
        Selection.SelectedName = string.Empty;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
