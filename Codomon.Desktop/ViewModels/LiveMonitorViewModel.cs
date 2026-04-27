using Avalonia.Threading;
using Codomon.Desktop.Models;
using Codomon.Desktop.Services;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Codomon.Desktop.ViewModels;

/// <summary>
/// Manages live log file monitoring.  Starts/stops a <see cref="LogWatcherService"/>,
/// batches incoming lines into 100 ms UI-thread ticks to keep the UI responsive at up to
/// 500 events per second, and raises <see cref="EntryArrived"/> for each new entry.
/// </summary>
public sealed class LiveMonitorViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly LogWatcherService _watcher = new();
    private readonly ConcurrentQueue<LogEntryModel> _pending = new();
    private readonly DispatcherTimer _flushTimer;

    /// <summary>Maximum entries processed per 100 ms tick (keeps the UI thread responsive).</summary>
    private const int MaxEntriesPerTick = 200;

    private bool   _isWatching;
    private string _watchedFilePath = string.Empty;
    private string _errorMessage    = string.Empty;

    // ── Public state ──────────────────────────────────────────────────────────

    /// <summary>All log entries received since the last <see cref="Start"/> call.</summary>
    public ObservableCollection<LogEntryModel> Entries { get; } = new();

    /// <summary>True while a file is actively being monitored.</summary>
    public bool IsWatching
    {
        get => _isWatching;
        private set { _isWatching = value; OnPropertyChanged(); }
    }

    /// <summary>Absolute path of the file currently being watched, or empty string.</summary>
    public string WatchedFilePath
    {
        get => _watchedFilePath;
        private set { _watchedFilePath = value; OnPropertyChanged(); }
    }

    /// <summary>Last error message from the underlying watcher, or empty string.</summary>
    public string ErrorMessage
    {
        get => _errorMessage;
        private set { _errorMessage = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasError)); }
    }

    /// <summary>True when <see cref="ErrorMessage"/> is non-empty.</summary>
    public bool HasError => !string.IsNullOrEmpty(_errorMessage);

    // ── Events ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Raised on the UI thread for each log entry that arrives during live monitoring.
    /// Subscribers use <see cref="LogMatcher"/> to translate the entry into canvas highlights.
    /// </summary>
    public event Action<LogEntryModel>? EntryArrived;

    /// <summary>
    /// Raised on the UI thread after each flush batch when at least one entry was processed.
    /// Subscribers may use this to trigger a throttled timeline rebuild.
    /// </summary>
    public event Action? EntriesFlushed;

    // ── Constructor ───────────────────────────────────────────────────────────

    public LiveMonitorViewModel()
    {
        _watcher.LinesAvailable += OnLinesAvailable;
        _watcher.Error          += OnWatcherError;

        _flushTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _flushTimer.Tick += OnFlushTick;
    }

    // ── API ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// Clears prior entries and begins watching <paramref name="filePath"/> for new lines.
    /// Throws if the file path is invalid or the file does not exist.
    /// </summary>
    public void Start(string filePath)
    {
        ErrorMessage = string.Empty;
        Entries.Clear();
        while (_pending.TryDequeue(out _)) { }

        _watcher.Start(filePath);
        WatchedFilePath = filePath;
        IsWatching      = true;
        _flushTimer.Start();
    }

    /// <summary>Stops monitoring.  Safe to call when not currently watching.</summary>
    public void Stop()
    {
        _watcher.Stop();
        _flushTimer.Stop();
        IsWatching = false;
    }

    // ── Callbacks from background thread ─────────────────────────────────────

    private void OnLinesAvailable(IReadOnlyList<string> lines)
    {
        foreach (var line in lines)
            _pending.Enqueue(LogParser.Parse(line));
    }

    private void OnWatcherError(string message)
    {
        // ErrorMessage and state must be set on the UI thread.
        Dispatcher.UIThread.Post(() =>
        {
            ErrorMessage = message;
            IsWatching   = false;
            _flushTimer.Stop();
        });
    }

    // ── UI-thread flush ───────────────────────────────────────────────────────

    private void OnFlushTick(object? sender, EventArgs e)
    {
        int processed = 0;
        while (processed < MaxEntriesPerTick && _pending.TryDequeue(out var entry))
        {
            Entries.Add(entry);
            EntryArrived?.Invoke(entry);
            processed++;
        }

        if (processed > 0)
            EntriesFlushed?.Invoke();
    }

    // ── INotifyPropertyChanged ────────────────────────────────────────────────

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    // ── IDisposable ───────────────────────────────────────────────────────────

    public void Dispose()
    {
        _flushTimer.Stop();
        _watcher.LinesAvailable -= OnLinesAvailable;
        _watcher.Error          -= OnWatcherError;
        _watcher.Dispose();
    }
}
