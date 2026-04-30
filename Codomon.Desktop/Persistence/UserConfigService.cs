using System.IO;
using System.Text.Json;
using Codomon.Desktop.Models;

namespace Codomon.Desktop.Persistence;

/// <summary>
/// Loads and saves the user-wide <see cref="UserConfigModel"/> to
/// <c>%APPDATA%/Codomon/user_config.json</c>.
/// </summary>
public static class UserConfigService
{
    private static string ConfigFilePath
    {
        get
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "Codomon", "user_config.json");
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Loads the user configuration from disk.
    /// Returns a default instance if the file does not exist or cannot be read.
    /// </summary>
    public static UserConfigModel Load()
    {
        try
        {
            if (!File.Exists(ConfigFilePath))
                return new UserConfigModel();

            var json = File.ReadAllText(ConfigFilePath);
            return JsonSerializer.Deserialize<UserConfigModel>(json, JsonOptions)
                   ?? new UserConfigModel();
        }
        catch
        {
            return new UserConfigModel();
        }
    }

    /// <summary>
    /// Persists <paramref name="config"/> to disk.
    /// Silently ignores IO errors so a save failure never crashes the application.
    /// </summary>
    public static void Save(UserConfigModel config)
    {
        try
        {
            var dir = Path.GetDirectoryName(ConfigFilePath)!;
            Directory.CreateDirectory(dir);
            File.WriteAllText(ConfigFilePath, JsonSerializer.Serialize(config, JsonOptions));
        }
        catch
        {
            // Non-critical — silently ignore persistence errors.
        }
    }
}
