using Codomon.Desktop.Models;
using Codomon.Desktop.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Codomon.Desktop.ViewModels;

/// <summary>
/// ViewModel for the Roslyn scan dialog.
/// Manages the preflight check, background scan, results browsing, and connection promotion.
/// </summary>
public class RoslynScanViewModel : INotifyPropertyChanged
{
    private readonly string _sourcePath;
    private readonly string _workspaceFolderPath;
    private readonly WorkspaceModel? _workspace;

    private ScanDialogStep _step = ScanDialogStep.Preflight;
    private bool _isRunning;
    private bool _scanFinished;
    private string _preflightMessage = string.Empty;
    private bool _preflightOk;
    private int _csFileCount;
    private string? _dotnetVersion;
    private RoslynScanResult? _scanResult;
    private SuggestedConnection? _selectedSuggestedConnection;
    private ScannedFile? _selectedFile;
    private ScannedClass? _selectedClass;

    private bool _wasAddedToCanvas;

    private CancellationTokenSource? _cts;

    public RoslynScanViewModel(string sourcePath, string workspaceFolderPath, WorkspaceModel? workspace = null)
    {
        _sourcePath = sourcePath;
        _workspaceFolderPath = workspaceFolderPath;
        _workspace = workspace;
    }

    /// <summary>The source path being scanned.</summary>
    public string SourcePath => _sourcePath;

    // ── Step ──────────────────────────────────────────────────────────────────

    public ScanDialogStep Step
    {
        get => _step;
        private set { _step = value; OnPropertyChanged(); }
    }

    // ── Preflight ─────────────────────────────────────────────────────────────

    public string PreflightMessage
    {
        get => _preflightMessage;
        private set { _preflightMessage = value; OnPropertyChanged(); }
    }

    public bool PreflightOk
    {
        get => _preflightOk;
        private set { _preflightOk = value; OnPropertyChanged(); }
    }

    public int CsFileCount => _csFileCount;

    public string? DotnetVersion => _dotnetVersion;

    // ── Scan progress ─────────────────────────────────────────────────────────

    /// <summary>Progress messages emitted by the scan service.</summary>
    public ObservableCollection<string> ProgressMessages { get; } = new();

    public bool IsRunning
    {
        get => _isRunning;
        private set { _isRunning = value; OnPropertyChanged(); }
    }

    public bool ScanFinished
    {
        get => _scanFinished;
        private set { _scanFinished = value; OnPropertyChanged(); }
    }

    // ── Results ───────────────────────────────────────────────────────────────

    public RoslynScanResult? ScanResult
    {
        get => _scanResult;
        private set { _scanResult = value; OnPropertyChanged(); }
    }

    public SuggestedConnection? SelectedSuggestedConnection
    {
        get => _selectedSuggestedConnection;
        set { _selectedSuggestedConnection = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanPromote)); }
    }

    public ScannedFile? SelectedFile
    {
        get => _selectedFile;
        set { _selectedFile = value; OnPropertyChanged(); SelectedClass = null; }
    }

    public ScannedClass? SelectedClass
    {
        get => _selectedClass;
        set { _selectedClass = value; OnPropertyChanged(); }
    }

    /// <summary>True when a not-yet-promoted suggested connection is selected.</summary>
    public bool CanPromote =>
        _selectedSuggestedConnection is { IsPromoted: false };

    /// <summary>Connections that have already been promoted to the workspace.</summary>
    public ObservableCollection<ConnectionModel> PromotedConnections { get; } = new();

    // ── Add all to canvas ─────────────────────────────────────────────────────

    /// <summary>True after <see cref="AddAllToCanvas"/> has been called successfully.</summary>
    public bool WasAddedToCanvas
    {
        get => _wasAddedToCanvas;
        private set { _wasAddedToCanvas = value; OnPropertyChanged(); }
    }

    // ── Commands ──────────────────────────────────────────────────────────────

    /// <summary>Runs the preflight availability check.</summary>
    public async Task RunPreflightAsync()
    {
        IsRunning = true;
        PreflightMessage = "Checking…";
        PreflightOk = false;

        try
        {
            var result = await RoslynAvailabilityService.CheckAsync(_sourcePath);
            _csFileCount = result.CsFileCount;
            _dotnetVersion = result.DotnetVersion;
            PreflightMessage = result.Message;
            PreflightOk = result.IsAvailable;
        }
        catch (Exception ex)
        {
            PreflightMessage = $"Preflight check failed: {ex.Message}";
            PreflightOk = false;
        }
        finally
        {
            IsRunning = false;
        }
    }

    /// <summary>Transitions to the scan step and begins scanning in the background.</summary>
    public async Task StartScanAsync()
    {
        Step = ScanDialogStep.Scanning;
        IsRunning = true;
        ScanFinished = false;
        ProgressMessages.Clear();

        _cts = new CancellationTokenSource();
        var progress = new Progress<string>(msg =>
            Avalonia.Threading.Dispatcher.UIThread.Post(() => ProgressMessages.Add(msg)));

        try
        {
            var scanResult = await RoslynScanService.ScanAsync(_sourcePath, progress, _cts.Token);
            ScanResult = scanResult;

            // Persist results to the workspace scans/ folder.
            var savedPath = await RoslynScanService.SaveAsync(scanResult, _workspaceFolderPath);
            ProgressMessages.Add($"Scan results saved to: {Path.GetFileName(savedPath)}");

            ScanFinished = true;
        }
        catch (OperationCanceledException)
        {
            ProgressMessages.Add("Scan was cancelled.");
        }
        catch (Exception ex)
        {
            ProgressMessages.Add($"Scan error: {ex.Message}");
        }
        finally
        {
            IsRunning = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    /// <summary>Cancels an in-progress scan.</summary>
    public void CancelScan()
    {
        _cts?.Cancel();
    }

    /// <summary>Transitions to the results browsing step.</summary>
    public void ShowResults()
    {
        Step = ScanDialogStep.Results;
    }

    /// <summary>
    /// Promotes <paramref name="suggestion"/> to a <see cref="ConnectionModel"/> with
    /// <see cref="ConnectionOrigin.Roslyn"/> and <c>IsReadOnly = true</c>.
    /// Returns the new connection so the caller can add it to the workspace.
    /// When a workspace was supplied at construction time, attempts to resolve
    /// <c>FromId</c> and <c>ToId</c> by matching the short class name against
    /// workspace system and module names.
    /// </summary>
    public ConnectionModel? PromoteConnection(SuggestedConnection suggestion)
    {
        if (suggestion.IsPromoted) return null;

        suggestion.IsPromoted = true;

        var fromId = ResolveWorkspaceId(suggestion.FromClass) ?? string.Empty;
        var toId   = ResolveWorkspaceId(suggestion.ToClass)   ?? string.Empty;

        AppLogger.Debug($"[Promote] '{ShortName(suggestion.FromClass)}' → '{ShortName(suggestion.ToClass)}'  " +
                        $"FromId='{(string.IsNullOrEmpty(fromId) ? "<unresolved>" : fromId)}'  " +
                        $"ToId='{(string.IsNullOrEmpty(toId) ? "<unresolved>" : toId)}'");

        if (string.IsNullOrEmpty(fromId))
            AppLogger.Warn($"[Promote] Could not resolve workspace node for FromClass='{suggestion.FromClass}'. " +
                           $"Connection will be saved but won't appear on canvas until both ends are linked to a workspace system.");
        if (string.IsNullOrEmpty(toId))
            AppLogger.Warn($"[Promote] Could not resolve workspace node for ToClass='{suggestion.ToClass}'. " +
                           $"Connection will be saved but won't appear on canvas until both ends are linked to a workspace system.");

        var connection = new ConnectionModel
        {
            Id = Guid.NewGuid().ToString(),
            Name = $"{ShortName(suggestion.FromClass)} → {ShortName(suggestion.ToClass)}",
            Type = "Roslyn",
            Notes = $"Auto-generated from Roslyn scan. {suggestion.CallCount} call site(s).\n" +
                    $"From: {suggestion.FromClass}\nTo: {suggestion.ToClass}",
            FromId = fromId,
            ToId   = toId,
            Origin = ConnectionOrigin.Roslyn,
            IsReadOnly = true
        };

        PromotedConnections.Add(connection);
        AppLogger.Debug($"[Promote] PromotedConnections count is now {PromotedConnections.Count}. Close the dialog to apply to the canvas.");

        if (ScanResult != null)
            ScanResult.PromotedConnectionIds.Add(connection.Id);

        OnPropertyChanged(nameof(CanPromote));
        return connection;
    }

    /// <summary>
    /// Signals that the scan result should be imported into the System Map.
    /// The caller (<c>MainWindow</c>) is responsible for running
    /// <c>SystemDetector</c> and calling <c>ApplyRoslynScanAsync</c>.
    /// Sets <see cref="WasAddedToCanvas"/> to <c>true</c> so the dialog can
    /// return the result to the caller.
    /// </summary>
    public void AddAllToCanvas()
    {
        if (_scanResult == null) return;
        WasAddedToCanvas = true;
        AppLogger.Debug("[AddAllToCanvas] Flagged WasAddedToCanvas=true. Caller will run SystemDetector.");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string ShortName(string fullName)
    {
        var lastDot = fullName.LastIndexOf('.');
        return lastDot >= 0 ? fullName[(lastDot + 1)..] : fullName;
    }

    /// <summary>
    /// Tries to find a workspace system or module whose name matches the short name
    /// derived from <paramref name="className"/>. Returns the matching ID, or
    /// <c>null</c> when no match is found or no workspace was provided.
    /// </summary>
    private string? ResolveWorkspaceId(string className)
    {
        if (_workspace == null) return null;
        var shortName = ShortName(className);

        // Try systems first.
        var sys = _workspace.Systems.FirstOrDefault(s =>
            string.Equals(s.Name, shortName, StringComparison.OrdinalIgnoreCase));
        if (sys != null) return sys.Id;

        // Then try modules across all systems.
        foreach (var s in _workspace.Systems)
        {
            var mod = s.Modules.FirstOrDefault(m =>
                string.Equals(m.Name, shortName, StringComparison.OrdinalIgnoreCase));
            if (mod != null) return mod.Id;
        }

        return null;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>Steps in the Roslyn scan dialog.</summary>
public enum ScanDialogStep
{
    Preflight,
    Scanning,
    Results
}
