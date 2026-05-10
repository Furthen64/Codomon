using Codomon.Desktop.Models;

namespace Codomon.Tests;

/// <summary>
/// Unit tests for <see cref="LogEntryModel"/> — pure model logic with no UI or
/// Avalonia dependencies.
/// </summary>
public class LogEntryModelTests
{
    // ── LevelColor ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("ERROR", "#FF6666")]
    [InlineData("error", "#FF6666")]
    [InlineData("Error", "#FF6666")]
    [InlineData("WARN",  "#FFCC66")]
    [InlineData("warn",  "#FFCC66")]
    [InlineData("DEBUG", "#AAAAAA")]
    [InlineData("TRACE", "#888888")]
    [InlineData("INFO",  "#88CCAA")]
    [InlineData("info",  "#88CCAA")]
    [InlineData("",      "#88CCAA")]   // unknown → default
    [InlineData("FATAL", "#88CCAA")]   // unknown → default
    public void LevelColor_ReturnsCorrectColorForLevel(string level, string expected)
    {
        var entry = new LogEntryModel { Level = level };
        Assert.Equal(expected, entry.LevelColor);
    }

    // ── Formatted ─────────────────────────────────────────────────────────────

    [Fact]
    public void Formatted_WhenNotParsed_ReturnsRawLine()
    {
        var entry = new LogEntryModel
        {
            IsParsed = false,
            RawLine  = "raw log line here"
        };
        Assert.Equal("raw log line here", entry.Formatted);
    }

    [Fact]
    public void Formatted_WhenParsed_IncludesLevelAndMessage()
    {
        var timestamp = new DateTimeOffset(2024, 1, 15, 10, 30, 45, 123, TimeSpan.Zero);
        var entry = new LogEntryModel
        {
            IsParsed  = true,
            Timestamp = timestamp,
            Level     = "INFO",
            Source    = "AuthService",
            Message   = "User logged in"
        };

        var formatted = entry.Formatted;
        Assert.Contains("[10:30:45.123]", formatted);
        Assert.Contains("INFO", formatted);
        Assert.Contains("AuthService", formatted);
        Assert.Contains("User logged in", formatted);
    }

    [Fact]
    public void Formatted_WhenParsedWithNoSource_OmitsSourcePrefix()
    {
        var entry = new LogEntryModel
        {
            IsParsed  = true,
            Timestamp = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
            Level     = "DEBUG",
            Source    = string.Empty,
            Message   = "Some message"
        };

        var formatted = entry.Formatted;
        // Should not have ": " separator when source is empty.
        Assert.DoesNotContain(": Some message", formatted);
        Assert.Contains("Some message", formatted);
    }

    [Fact]
    public void Formatted_WhenParsedWithNoTimestamp_UsesFallback()
    {
        var entry = new LogEntryModel
        {
            IsParsed  = true,
            Timestamp = null,
            Level     = "INFO",
            Message   = "No timestamp"
        };

        Assert.Contains("??:??:??", entry.Formatted);
    }

    [Fact]
    public void Formatted_WhenParsedWithNoLevel_UsesFallback()
    {
        var entry = new LogEntryModel
        {
            IsParsed  = true,
            Timestamp = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
            Level     = string.Empty,
            Message   = "no level"
        };

        Assert.Contains("[?", entry.Formatted);
    }
}
