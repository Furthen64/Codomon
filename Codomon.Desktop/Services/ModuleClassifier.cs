using Codomon.Desktop.Models;
using Codomon.Desktop.Models.SystemMap;

namespace Codomon.Desktop.Services;

/// <summary>
/// Groups a flat list of <see cref="CodeNodeModel"/> entries into <see cref="ModuleModel"/>
/// instances using namespace segments, folder paths, project name, suffix families, and
/// (optionally) pre-generated LLM <c>.md</c> summary files.
///
/// Every grouping decision is recorded as evidence on the resulting module so humans
/// can inspect and override the classifier's reasoning.
///
/// Returned modules are not attached to any specific system; callers may populate
/// <see cref="ModuleModel.SystemIds"/> separately once system assignment is known.
/// </summary>
public static class ModuleClassifier
{
    private const string SrcNamespace = "Namespace";
    private const string SrcFolder = "FolderPath";
    private const string SrcProject = "ProjectName";
    private const string SrcSuffix = "SuffixFamily";
    private const string SrcLlmSummary = "LlmSummary";

    /// <summary>
    /// Modules with at least this many members are given <see cref="ConfidenceLevel.Likely"/>
    /// confidence (rather than <see cref="ConfidenceLevel.Possible"/>).
    /// </summary>
    private const int MinMembersForLikelyConfidence = 3;

    /// <summary>
    /// How many trailing namespace segments are kept when forming the grouping key.
    /// E.g. with value 2: <c>Acme.App.Services.Graph</c> → key <c>"Services.Graph"</c>.
    /// </summary>
    private const int NamespaceSegmentsToKeep = 2;

    // ── CodeNodeKind → ModuleKind heuristic ───────────────────────────────────

    private static readonly Dictionary<CodeNodeKind, ModuleKind> KindToModuleKind =
        new()
        {
            { CodeNodeKind.ViewModel,   ModuleKind.Presentation },
            { CodeNodeKind.Dialog,      ModuleKind.Presentation },
            { CodeNodeKind.View,        ModuleKind.Presentation },
            { CodeNodeKind.Service,     ModuleKind.BusinessLogic },
            { CodeNodeKind.Repository,  ModuleKind.DataAccess },
            { CodeNodeKind.Dto,         ModuleKind.BusinessLogic },
            { CodeNodeKind.Model,       ModuleKind.BusinessLogic },
            { CodeNodeKind.ConfigFile,  ModuleKind.Configuration },
            { CodeNodeKind.EntryPoint,  ModuleKind.Configuration },
            { CodeNodeKind.Interface,   ModuleKind.BusinessLogic },
            { CodeNodeKind.Record,      ModuleKind.BusinessLogic },
            { CodeNodeKind.Class,       ModuleKind.Other },
            { CodeNodeKind.Enum,        ModuleKind.Other },
            { CodeNodeKind.Script,      ModuleKind.Infrastructure },
            { CodeNodeKind.SourceFile,  ModuleKind.Other },
            { CodeNodeKind.Other,       ModuleKind.Other },
        };

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Classifies <paramref name="codeNodes"/> into modules.
    /// </summary>
    /// <param name="codeNodes">Nodes produced by <see cref="CodeNodeBuilder"/>.</param>
    /// <param name="scan">The scan that produced the nodes (used for project metadata).</param>
    /// <param name="summaryFolderPath">
    /// Optional path to a folder containing LLM-generated <c>.md</c> summary files.
    /// Each file is expected to be named after the class (e.g. <c>MainViewModel.md</c>).
    /// When a matching file is found its name is recorded as additional evidence.
    /// </param>
    /// <param name="progress">Optional progress reporter.</param>
    public static List<ModuleModel> Classify(
        IReadOnlyList<CodeNodeModel> codeNodes,
        RoslynScanResult scan,
        string? summaryFolderPath = null,
        IProgress<string>? progress = null)
    {
        // Build a lookup of available LLM summary files (by simple base-name, case-insensitive).
        var summaryFiles = BuildSummaryLookup(summaryFolderPath);

        // Build project-folder lookup: file path prefix → project name.
        var projectByFolder = scan.Projects
            .Where(p => !string.IsNullOrEmpty(p.FolderPath))
            .OrderByDescending(p => p.FolderPath.Length) // longest prefix first
            .ToList();

        // Group nodes by their primary grouping key (namespace path).
        var groups = new Dictionary<string, List<CodeNodeModel>>(StringComparer.Ordinal);

        foreach (var node in codeNodes)
        {
            var key = ResolveGroupKey(node, projectByFolder, scan.SourcePath);
            if (!groups.TryGetValue(key, out var bucket))
                groups[key] = bucket = new List<CodeNodeModel>();
            bucket.Add(node);
        }

        var modules = new List<ModuleModel>();

        foreach (var (key, members) in groups)
        {
            var module = BuildModule(key, members, summaryFiles, scan.SourcePath);
            modules.Add(module);
        }

        // Sort deterministically: by module name.
        modules.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));

        progress?.Report($"ModuleClassifier: produced {modules.Count} module(s) from {codeNodes.Count} node(s).");
        return modules;
    }

    // ── Group-key resolution ──────────────────────────────────────────────────

    /// <summary>
    /// Returns the grouping key for a node, preferring the node's namespace
    /// (the first two meaningful segments), then folder, then project name.
    /// </summary>
    private static string ResolveGroupKey(
        CodeNodeModel node,
        IReadOnlyList<ScannedProject> projectByFolder,
        string sourcePath)
    {
        // Extract namespace from FullName (everything before the last dot).
        var ns = ExtractNamespace(node.FullName);
        if (!string.IsNullOrEmpty(ns))
            return NormaliseNamespaceKey(ns);

        // Fall back to folder path relative to source root.
        if (!string.IsNullOrEmpty(node.FilePath))
        {
            var folder = Path.GetDirectoryName(node.FilePath)?.Replace('\\', '/') ?? string.Empty;
            if (!string.IsNullOrEmpty(folder))
                return $"folder:{folder}";
        }

        // Fall back to project name.
        var project = FindProject(node.FilePath, projectByFolder);
        if (!string.IsNullOrEmpty(project))
            return $"project:{project}";

        return "project:(root)";
    }

    private static string ExtractNamespace(string fullName)
    {
        var lastDot = fullName.LastIndexOf('.');
        return lastDot > 0 ? fullName[..lastDot] : string.Empty;
    }

    /// <summary>
    /// Normalises a namespace to at most the last <see cref="NamespaceSegmentsToKeep"/>
    /// meaningful segments so that fine-grained sub-namespaces form their own modules
    /// rather than all collapsing into one.
    /// E.g. <c>Acme.App.Services.Graph</c> → <c>"Services.Graph"</c>.
    /// </summary>
    private static string NormaliseNamespaceKey(string ns)
    {
        var parts = ns.Split('.');
        return parts.Length <= NamespaceSegmentsToKeep
            ? ns
            : string.Join('.', parts[^NamespaceSegmentsToKeep..]);
    }

    private static string FindProject(string filePath, IReadOnlyList<ScannedProject> projects)
    {
        if (string.IsNullOrEmpty(filePath)) return string.Empty;
        var normPath = filePath.Replace('\\', '/');
        foreach (var p in projects)
        {
            var normFolder = p.FolderPath.Replace('\\', '/');
            if (normPath.StartsWith(normFolder, StringComparison.OrdinalIgnoreCase))
                return p.Name;
        }
        return string.Empty;
    }

    // ── Module construction ───────────────────────────────────────────────────

    private static ModuleModel BuildModule(
        string groupKey,
        List<CodeNodeModel> members,
        IReadOnlyDictionary<string, string> summaryFiles,
        string sourcePath)
    {
        var moduleName = DeriveModuleName(groupKey);
        var moduleKind = DeriveModuleKind(members);

        // Confidence: more nodes in a coherent namespace → higher confidence.
        var confidence = members.Count >= MinMembersForLikelyConfidence
            ? ConfidenceLevel.Likely
            : ConfidenceLevel.Possible;

        var evidence = new List<EvidenceModel>();

        // Primary grouping evidence.
        if (groupKey.StartsWith("folder:", StringComparison.Ordinal))
        {
            var folder = groupKey["folder:".Length..];
            evidence.Add(new EvidenceModel
            {
                Source = SrcFolder,
                Description = $"All {members.Count} node(s) share folder path '{folder}'.",
                SourceRef = folder
            });
        }
        else if (groupKey.StartsWith("project:", StringComparison.Ordinal))
        {
            var proj = groupKey["project:".Length..];
            evidence.Add(new EvidenceModel
            {
                Source = SrcProject,
                Description = $"All {members.Count} node(s) belong to project '{proj}'.",
                SourceRef = proj
            });
        }
        else
        {
            evidence.Add(new EvidenceModel
            {
                Source = SrcNamespace,
                Description = $"All {members.Count} node(s) share namespace key '{groupKey}'.",
                SourceRef = groupKey
            });
        }

        // Secondary: suffix family consistency.
        var kindGroups = members
            .GroupBy(n => n.Kind)
            .OrderByDescending(g => g.Count())
            .ToList();

        if (kindGroups.Count == 1)
        {
            evidence.Add(new EvidenceModel
            {
                Source = SrcSuffix,
                Description = $"All nodes have the same kind ({kindGroups[0].Key}), " +
                              "confirming a cohesive module.",
                SourceRef = groupKey
            });
            confidence = ConfidenceLevel.Likely;
        }
        else if (kindGroups.Count <= 3)
        {
            var summary = string.Join(", ", kindGroups.Select(g => $"{g.Key}×{g.Count()}"));
            evidence.Add(new EvidenceModel
            {
                Source = SrcSuffix,
                Description = $"Node kinds: {summary}.",
                SourceRef = groupKey
            });
        }

        // LLM summary evidence.
        foreach (var node in members)
        {
            if (summaryFiles.TryGetValue(node.Name, out var summaryPath))
            {
                evidence.Add(new EvidenceModel
                {
                    Source = SrcLlmSummary,
                    Description = $"LLM summary available for '{node.Name}'.",
                    SourceRef = summaryPath
                });
                if (confidence < ConfidenceLevel.Likely)
                    confidence = ConfidenceLevel.Likely;
                break; // one note per module is enough
            }
        }

        return new ModuleModel
        {
            Name = moduleName,
            Kind = moduleKind,
            Confidence = confidence,
            CodeNodes = members,
            Evidence = evidence
        };
    }

    // ── Name derivation ───────────────────────────────────────────────────────

    private static string DeriveModuleName(string groupKey)
    {
        if (groupKey.StartsWith("folder:", StringComparison.Ordinal))
        {
            var folder = groupKey["folder:".Length..];
            var segments = folder.Trim('/').Split('/');
            return segments.Length > 0 ? segments[^1] : folder;
        }

        if (groupKey.StartsWith("project:", StringComparison.Ordinal))
            return groupKey["project:".Length..];

        // Namespace key — make it human-friendly.
        // e.g. "Services.Graph" → "Graph Services", "ViewModels" → "ViewModels"
        var parts = groupKey.Split('.');
        return parts.Length == 1
            ? parts[0]
            : $"{parts[^1]} ({parts[^2]})";
    }

    // ── ModuleKind derivation ─────────────────────────────────────────────────

    private static ModuleKind DeriveModuleKind(List<CodeNodeModel> members)
    {
        // Vote: pick the ModuleKind supported by the most nodes.
        var votes = new Dictionary<ModuleKind, int>();
        foreach (var node in members)
        {
            var mk = KindToModuleKind.TryGetValue(node.Kind, out var mapped)
                ? mapped
                : ModuleKind.Other;
            votes[mk] = votes.GetValueOrDefault(mk, 0) + 1;
        }

        return votes
            .OrderByDescending(kvp => kvp.Value)
            .ThenBy(kvp => kvp.Key) // stable tie-break
            .First().Key;
    }

    // ── LLM summary lookup ────────────────────────────────────────────────────

    private static Dictionary<string, string> BuildSummaryLookup(string? folderPath)
    {
        var lookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath))
            return lookup;

        try
        {
            foreach (var file in Directory.EnumerateFiles(folderPath, "*.md"))
            {
                var baseName = Path.GetFileNameWithoutExtension(file);
                lookup.TryAdd(baseName, file);
            }
        }
        catch (IOException)
        {
            // Non-fatal: summaries are optional evidence.
        }

        return lookup;
    }
}
