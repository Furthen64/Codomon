using Codomon.Desktop.Models;
using Codomon.Desktop.Services;

namespace Codomon.Tests;

/// <summary>
/// Unit tests for <see cref="LogMatcher"/> — name-based and rule-based matching
/// of log entries against workspace topology.  These rely only on pure models
/// and have no UI or Avalonia dependencies.
/// </summary>
public class LogMatcherTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static WorkspaceModel BuildWorkspace(
        string systemName,
        string? moduleName = null)
    {
        var workspace = new WorkspaceModel();
        var sys = new SystemBoxModel { Id = "sys-1", Name = systemName };
        if (moduleName != null)
        {
            sys.Modules.Add(new ModuleBoxModel { Id = "mod-1", Name = moduleName });
        }
        workspace.Systems.Add(sys);
        return workspace;
    }

    private static LogEntryModel Entry(string source = "", string message = "") =>
        new() { Source = source, Message = message };

    // ── No match ─────────────────────────────────────────────────────────────

    [Fact]
    public void Match_EmptySourceAndMessage_ReturnsNoMatch()
    {
        var workspace = BuildWorkspace("PaymentService");
        var result = LogMatcher.Match(Entry(), workspace);
        Assert.Equal(MatchStrength.None, result.Strength);
        Assert.Null(result.System);
        Assert.Null(result.Module);
    }

    [Fact]
    public void Match_UnrelatedSource_ReturnsNoMatch()
    {
        var workspace = BuildWorkspace("PaymentService");
        var result = LogMatcher.Match(Entry("com.company.unrelated.SomeClass"), workspace);
        Assert.Equal(MatchStrength.None, result.Strength);
    }

    // ── Automatic source-based matching ──────────────────────────────────────

    [Fact]
    public void Match_SystemNameInSource_ReturnsSystemOnlyMatch()
    {
        var workspace = BuildWorkspace("OrderService");
        var result = LogMatcher.Match(Entry(source: "com.company.OrderService.Handler"), workspace);
        Assert.Equal(MatchStrength.SystemOnly, result.Strength);
        Assert.Equal("OrderService", result.System?.Name);
        Assert.Null(result.Module);
    }

    [Fact]
    public void Match_ModuleNameInSource_ReturnsModuleExactMatch()
    {
        var workspace = BuildWorkspace("OrderService", "PaymentModule");
        var result = LogMatcher.Match(Entry(source: "OrderService.PaymentModule.Processor"), workspace);
        Assert.Equal(MatchStrength.ModuleExact, result.Strength);
        Assert.Equal("PaymentModule", result.Module?.Name);
    }

    // ── Automatic message-based matching (fallback) ───────────────────────────

    [Fact]
    public void Match_SystemNameInMessage_ReturnsSystemOnlyMatch()
    {
        var workspace = BuildWorkspace("BillingService");
        var result = LogMatcher.Match(
            Entry(source: "com.company.unrelated", message: "Error in BillingService"),
            workspace);
        Assert.Equal(MatchStrength.SystemOnly, result.Strength);
        Assert.Equal("BillingService", result.System?.Name);
    }

    [Fact]
    public void Match_ModuleNameInMessage_ReturnsModuleExactMatch()
    {
        var workspace = BuildWorkspace("OrderService", "InvoiceModule");
        var result = LogMatcher.Match(
            Entry(source: "com.unrelated", message: "InvoiceModule raised an exception"),
            workspace);
        Assert.Equal(MatchStrength.ModuleExact, result.Strength);
        Assert.Equal("InvoiceModule", result.Module?.Name);
    }

    // ── Source-based match takes priority over message-based ─────────────────

    [Fact]
    public void Match_ModuleInSourceAndSystemInMessage_PrefersSourceMatch()
    {
        var workspace = BuildWorkspace("OrderService", "PaymentModule");
        var result = LogMatcher.Match(
            Entry(source: "OrderService.PaymentModule", message: "Error in OrderService"),
            workspace);
        // Module match via source should win over system match via message.
        Assert.Equal(MatchStrength.ModuleExact, result.Strength);
    }

    // ── Empty workspace ───────────────────────────────────────────────────────

    [Fact]
    public void Match_EmptyWorkspace_ReturnsNoMatch()
    {
        var workspace = new WorkspaceModel();
        var result = LogMatcher.Match(Entry("SomeService.Handler"), workspace);
        Assert.Equal(MatchStrength.None, result.Strength);
    }

    // ── Match reason text ─────────────────────────────────────────────────────

    [Fact]
    public void Match_ModuleMatch_ReasonContainsModuleName()
    {
        var workspace = BuildWorkspace("OrderService", "PaymentModule");
        var result = LogMatcher.Match(Entry("OrderService.PaymentModule.x"), workspace);
        Assert.Equal(MatchStrength.ModuleExact, result.Strength);
        Assert.Contains("PaymentModule", result.MatchReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Match_SystemMatch_ReasonContainsSystemName()
    {
        var workspace = BuildWorkspace("AuthService");
        var result = LogMatcher.Match(Entry("com.company.AuthService.Login"), workspace);
        Assert.Equal(MatchStrength.SystemOnly, result.Strength);
        Assert.Contains("AuthService", result.MatchReason, StringComparison.OrdinalIgnoreCase);
    }

    // ── Case sensitivity ──────────────────────────────────────────────────────

    [Fact]
    public void Match_IsCaseInsensitiveForSourceComparison()
    {
        var workspace = BuildWorkspace("OrderService");
        var result = LogMatcher.Match(Entry("ORDERSERVICE.handler"), workspace);
        // The name-based matching uses Contains with case-insensitive comparison.
        // Expect SystemOnly at minimum.
        Assert.NotEqual(MatchStrength.None, result.Strength);
    }
}
