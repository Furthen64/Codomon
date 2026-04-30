using Codomon.Desktop.Models;
using Codomon.Desktop.Models.ArchitectureHypothesis;
using Codomon.Desktop.Models.SystemMap;

namespace Codomon.Desktop.Services;

/// <summary>
/// Responsible for safe, idempotent mutation of a <see cref="SystemMapModel"/>.
/// All acceptance handlers must call this service instead of directly appending
/// to collections such as <c>SystemMap.Systems</c>.
/// </summary>
/// <remarks>
/// Merge precedence (highest wins):
/// Manual Override > Runtime-confirmed > Hard Facts > Heuristics > LLM Suggestions > Unknown
/// </remarks>
public static class SystemMapUpsertService
{
    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Inserts or merges a hypothesis system suggestion into <paramref name="map"/>.
    /// Returns the resulting <see cref="SystemModel"/> and a flag indicating whether
    /// a new entity was created (<c>true</c>) or an existing one was merged (<c>false</c>).
    /// </summary>
    public static (SystemModel System, bool IsNew) UpsertSystem(
        SystemMapModel map,
        HypothesisSystemModel suggestion)
    {
        var key = SystemMapIdentity.CreateSystemKey(suggestion.Name, suggestion.Kind.ToString());

        var existing = FindSystemByKey(map, key);
        if (existing != null)
        {
            MergeSystem(existing, suggestion);
            MarkSuggestionAccepted(suggestion, existing.Id);
            AppLogger.Debug($"[Upsert] Merged system '{suggestion.Name}' into existing id={existing.Id}");
            return (existing, false);
        }

        var system = CreateSystem(suggestion, key);
        map.Systems.Add(system);
        map.UpdatedAt = DateTime.UtcNow;
        MarkSuggestionAccepted(suggestion, system.Id);
        AppLogger.Debug($"[Upsert] Created new system '{system.Name}' id={system.Id}");
        return (system, true);
    }

    /// <summary>
    /// Inserts or merges a hypothesis module suggestion into <paramref name="targetSystem"/>.
    /// Returns the resulting <see cref="ModuleModel"/> and a flag indicating whether a new
    /// entity was created.
    /// </summary>
    public static (ModuleModel Module, bool IsNew) UpsertModule(
        SystemMapModel map,
        HypothesisModuleModel suggestion,
        SystemModel targetSystem)
    {
        var systemKey = string.IsNullOrWhiteSpace(targetSystem.IdentityKey)
            ? SystemMapIdentity.CreateSystemKey(targetSystem.Name, targetSystem.Kind.ToString())
            : targetSystem.IdentityKey;
        var key = SystemMapIdentity.CreateModuleKey(systemKey, suggestion.Name);

        var existing = FindModuleByKey(targetSystem, key)
            ?? FindModuleByKey(map.Modules, key);
        if (existing != null)
        {
            MergeModule(existing, suggestion);
            AppLogger.Debug($"[Upsert] Merged module '{suggestion.Name}' into existing id={existing.Id}");
            return (existing, false);
        }

        var module = new ModuleModel
        {
            Name        = suggestion.Name,
            Confidence  = suggestion.Confidence,
            IdentityKey = key,
            SystemIds   = new List<string> { targetSystem.Id }
        };
        targetSystem.Modules.Add(module);
        map.UpdatedAt = DateTime.UtcNow;
        AppLogger.Debug($"[Upsert] Created new module '{module.Name}' id={module.Id}");
        return (module, true);
    }

    /// <summary>
    /// Inserts or merges a high-value node suggestion as a <see cref="CodeNodeModel"/>.
    /// Places the node in the first available module, or creates a holding module on the
    /// first system if no modules exist.
    /// Returns the resulting <see cref="CodeNodeModel"/> and a flag indicating whether
    /// a new entity was created.
    /// </summary>
    public static (CodeNodeModel Node, bool IsNew) UpsertHighValueNode(
        SystemMapModel map,
        HypothesisHighValueNodeModel suggestion)
    {
        var key = SystemMapIdentity.CreateCodeNodeKey(null, null, null, suggestion.Name);

        var existing = FindCodeNodeByKey(map, key);
        if (existing != null)
        {
            MergeHighValueNode(existing, suggestion);
            MarkNodeSuggestionAccepted(suggestion, existing.Id);
            AppLogger.Debug($"[Upsert] Merged high-value node '{suggestion.Name}' into existing id={existing.Id}");
            return (existing, false);
        }

        var node = new CodeNodeModel
        {
            Name        = suggestion.Name,
            Confidence  = suggestion.Confidence,
            Notes       = suggestion.Reason,
            IdentityKey = key,
            IsHighValue = true,
            Evidence    = new List<EvidenceModel>
            {
                new() { Source = "LLM", Description = suggestion.Reason }
            }
        };

        PlaceCodeNode(map, node);
        map.UpdatedAt = DateTime.UtcNow;
        MarkNodeSuggestionAccepted(suggestion, node.Id);
        AppLogger.Debug($"[Upsert] Created new high-value node '{node.Name}' id={node.Id}");
        return (node, true);
    }

    // ── Merge helpers ─────────────────────────────────────────────────────────

    private static void MergeSystem(SystemModel target, HypothesisSystemModel suggestion)
    {
        // Only update fields that are not manually locked.
        if (target.Confidence != ConfidenceLevel.Manual)
        {
            // Raise confidence when the incoming suggestion is stronger.
            if (IsStronger(suggestion.Confidence, target.Confidence))
                target.Confidence = suggestion.Confidence;
        }

        // Merge evidence: append entries not already present (by description).
        foreach (var ev in suggestion.Evidence)
        {
            var desc = ev.Trim();
            if (string.IsNullOrWhiteSpace(desc)) continue;
            var alreadyPresent = target.Evidence.Any(e =>
                string.Equals(e.Description, desc, StringComparison.OrdinalIgnoreCase));
            if (!alreadyPresent)
                target.Evidence.Add(new EvidenceModel { Source = "LLM", Description = desc });
        }

        // Merge modules from the suggestion.
        // (Full module upsert is the caller's responsibility when modules need tracking.)
    }

    private static void MergeModule(ModuleModel target, HypothesisModuleModel suggestion)
    {
        if (target.Confidence != ConfidenceLevel.Manual)
        {
            if (IsStronger(suggestion.Confidence, target.Confidence))
                target.Confidence = suggestion.Confidence;
        }
    }

    private static void MergeHighValueNode(CodeNodeModel target, HypothesisHighValueNodeModel suggestion)
    {
        if (target.Confidence != ConfidenceLevel.Manual)
        {
            if (IsStronger(suggestion.Confidence, target.Confidence))
                target.Confidence = suggestion.Confidence;
        }

        // Add the high-value flag if not already set (never unset a manual flag).
        if (!target.IsHighValue)
            target.IsHighValue = true;

        // Append reason as evidence if not already present.
        if (!string.IsNullOrWhiteSpace(suggestion.Reason))
        {
            var alreadyPresent = target.Evidence.Any(e =>
                string.Equals(e.Description, suggestion.Reason, StringComparison.OrdinalIgnoreCase));
            if (!alreadyPresent)
                target.Evidence.Add(new EvidenceModel { Source = "LLM", Description = suggestion.Reason });
        }

        // Merge the reason into Notes when Notes is empty or was set by a previous LLM pass.
        if (string.IsNullOrWhiteSpace(target.Notes))
            target.Notes = suggestion.Reason;
    }

    // ── Creation helpers ──────────────────────────────────────────────────────

    private static SystemModel CreateSystem(HypothesisSystemModel suggestion, string key) =>
        new()
        {
            Name        = suggestion.Name,
            Kind        = suggestion.Kind,
            Confidence  = suggestion.Confidence,
            IdentityKey = key,
            Evidence    = suggestion.Evidence
                .Where(e => !string.IsNullOrWhiteSpace(e))
                .Select(e => new EvidenceModel { Source = "LLM", Description = e })
                .ToList()
        };

    private static void PlaceCodeNode(SystemMapModel map, CodeNodeModel node)
    {
        var firstModule = map.AllModules.FirstOrDefault();
        if (firstModule != null)
        {
            firstModule.CodeNodes.Add(node);
            return;
        }

        var firstSystem = map.Systems.FirstOrDefault();
        if (firstSystem != null)
        {
            // Find or create the holding module.
            var holding = firstSystem.Modules
                .FirstOrDefault(m => m.Name == "UnassignedHighValueNodes");
            if (holding == null)
            {
                holding = new ModuleModel
                {
                    Name        = "UnassignedHighValueNodes",
                    Confidence  = ConfidenceLevel.Unknown,
                    IdentityKey = SystemMapIdentity.CreateModuleKey(
                        firstSystem.IdentityKey, "UnassignedHighValueNodes")
                };
                firstSystem.Modules.Add(holding);
            }
            holding.CodeNodes.Add(node);
        }
        // If there are no systems either, the node cannot be placed yet.
    }

    // ── Lookup helpers ────────────────────────────────────────────────────────

    private static SystemModel? FindSystemByKey(SystemMapModel map, string key)
    {
        foreach (var s in map.Systems)
        {
            // Prefer stored key; fall back to computing from name+kind for legacy entries.
            var effectiveKey = string.IsNullOrWhiteSpace(s.IdentityKey)
                ? SystemMapIdentity.CreateSystemKey(s.Name, s.Kind.ToString())
                : s.IdentityKey;

            if (string.Equals(effectiveKey, key, StringComparison.Ordinal))
            {
                // Stamp the stored key for future lookups.
                if (string.IsNullOrWhiteSpace(s.IdentityKey))
                    s.IdentityKey = effectiveKey;
                return s;
            }
        }
        return null;
    }

    private static ModuleModel? FindModuleByKey(SystemModel system, string key) =>
        FindModuleByKey(system.Modules, key);

    private static ModuleModel? FindModuleByKey(IEnumerable<ModuleModel> modules, string key)
    {
        foreach (var m in modules)
        {
            var effectiveKey = string.IsNullOrWhiteSpace(m.IdentityKey) ? null : m.IdentityKey;
            if (effectiveKey != null && string.Equals(effectiveKey, key, StringComparison.Ordinal))
                return m;
        }
        return null;
    }

    private static CodeNodeModel? FindCodeNodeByKey(SystemMapModel map, string key)
    {
        foreach (var node in map.AllCodeNodes)
        {
            var effectiveKey = string.IsNullOrWhiteSpace(node.IdentityKey)
                ? SystemMapIdentity.CreateCodeNodeKey(node.FullName, null, node.FilePath, node.Name)
                : node.IdentityKey;

            if (string.Equals(effectiveKey, key, StringComparison.Ordinal))
            {
                if (string.IsNullOrWhiteSpace(node.IdentityKey))
                    node.IdentityKey = effectiveKey;
                return node;
            }
        }
        return null;
    }

    // ── Suggestion state helpers ──────────────────────────────────────────────

    private static void MarkSuggestionAccepted(HypothesisSystemModel suggestion, string entityId)
    {
        suggestion.IsAccepted    = true;
        suggestion.AcceptedIntoId = entityId;
        suggestion.AcceptedAt    ??= DateTimeOffset.UtcNow;
    }

    private static void MarkNodeSuggestionAccepted(HypothesisHighValueNodeModel suggestion, string entityId)
    {
        suggestion.IsAccepted    = true;
        suggestion.AcceptedIntoId = entityId;
        suggestion.AcceptedAt    ??= DateTimeOffset.UtcNow;
    }

    // ── Confidence ordering ───────────────────────────────────────────────────

    /// <summary>Returns true when <paramref name="incoming"/> is stronger than <paramref name="current"/>.</summary>
    private static bool IsStronger(ConfidenceLevel incoming, ConfidenceLevel current)
    {
        // Lower ordinal value = higher confidence (Manual=0 is strongest).
        return (int)incoming < (int)current;
    }
}
