using Codomon.Desktop.Persistence;
using Codomon.Desktop.Services;

namespace Codomon.Tests;

public sealed class QualityFixRegressionTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(
        Path.GetTempPath(),
        "codomon-tests",
        Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
            Directory.Delete(_tempDirectory, recursive: true);
    }

    [Fact]
    public void Parse_WhenLineIsNull_ReturnsSafeUnparsedEntry()
    {
        var entry = LogParser.Parse(null!);

        Assert.False(entry.IsParsed);
        Assert.Equal(string.Empty, entry.RawLine);
        Assert.Equal(string.Empty, entry.Formatted);
    }

    [Fact]
    public void ParseDelimited_WhenLineIsNull_ReturnsSafeUnparsedEntry()
    {
        var entry = LogParser.ParseDelimited(null!, new ImportOptions());

        Assert.False(entry.IsParsed);
        Assert.Equal(string.Empty, entry.RawLine);
        Assert.Equal(string.Empty, entry.Formatted);
    }

    [Fact]
    public async Task LoadPromptTemplateAsync_WhenPromptFileIsMissing_CreatesDefaultPrompt()
    {
        var prompt = await LlmSummaryService.LoadPromptTemplateAsync(_tempDirectory);

        Assert.Equal(WorkspaceSerializer.GetDefaultSummaryPrompt(), prompt);
        Assert.True(File.Exists(Path.Combine(_tempDirectory, "summary_prompt.md")));
    }

    [Fact]
    public async Task SavePromptTemplateAsync_WhenWorkspaceFolderIsMissing_CreatesIt()
    {
        const string content = "custom prompt";

        await LlmSummaryService.SavePromptTemplateAsync(_tempDirectory, content);

        Assert.Equal(content, await File.ReadAllTextAsync(Path.Combine(_tempDirectory, "summary_prompt.md")));
    }
}
