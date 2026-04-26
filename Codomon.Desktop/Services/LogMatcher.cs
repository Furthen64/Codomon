using Codomon.Desktop.Models;

namespace Codomon.Desktop.Services;

public enum MatchStrength { None, SystemOnly, ModuleExact }

/// <summary>Result of matching a log entry against the workspace topology.</summary>
public record MatchResult(MatchStrength Strength, SystemBoxModel? System, ModuleBoxModel? Module);

/// <summary>
/// Matches a <see cref="LogEntryModel"/> against the Systems and Modules defined in a
/// <see cref="WorkspaceModel"/>. Priority order mirrors the Phase 06 spec:
/// <list type="number">
///   <item>Module name contained in entry Source</item>
///   <item>System name contained in entry Source</item>
///   <item>Module name contained in entry Message</item>
///   <item>System name contained in entry Message</item>
/// </list>
/// </summary>
public static class LogMatcher
{
    public static MatchResult Match(LogEntryModel entry, WorkspaceModel workspace)
    {
        // 1 + 2: source-based matching (most precise)
        if (!string.IsNullOrEmpty(entry.Source))
        {
            foreach (var sys in workspace.Systems)
            {
                foreach (var mod in sys.Modules)
                {
                    if (ContainsName(entry.Source, mod.Name))
                        return new MatchResult(MatchStrength.ModuleExact, sys, mod);
                }
            }

            foreach (var sys in workspace.Systems)
            {
                if (ContainsName(entry.Source, sys.Name))
                    return new MatchResult(MatchStrength.SystemOnly, sys, null);
            }
        }

        // 3 + 4: message keyword matching (fallback)
        if (!string.IsNullOrEmpty(entry.Message))
        {
            foreach (var sys in workspace.Systems)
            {
                foreach (var mod in sys.Modules)
                {
                    if (ContainsName(entry.Message, mod.Name))
                        return new MatchResult(MatchStrength.ModuleExact, sys, mod);
                }
            }

            foreach (var sys in workspace.Systems)
            {
                if (ContainsName(entry.Message, sys.Name))
                    return new MatchResult(MatchStrength.SystemOnly, sys, null);
            }
        }

        return new MatchResult(MatchStrength.None, null, null);
    }

    private static bool ContainsName(string text, string name)
    {
        // Require at least 2 characters to avoid spurious single-char matches.
        if (string.IsNullOrEmpty(name) || name.Length < 2) return false;
        return text.Contains(name, StringComparison.OrdinalIgnoreCase);
    }
}
