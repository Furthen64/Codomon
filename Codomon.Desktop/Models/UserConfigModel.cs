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

    /// <summary>Whether the Dev Console should auto-open after a workspace is available.</summary>
    public bool AutoStartDevConsole { get; set; } = false;

    // ── Graph auto-align defaults ───────────────────────────────────────────

    /// <summary>
    /// Last-used graph auto-align preset and tuning values from the right-side panel.
    /// </summary>
    public GraphAutoAlignSettingsModel GraphAutoAlignSettings { get; set; } = new();

    /// <summary>Whether the main window should start maximized.</summary>
    public bool StartMaximized { get; set; } = false;

    // ── LLM Summaries dialog layout ───────────────────────────────────────────

    /// <summary>Last-used width for the LLM Summaries window.</summary>
    public double LlmSummaryWindowWidth { get; set; } = 960;

    /// <summary>Last-used height for the LLM Summaries window.</summary>
    public double LlmSummaryWindowHeight { get; set; } = 720;

    /// <summary>Last-used left pane width in the LLM Summaries Generate tab split view.</summary>
    public double LlmSummaryGenerateLeftPaneWidth { get; set; } = 300;

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

/// <summary>
/// User-level defaults for the Graph tab auto-align controls.
/// </summary>
public class GraphAutoAlignSettingsModel
{
    /// <summary>Selected preset key: balanced, dense, or separated.</summary>
    public string PresetKey { get; set; } = "balanced";

    /// <summary>Barycentric ordering sweeps per pass.</summary>
    public int BarycentricSweeps { get; set; } = 6;

    /// <summary>Whether to run a second refinement pass.</summary>
    public bool RunTwoPassRefinement { get; set; }

    /// <summary>Horizontal distance between columns.</summary>
    public double ColumnGap { get; set; } = 280;

    /// <summary>Base vertical distance between nodes.</summary>
    public double BaseRowGap { get; set; } = 96;

    /// <summary>Vertical gap between disconnected components.</summary>
    public double ComponentGap { get; set; } = 180;
}
