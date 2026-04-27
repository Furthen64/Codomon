using System.IO;
using System.Text.Json;

namespace Codomon.Desktop.Persistence;

/// <summary>Metadata for a single recently opened workspace entry.</summary>
public sealed class RecentWorkspaceEntry
{
    public string FolderPath { get; set; } = string.Empty;
    public string WorkspaceName { get; set; } = string.Empty;
    public DateTime LastOpened { get; set; }

    /// <summary>Last modification time of workspace.json on disk (UTC), or default if unavailable.</summary>
    public DateTime LastModified
    {
        get
        {
            var wsFile = Path.Combine(FolderPath, "workspace.json");
            return File.Exists(wsFile) ? File.GetLastWriteTimeUtc(wsFile) : default;
        }
    }
}

/// <summary>
/// Persists and retrieves the list of recently opened Codomon workspaces so the
/// welcome screen can show them on startup.
/// </summary>
public static class RecentWorkspacesService
{
    private const int MaxEntries = 20;

    private static string ConfigFilePath
    {
        get
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "Codomon", "recent_workspaces.json");
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Loads the stored list of recent workspace entries, newest-first.
    /// Returns an empty list if the file does not exist or cannot be read.
    /// </summary>
    public static List<RecentWorkspaceEntry> Load()
    {
        try
        {
            if (!File.Exists(ConfigFilePath))
                return new List<RecentWorkspaceEntry>();

            var json = File.ReadAllText(ConfigFilePath);
            return JsonSerializer.Deserialize<List<RecentWorkspaceEntry>>(json, JsonOptions)
                   ?? new List<RecentWorkspaceEntry>();
        }
        catch
        {
            return new List<RecentWorkspaceEntry>();
        }
    }

    /// <summary>
    /// Records <paramref name="folderPath"/> as recently opened (or updates its entry if it
    /// already exists) and saves the updated list to disk.
    /// </summary>
    public static void AddOrUpdate(string folderPath, string workspaceName)
    {
        if (string.IsNullOrWhiteSpace(folderPath)) return;

        var entries = Load();

        // Remove any existing entry for the same path so we can re-insert at the front.
        entries.RemoveAll(e => string.Equals(
            e.FolderPath, folderPath, StringComparison.OrdinalIgnoreCase));

        entries.Insert(0, new RecentWorkspaceEntry
        {
            FolderPath = folderPath,
            WorkspaceName = string.IsNullOrWhiteSpace(workspaceName) ? folderPath : workspaceName,
            LastOpened = DateTime.UtcNow
        });

        // Keep list bounded.
        if (entries.Count > MaxEntries)
            entries = entries.Take(MaxEntries).ToList();

        Save(entries);
    }

    /// <summary>Removes a stale entry (e.g. the workspace folder was deleted).</summary>
    public static void Remove(string folderPath)
    {
        var entries = Load();
        entries.RemoveAll(e => string.Equals(
            e.FolderPath, folderPath, StringComparison.OrdinalIgnoreCase));
        Save(entries);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static void Save(List<RecentWorkspaceEntry> entries)
    {
        try
        {
            var dir = Path.GetDirectoryName(ConfigFilePath)!;
            Directory.CreateDirectory(dir);
            File.WriteAllText(ConfigFilePath, JsonSerializer.Serialize(entries, JsonOptions));
        }
        catch
        {
            // Non-critical — silently ignore persistence errors.
        }
    }
}
