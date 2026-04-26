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

    private CancellationTokenSource? _cts;

    public RoslynScanViewModel(string sourcePath, string workspaceFolderPath)
    {
        _sourcePath = sourcePath;
        _workspaceFolderPath = workspaceFolderPath;
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
    /// </summary>
    public ConnectionModel? PromoteConnection(SuggestedConnection suggestion)
    {
        if (suggestion.IsPromoted) return null;

        suggestion.IsPromoted = true;

        var connection = new ConnectionModel
        {
            Id = Guid.NewGuid().ToString(),
            Name = $"{ShortName(suggestion.FromClass)} → {ShortName(suggestion.ToClass)}",
            Type = "Roslyn",
            Notes = $"Auto-generated from Roslyn scan. {suggestion.CallCount} call site(s).\n" +
                    $"From: {suggestion.FromClass}\nTo: {suggestion.ToClass}",
            FromId = string.Empty,
            ToId = string.Empty,
            Origin = ConnectionOrigin.Roslyn,
            IsReadOnly = true
        };

        PromotedConnections.Add(connection);

        if (ScanResult != null)
            ScanResult.PromotedConnectionIds.Add(connection.Id);

        OnPropertyChanged(nameof(CanPromote));
        return connection;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string ShortName(string fullName)
    {
        var lastDot = fullName.LastIndexOf('.');
        return lastDot >= 0 ? fullName[(lastDot + 1)..] : fullName;
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
