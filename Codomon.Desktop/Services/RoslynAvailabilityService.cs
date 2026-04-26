namespace Codomon.Desktop.Services;

/// <summary>Result of a Roslyn availability preflight check.</summary>
public record RoslynAvailabilityResult(
    bool IsAvailable,
    string Message,
    int CsFileCount,
    string? DotnetVersion);

/// <summary>
/// Checks whether a Roslyn scan can be run against a given source path.
/// </summary>
public static class RoslynAvailabilityService
{
    /// <summary>
    /// Verifies that the source path exists and contains C# files.
    /// Also checks whether the <c>dotnet</c> CLI is available on the system PATH.
    /// </summary>
    public static async Task<RoslynAvailabilityResult> CheckAsync(string sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
            return new RoslynAvailabilityResult(false,
                "No source project path is configured for this workspace. " +
                "Set the source path via the workspace setup wizard.", 0, null);

        if (!Directory.Exists(sourcePath) && !File.Exists(sourcePath))
            return new RoslynAvailabilityResult(false,
                $"The source path does not exist:\n{sourcePath}", 0, null);

        // Resolve the root folder to search for .cs files.
        string searchRoot = Directory.Exists(sourcePath)
            ? sourcePath
            : Path.GetDirectoryName(sourcePath) ?? sourcePath;

        int csFileCount = await Task.Run(() =>
            Directory.EnumerateFiles(searchRoot, "*.cs", SearchOption.AllDirectories)
                .Count(f => !IsExcluded(f)));

        if (csFileCount == 0)
            return new RoslynAvailabilityResult(false,
                $"No C# source files (.cs) found under:\n{searchRoot}\n\n" +
                "Make sure the source path points to a C# project, solution, or folder.", 0, null);

        // Optionally detect dotnet CLI version (not required for file-based Roslyn analysis).
        string? dotnetVersion = await TryGetDotnetVersionAsync();

        return new RoslynAvailabilityResult(true,
            $"Found {csFileCount} C# source file{(csFileCount == 1 ? "" : "s")} ready to scan.",
            csFileCount, dotnetVersion);
    }

    private static async Task<string?> TryGetDotnetVersionAsync()
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("dotnet", "--version")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = System.Diagnostics.Process.Start(psi);
            if (proc == null) return null;

            var output = await proc.StandardOutput.ReadToEndAsync();
            await proc.WaitForExitAsync();
            return proc.ExitCode == 0 ? output.Trim() : null;
        }
        catch
        {
            return null;
        }
    }

    private static bool IsExcluded(string filePath)
    {
        // Skip generated code, obj/bin directories and designer files.
        var norm = filePath.Replace('\\', '/');
        return norm.Contains("/obj/") || norm.Contains("/bin/") ||
               norm.Contains("/.vs/") || norm.Contains("/node_modules/") ||
               norm.EndsWith(".Designer.cs", StringComparison.OrdinalIgnoreCase) ||
               norm.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase) ||
               norm.EndsWith(".g.i.cs", StringComparison.OrdinalIgnoreCase);
    }
}
