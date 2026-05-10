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
        var ws  = new WorkspaceModel();
        var sys = new SystemBoxModel { Id = "sys-1", Name = systemName };
        if (moduleName != null)
        {
            sys.Modules.Add(new ModuleBoxModel { Id = "mod-1", Name = moduleName });
        }
        ws.Systems.Add(sys);
        return ws;
    }

    private static LogEntryModel Entry(string source = "", string message = "") =>
        new() { Source = source, Message = message };

    // ── No match ─────────────────────────────────────────────────────────────

    [Fact]
    public void Match_EmptySourceAndMessage_ReturnsNoMatch()
    {
        var ws     = BuildWorkspace("PaymentService");
        var result = LogMatcher.Match(Entry(), ws);
        Assert.Equal(MatchStrength.None, result.Strength);
        Assert.Null(result.System);
        Assert.Null(result.Module);
    }

    [Fact]
    public void Match_UnrelatedSource_ReturnsNoMatch()
    {
        var ws     = BuildWorkspace("PaymentService");
        var result = LogMatcher.Match(Entry("com.company.unrelated.SomeClass"), ws);
        Assert.Equal(MatchStrength.None, result.Strength);
    }

    // ── Automatic source-based matching ──────────────────────────────────────

    [Fact]
    public void Match_SystemNameInSource_ReturnsSystemOnlyMatch()
    {
        var ws     = BuildWorkspace("OrderService");
        var result = LogMatcher.Match(Entry(source: "com.company.OrderService.Handler"), ws);
        Assert.Equal(MatchStrength.SystemOnly, result.Strength);
        Assert.Equal("OrderService", result.System?.Name);
        Assert.Null(result.Module);
    }

    [Fact]
    public void Match_ModuleNameInSource_ReturnsModuleExactMatch()
    {
        var ws     = BuildWorkspace("OrderService", "PaymentModule");
        var result = LogMatcher.Match(Entry(source: "OrderService.PaymentModule.Processor"), ws);
        Assert.Equal(MatchStrength.ModuleExact, result.Strength);
        Assert.Equal("PaymentModule", result.Module?.Name);
    }

    // ── Automatic message-based matching (fallback) ───────────────────────────

    [Fact]
    public void Match_SystemNameInMessage_ReturnsSystemOnlyMatch()
    {
        var ws     = BuildWorkspace("BillingService");
        var result = LogMatcher.Match(
            Entry(source: "com.company.unrelated", message: "Error in BillingService"),
            ws);
        Assert.Equal(MatchStrength.SystemOnly, result.Strength);
        Assert.Equal("BillingService", result.System?.Name);
    }

    [Fact]
    public void Match_ModuleNameInMessage_ReturnsModuleExactMatch()
    {
        var ws     = BuildWorkspace("OrderService", "InvoiceModule");
        var result = LogMatcher.Match(
            Entry(source: "com.unrelated", message: "InvoiceModule raised an exception"),
            ws);
        Assert.Equal(MatchStrength.ModuleExact, result.Strength);
        Assert.Equal("InvoiceModule", result.Module?.Name);
    }

    // ── Source-based match takes priority over message-based ─────────────────

    [Fact]
    public void Match_ModuleInSourceAndSystemInMessage_PrefersSourceMatch()
    {
        var ws     = BuildWorkspace("OrderService", "PaymentModule");
        var result = LogMatcher.Match(
            Entry(source: "OrderService.PaymentModule", message: "Error in OrderService"),
            ws);
        // Module match via source should win over system match via message.
        Assert.Equal(MatchStrength.ModuleExact, result.Strength);
    }

    // ── Empty workspace ───────────────────────────────────────────────────────

    [Fact]
    public void Match_EmptyWorkspace_ReturnsNoMatch()
    {
        var ws     = new WorkspaceModel();
        var result = LogMatcher.Match(Entry("SomeService.Handler"), ws);
        Assert.Equal(MatchStrength.None, result.Strength);
    }

    // ── Match reason text ─────────────────────────────────────────────────────

    [Fact]
    public void Match_ModuleMatch_ReasonContainsModuleName()
    {
        var ws     = BuildWorkspace("OrderService", "PaymentModule");
        var result = LogMatcher.Match(Entry("OrderService.PaymentModule.x"), ws);
        Assert.Equal(MatchStrength.ModuleExact, result.Strength);
        Assert.Contains("PaymentModule", result.MatchReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Match_SystemMatch_ReasonContainsSystemName()
    {
        var ws     = BuildWorkspace("AuthService");
        var result = LogMatcher.Match(Entry("com.company.AuthService.Login"), ws);
        Assert.Equal(MatchStrength.SystemOnly, result.Strength);
        Assert.Contains("AuthService", result.MatchReason, StringComparison.OrdinalIgnoreCase);
    }

    // ── Case sensitivity ──────────────────────────────────────────────────────

    [Fact]
    public void Match_IsCaseInsensitiveForSourceComparison()
    {
        var ws     = BuildWorkspace("OrderService");
        var result = LogMatcher.Match(Entry("ORDERSERVICE.handler"), ws);
        // The name-based matching uses Contains with case-insensitive comparison.
        // Expect SystemOnly at minimum.
        Assert.NotEqual(MatchStrength.None, result.Strength);
    }
}
