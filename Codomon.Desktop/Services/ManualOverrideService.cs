using Codomon.Desktop.Models;
using Codomon.Desktop.Models.SystemMap;

namespace Codomon.Desktop.Services;

/// <summary>
/// Applies <see cref="ManualOverrideModel"/> corrections to a <see cref="SystemMapModel"/>,
/// enforcing the precedence rule:
/// <para>
///   Manual Override &gt; Runtime-confirmed Evidence &gt; Hard Facts &gt; Heuristics &gt; LLM Suggestions &gt; Unknown
/// </para>
/// Call <see cref="Apply"/> after any analysis pass (hard facts, heuristics, LLM suggestions)
/// so that human curation is always the final authority.
/// </summary>
public static class ManualOverrideService
{
    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Re-applies all <paramref name="overrides"/> to <paramref name="map"/> in
    /// chronological order (oldest first).  Idempotent — safe to call repeatedly.
    /// </summary>
    /// <param name="map">The System Map to mutate.</param>
    /// <param name="overrides">The ordered list of human corrections to apply.</param>
    /// <param name="progress">Optional progress reporter.</param>
    public static void Apply(
        SystemMapModel map,
        IReadOnlyList<ManualOverrideModel> overrides,
        IProgress<string>? progress = null)
    {
        if (map == null) throw new ArgumentNullException(nameof(map));
        if (overrides == null || overrides.Count == 0) return;

        // Build fast-lookup caches to avoid repeated linear scans.
        var systemsById     = BuildSystemIndex(map);
        var modulesById     = BuildModuleIndex(map);
        var codeNodesById   = BuildCodeNodeIndex(map);
        var relationshipsById = BuildRelationshipIndex(map);

        int applied = 0;
        foreach (var o in overrides.OrderBy(x => x.CreatedAt))
        {
            bool changed = ApplyOne(o, map, systemsById, modulesById, codeNodesById, relationshipsById);
            if (changed) applied++;
        }

        map.UpdatedAt = DateTime.UtcNow;
        progress?.Report($"ManualOverrideService: applied {applied} of {overrides.Count} override(s).");
        AppLogger.Info($"[ManualOverride] Applied {applied}/{overrides.Count} override(s) to SystemMap.");
    }

    // ── Per-override dispatch ─────────────────────────────────────────────────

    private static bool ApplyOne(
        ManualOverrideModel o,
        SystemMapModel map,
        Dictionary<string, SystemModel> systemsById,
        Dictionary<string, ModuleModel> modulesById,
        Dictionary<string, (CodeNodeModel Node, ModuleModel Owner)> codeNodesById,
        Dictionary<string, RelationshipModel> relationshipsById)
    {
        switch (o.Type)
        {
            case ManualOverrideType.Rename:
                return ApplyRename(o, systemsById, modulesById, codeNodesById);

            case ManualOverrideType.AssignToSystem:
                return ApplyAssignToSystem(o, map, systemsById, modulesById);

            case ManualOverrideType.AssignToModule:
                return ApplyAssignToModule(o, map, modulesById, codeNodesById);

            case ManualOverrideType.MarkHighValue:
                return ApplyFlag(o, codeNodesById, (n, v) => n.IsHighValue = v);

            case ManualOverrideType.MarkNoisy:
                return ApplyFlag(o, codeNodesById, (n, v) => n.IsNoisy = v);

            case ManualOverrideType.HideFromOverview:
                return ApplyFlag(o, codeNodesById, (n, v) => n.HideFromOverview = v);

            case ManualOverrideType.SetStartupMechanism:
                return ApplySetStartupMechanism(o, systemsById);

            case ManualOverrideType.AddRelationship:
                return ApplyAddRelationship(o, map, relationshipsById);

            case ManualOverrideType.RemoveRelationship:
                return ApplyRemoveRelationship(o, map, relationshipsById);

            case ManualOverrideType.PinPosition:
                // Position pinning is managed by the layout profile; no System Map mutation needed.
                return false;

            case ManualOverrideType.AcceptSuggestion:
            case ManualOverrideType.RejectSuggestion:
                // Suggestion acceptance/rejection is handled at the hypothesis layer;
                // the resulting entities are already in the map.
                return false;

            default:
                AppLogger.Warn($"[ManualOverride] Unknown override type '{o.Type}' (id={o.Id}). Skipped.");
                return false;
        }
    }

    // ── Override handlers ─────────────────────────────────────────────────────

    private static bool ApplyRename(
        ManualOverrideModel o,
        Dictionary<string, SystemModel> systemsById,
        Dictionary<string, ModuleModel> modulesById,
        Dictionary<string, (CodeNodeModel Node, ModuleModel Owner)> codeNodesById)
    {
        if (string.IsNullOrWhiteSpace(o.Value))
        {
            AppLogger.Warn($"[ManualOverride] Rename override (id={o.Id}) has empty Value. Skipped.");
            return false;
        }

        if (systemsById.TryGetValue(o.TargetId, out var sys))
        {
            sys.Name = o.Value;
            sys.Confidence = ConfidenceLevel.Manual;
            return true;
        }
        if (modulesById.TryGetValue(o.TargetId, out var mod))
        {
            mod.Name = o.Value;
            mod.Confidence = ConfidenceLevel.Manual;
            return true;
        }
        if (codeNodesById.TryGetValue(o.TargetId, out var entry))
        {
            entry.Node.Name = o.Value;
            entry.Node.Confidence = ConfidenceLevel.Manual;
            return true;
        }

        AppLogger.Warn($"[ManualOverride] Rename target not found (TargetId={o.TargetId}, id={o.Id}). Skipped.");
        return false;
    }

    private static bool ApplyAssignToSystem(
        ManualOverrideModel o,
        SystemMapModel map,
        Dictionary<string, SystemModel> systemsById,
        Dictionary<string, ModuleModel> modulesById)
    {
        if (!systemsById.TryGetValue(o.Value, out var targetSystem))
        {
            AppLogger.Warn($"[ManualOverride] AssignToSystem: target system '{o.Value}' not found (id={o.Id}). Skipped.");
            return false;
        }

        // Module assignment to system.
        if (modulesById.TryGetValue(o.TargetId, out var mod))
        {
            // Update SystemIds to point exclusively to the target system (manual override = authoritative).
            mod.SystemIds = new List<string> { targetSystem.Id };
            mod.Confidence = ConfidenceLevel.Manual;

            // Move the module into the target system's Modules list if not already there.
            if (!targetSystem.Modules.Contains(mod))
            {
                // Remove from any other system first.
                foreach (var sys in map.Systems)
                    sys.Modules.Remove(mod);
                // Remove from top-level modules list too (it now lives inside the system).
                map.Modules.Remove(mod);
                targetSystem.Modules.Add(mod);
            }
            return true;
        }

        AppLogger.Warn($"[ManualOverride] AssignToSystem: module target not found (TargetId={o.TargetId}, id={o.Id}). Skipped.");
        return false;
    }

    private static bool ApplyAssignToModule(
        ManualOverrideModel o,
        SystemMapModel map,
        Dictionary<string, ModuleModel> modulesById,
        Dictionary<string, (CodeNodeModel Node, ModuleModel Owner)> codeNodesById)
    {
        if (!modulesById.TryGetValue(o.Value, out var targetModule))
        {
            AppLogger.Warn($"[ManualOverride] AssignToModule: target module '{o.Value}' not found (id={o.Id}). Skipped.");
            return false;
        }

        if (!codeNodesById.TryGetValue(o.TargetId, out var entry))
        {
            AppLogger.Warn($"[ManualOverride] AssignToModule: code node target not found (TargetId={o.TargetId}, id={o.Id}). Skipped.");
            return false;
        }

        if (entry.Owner.Id == targetModule.Id)
            return false; // Already in the right module.

        entry.Owner.CodeNodes.Remove(entry.Node);
        entry.Node.Confidence = ConfidenceLevel.Manual;
        targetModule.CodeNodes.Add(entry.Node);
        return true;
    }

    private static bool ApplyFlag(
        ManualOverrideModel o,
        Dictionary<string, (CodeNodeModel Node, ModuleModel Owner)> codeNodesById,
        Action<CodeNodeModel, bool> setter)
    {
        if (!codeNodesById.TryGetValue(o.TargetId, out var entry))
        {
            AppLogger.Warn($"[ManualOverride] Flag override: code node target not found (TargetId={o.TargetId}, id={o.Id}). Skipped.");
            return false;
        }

        bool value = !string.Equals(o.Value, "false", StringComparison.OrdinalIgnoreCase);
        setter(entry.Node, value);
        entry.Node.Confidence = ConfidenceLevel.Manual;
        return true;
    }

    private static bool ApplySetStartupMechanism(
        ManualOverrideModel o,
        Dictionary<string, SystemModel> systemsById)
    {
        if (!systemsById.TryGetValue(o.TargetId, out var sys))
        {
            AppLogger.Warn($"[ManualOverride] SetStartupMechanism: system not found (TargetId={o.TargetId}, id={o.Id}). Skipped.");
            return false;
        }

        sys.StartupMechanism = o.Value;
        sys.Confidence = ConfidenceLevel.Manual;
        return true;
    }

    private static bool ApplyAddRelationship(
        ManualOverrideModel o,
        SystemMapModel map,
        Dictionary<string, RelationshipModel> relationshipsById)
    {
        // Value format: "to-entity-id|RelationshipKind"
        var parts = o.Value.Split('|');
        if (parts.Length < 2)
        {
            AppLogger.Warn($"[ManualOverride] AddRelationship: invalid Value format '{o.Value}' " +
                           $"(expected 'toId|Kind', id={o.Id}). Skipped.");
            return false;
        }

        var toId = parts[0].Trim();
        var kindStr = parts[1].Trim();
        var kind = Enum.TryParse<RelationshipKind>(kindStr, out var k) ? k : RelationshipKind.Other;

        // Avoid duplicate relationships (same from/to/kind).
        bool exists = map.Relationships.Any(r =>
            r.FromId == o.TargetId && r.ToId == toId && r.Kind == kind);
        if (exists) return false;

        var relationship = new RelationshipModel
        {
            FromId     = o.TargetId,
            ToId       = toId,
            Kind       = kind,
            Confidence = ConfidenceLevel.Manual,
            Evidence   =
            {
                new EvidenceModel
                {
                    Source      = "Manual",
                    Description = $"Manual override (id={o.Id}): {o.Notes}".TrimEnd(':').TrimEnd(' ')
                }
            }
        };

        map.Relationships.Add(relationship);
        relationshipsById[relationship.Id] = relationship;
        return true;
    }

    private static bool ApplyRemoveRelationship(
        ManualOverrideModel o,
        SystemMapModel map,
        Dictionary<string, RelationshipModel> relationshipsById)
    {
        if (!relationshipsById.TryGetValue(o.TargetId, out var rel))
        {
            AppLogger.Warn($"[ManualOverride] RemoveRelationship: relationship not found (TargetId={o.TargetId}, id={o.Id}). Skipped.");
            return false;
        }

        map.Relationships.Remove(rel);
        relationshipsById.Remove(o.TargetId);
        return true;
    }

    // ── Index builders ────────────────────────────────────────────────────────

    private static Dictionary<string, SystemModel> BuildSystemIndex(SystemMapModel map) =>
        map.Systems.ToDictionary(s => s.Id, StringComparer.Ordinal);

    private static Dictionary<string, ModuleModel> BuildModuleIndex(SystemMapModel map) =>
        map.AllModules.ToDictionary(m => m.Id, StringComparer.Ordinal);

    private static Dictionary<string, (CodeNodeModel Node, ModuleModel Owner)> BuildCodeNodeIndex(
        SystemMapModel map)
    {
        var dict = new Dictionary<string, (CodeNodeModel, ModuleModel)>(StringComparer.Ordinal);
        foreach (var mod in map.AllModules)
            foreach (var node in mod.CodeNodes)
                dict.TryAdd(node.Id, (node, mod));
        return dict;
    }

    private static Dictionary<string, RelationshipModel> BuildRelationshipIndex(SystemMapModel map) =>
        map.Relationships.ToDictionary(r => r.Id, StringComparer.Ordinal);
}
