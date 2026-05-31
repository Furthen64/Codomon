using Codomon.Desktop.Models;
using Codomon.Desktop.Services;

namespace Codomon.Tests;

public class LlmSettingsResolverTests
{
    [Fact]
    public void ResolveEffectiveSettings_UsesUserEndpointAndModel_WhenWorkspaceHasLegacyValues()
    {
        var workspace = new WorkspaceModel
        {
            LlmSettings = new LlmSettingsModel
            {
                ApiEndpoint = "http://localhost:8079/v1",
                ModelName = "legacy-model"
            }
        };
        var userConfig = new UserConfigModel
        {
            DefaultLlmSettings = new LlmSettingsModel
            {
                ApiEndpoint = "http://localhost:8080/v1",
                ModelName = "new-model"
            }
        };

        var resolved = LlmSettingsResolver.ResolveEffectiveSettings(workspace, userConfig);

        Assert.Equal("http://localhost:8080/v1", resolved.ApiEndpoint);
        Assert.Equal("new-model", resolved.ModelName);
    }

    [Fact]
    public void ResolveEffectiveSettings_PreservesWorkspaceTuning_WhenDifferentFromBuiltInDefaults()
    {
        var workspace = new WorkspaceModel
        {
            LlmSettings = new LlmSettingsModel
            {
                SummaryQueueSize = 3,
                SummaryMaxOutputTokens = 1024,
                HypothesisTokenThreshold = 90_000
            }
        };
        var userConfig = new UserConfigModel
        {
            DefaultLlmSettings = new LlmSettingsModel
            {
                ApiEndpoint = "http://localhost:8080/v1",
                ModelName = "new-model",
                SummaryQueueSize = 1,
                SummaryMaxOutputTokens = 512,
                HypothesisTokenThreshold = 60_000
            }
        };

        var resolved = LlmSettingsResolver.ResolveEffectiveSettings(workspace, userConfig);

        Assert.Equal(3, resolved.SummaryQueueSize);
        Assert.Equal(1024, resolved.SummaryMaxOutputTokens);
        Assert.Equal(90_000, resolved.HypothesisTokenThreshold);
    }
}
