using Codomon.Desktop.Models;
using Codomon.Desktop.Persistence;

namespace Codomon.Desktop.Services;

/// <summary>
/// Resolves the effective LLM settings used by runtime features.
/// Endpoint/model now come from user settings; legacy workspace copies are ignored.
/// Workspace-level numeric tuning values still override user defaults when they differ
/// from the built-in defaults.
/// </summary>
public static class LlmSettingsResolver
{
    public static LlmSettingsModel ResolveEffectiveSettings(
        WorkspaceModel? workspace,
        UserConfigModel? userConfig = null)
    {
        workspace ??= new WorkspaceModel();
        userConfig ??= UserConfigService.Load();

        var defaults = new LlmSettingsModel();
        var workspaceSettings = workspace.LlmSettings ?? new LlmSettingsModel();
        var userDefaults = userConfig.DefaultLlmSettings ?? new LlmSettingsModel();

        return new LlmSettingsModel
        {
            ApiEndpoint = userDefaults.ApiEndpoint,
            ModelName = userDefaults.ModelName,
            SummaryMaxOutputTokens = workspaceSettings.SummaryMaxOutputTokens != defaults.SummaryMaxOutputTokens
                ? workspaceSettings.SummaryMaxOutputTokens
                : userDefaults.SummaryMaxOutputTokens,
            SummaryQueueSize = workspaceSettings.SummaryQueueSize != defaults.SummaryQueueSize
                ? workspaceSettings.SummaryQueueSize
                : userDefaults.SummaryQueueSize,
            HypothesisTokenThreshold = workspaceSettings.HypothesisTokenThreshold != defaults.HypothesisTokenThreshold
                ? workspaceSettings.HypothesisTokenThreshold
                : userDefaults.HypothesisTokenThreshold
        };
    }
}
