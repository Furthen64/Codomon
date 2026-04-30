namespace Codomon.Desktop.Models;

/// <summary>
/// Application-wide user preferences that are persisted independently of any workspace.
/// Stored in %APPDATA%/Codomon/user_config.json.
/// </summary>
public class UserConfigModel
{
    /// <summary>
    /// Default LLM API settings used across all workspaces.
    /// A workspace can still override these via its own <see cref="LlmSettingsModel"/>;
    /// these values are used as the initial seed when no workspace-level settings exist.
    /// </summary>
    public LlmSettingsModel DefaultLlmSettings { get; set; } = new();
}
