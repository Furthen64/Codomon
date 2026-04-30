using Codomon.Desktop.Models;
using Codomon.Desktop.Models.SystemMap;

namespace Codomon.Desktop.Services;

/// <summary>
/// Converts <see cref="RoslynScanResult"/> facts into <see cref="CodeNodeModel"/> entries,
/// applying first-pass <see cref="CodeNodeKind"/> classification rules and attaching
/// supporting evidence to every node.
/// </summary>
public static class CodeNodeBuilder
{
    private const string SrcRoslyn = "Roslyn";
    private const string SrcFileName = "FileName";

    // ── Suffix classification rules (checked in order; first match wins) ──────

    private static readonly (string Suffix, CodeNodeKind Kind)[] SuffixRules =
    {
        ("ViewModel", CodeNodeKind.ViewModel),
        ("Dialog",    CodeNodeKind.Dialog),
        ("Window",    CodeNodeKind.View),
        ("Service",   CodeNodeKind.Service),
        ("Repository",CodeNodeKind.Repository),
        ("Model",     CodeNodeKind.Model),
        ("Dto",       CodeNodeKind.Dto),
    };

    // ── Entry-point file name patterns (checked case-insensitively) ───────────

    private static readonly HashSet<string> EntryPointFileNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "Program.cs",
            "App.axaml.cs",
            "App.xaml.cs",
        };

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a flat list of <see cref="CodeNodeModel"/> from all classes and
    /// config files discovered in <paramref name="scan"/>.
    /// Each node receives a first-pass kind classification and attached evidence.
    /// </summary>
    public static List<CodeNodeModel> Build(
        RoslynScanResult scan,
        IProgress<string>? progress = null)
    {
        var nodes = new List<CodeNodeModel>();

        // ── 1. Create code nodes from every scanned class / interface / record ─

        foreach (var file in scan.Files)
        {
            var fileName = Path.GetFileName(file.RelativePath);
            bool isEntryPointFile = EntryPointFileNames.Contains(fileName);

            foreach (var cls in file.Classes)
            {
                var node = BuildFromClass(cls, file.RelativePath, isEntryPointFile);
                nodes.Add(node);
            }

            // If the file has no classes at all but is recognised as an entry-point
            // file, add a synthetic node for the file itself.
            if (file.Classes.Count == 0 && isEntryPointFile)
            {
                nodes.Add(new CodeNodeModel
                {
                    Name = fileName,
                    FullName = file.RelativePath,
                    FilePath = file.RelativePath,
                    Kind = CodeNodeKind.EntryPoint,
                    Confidence = ConfidenceLevel.Likely,
                    Evidence =
                    {
                        new EvidenceModel
                        {
                            Source = SrcFileName,
                            Description = $"File name '{fileName}' matches a known entry-point pattern.",
                            SourceRef = file.RelativePath
                        }
                    }
                });
            }
        }

        // ── 2. Create config-file nodes from appsettings*.json in project dirs ─

        foreach (var project in scan.Projects)
        {
            if (string.IsNullOrEmpty(project.FolderPath)) continue;

            try
            {
                var jsonFiles = Directory.EnumerateFiles(
                    project.FolderPath, "appsettings*.json", SearchOption.TopDirectoryOnly);

                foreach (var jsonPath in jsonFiles)
                {
                    var jsonName = Path.GetFileName(jsonPath);
                    var relPath = Path.GetRelativePath(
                        Path.GetDirectoryName(scan.SourcePath) ?? scan.SourcePath, jsonPath);

                    nodes.Add(new CodeNodeModel
                    {
                        Name = jsonName,
                        FullName = relPath,
                        FilePath = relPath,
                        Kind = CodeNodeKind.ConfigFile,
                        Confidence = ConfidenceLevel.Likely,
                        Evidence =
                        {
                            new EvidenceModel
                            {
                                Source = SrcFileName,
                                Description = $"File name '{jsonName}' matches the appsettings*.json config-file pattern.",
                                SourceRef = relPath
                            }
                        }
                    });
                }
            }
            catch (IOException)
            {
                // Skip project directories that cannot be enumerated.
            }
        }

        progress?.Report($"CodeNodeBuilder: built {nodes.Count} code node(s).");
        return nodes;
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static CodeNodeModel BuildFromClass(
        ScannedClass cls, string relativePath, bool isEntryPointFile)
    {
        var node = new CodeNodeModel
        {
            Name = cls.SimpleName,
            FullName = cls.FullName,
            FilePath = relativePath,
        };

        // ── Roslyn kind (interface / record) takes priority over suffix rules ─
        if (cls.Kind.StartsWith("interface", StringComparison.OrdinalIgnoreCase))
        {
            node.Kind = CodeNodeKind.Interface;
            node.Confidence = ConfidenceLevel.Likely;
            node.Evidence.Add(new EvidenceModel
            {
                Source = SrcRoslyn,
                Description = "Roslyn identified this type as an interface.",
                SourceRef = $"{relativePath}"
            });
            return node;
        }

        if (cls.Kind.StartsWith("record", StringComparison.OrdinalIgnoreCase))
        {
            node.Kind = CodeNodeKind.Record;
            node.Confidence = ConfidenceLevel.Likely;
            node.Evidence.Add(new EvidenceModel
            {
                Source = SrcRoslyn,
                Description = "Roslyn identified this type as a record.",
                SourceRef = $"{relativePath}"
            });
            return node;
        }

        // ── Entry-point check (file name beats suffix rules) ──────────────────
        if (isEntryPointFile)
        {
            node.Kind = CodeNodeKind.EntryPoint;
            node.Confidence = ConfidenceLevel.Likely;
            node.Evidence.Add(new EvidenceModel
            {
                Source = SrcFileName,
                Description = $"Class lives in '{Path.GetFileName(relativePath)}', a known entry-point file.",
                SourceRef = relativePath
            });
            return node;
        }

        // ── Suffix-based classification ───────────────────────────────────────
        foreach (var (suffix, kind) in SuffixRules)
        {
            if (cls.SimpleName.EndsWith(suffix, StringComparison.Ordinal))
            {
                node.Kind = kind;
                node.Confidence = ConfidenceLevel.Likely;
                node.Evidence.Add(new EvidenceModel
                {
                    Source = SrcRoslyn,
                    Description = $"Class name ends with '{suffix}' — classified as {kind}.",
                    SourceRef = $"{relativePath}: {cls.SimpleName}"
                });
                return node;
            }
        }

        // ── Fallback: plain class ─────────────────────────────────────────────
        node.Kind = CodeNodeKind.Class;
        node.Confidence = ConfidenceLevel.Possible;
        node.Evidence.Add(new EvidenceModel
        {
            Source = SrcRoslyn,
            Description = "No suffix rule matched; classified as a plain class.",
            SourceRef = $"{relativePath}: {cls.SimpleName}"
        });
        return node;
    }
}
