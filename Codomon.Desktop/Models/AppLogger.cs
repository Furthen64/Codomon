using System;
using System.Collections.ObjectModel;
using Avalonia.Threading;

namespace Codomon.Desktop.Models;

/// <summary>Simple in-process log sink. Shared between the main window and the Dev Console.</summary>
public static class AppLogger
{
    public static ObservableCollection<LogEntry> Entries { get; } = new();

    public static void Info(string message) => Append("INFO", message);
    public static void Debug(string message) => Append("DEBUG", message);
    public static void Warn(string message) => Append("WARN", message);
    public static void Error(string message) => Append("ERROR", message);

    private static void Append(string level, string message)
    {
        var entry = new LogEntry(DateTime.Now, level, message);
        Dispatcher.UIThread.Post(() => Entries.Add(entry));
    }
}

public record LogEntry(DateTime Timestamp, string Level, string Message)
{
    public string Formatted => $"[{Timestamp:HH:mm:ss}] [{Level,-5}]  {Message}";

    public string LevelColor => Level switch
    {
        "ERROR" => "#FF6666",
        "WARN"  => "#FFCC66",
        "DEBUG" => "#AAAAAA",
        _       => "#88CCAA"   // INFO
    };
}
