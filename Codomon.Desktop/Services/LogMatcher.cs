using Codomon.Desktop.Models;

namespace Codomon.Desktop.Services;

public enum MatchStrength { None, SystemOnly, ModuleExact }

/// <summary>Result of matching a log entry against the workspace topology.</summary>
/// <param name="Strength">How precisely the entry was matched.</param>
/// <param name="System">The matched system, or <c>null</c> when there is no match.</param>
/// <param name="Module">The matched module, or <c>null</c> when the match is system-level only.</param>
/// <param name="MatchedRule">The mapping rule that caused this match, or <c>null</c> for automatic name-based matches.</param>
/// <param name="MatchReason">Human-readable description of why this entry matched.</param>
public record MatchResult(
    MatchStrength Strength,
    SystemBoxModel? System,
    ModuleBoxModel? Module,
    MappingRuleModel? MatchedRule = null,
    string MatchReason = "");

/// <summary>
/// Matches a <see cref="LogEntryModel"/> against the Systems and Modules defined in a
/// <see cref="WorkspaceModel"/>.
/// <para>
/// Priority order (Phase 08):
/// </para>
/// <list type="number">
///   <item>Enabled <see cref="MappingRuleModel"/> with <see cref="RuleType.LogSourcePattern"/> — match against Source</item>
///   <item>Enabled <see cref="MappingRuleModel"/> with <see cref="RuleType.NamespacePattern"/> — match against Source</item>
///   <item>Enabled <see cref="MappingRuleModel"/> with <see cref="RuleType.ClassNamePattern"/> — match against Source</item>
///   <item>Enabled <see cref="MappingRuleModel"/> with <see cref="RuleType.FolderPathPattern"/> — match against Source</item>
///   <item>Enabled <see cref="MappingRuleModel"/> with <see cref="RuleType.MessageKeywordPattern"/> — match against Message</item>
///   <item>Automatic: Module name contained in entry Source</item>
///   <item>Automatic: System name contained in entry Source</item>
///   <item>Automatic: Module name contained in entry Message</item>
///   <item>Automatic: System name contained in entry Message</item>
/// </list>
/// </summary>
public static class LogMatcher
{
    /// <summary>Priority order for rule types (lower index = higher priority).</summary>
    private static readonly RuleType[] RulePriority =
    {
        RuleType.LogSourcePattern,
        RuleType.NamespacePattern,
        RuleType.ClassNamePattern,
        RuleType.FolderPathPattern,
        RuleType.MessageKeywordPattern
    };

    public static MatchResult Match(LogEntryModel entry, WorkspaceModel workspace)
    {
        // ── Rule-based matching (Phase 08) ────────────────────────────────────

        if (workspace.MappingRules.Count > 0)
        {
            var enabledRules = workspace.MappingRules.Where(r => r.IsEnabled).ToList();

            foreach (var ruleType in RulePriority)
            {
                // Determine which entry field to match this rule type against.
                string fieldValue = ruleType == RuleType.MessageKeywordPattern
                    ? entry.Message
                    : entry.Source;

                if (string.IsNullOrEmpty(fieldValue)) continue;

                foreach (var rule in enabledRules.Where(r => r.RuleType == ruleType))
                {
                    if (!PatternMatches(fieldValue, rule.Pattern)) continue;

                    // Resolve the target System/Module.
                    if (rule.TargetType == RuleTargetType.Module)
                    {
                        foreach (var sys in workspace.Systems)
                        {
                            var mod = sys.Modules.FirstOrDefault(m => m.Id == rule.TargetId);
                            if (mod != null)
                                return new MatchResult(MatchStrength.ModuleExact, sys, mod,
                                    rule, BuildRuleReason(rule, ruleType, fieldValue));
                        }
                    }
                    else // System
                    {
                        var sys = workspace.Systems.FirstOrDefault(s => s.Id == rule.TargetId);
                        if (sys != null)
                            return new MatchResult(MatchStrength.SystemOnly, sys, null,
                                rule, BuildRuleReason(rule, ruleType, fieldValue));
                    }
                }
            }
        }

        // ── Automatic name-based matching (fallback) ──────────────────────────

        // 1 + 2: source-based matching (most precise)
        if (!string.IsNullOrEmpty(entry.Source))
        {
            foreach (var sys in workspace.Systems)
            {
                foreach (var mod in sys.Modules)
                {
                    if (ContainsName(entry.Source, mod.Name))
                        return new MatchResult(MatchStrength.ModuleExact, sys, mod,
                            null, $"Module name \"{mod.Name}\" found in Source");
                }
            }

            foreach (var sys in workspace.Systems)
            {
                if (ContainsName(entry.Source, sys.Name))
                    return new MatchResult(MatchStrength.SystemOnly, sys, null,
                        null, $"System name \"{sys.Name}\" found in Source");
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
                        return new MatchResult(MatchStrength.ModuleExact, sys, mod,
                            null, $"Module name \"{mod.Name}\" found in Message");
                }
            }

            foreach (var sys in workspace.Systems)
            {
                if (ContainsName(entry.Message, sys.Name))
                    return new MatchResult(MatchStrength.SystemOnly, sys, null,
                        null, $"System name \"{sys.Name}\" found in Message");
            }
        }

        return new MatchResult(MatchStrength.None, null, null);
    }

    private static bool PatternMatches(string text, string pattern)
    {
        if (string.IsNullOrEmpty(pattern)) return false;
        return text.Contains(pattern, StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsName(string text, string name)
    {
        // Require at least 2 characters to avoid spurious single-char matches.
        if (string.IsNullOrEmpty(name) || name.Length < 2) return false;
        return text.Contains(name, StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildRuleReason(MappingRuleModel rule, RuleType ruleType, string fieldValue)
    {
        var field = ruleType == RuleType.MessageKeywordPattern ? "Message" : "Source";
        return $"Rule \"{rule.Pattern}\" ({ruleType}) matched {field}";
    }
}
