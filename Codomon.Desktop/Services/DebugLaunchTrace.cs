using System;
using System.IO;
using System.Runtime.CompilerServices;

namespace Codomon.Desktop.Services;

public static class DebugLaunchTrace
{
    public const string CommandLineFlag = "--debug-launch";

    public static bool IsEnabled { get; private set; }

    public static void Enable()
    {
        IsEnabled = true;
        Write("Startup diagnostics enabled.");
    }

    public static void Write(
        string message,
        [CallerFilePath] string sourceFile = "",
        [CallerLineNumber] int sourceLine = 0)
    {
        if (!IsEnabled)
            return;

        Console.Error.WriteLine(
            $"[debuglaunch {DateTime.Now:HH:mm:ss.fff}] {Path.GetFileName(sourceFile)}:{sourceLine} | {message}");
    }

    public static void Exception(
        Exception exception,
        [CallerFilePath] string sourceFile = "",
        [CallerLineNumber] int sourceLine = 0)
    {
        if (!IsEnabled)
            return;

        Write($"UNHANDLED STARTUP EXCEPTION\n{exception}", sourceFile, sourceLine);
    }
}
