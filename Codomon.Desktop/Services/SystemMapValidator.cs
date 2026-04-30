using Codomon.Desktop.Models;
using Codomon.Desktop.Models.SystemMap;

namespace Codomon.Desktop.Services;

/// <summary>
/// Validates a <see cref="SystemMapModel"/> for internal consistency.
/// Detected issues are logged to <see cref="AppLogger"/>; no exceptions are thrown.
/// </summary>
/// <remarks>
/// Run after:
/// <list type="bullet">
///   <item>accepting a System or high-value node</item>
///   <item>accepting or merging a whole hypothesis</item>
///   <item>loading a workspace</item>
///   <item>running hypothesis synthesis</item>
/// </list>
/// </remarks>
public static class SystemMapValidator
{
    /// <summary>
    /// Validates <paramref name="map"/> and logs all detected issues.
    /// Returns the number of issues found.
    /// </summary>
    public static int Validate(SystemMapModel map)
    {
        if (map == null) throw new ArgumentNullException(nameof(map));

        var issues = new List<string>();

        CheckDuplicateSystemKeys(map, issues);
        CheckDuplicateSystemDisplayNames(map, issues);
        CheckDuplicateModuleKeys(map, issues);
        CheckDuplicateCodeNodeKeys(map, issues);
        CheckDuplicateExternalSystemKeys(map, issues);
        CheckDuplicateRelationshipKeys(map, issues);
        CheckDanglingRelationships(map, issues);

        if (issues.Count == 0)
        {
            AppLogger.Debug("[Validator] System Map is consistent — no issues found.");
        }
        else
        {
            foreach (var issue in issues)
                AppLogger.Warn($"[Validator] {issue}");
        }

        return issues.Count;
    }

    // ── Checkers ──────────────────────────────────────────────────────────────

    private static void CheckDuplicateSystemKeys(SystemMapModel map, List<string> issues)
    {
        var seen = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var sys in map.Systems)
        {
            var key = EffectiveSystemKey(sys);
            if (string.IsNullOrWhiteSpace(key)) continue;
            if (seen.TryGetValue(key, out var existingId))
                issues.Add($"Duplicate System key '{key}': id={existingId} and id={sys.Id} ({sys.Name})");
            else
                seen[key] = sys.Id;
        }
    }

    private static void CheckDuplicateSystemDisplayNames(SystemMapModel map, List<string> issues)
    {
        var groups = map.Systems
            .GroupBy(s => $"{SystemMapIdentity.NormalizeKeyPart(s.Name)}|{s.Kind}")
            .Where(g => g.Count() > 1);

        foreach (var g in groups)
            issues.Add($"Systems with same display name + kind but different IDs: key='{g.Key}' " +
                       $"ids=[{string.Join(", ", g.Select(s => s.Id))}]");
    }

    private static void CheckDuplicateModuleKeys(SystemMapModel map, List<string> issues)
    {
        var seen = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var sys in map.Systems)
        {
            foreach (var mod in sys.Modules)
            {
                if (string.IsNullOrWhiteSpace(mod.IdentityKey)) continue;
                if (seen.TryGetValue(mod.IdentityKey, out var existingId))
                    issues.Add($"Duplicate Module key '{mod.IdentityKey}': id={existingId} and id={mod.Id} ({mod.Name})");
                else
                    seen[mod.IdentityKey] = mod.Id;
            }
        }
        foreach (var mod in map.Modules)
        {
            if (string.IsNullOrWhiteSpace(mod.IdentityKey)) continue;
            if (seen.TryGetValue(mod.IdentityKey, out var existingId))
                issues.Add($"Duplicate Module key '{mod.IdentityKey}': id={existingId} and id={mod.Id} ({mod.Name})");
            else
                seen[mod.IdentityKey] = mod.Id;
        }
    }

    private static void CheckDuplicateCodeNodeKeys(SystemMapModel map, List<string> issues)
    {
        var seen = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var node in map.AllCodeNodes)
        {
            var key = string.IsNullOrWhiteSpace(node.IdentityKey)
                ? SystemMapIdentity.CreateCodeNodeKey(node.FullName, null, node.FilePath, node.Name)
                : node.IdentityKey;
            if (string.IsNullOrWhiteSpace(key)) continue;
            if (seen.TryGetValue(key, out var existingId))
                issues.Add($"Duplicate CodeNode key '{key}': id={existingId} and id={node.Id} ({node.Name})");
            else
                seen[key] = node.Id;
        }
    }

    private static void CheckDuplicateExternalSystemKeys(SystemMapModel map, List<string> issues)
    {
        var seen = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var ext in map.ExternalSystems)
        {
            var key = string.IsNullOrWhiteSpace(ext.IdentityKey)
                ? SystemMapIdentity.CreateExternalSystemKey(ext.Name, ext.Kind)
                : ext.IdentityKey;
            if (string.IsNullOrWhiteSpace(key)) continue;
            if (seen.TryGetValue(key, out var existingId))
                issues.Add($"Duplicate ExternalSystem key '{key}': id={existingId} and id={ext.Id} ({ext.Name})");
            else
                seen[key] = ext.Id;
        }
    }

    private static void CheckDuplicateRelationshipKeys(SystemMapModel map, List<string> issues)
    {
        var seen = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var rel in map.Relationships)
        {
            var key = string.IsNullOrWhiteSpace(rel.IdentityKey)
                ? $"{rel.FromId}→{rel.ToId}|{rel.Kind}"
                : rel.IdentityKey;
            if (string.IsNullOrWhiteSpace(key)) continue;
            if (seen.TryGetValue(key, out var existingId))
                issues.Add($"Duplicate Relationship key '{key}': id={existingId} and id={rel.Id}");
            else
                seen[key] = rel.Id;
        }
    }

    private static void CheckDanglingRelationships(SystemMapModel map, List<string> issues)
    {
        // Build a set of all known entity IDs.
        var allIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var s in map.Systems)       allIds.Add(s.Id);
        foreach (var m in map.AllModules)    allIds.Add(m.Id);
        foreach (var n in map.AllCodeNodes)  allIds.Add(n.Id);
        foreach (var e in map.ExternalSystems) allIds.Add(e.Id);

        foreach (var rel in map.Relationships)
        {
            if (!allIds.Contains(rel.FromId))
                issues.Add($"Relationship id={rel.Id} has unknown source FromId='{rel.FromId}'");
            if (!allIds.Contains(rel.ToId))
                issues.Add($"Relationship id={rel.Id} has unknown target ToId='{rel.ToId}'");
        }
    }

    // ── Key helpers ───────────────────────────────────────────────────────────

    private static string EffectiveSystemKey(SystemModel sys) =>
        string.IsNullOrWhiteSpace(sys.IdentityKey)
            ? SystemMapIdentity.CreateSystemKey(sys.Name, sys.Kind.ToString())
            : sys.IdentityKey;
}
