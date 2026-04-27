using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Codomon.Desktop.Persistence;

/// <summary>Metadata for a single autosave snapshot.</summary>
public sealed class AutosaveEntry
{
    public string Path { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public string DisplayName { get; set; } = string.Empty;
}

/// <summary>Creates, validates, prunes, and restores workspace autosave snapshots.</summary>
public static class AutosaveService
{
    private const string AutosavePrefix = "codomon_as_";
    private const string AutosavesFolder = "autosaves";
    private const string HashFileName = ".wshash";
    private const string ProfilesFolder = "profiles";
    private const int MaxAutosaves = 10;
    private const string TimestampFormat = "yyyyMMdd_HHmmssffff";

    /// <summary>Root JSON files in the workspace folder that are included in every autosave.</summary>
    private static readonly string[] WorkspaceRootFiles =
    {
        "workspace.json", "systems.json", "modules.json", "connections.json", ".wsversion"
    };

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a timestamped autosave snapshot inside <paramref name="workspaceFolderPath"/>/autosaves/,
    /// writes a .wshash integrity file, and prunes autosaves beyond <see cref="MaxAutosaves"/>.
    /// </summary>
    /// <returns>The path of the newly created autosave folder.</returns>
    public static async Task<string> CreateAutosaveAsync(string workspaceFolderPath)
    {
        var timestamp = DateTime.Now.ToString(TimestampFormat, CultureInfo.InvariantCulture);
        var autosaveName = $"{AutosavePrefix}{timestamp}";
        var autosavesRoot = System.IO.Path.Combine(workspaceFolderPath, AutosavesFolder);
        var autosavePath = System.IO.Path.Combine(autosavesRoot, autosaveName);

        Directory.CreateDirectory(autosavePath);

        // Copy workspace root files.
        foreach (var fileName in WorkspaceRootFiles)
        {
            var src = System.IO.Path.Combine(workspaceFolderPath, fileName);
            if (File.Exists(src))
                File.Copy(src, System.IO.Path.Combine(autosavePath, fileName), overwrite: true);
        }

        // Copy profiles/ folder.
        var profilesSrc = System.IO.Path.Combine(workspaceFolderPath, ProfilesFolder);
        if (Directory.Exists(profilesSrc))
            CopyDirectory(profilesSrc, System.IO.Path.Combine(autosavePath, ProfilesFolder));

        // Write integrity hash.
        var hash = await ComputeHashAsync(autosavePath);
        await File.WriteAllTextAsync(System.IO.Path.Combine(autosavePath, HashFileName), hash);

        PruneAutosaves(workspaceFolderPath);

        return autosavePath;
    }

    /// <summary>
    /// Returns true when the .wshash stored in <paramref name="autosavePath"/> matches
    /// the hash recomputed from the autosave contents.
    /// </summary>
    public static async Task<bool> ValidateAutosaveAsync(string autosavePath)
    {
        var hashFilePath = System.IO.Path.Combine(autosavePath, HashFileName);
        if (!File.Exists(hashFilePath))
            return false;

        var storedHash = (await File.ReadAllTextAsync(hashFilePath)).Trim();
        var computedHash = await ComputeHashAsync(autosavePath);
        return string.Equals(storedHash, computedHash, StringComparison.Ordinal);
    }

    /// <summary>
    /// Copies all files from <paramref name="autosavePath"/> back into <paramref name="workspaceFolderPath"/>,
    /// overwriting existing files. The .wshash file is excluded.
    /// </summary>
    public static Task RestoreAutosaveAsync(string autosavePath, string workspaceFolderPath)
    {
        foreach (var file in Directory.EnumerateFiles(autosavePath, "*", SearchOption.AllDirectories))
        {
            var relativePath = System.IO.Path.GetRelativePath(autosavePath, file);

            // Exclude .wshash at any directory level.
            if (System.IO.Path.GetFileName(relativePath) == HashFileName)
                continue;

            var destPath = System.IO.Path.Combine(workspaceFolderPath, relativePath);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(destPath)!);
            File.Copy(file, destPath, overwrite: true);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Returns all autosave entries for <paramref name="workspaceFolderPath"/>,
    /// sorted newest first.
    /// </summary>
    public static List<AutosaveEntry> GetAutosaveEntries(string workspaceFolderPath)
    {
        var autosavesRoot = System.IO.Path.Combine(workspaceFolderPath, AutosavesFolder);
        if (!Directory.Exists(autosavesRoot))
            return new List<AutosaveEntry>();

        return Directory
            .EnumerateDirectories(autosavesRoot, $"{AutosavePrefix}*")
            .Select(d =>
            {
                var name = System.IO.Path.GetFileName(d);
                var dateStr = name.Length > AutosavePrefix.Length
                    ? name[AutosavePrefix.Length..]
                    : string.Empty;

                DateTime.TryParseExact(
                    dateStr, TimestampFormat,
                    CultureInfo.InvariantCulture, DateTimeStyles.None,
                    out var dt);

                var displayName = dt == default
                    ? name
                    : dt.ToString("yyyy-MM-dd  HH:mm:ss", CultureInfo.InvariantCulture);

                return new AutosaveEntry { Path = d, Timestamp = dt, DisplayName = displayName };
            })
            .OrderByDescending(e => e.Timestamp)
            .ToList();
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static void PruneAutosaves(string workspaceFolderPath)
    {
        var entries = GetAutosaveEntries(workspaceFolderPath);
        foreach (var old in entries.Skip(MaxAutosaves))
            Directory.Delete(old.Path, recursive: true);
    }

    private static async Task<string> ComputeHashAsync(string autosavePath)
    {
        var relativePaths = Directory
            .EnumerateFiles(autosavePath, "*", SearchOption.AllDirectories)
            .Select(f => System.IO.Path.GetRelativePath(autosavePath, f)
                             .Replace('\\', '/'))
            .Where(r => System.IO.Path.GetFileName(r) != HashFileName)
            .OrderBy(r => r, StringComparer.Ordinal)
            .ToList();

        using var combinedStream = new MemoryStream();

        foreach (var relativePath in relativePaths)
        {
            var pathBytes = Encoding.UTF8.GetBytes(relativePath);
            await combinedStream.WriteAsync(pathBytes);

            var fileBytes = await File.ReadAllBytesAsync(
                System.IO.Path.Combine(autosavePath, relativePath.Replace('/', System.IO.Path.DirectorySeparatorChar)));
            await combinedStream.WriteAsync(fileBytes);
        }

        combinedStream.Position = 0;
        var hashBytes = await SHA256.HashDataAsync(combinedStream);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    private static void CopyDirectory(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (var file in Directory.EnumerateFiles(src))
            File.Copy(file, System.IO.Path.Combine(dst, System.IO.Path.GetFileName(file)), overwrite: true);
        foreach (var dir in Directory.EnumerateDirectories(src))
            CopyDirectory(dir, System.IO.Path.Combine(dst, System.IO.Path.GetFileName(dir)));
    }
}
