using Codomon.Desktop.Models;
using Codomon.Desktop.Persistence;
using Codomon.Desktop.Services;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Codomon.Desktop.ViewModels;

public class MainViewModel : INotifyPropertyChanged
{
    private WorkspaceModel _workspace = new WorkspaceModel();
    private string _workspaceFolderPath = string.Empty;
    private string _statusMessage = "Ready";
    private bool _isDirty = false;
    private bool _hasWorkspace = false;

    /// <summary>Periodic autosave timer -- fires every 5 minutes while a workspace is open.</summary>
    private System.Timers.Timer? _autosaveTimer;

    public MainViewModel()
    {
        _logReplay = new LogReplayViewModel(_workspace);
    }

    public bool HasWorkspace
    {
        get => _hasWorkspace;
        private set { _hasWorkspace = value; OnPropertyChanged(); }
    }

    public WorkspaceModel Workspace
    {
        get => _workspace;
        private set
        {
            _workspace = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Profiles));
            OnPropertyChanged(nameof(ActiveProfileId));
            // Re-create the replay VM so it references the new workspace model.
            _logReplay = new LogReplayViewModel(value);
            OnPropertyChanged(nameof(LogReplay));
        }
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

    // ── Log Replay ────────────────────────────────────────────────────────────

    private LogReplayViewModel _logReplay;

    /// <summary>The replay controller for imported log files.</summary>
    public LogReplayViewModel LogReplay => _logReplay;

    /// <summary>
    /// Imports a log file from <paramref name="sourcePath"/> into the workspace
    /// <c>logs/imported/</c> folder and loads its entries into <see cref="LogReplay"/>.
    /// </summary>
    public async Task ImportLogsAsync(string sourcePath)
    {
        if (string.IsNullOrEmpty(WorkspaceFolderPath))
            throw new InvalidOperationException("No workspace is open. Please open or create a workspace first.");

        var destPath = await LogImportService.CopyToWorkspaceAsync(sourcePath, WorkspaceFolderPath);
        var entries  = await LogImportService.LoadEntriesAsync(destPath);
        _logReplay.LoadEntries(entries);

        StatusMessage = $"Imported {entries.Count} log entries from {Path.GetFileName(destPath)}";
        AppLogger.Info($"Log imported: {destPath} ({entries.Count} entries)");
    }

    /// <summary>
    /// Imports a delimiter-separated log file using the wizard-supplied
    /// <paramref name="options"/> (delimiter, timestamp column, format, timezone).
    /// </summary>
    public async Task ImportLogsWithOptionsAsync(string sourcePath, Services.ImportOptions options)
    {
        if (string.IsNullOrEmpty(WorkspaceFolderPath))
            throw new InvalidOperationException("No workspace is open. Please open or create a workspace first.");

        var destPath = await LogImportService.CopyToWorkspaceAsync(sourcePath, WorkspaceFolderPath);
        var entries  = await LogImportService.LoadEntriesWithOptionsAsync(destPath, options);
        _logReplay.LoadEntries(entries);

        StatusMessage = $"Imported {entries.Count} log entries from {Path.GetFileName(destPath)}";
        AppLogger.Info($"Log imported (wizard): {destPath} ({entries.Count} entries)");
    }

    // ── Profile management ───────────────────────────────────────────────────

    /// <summary>All profiles in the current workspace.</summary>
    public IReadOnlyList<ProfileModel> Profiles => Workspace.Profiles;

    /// <summary>ID of the currently active profile.</summary>
    public string ActiveProfileId => Workspace.ActiveProfileId;

    /// <summary>
    /// Saves the live layout into the current profile, then switches the active profile to
    /// <paramref name="profileId"/> and fires property-change notifications. The view responds
    /// to those notifications to apply the new profile layout and redraw the canvas.
    /// </summary>
    public void SwitchProfile(string profileId)
    {
        if (string.IsNullOrEmpty(profileId) || Workspace.ActiveProfileId == profileId) return;

        CaptureLayoutToActiveProfile();

        Workspace.ActiveProfileId = profileId;

        var newProfile = Workspace.ActiveProfile;
        if (newProfile != null)
            WorkspaceSerializer.ApplyProfileLayout(newProfile, Workspace.Systems);

        IsDirty = true;
        OnPropertyChanged(nameof(ActiveProfileId));
        StatusMessage = $"Profile: {newProfile?.ProfileName ?? profileId}";
    }

    /// <summary>
    /// Creates a new profile initialized with the current live layout state, adds it to the
    /// workspace, and switches to it as the active profile.
    /// </summary>
    public ProfileModel CreateProfile(string name)
    {
        CaptureLayoutToActiveProfile();

        var profile = new ProfileModel
        {
            Id = Guid.NewGuid().ToString(),
            ProfileName = name,
        };
        // Start the new profile with the current layout so the canvas looks the same initially.
        WorkspaceSerializer.CaptureLayoutIntoProfile(Workspace, profile);

        Workspace.Profiles.Add(profile);
        Workspace.ActiveProfileId = profile.Id;

        IsDirty = true;
        OnPropertyChanged(nameof(Profiles));
        OnPropertyChanged(nameof(ActiveProfileId));
        StatusMessage = $"Profile created: {name}";
        return profile;
    }

    /// <summary>Renames the profile with the given ID.</summary>
    public void RenameProfile(string profileId, string newName)
    {
        var profile = Workspace.Profiles.FirstOrDefault(p => p.Id == profileId);
        if (profile == null) return;

        profile.ProfileName = newName;
        IsDirty = true;
        OnPropertyChanged(nameof(Profiles));
        StatusMessage = $"Profile renamed to: {newName}";
    }

    /// <summary>Duplicates a profile, saves its copy with <paramref name="newName"/>, and switches to it.</summary>
    public ProfileModel DuplicateProfile(string profileId, string newName)
    {
        CaptureLayoutToActiveProfile();

        var source = Workspace.Profiles.FirstOrDefault(p => p.Id == profileId)
            ?? throw new InvalidOperationException($"Profile '{profileId}' not found.");

        var copy = new ProfileModel
        {
            Id = Guid.NewGuid().ToString(),
            ProfileName = newName,
            LayoutPositions = source.LayoutPositions.ToDictionary(
                kvp => kvp.Key,
                kvp => new LayoutPosition
                {
                    X = kvp.Value.X, Y = kvp.Value.Y,
                    Width = kvp.Value.Width, Height = kvp.Value.Height
                }),
            CheckboxFilterState = new Dictionary<string, bool>(source.CheckboxFilterState),
            VisualSettings = new Dictionary<string, string>(source.VisualSettings),
            Notes = source.Notes
        };

        Workspace.Profiles.Add(copy);
        Workspace.ActiveProfileId = copy.Id;
        WorkspaceSerializer.ApplyProfileLayout(copy, Workspace.Systems);

        IsDirty = true;
        OnPropertyChanged(nameof(Profiles));
        OnPropertyChanged(nameof(ActiveProfileId));
        StatusMessage = $"Profile duplicated: {newName}";
        return copy;
    }

    /// <summary>
    /// Deletes the profile with the given ID from the workspace.
    /// Throws <see cref="InvalidOperationException"/> when only one profile remains.
    /// If the deleted profile was active, the first remaining profile becomes active.
    /// </summary>
    public void DeleteProfile(string profileId)
    {
        if (Workspace.Profiles.Count <= 1)
            throw new InvalidOperationException(
                "Cannot delete the last profile. At least one profile must remain.");

        var profile = Workspace.Profiles.FirstOrDefault(p => p.Id == profileId);
        if (profile == null) return;

        bool wasActive = Workspace.ActiveProfileId == profileId;
        Workspace.Profiles.Remove(profile);

        if (wasActive)
        {
            var next = Workspace.Profiles.FirstOrDefault();
            if (next != null)
            {
                Workspace.ActiveProfileId = next.Id;
                WorkspaceSerializer.ApplyProfileLayout(next, Workspace.Systems);
                OnPropertyChanged(nameof(ActiveProfileId));
            }
        }

        IsDirty = true;
        OnPropertyChanged(nameof(Profiles));
        StatusMessage = $"Profile deleted: {profile.ProfileName}";
    }

    /// <summary>Captures the live canvas layout into the currently active profile in memory.</summary>
    public void CaptureLayoutToActiveProfile()
    {
        var profile = Workspace.ActiveProfile;
        if (profile != null)
            WorkspaceSerializer.CaptureLayoutIntoProfile(Workspace, profile);
    }

    // ── Workspace operations ─────────────────────────────────────────────────

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
        HasWorkspace = true;
        ClearSelection();
        StatusMessage = $"New workspace created: {folderPath}";
        RecentWorkspacesService.AddOrUpdate(folderPath, workspace.WorkspaceName);
        StartAutosaveTimer();
        await TryCreateAutosaveAsync();
    }

    public async Task OpenWorkspaceAsync(string folderPath)
    {
        var workspace = await WorkspaceSerializer.LoadAsync(folderPath);
        Workspace = workspace;
        WorkspaceFolderPath = folderPath;
        IsDirty = false;
        HasWorkspace = true;
        ClearSelection();
        StatusMessage = $"Opened: {folderPath}";
        RecentWorkspacesService.AddOrUpdate(folderPath, workspace.WorkspaceName);
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
