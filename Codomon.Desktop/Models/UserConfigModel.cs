namespace Codomon.Desktop.Models;

/// <summary>
/// Application-wide user preferences that are persisted independently of any workspace.
/// Stored in the user's application data directory as Codomon/config.json.
/// </summary>
public class UserConfigModel
{
    /// <summary>
    /// Default LLM API settings used across all workspaces.
    /// A workspace can still override these via its own <see cref="LlmSettingsModel"/>;
    /// these values are used as the initial seed when no workspace-level settings exist.
    /// </summary>
    public LlmSettingsModel DefaultLlmSettings { get; set; } = new();

    // ── Autosave ─────────────────────────────────────────────────────────────

    /// <summary>How often (in minutes) a workspace autosave is created. Minimum 1.</summary>
    public int AutosaveIntervalMinutes { get; set; } = 5;

    /// <summary>Maximum number of autosave snapshots to keep per workspace.</summary>
    public int MaxAutosaves { get; set; } = 10;

    // ── Recent workspaces ─────────────────────────────────────────────────────

    /// <summary>Maximum number of entries in the recent workspaces list.</summary>
    public int MaxRecentWorkspaces { get; set; } = 20;

    // ── Log replay ────────────────────────────────────────────────────────────

    /// <summary>Default playback speed selected when the replay toolbar is first shown.</summary>
    public double DefaultReplaySpeed { get; set; } = 1.0;

    // ── Log import defaults ───────────────────────────────────────────────────

    /// <summary>Key of the delimiter option pre-selected in the import wizard (e.g. "tab").</summary>
    public string DefaultImportDelimiterKey { get; set; } = "tab";

    /// <summary>Key of the timestamp-format option pre-selected in the import wizard (e.g. "auto").</summary>
    public string DefaultImportTimestampFormatKey { get; set; } = "auto";

    /// <summary>Time-zone ID pre-selected in the import wizard (e.g. "UTC").</summary>
    public string DefaultImportTimeZoneId { get; set; } = "UTC";

    /// <summary>Key of the known-app-format pre-selected in the import wizard (e.g. "none").</summary>
    public string DefaultImportKnownFormatKey { get; set; } = "none";
}
