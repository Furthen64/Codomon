namespace Codomon.Desktop.Services;

/// <summary>
/// Watches a text log file for newly appended lines using <see cref="FileSystemWatcher"/>
/// and raises <see cref="LinesAvailable"/> on a thread-pool thread each time new content
/// is detected.  Existing file content is skipped on start; only lines written
/// <em>after</em> <see cref="Start"/> are reported.
/// </summary>
public sealed class LogWatcherService : IDisposable
{
    private FileSystemWatcher? _watcher;
    private string _filePath = string.Empty;
    private long _lastPosition;

    // Prevents concurrent reads when FileSystemWatcher fires multiple events.
    private readonly SemaphoreSlim _readGate = new(1, 1);

    // ── Events ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Raised on a thread-pool thread when new lines are appended to the watched file.
    /// The list is non-empty.
    /// </summary>
    public event Action<IReadOnlyList<string>>? LinesAvailable;

    /// <summary>
    /// Raised when the <see cref="FileSystemWatcher"/> encounters an internal error or the
    /// watched file becomes unreadable.  After this event the watcher is stopped automatically.
    /// </summary>
    public event Action<string>? Error;

    // ── State ─────────────────────────────────────────────────────────────────

    /// <summary>True while a file is actively being watched.</summary>
    public bool IsWatching => _watcher != null;

    /// <summary>Absolute path of the file currently being watched, or empty string.</summary>
    public string WatchedFilePath => _filePath;

    // ── API ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// Starts watching <paramref name="filePath"/> for appended lines.
    /// Any previously watched file is stopped first.
    /// Throws <see cref="ArgumentException"/> when the path is empty or
    /// <see cref="FileNotFoundException"/> when the file does not exist.
    /// </summary>
    public void Start(string filePath)
    {
        Stop();

        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("File path must not be empty.", nameof(filePath));

        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Log file not found: {filePath}", filePath);

        _filePath = filePath;

        // Skip existing content — only report lines written after this point.
        _lastPosition = new FileInfo(filePath).Length;

        var dir  = Path.GetDirectoryName(filePath)!;
        var name = Path.GetFileName(filePath);

        _watcher = new FileSystemWatcher(dir, name)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
            EnableRaisingEvents = true
        };

        _watcher.Changed += OnFileChanged;
        _watcher.Error   += OnWatcherError;
    }

    /// <summary>Stops watching the current file.  Safe to call when not watching.</summary>
    public void Stop()
    {
        if (_watcher == null) return;

        _watcher.EnableRaisingEvents = false;
        _watcher.Changed -= OnFileChanged;
        _watcher.Error   -= OnWatcherError;
        _watcher.Dispose();
        _watcher      = null;
        _filePath     = string.Empty;
        _lastPosition = 0;
    }

    // ── FileSystemWatcher callbacks ───────────────────────────────────────────

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        // Skip if another read is already in flight.
        if (!_readGate.Wait(0)) return;
        try
        {
            ReadNewLines();
        }
        finally
        {
            _readGate.Release();
        }
    }

    private void ReadNewLines()
    {
        if (string.IsNullOrEmpty(_filePath)) return;

        string[] newLines;
        try
        {
            using var stream = new FileStream(
                _filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);

            // If the file was truncated / rotated, restart from the beginning.
            if (stream.Length < _lastPosition)
                _lastPosition = 0;

            if (stream.Length == _lastPosition) return;

            stream.Seek(_lastPosition, SeekOrigin.Begin);

            using var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true,
                                                bufferSize: 16384, leaveOpen: true);
            var lines = new List<string>();
            string? line;
            while ((line = reader.ReadLine()) != null)
                lines.Add(line);

            // Advance our position to the end of what we just read.
            _lastPosition = stream.Position;
            newLines = lines.ToArray();
        }
        catch (Exception ex)
        {
            Error?.Invoke($"Failed to read log file: {ex.Message}");
            return;
        }

        if (newLines.Length > 0)
            LinesAvailable?.Invoke(newLines);
    }

    private void OnWatcherError(object sender, ErrorEventArgs e)
    {
        var message = e.GetException()?.Message ?? "Unknown error";
        Error?.Invoke($"File watcher error: {message}");
        Stop();
    }

    // ── IDisposable ───────────────────────────────────────────────────────────

    public void Dispose()
    {
        Stop();
        _readGate.Dispose();
    }
}
