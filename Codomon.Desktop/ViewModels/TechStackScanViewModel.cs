using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Codomon.Desktop.Models;
using Codomon.Desktop.Services;

namespace Codomon.Desktop.ViewModels;

/// <summary>
/// View model for the tech stack scan dialog.
/// Manages preflight, background scan, saved-scan restore, and result browsing.
/// </summary>
public class TechStackScanViewModel : INotifyPropertyChanged
{
    private readonly string _sourcePath;
    private readonly string _workspaceFolderPath;

    private TechStackScanDialogStep _step = TechStackScanDialogStep.Preflight;
    private bool _isRunning;
    private bool _scanFinished;
    private string _preflightMessage = string.Empty;
    private bool _preflightOk;
    private int _projectCount;
    private int _markerCount;
    private TechStackScanResult? _scanResult;
    private DetectedTechnology? _selectedTechnology;
    private bool _isRestoredScanLoaded;
    private string _restoredScanLabel = string.Empty;

    private CancellationTokenSource? _cts;

    public TechStackScanViewModel(string sourcePath, string workspaceFolderPath)
    {
        _sourcePath = sourcePath;
        _workspaceFolderPath = workspaceFolderPath;
    }

    public string SourcePath => _sourcePath;

    public TechStackScanDialogStep Step
    {
        get => _step;
        private set { _step = value; OnPropertyChanged(); }
    }

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

    public int ProjectCount => _projectCount;

    public int MarkerCount => _markerCount;

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

    public TechStackScanResult? ScanResult
    {
        get => _scanResult;
        private set { _scanResult = value; OnPropertyChanged(); OnPropertyChanged(nameof(CategoryNames)); OnPropertyChanged(nameof(ProjectNames)); }
    }

    public bool IsRestoredScanLoaded
    {
        get => _isRestoredScanLoaded;
        private set { _isRestoredScanLoaded = value; OnPropertyChanged(); }
    }

    public string RestoredScanLabel
    {
        get => _restoredScanLabel;
        private set { _restoredScanLabel = value; OnPropertyChanged(); }
    }

    public DetectedTechnology? SelectedTechnology
    {
        get => _selectedTechnology;
        set { _selectedTechnology = value; OnPropertyChanged(); }
    }

    public IReadOnlyList<string> CategoryNames =>
        ScanResult?.Technologies
            .Select(t => t.Category)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList()
        ?? new List<string>();

    public IReadOnlyList<string> ProjectNames =>
        ScanResult?.Technologies
            .Select(t => string.IsNullOrWhiteSpace(t.ProjectName) ? "(workspace)" : t.ProjectName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList()
        ?? new List<string>();

    public async Task RunPreflightAsync()
    {
        IsRunning = true;
        PreflightMessage = "Checking…";
        PreflightOk = false;

        try
        {
            var result = await TechStackScanService.CheckAsync(_sourcePath);
            _projectCount = result.ProjectCount;
            _markerCount = result.DetectionMarkerCount;
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

    public async Task StartScanAsync()
    {
        ClearRestoredScanInfo();
        Step = TechStackScanDialogStep.Scanning;
        IsRunning = true;
        ScanFinished = false;
        ProgressMessages.Clear();

        _cts = new CancellationTokenSource();
        var progress = new Progress<string>(msg =>
            Avalonia.Threading.Dispatcher.UIThread.Post(() => ProgressMessages.Add(msg)));

        try
        {
            var scanResult = await TechStackScanService.ScanAsync(_sourcePath, progress, _cts.Token);
            ScanResult = scanResult;

            var savedPath = await TechStackScanService.SaveAsync(scanResult, _workspaceFolderPath);
            ProgressMessages.Add($"Tech stack results saved to: {Path.GetFileName(savedPath)}");

            ScanFinished = true;
        }
        catch (OperationCanceledException)
        {
            ProgressMessages.Add("Tech stack scan was cancelled.");
        }
        catch (Exception ex)
        {
            ProgressMessages.Add($"Tech stack scan error: {ex.Message}");
        }
        finally
        {
            IsRunning = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    public async Task<bool> TryRestoreLatestSavedScanAsync()
    {
        try
        {
            var saved = TechStackScanService.ListSavedScans(_workspaceFolderPath);
            if (saved.Count == 0)
                return false;

            var latest = saved[0];
            var restored = await TechStackScanService.LoadAsync(latest.FilePath);
            if (restored == null)
                return false;

            ScanResult = restored;
            ScanFinished = true;
            ProgressMessages.Clear();
            ProgressMessages.Add($"Loaded saved tech stack scan: {Path.GetFileName(latest.FilePath)}");
            SetRestoredScanInfo(Path.GetFileName(latest.FilePath), restored.ScanTime == default ? latest.ScanTime : restored.ScanTime);
            Step = TechStackScanDialogStep.Results;
            return true;
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"[TechStack] Failed to restore latest saved scan: {ex.Message}");
            return false;
        }
    }

    public void CancelScan() => _cts?.Cancel();

    public void ShowResults() => Step = TechStackScanDialogStep.Results;

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private void SetRestoredScanInfo(string fileName, DateTime scanTimeUtc)
    {
        IsRestoredScanLoaded = true;
        RestoredScanLabel = $"Loaded saved tech stack scan from {scanTimeUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss} ({fileName}).";
    }

    private void ClearRestoredScanInfo()
    {
        IsRestoredScanLoaded = false;
        RestoredScanLabel = string.Empty;
    }
}

public enum TechStackScanDialogStep
{
    Preflight,
    Scanning,
    Results
}
