using Codomon.Desktop.Models;

namespace Codomon.Desktop.Services;

/// <summary>
/// Copies an external log file into the workspace <c>logs/imported/</c> folder and
/// parses it into a list of <see cref="LogEntryModel"/> objects.
/// </summary>
public static class LogImportService
{
    private const string ImportedSubPath = "logs/imported";

    /// <summary>
    /// Copies <paramref name="sourcePath"/> into the workspace imported-logs folder,
    /// adding a timestamp suffix when a file with the same name already exists.
    /// Returns the destination path.
    /// </summary>
    public static async Task<string> CopyToWorkspaceAsync(string sourcePath, string workspaceFolderPath)
    {
        var destDir = Path.Combine(workspaceFolderPath, ImportedSubPath);
        Directory.CreateDirectory(destDir);

        var fileName    = Path.GetFileName(sourcePath);
        var destPath    = Path.Combine(destDir, fileName);

        // Avoid overwriting an existing file — append a timestamp suffix instead.
        if (File.Exists(destPath) &&
            !string.Equals(sourcePath, destPath, StringComparison.OrdinalIgnoreCase))
        {
            var ts  = DateTime.Now.ToString("yyyyMMdd_HHmmssffff");
            var stem = Path.GetFileNameWithoutExtension(fileName);
            var ext  = Path.GetExtension(fileName);
            destPath = Path.Combine(destDir, $"{stem}_{ts}{ext}");
        }

        if (!string.Equals(sourcePath, destPath, StringComparison.OrdinalIgnoreCase))
            await Task.Run(() => File.Copy(sourcePath, destPath, overwrite: false));

        return destPath;
    }

    /// <summary>Reads a log file and returns one <see cref="LogEntryModel"/> per line.</summary>
    public static async Task<List<LogEntryModel>> LoadEntriesAsync(string filePath)
    {
        var lines = await File.ReadAllLinesAsync(filePath);
        return lines.Select(LogParser.Parse).ToList();
    }

    /// <summary>
    /// Reads a delimiter-separated log file and parses each line using
    /// <paramref name="options"/> (custom delimiter, timestamp column, format, and timezone).
    /// </summary>
    public static async Task<List<LogEntryModel>> LoadEntriesWithOptionsAsync(
        string filePath, ImportOptions options)
    {
        var lines = await File.ReadAllLinesAsync(filePath);
        return lines.Select(l => LogParser.ParseDelimited(l, options)).ToList();
    }
}
