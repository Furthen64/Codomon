using Codomon.Desktop.Services;

namespace Codomon.Tests;

/// <summary>
/// Unit tests for <see cref="SystemMapIdentity"/> — deterministic key generation
/// for System Map entities.  These methods are pure and have no UI or Avalonia
/// dependencies.
/// </summary>
public class SystemMapIdentityTests
{
    // ── CreateSystemKey ───────────────────────────────────────────────────────

    [Fact]
    public void CreateSystemKey_CombinesNameAndKindWithPipe()
    {
        var key = SystemMapIdentity.CreateSystemKey("Clearview.Desktop", "DesktopApp");
        Assert.Equal("clearview.desktop|desktopapp", key);
    }

    [Fact]
    public void CreateSystemKey_NormalisesCase()
    {
        var lower = SystemMapIdentity.CreateSystemKey("api", "webapi");
        var upper = SystemMapIdentity.CreateSystemKey("API", "WebApi");
        Assert.Equal(lower, upper);
    }

    [Fact]
    public void CreateSystemKey_TrimsWhitespace()
    {
        var key = SystemMapIdentity.CreateSystemKey("  Auth  ", "  Service  ");
        Assert.Equal("auth|service", key);
    }

    [Fact]
    public void CreateSystemKey_HandlesEmptyStrings()
    {
        var key = SystemMapIdentity.CreateSystemKey("", "");
        Assert.Equal("|", key);
    }

    // ── CreateModuleKey ───────────────────────────────────────────────────────

    [Fact]
    public void CreateModuleKey_ScopedUnderSystemKey()
    {
        var systemKey = SystemMapIdentity.CreateSystemKey("Api", "Web");
        var moduleKey = SystemMapIdentity.CreateModuleKey(systemKey, "AuthModule");
        Assert.Equal("api|web::authmodule", moduleKey);
    }

    [Fact]
    public void CreateModuleKey_NullSystemKeyProducesModuleKeyOnly()
    {
        var key = SystemMapIdentity.CreateModuleKey(null, "AuthModule");
        Assert.Equal("authmodule", key);
    }

    [Fact]
    public void CreateModuleKey_EmptySystemKeyProducesModuleKeyOnly()
    {
        var key = SystemMapIdentity.CreateModuleKey(string.Empty, "AuthModule");
        Assert.Equal("authmodule", key);
    }

    // ── CreateCodeNodeKey ─────────────────────────────────────────────────────

    [Fact]
    public void CreateCodeNodeKey_PrefersFullyQualifiedName()
    {
        var key = SystemMapIdentity.CreateCodeNodeKey(
            "MyApp.Auth.AuthService",
            projectPath: "MyApp.csproj",
            filePath: "Auth/AuthService.cs",
            name: "AuthService");
        Assert.Equal("myapp.auth.authservice", key);
    }

    [Fact]
    public void CreateCodeNodeKey_FallsBackToProjectAndFilePath()
    {
        var key = SystemMapIdentity.CreateCodeNodeKey(
            fullyQualifiedName: null,
            projectPath: "MyApp.csproj",
            filePath: "Auth/AuthService.cs",
            name: "AuthService");
        Assert.Equal("myapp.csproj::auth/authservice.cs::authservice", key);
    }

    // ── CreateRelationshipKey ─────────────────────────────────────────────────

    [Fact]
    public void CreateRelationshipKey_CombinesSourceTargetAndKind()
    {
        var key = SystemMapIdentity.CreateRelationshipKey("src|system", "tgt|system", "calls");
        Assert.Equal("src|system→tgt|system|calls", key);
    }

    [Fact]
    public void CreateRelationshipKey_NormalisesKind()
    {
        var lower = SystemMapIdentity.CreateRelationshipKey("a", "b", "calls");
        var upper = SystemMapIdentity.CreateRelationshipKey("a", "b", "Calls");
        Assert.Equal(lower, upper);
    }

    // ── NormalizeKeyPart ──────────────────────────────────────────────────────

    [Fact]
    public void NormalizeKeyPart_LowercasesTrimmedValue()
    {
        Assert.Equal("hello.world", SystemMapIdentity.NormalizeKeyPart("  Hello.World  "));
    }

    [Fact]
    public void NormalizeKeyPart_ReturnsEmptyForNullOrWhitespace()
    {
        Assert.Equal(string.Empty, SystemMapIdentity.NormalizeKeyPart(""));
        Assert.Equal(string.Empty, SystemMapIdentity.NormalizeKeyPart("   "));
    }

    // ── CreateExternalSystemKey ───────────────────────────────────────────────

    [Fact]
    public void CreateExternalSystemKey_MatchesSamePatternAsSystem()
    {
        var extKey = SystemMapIdentity.CreateExternalSystemKey("Stripe", "PaymentGateway");
        var sysKey = SystemMapIdentity.CreateSystemKey("Stripe", "PaymentGateway");
        Assert.Equal(extKey, sysKey);
    }

    // ── Determinism ───────────────────────────────────────────────────────────

    [Fact]
    public void Keys_AreDeterministicAcrossMultipleCalls()
    {
        var key1 = SystemMapIdentity.CreateSystemKey("My.Service", "Api");
        var key2 = SystemMapIdentity.CreateSystemKey("My.Service", "Api");
        Assert.Equal(key1, key2);
    }

    [Fact]
    public void Keys_DifferForDifferentInputs()
    {
        var key1 = SystemMapIdentity.CreateSystemKey("ServiceA", "Web");
        var key2 = SystemMapIdentity.CreateSystemKey("ServiceB", "Web");
        Assert.NotEqual(key1, key2);
    }
}
