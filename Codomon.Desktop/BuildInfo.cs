using System.Linq;
using System.Reflection;

namespace Codomon.Desktop;

/// <summary>
/// Build-time constants injected by build.sh via MSBuild assembly metadata.
/// When running from an IDE without build.sh, fallback values are used.
/// </summary>
public static class BuildInfo
{
    private static string GetMetadata(string key, string fallback)
    {
        return typeof(BuildInfo).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == key)?.Value ?? fallback;
    }

    /// <summary>Application version, e.g. "0.1.0".</summary>
    public static readonly string AppVersion = GetMetadata("AppVersion", "0.1.0-dev");

    /// <summary>Build date in ISO 8601 format (YYYY-MM-DD), e.g. "2026-04-26".</summary>
    public static readonly string BuildDate = GetMetadata("BuildDate", "dev");
}
