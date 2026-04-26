using Avalonia.Threading;
using Codomon.Desktop.Models;
using Codomon.Desktop.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Codomon.Desktop.ViewModels;

/// <summary>
/// Drives log-replay state: loading imported entries, stepping through them at a
/// configurable speed, and raising <see cref="EntryActivated"/> for each replayed line
/// so the canvas can apply highlights.
/// </summary>
public class LogReplayViewModel : INotifyPropertyChanged
{
    private readonly WorkspaceModel _workspace;
    private DispatcherTimer? _timer;
    private int _currentIndex = -1;
    private bool _isPlaying;
    private double _speedMultiplier = 1.0;

    /// <summary>All entries loaded from the most recently imported log file.</summary>
    public ObservableCollection<LogEntryModel> Entries { get; } = new();

    public int CurrentIndex
    {
        get => _currentIndex;
        private set
        {
            _currentIndex = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CurrentEntry));
        }
    }

    public LogEntryModel? CurrentEntry =>
        _currentIndex >= 0 && _currentIndex < Entries.Count
            ? Entries[_currentIndex]
            : null;

    public bool IsPlaying
    {
        get => _isPlaying;
        private set { _isPlaying = value; OnPropertyChanged(); }
    }

    /// <summary>Replay speed multiplier (0.25x – 8x). Default 1x ≈ 200 ms / entry.</summary>
    public double SpeedMultiplier
    {
        get => _speedMultiplier;
        set
        {
            _speedMultiplier = Math.Clamp(value, 0.25, 8.0);
            OnPropertyChanged();
            UpdateTimerInterval();
        }
    }

    /// <summary>
    /// Raised on the UI thread each time an entry is activated during replay.
    /// Listeners can use <see cref="LogMatcher"/> to translate the entry into
    /// canvas highlights.
    /// </summary>
    public event Action<LogEntryModel>? EntryActivated;

    public LogReplayViewModel(WorkspaceModel workspace)
    {
        _workspace = workspace;
    }

    // ── Data loading ──────────────────────────────────────────────────────────

    /// <summary>
    /// Replaces the current entry list.  Any active replay is stopped first.
    /// </summary>
    public void LoadEntries(IEnumerable<LogEntryModel> entries)
    {
        Stop();
        Entries.Clear();
        foreach (var e in entries)
            Entries.Add(e);
        CurrentIndex = -1;
    }

    // ── Replay controls ───────────────────────────────────────────────────────

    public void Play()
    {
        if (Entries.Count == 0) return;

        // If replay finished, restart from the beginning.
        if (_currentIndex >= Entries.Count - 1)
            CurrentIndex = -1;

        IsPlaying = true;

        if (_timer == null)
        {
            _timer = new DispatcherTimer();
            _timer.Tick += OnTimerTick;
        }

        UpdateTimerInterval();
        _timer.Start();
    }

    public void Pause()
    {
        _timer?.Stop();
        IsPlaying = false;
    }

    /// <summary>Stop replay and reset position to the beginning.</summary>
    public void Stop()
    {
        _timer?.Stop();
        IsPlaying = false;
        CurrentIndex = -1;
    }

    // ── Timer ─────────────────────────────────────────────────────────────────

    private void OnTimerTick(object? sender, EventArgs e)
    {
        if (_currentIndex >= Entries.Count - 1)
        {
            Stop();
            return;
        }

        CurrentIndex++;
        EntryActivated?.Invoke(Entries[_currentIndex]);
    }

    private void UpdateTimerInterval()
    {
        if (_timer == null) return;
        // Base interval: 200 ms at 1×; halved per doubling of speed.
        var ms = Math.Max(50, (int)(200.0 / _speedMultiplier));
        _timer.Interval = TimeSpan.FromMilliseconds(ms);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
