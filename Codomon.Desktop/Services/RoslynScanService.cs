using Codomon.Desktop.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Text.Json;

namespace Codomon.Desktop.Services;

/// <summary>
/// Uses Roslyn's file-based syntax analysis to scan C# source code and produce a
/// <see cref="RoslynScanResult"/> containing projects, files, namespaces, classes,
/// methods, logging call locations, and suggested inter-class connections.
/// </summary>
public static class RoslynScanService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private const string ScansFolder = "scans";

    // ── Well-known logging method names ──────────────────────────────────────

    private static readonly HashSet<string> LoggingMethodNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "LogTrace", "LogDebug", "LogInformation", "LogWarning", "LogError", "LogCritical",
        "Trace", "Debug", "Info", "Information", "Warn", "Warning", "Error", "Critical", "Fatal",
        "Log", "Write", "WriteLine"
    };

    private static readonly HashSet<string> LoggingTypeHints = new(StringComparer.OrdinalIgnoreCase)
    {
        "ILogger", "Logger", "Log", "_logger", "_log", "logger", "log"
    };

    // ── Public entry point ───────────────────────────────────────────────────

    /// <summary>
    /// Scans all C# files under <paramref name="sourcePath"/>, extracts code structure and
    /// returns a <see cref="RoslynScanResult"/>. Progress messages are reported via
    /// <paramref name="progress"/>. Pass a <see cref="CancellationToken"/> to support
    /// cancellation.
    /// </summary>
    public static async Task<RoslynScanResult> ScanAsync(
        string sourcePath,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var result = new RoslynScanResult
        {
            ScanTime = DateTime.UtcNow,
            SourcePath = sourcePath
        };

        string searchRoot = Directory.Exists(sourcePath)
            ? sourcePath
            : Path.GetDirectoryName(sourcePath) ?? sourcePath;

        progress?.Report($"Searching for C# files under: {searchRoot}");

        var csFiles = await Task.Run(() =>
            Directory.EnumerateFiles(searchRoot, "*.cs", SearchOption.AllDirectories)
                .Where(f => !IsExcluded(f))
                .OrderBy(f => f)
                .ToList(), cancellationToken);

        progress?.Report($"Found {csFiles.Count} C# source file(s). Discovering projects…");
        cancellationToken.ThrowIfCancellationRequested();

        // Discover .csproj files to populate the projects list.
        result.Projects = await Task.Run(() => DiscoverProjects(searchRoot, csFiles), cancellationToken);
        progress?.Report($"Discovered {result.Projects.Count} project(s).");

        // Parse every .cs file.
        int parsed = 0;
        foreach (var filePath in csFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var scannedFile = await ParseFileAsync(filePath, searchRoot, cancellationToken);
            if (scannedFile != null)
                result.Files.Add(scannedFile);

            parsed++;
            if (parsed % 20 == 0 || parsed == csFiles.Count)
                progress?.Report($"Parsed {parsed} / {csFiles.Count} files…");
        }

        progress?.Report("Building suggested connections from call relationships…");
        result.SuggestedConnections = BuildSuggestedConnections(result.Files);

        progress?.Report($"Scan complete. {result.Files.Count} files, " +
                         $"{result.Files.SelectMany(f => f.Classes).Count()} classes, " +
                         $"{result.SuggestedConnections.Count} suggested connection(s).");

        return result;
    }

    /// <summary>
    /// Saves <paramref name="scanResult"/> to the workspace <c>scans/</c> folder as
    /// <c>{timestamp}_roslyn.json</c> and returns the saved file path.
    /// </summary>
    public static async Task<string> SaveAsync(RoslynScanResult scanResult, string workspaceFolderPath)
    {
        var scansDir = Path.Combine(workspaceFolderPath, ScansFolder);
        Directory.CreateDirectory(scansDir);

        var timestamp = scanResult.ScanTime.ToString("yyyyMMdd_HHmmss");
        var fileName = $"{timestamp}_roslyn.json";
        var filePath = Path.Combine(scansDir, fileName);

        await using var stream = File.Create(filePath);
        await JsonSerializer.SerializeAsync(stream, scanResult, JsonOptions);
        return filePath;
    }

    /// <summary>
    /// Returns all previously saved Roslyn scan results for a workspace, newest first.
    /// </summary>
    public static List<(string FilePath, DateTime ScanTime)> ListSavedScans(string workspaceFolderPath)
    {
        var scansDir = Path.Combine(workspaceFolderPath, ScansFolder);
        if (!Directory.Exists(scansDir))
            return new List<(string, DateTime)>();

        return Directory.EnumerateFiles(scansDir, "*_roslyn.json")
            .Select(f => (FilePath: f, ScanTime: File.GetLastWriteTimeUtc(f)))
            .OrderByDescending(x => x.ScanTime)
            .ToList();
    }

    // ── Project discovery ────────────────────────────────────────────────────

    private static List<ScannedProject> DiscoverProjects(string searchRoot, List<string> csFiles)
    {
        var csprojFiles = Directory
            .EnumerateFiles(searchRoot, "*.csproj", SearchOption.AllDirectories)
            .Where(f => !IsExcluded(f))
            .ToList();

        if (csprojFiles.Count == 0)
        {
            // No .csproj found — treat the root as a single virtual project.
            return new List<ScannedProject>
            {
                new ScannedProject
                {
                    Name = Path.GetFileName(searchRoot),
                    ProjectFilePath = string.Empty,
                    FolderPath = searchRoot,
                    FilePaths = csFiles
                }
            };
        }

        return csprojFiles.Select(proj =>
        {
            var projDir = Path.GetDirectoryName(proj) ?? string.Empty;
            var projFiles = csFiles
                .Where(f => f.StartsWith(projDir, StringComparison.OrdinalIgnoreCase))
                .ToList();
            return new ScannedProject
            {
                Name = Path.GetFileNameWithoutExtension(proj),
                ProjectFilePath = proj,
                FolderPath = projDir,
                FilePaths = projFiles
            };
        }).ToList();
    }

    // ── File parsing ─────────────────────────────────────────────────────────

    private static async Task<ScannedFile?> ParseFileAsync(
        string filePath, string searchRoot, CancellationToken ct)
    {
        try
        {
            var source = await File.ReadAllTextAsync(filePath, ct);
            var tree = CSharpSyntaxTree.ParseText(source,
                new CSharpParseOptions(LanguageVersion.Latest),
                filePath, cancellationToken: ct);

            var root = await tree.GetRootAsync(ct);

            var scannedFile = new ScannedFile
            {
                FilePath = filePath,
                RelativePath = Path.GetRelativePath(searchRoot, filePath)
            };

            var walker = new ClassWalker();
            walker.Visit(root);
            scannedFile.Classes = walker.Classes;

            return scannedFile;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Skip files that can't be parsed (generated, malformed, etc.)
            return null;
        }
    }

    // ── Suggested connections ─────────────────────────────────────────────────

    private static List<SuggestedConnection> BuildSuggestedConnections(List<ScannedFile> files)
    {
        // Build a lookup of all known class full names.
        var allClasses = files
            .SelectMany(f => f.Classes)
            .Select(c => c.FullName)
            .ToHashSet(StringComparer.Ordinal);

        // Aggregate call counts: (callerClass, calleeClass) → (count, callSites)
        var callMap = new Dictionary<(string, string), (int Count, List<string> Sites)>(
            EqualityComparer<(string, string)>.Default);

        foreach (var file in files)
        {
            foreach (var cls in file.Classes)
            {
                foreach (var method in cls.Methods)
                {
                    foreach (var callee in method.CalledClasses)
                    {
                        if (!allClasses.Contains(callee)) continue;
                        if (callee == cls.FullName) continue;   // ignore self-calls

                        var key = (cls.FullName, callee);
                        var callSite = $"{cls.FullName}.{method.Name}";
                        if (!callMap.TryGetValue(key, out var entry))
                        {
                            callMap[key] = (1, new List<string> { callSite });
                        }
                        else if (entry.Sites.Count < 10 && !entry.Sites.Contains(callSite))
                        {
                            entry.Sites.Add(callSite);
                            callMap[key] = (entry.Count + 1, entry.Sites);
                        }
                        else
                        {
                            callMap[key] = (entry.Count + 1, entry.Sites);
                        }
                    }
                }
            }
        }

        return callMap
            .Where(kvp => kvp.Value.Count > 0)
            .Select(kvp => new SuggestedConnection
            {
                Id = Guid.NewGuid().ToString(),
                FromClass = kvp.Key.Item1,
                ToClass = kvp.Key.Item2,
                CallCount = kvp.Value.Count,
                CallSites = kvp.Value.Sites
            })
            .OrderByDescending(c => c.CallCount)
            .ToList();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static bool IsExcluded(string filePath)
    {
        var norm = filePath.Replace('\\', '/');
        return norm.Contains("/obj/") || norm.Contains("/bin/") ||
               norm.Contains("/.vs/") || norm.Contains("/node_modules/") ||
               norm.EndsWith(".Designer.cs", StringComparison.OrdinalIgnoreCase) ||
               norm.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase) ||
               norm.EndsWith(".g.i.cs", StringComparison.OrdinalIgnoreCase);
    }

    // ── Syntax walker ─────────────────────────────────────────────────────────

    /// <summary>
    /// Walks a syntax tree and extracts class/method/call/logging information.
    /// </summary>
    private sealed class ClassWalker : CSharpSyntaxWalker
    {
        public List<ScannedClass> Classes { get; } = new();

        // Track the namespace context stack.
        private readonly Stack<string> _namespaceStack = new();

        public override void VisitNamespaceDeclaration(NamespaceDeclarationSyntax node)
        {
            _namespaceStack.Push(node.Name.ToString());
            base.VisitNamespaceDeclaration(node);
            _namespaceStack.Pop();
        }

        public override void VisitFileScopedNamespaceDeclaration(FileScopedNamespaceDeclarationSyntax node)
        {
            _namespaceStack.Push(node.Name.ToString());
            base.VisitFileScopedNamespaceDeclaration(node);
            _namespaceStack.Pop();
        }

        public override void VisitClassDeclaration(ClassDeclarationSyntax node)
            => VisitTypeDeclaration(node, "class");

        public override void VisitInterfaceDeclaration(InterfaceDeclarationSyntax node)
            => VisitTypeDeclaration(node, "interface");

        public override void VisitRecordDeclaration(RecordDeclarationSyntax node)
            => VisitTypeDeclaration(node, node.ClassOrStructKeyword.IsKind(SyntaxKind.StructKeyword)
                ? "record struct" : "record");

        private void VisitTypeDeclaration(TypeDeclarationSyntax node, string kind)
        {
            var ns = _namespaceStack.Count > 0 ? _namespaceStack.Peek() : string.Empty;
            var simpleName = node.Identifier.Text;
            var fullName = string.IsNullOrEmpty(ns) ? simpleName : $"{ns}.{simpleName}";

            var scannedClass = new ScannedClass
            {
                SimpleName = simpleName,
                FullName = fullName,
                Namespace = ns,
                Kind = kind,
                Methods = ExtractMethods(node, fullName)
            };

            Classes.Add(scannedClass);

            // Visit nested types (push namespace-like context).
            _namespaceStack.Push(fullName);
            foreach (var member in node.Members)
            {
                if (member is TypeDeclarationSyntax nested)
                    nested.Accept(this);
            }
            _namespaceStack.Pop();
        }

        private static List<ScannedMethod> ExtractMethods(TypeDeclarationSyntax typeNode, string ownerFullName)
        {
            var methods = new List<ScannedMethod>();

            foreach (var member in typeNode.Members)
            {
                if (member is not MethodDeclarationSyntax method) continue;

                var sm = new ScannedMethod
                {
                    Name = method.Identifier.Text,
                    ReturnType = method.ReturnType.ToString(),
                    Accessibility = GetAccessibility(method.Modifiers),
                    LineNumber = method.GetLocation().GetLineSpan().StartLinePosition.Line + 1
                };

                if (method.Body != null || method.ExpressionBody != null)
                {
                    SyntaxNode body = (SyntaxNode?)method.Body ?? method.ExpressionBody!;
                    sm.CalledClasses = ExtractCalledClasses(body);
                    sm.LoggingCalls = ExtractLoggingCalls(body);
                }

                methods.Add(sm);
            }

            return methods;
        }

        private static List<string> ExtractCalledClasses(SyntaxNode body)
        {
            // Collect member-access expressions like "SomeClass.Method()" or "someField.Method()".
            // We capture the left-hand side identifier as a potential class name.
            var names = new HashSet<string>(StringComparer.Ordinal);

            foreach (var invoc in body.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                if (invoc.Expression is MemberAccessExpressionSyntax memberAccess)
                {
                    var target = memberAccess.Expression;
                    string? candidate = target switch
                    {
                        IdentifierNameSyntax id => id.Identifier.Text,
                        MemberAccessExpressionSyntax inner => inner.Name.Identifier.Text,
                        _ => null
                    };

                    if (candidate != null && candidate.Length > 0 && char.IsUpper(candidate[0]))
                        names.Add(candidate);
                }
            }

            return names.ToList();
        }

        private static List<LoggingCallLocation> ExtractLoggingCalls(SyntaxNode body)
        {
            var results = new List<LoggingCallLocation>();

            foreach (var invoc in body.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                if (invoc.Expression is not MemberAccessExpressionSyntax memberAccess) continue;

                var methodName = memberAccess.Name.Identifier.Text;
                if (!LoggingMethodNames.Contains(methodName)) continue;

                var target = memberAccess.Expression.ToString();
                bool isLogger = LoggingTypeHints.Any(hint =>
                    target.EndsWith(hint, StringComparison.OrdinalIgnoreCase));

                if (!isLogger) continue;

                results.Add(new LoggingCallLocation
                {
                    LoggerExpression = target,
                    LogLevel = NormaliseLogLevel(methodName),
                    LineNumber = invoc.GetLocation().GetLineSpan().StartLinePosition.Line + 1
                });
            }

            return results;
        }

        private static string GetAccessibility(SyntaxTokenList modifiers)
        {
            if (modifiers.Any(SyntaxKind.PublicKeyword)) return "public";
            if (modifiers.Any(SyntaxKind.ProtectedKeyword) && modifiers.Any(SyntaxKind.InternalKeyword)) return "protected internal";
            if (modifiers.Any(SyntaxKind.ProtectedKeyword)) return "protected";
            if (modifiers.Any(SyntaxKind.InternalKeyword)) return "internal";
            if (modifiers.Any(SyntaxKind.PrivateKeyword)) return "private";
            return "private";
        }

        private static string NormaliseLogLevel(string methodName)
        {
            var lower = methodName.ToLowerInvariant();
            if (lower.Contains("trace")) return "Trace";
            if (lower.Contains("debug")) return "Debug";
            if (lower.Contains("info")) return "Information";
            if (lower.Contains("warn")) return "Warning";
            if (lower.Contains("error")) return "Error";
            if (lower.Contains("crit") || lower.Contains("fatal")) return "Critical";
            return "Unknown";
        }
    }
}
