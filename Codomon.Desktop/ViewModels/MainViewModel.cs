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
        _logReplay   = new LogReplayViewModel(_workspace);
        _timeline    = new TimelineViewModel();
        _liveMonitor = new LiveMonitorViewModel();
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
            _logReplay.Dispose();
            _logReplay = new LogReplayViewModel(value);
            OnPropertyChanged(nameof(LogReplay));
            // Stop live monitoring and create a fresh VM for the new workspace.
            _liveMonitor.Stop();
            _liveMonitor.Dispose();
            _liveMonitor = new LiveMonitorViewModel();
            OnPropertyChanged(nameof(LiveMonitor));
            // Re-create the timeline VM; existing data is stale for the new workspace.
            _timeline = new TimelineViewModel();
            OnPropertyChanged(nameof(Timeline));
            // Reload the System Map from the new workspace.
            SystemMap.LoadFrom(value.SystemMap);
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

    // ── System Map ─────────────────────────────────────────────────────────

    /// <summary>
    /// View-model for the System Map views (System Overview, Module View,
    /// Code Detail View, Startup View).  Reloaded whenever the workspace changes.
    /// </summary>
    public SystemMapViewModel SystemMap { get; } = new SystemMapViewModel();

    // ── Log Replay ────────────────────────────────────────────────────────────

    private LogReplayViewModel _logReplay;

    /// <summary>The replay controller for imported log files.</summary>
    public LogReplayViewModel LogReplay => _logReplay;

    private TimelineViewModel _timeline;

    /// <summary>Aggregated day-timeline built from imported log entries.</summary>
    public TimelineViewModel Timeline => _timeline;

    // ── Live Monitor ──────────────────────────────────────────────────────────

    private LiveMonitorViewModel _liveMonitor;

    /// <summary>The live log monitoring controller.</summary>
    public LiveMonitorViewModel LiveMonitor => _liveMonitor;

    /// <summary>
    /// Imports a log file from <paramref name="sourcePath"/> into the workspace
    /// <c>logs/imported/</c> folder and loads its entries into <see cref="LogReplay"/>.
    /// </summary>
    public async Task ImportLogsAsync(string sourcePath)
    {
        if (string.IsNullOrEmpty(WorkspaceFolderPath))
            throw new InvalidOperationException("No workspace is open. Please open or create a workspace first.");

        // Stop live monitoring before an import so both sources don't write to the same list.
        _liveMonitor.Stop();

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

        // Stop live monitoring before an import so both sources don't write to the same list.
        _liveMonitor.Stop();

        var destPath = await LogImportService.CopyToWorkspaceAsync(sourcePath, WorkspaceFolderPath);
        var entries  = await LogImportService.LoadEntriesWithOptionsAsync(destPath, options);
        _logReplay.LoadEntries(entries);

        StatusMessage = $"Imported {entries.Count} log entries from {Path.GetFileName(destPath)}";
        AppLogger.Info($"Log imported (wizard): {destPath} ({entries.Count} entries)");
    }

    /// <summary>
    /// Starts live monitoring of <paramref name="filePath"/>.
    /// The last-browsed folder and watched paths list are updated in the workspace model.
    /// </summary>
    public void StartLiveMonitoring(string filePath)
    {
        if (string.IsNullOrEmpty(filePath)) return;

        // Remember the folder for the next file-picker open.
        var folder = Path.GetDirectoryName(filePath) ?? string.Empty;
        if (!string.IsNullOrEmpty(folder))
            Workspace.LastBrowsedFolder = folder;

        // Add the path to the workspace's configured paths list (no duplicates).
        if (!Workspace.WatchedLogPaths.Contains(filePath, StringComparer.OrdinalIgnoreCase))
            Workspace.WatchedLogPaths.Add(filePath);

        _liveMonitor.Start(filePath);

        IsDirty       = true;
        StatusMessage = $"Watching: {Path.GetFileName(filePath)}";
        AppLogger.Info($"Live monitoring started: {filePath}");
    }

    /// <summary>Stops live monitoring if currently active.</summary>
    public void StopLiveMonitoring()
    {
        if (!_liveMonitor.IsWatching) return;
        _liveMonitor.Stop();
        StatusMessage = "Live monitoring stopped.";
        AppLogger.Info("Live monitoring stopped.");
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
        HasWorkspace = true;
        Workspace = workspace;
        WorkspaceFolderPath = folderPath;
        IsDirty = false;
        ClearSelection();
        StatusMessage = $"New workspace created: {folderPath}";
        RecentWorkspacesService.AddOrUpdate(folderPath, workspace.WorkspaceName, Math.Max(1, UserConfigService.Load().MaxRecentWorkspaces));
        StartAutosaveTimer();
        await TryCreateAutosaveAsync();
    }

    public async Task OpenWorkspaceAsync(string folderPath)
    {
        var workspace = await WorkspaceSerializer.LoadAsync(folderPath);
        HasWorkspace = true;
        Workspace = workspace;
        WorkspaceFolderPath = folderPath;
        IsDirty = false;
        ClearSelection();
        StatusMessage = $"Opened: {folderPath}";
        RecentWorkspacesService.AddOrUpdate(folderPath, workspace.WorkspaceName, Math.Max(1, UserConfigService.Load().MaxRecentWorkspaces));
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

        var intervalMinutes = Math.Max(1, UserConfigService.Load().AutosaveIntervalMinutes);
        _autosaveTimer = new System.Timers.Timer(TimeSpan.FromMinutes(intervalMinutes).TotalMilliseconds)
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
            var maxAutosaves = Math.Max(1, UserConfigService.Load().MaxAutosaves);
            await AutosaveService.CreateAutosaveAsync(WorkspaceFolderPath, maxAutosaves);
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

    // ── Roslyn scan integration ───────────────────────────────────────────────

    /// <summary>
    /// Adds Roslyn-origin connections (promoted from a scan) to the workspace connection list.
    /// The connections are marked read-only and their Origin is set to
    /// <see cref="ConnectionOrigin.Roslyn"/>.
    /// </summary>
    public void AddRoslynConnections(IEnumerable<Models.ConnectionModel> connections)
    {
        // Materialise once to avoid double-enumeration of deferred LINQ queries.
        var connectionList = connections.ToList();

        foreach (var conn in connectionList)
        {
            // Avoid duplicates by ID.
            if (Workspace.Connections.Any(c => c.Id == conn.Id)) continue;
            Workspace.Connections.Add(conn);
        }

        IsDirty = true;
        StatusMessage = "Roslyn connections added to workspace.";
        AppLogger.Info($"Roslyn scan: added {connectionList.Count} connection(s).");
    }

    /// <summary>
    /// Promotes a Roslyn-origin connection to a manual connection by clearing
    /// the read-only flag and changing its origin to <see cref="ConnectionOrigin.Manual"/>.
    /// </summary>
    public void PromoteConnectionToManual(string connectionId)
    {
        var conn = Workspace.Connections.FirstOrDefault(c => c.Id == connectionId);
        if (conn == null) return;

        conn.IsReadOnly = false;
        conn.Origin = Models.ConnectionOrigin.Manual;

        IsDirty = true;
        StatusMessage = $"Connection '{conn.Name}' promoted to manual.";
        AppLogger.Info($"Roslyn connection promoted to manual: {conn.Id}");
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
