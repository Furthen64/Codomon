using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Codomon.Desktop.Models;

namespace Codomon.Desktop.Services;

/// <summary>
/// Scans project files and known repo markers to detect the technologies used by a workspace.
/// </summary>
public static class TechStackScanService
{
    private const string ScansFolder = "scans";
    private const string WorkspaceProjectName = "(workspace)";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static readonly string[] IgnoredDirectories =
    {
        ".git", ".vs", ".idea", "bin", "obj", "node_modules"
    };

    private static readonly string[] MarkerFilePatterns =
    {
        "Dockerfile",
        "docker-compose.yml",
        "docker-compose.yaml",
        "project.godot",
        "serilog.json",
        "serilog.*.json",
        "nlog.config",
        "nlog.*.config",
        "log4net.config",
        "log4net.*.config"
    };

    private sealed record TechnologyPattern(
        string PackageOrMarker,
        string Name,
        string Category,
        bool PrefixMatch = false);

    private static readonly TechnologyPattern[] PackagePatterns =
    {
        new("Avalonia", "Avalonia", "UI", PrefixMatch: true),
        new("GodotSharp", "Godot", "Game Engine", PrefixMatch: true),
        new("Godot.NET.Sdk", "Godot", "Game Engine", PrefixMatch: true),
        new("Microsoft.EntityFrameworkCore", "Entity Framework Core", "Data", PrefixMatch: true),
        new("Dapper", "Dapper", "Data"),
        new("Npgsql", "Npgsql", "Database", PrefixMatch: true),
        new("Microsoft.Data.SqlClient", "Microsoft.Data.SqlClient", "Database"),
        new("System.Data.SqlClient", "System.Data.SqlClient", "Database"),
        new("MongoDB.Driver", "MongoDB", "Database"),
        new("MassTransit", "MassTransit", "Messaging", PrefixMatch: true),
        new("RabbitMQ.Client", "RabbitMQ", "Messaging"),
        new("Azure.Messaging.ServiceBus", "Azure Service Bus", "Messaging"),
        new("Quartz", "Quartz", "Scheduling", PrefixMatch: true),
        new("Hangfire", "Hangfire", "Scheduling", PrefixMatch: true),
        new("Serilog", "Serilog", "Logging", PrefixMatch: true),
        new("NLog", "NLog", "Logging", PrefixMatch: true),
        new("log4net", "log4net", "Logging", PrefixMatch: true),
        new("OpenTelemetry", "OpenTelemetry", "Observability", PrefixMatch: true),
        new("xunit", "xUnit", "Testing", PrefixMatch: true),
        new("NUnit", "NUnit", "Testing", PrefixMatch: true),
        new("Moq", "Moq", "Testing")
    };

    public static async Task<TechStackAvailabilityResult> CheckAsync(string sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
            return new TechStackAvailabilityResult(false,
                "No source project path is configured for this workspace. Set the source path via the workspace setup wizard.",
                0, 0);

        if (!Directory.Exists(sourcePath) && !File.Exists(sourcePath))
            return new TechStackAvailabilityResult(false,
                $"The source path does not exist:\n{sourcePath}",
                0, 0);

        var searchRoot = GetSearchRoot(sourcePath);
        if (!Directory.Exists(searchRoot))
            return new TechStackAvailabilityResult(false,
                $"The source folder does not exist:\n{searchRoot}",
                0, 0);

        var projectFiles = await Task.Run(() => EnumerateProjectFiles(searchRoot).ToList());
        var godotProjectFiles = await Task.Run(() => EnumerateGodotProjectFiles(searchRoot).ToList());
        var markerCount = await Task.Run(() => CountKnownMarkers(searchRoot));

        if (projectFiles.Count == 0 && godotProjectFiles.Count == 0 && markerCount == 0)
        {
            return new TechStackAvailabilityResult(false,
                $"No project files or known stack markers were found under:\n{searchRoot}\n\n" +
                "Tech stack scanning needs at least one supported project file (such as .csproj or project.godot) or an infrastructure/config marker such as Dockerfile or serilog.json.",
                0, 0);
        }

        var projectCount = projectFiles.Count + godotProjectFiles.Count(projectFile =>
            !projectFiles.Any(csproj => string.Equals(
                Path.GetDirectoryName(csproj), Path.GetDirectoryName(projectFile), StringComparison.OrdinalIgnoreCase)));
        var summary = $"{projectCount} project(s) and {markerCount} known stack marker(s) found.";
        return new TechStackAvailabilityResult(true, summary, projectCount, markerCount);
    }

    public static async Task<TechStackScanResult> ScanAsync(
        string sourcePath,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var result = new TechStackScanResult
        {
            ScanTime = DateTime.UtcNow,
            SourcePath = sourcePath
        };

        var searchRoot = GetSearchRoot(sourcePath);
        progress?.Report($"Scanning tech stack under: {searchRoot}");

        var projectFiles = await Task.Run(() => EnumerateProjectFiles(searchRoot).OrderBy(p => p).ToList(), cancellationToken);
        var godotProjectFiles = await Task.Run(() => EnumerateGodotProjectFiles(searchRoot).OrderBy(p => p).ToList(), cancellationToken);
        progress?.Report($"Discovered {projectFiles.Count + godotProjectFiles.Count} project file(s).");

        var technologies = new Dictionary<string, DetectedTechnology>(StringComparer.OrdinalIgnoreCase);

        foreach (var projectFile in projectFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var projectName = Path.GetFileNameWithoutExtension(projectFile);
            var projectFolder = Path.GetDirectoryName(projectFile) ?? searchRoot;
            result.Projects.Add(new TechStackProject
            {
                Name = projectName,
                ProjectFilePath = projectFile,
                FolderPath = projectFolder
            });

            progress?.Report($"Analyzing project: {projectName}");
            await AnalyzeProjectFileAsync(projectFile, projectName, projectFolder, technologies, cancellationToken);
            DetectProjectFiles(projectName, projectFile, projectFolder, technologies);
        }

        foreach (var godotProjectFile in godotProjectFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var projectFolder = Path.GetDirectoryName(godotProjectFile) ?? searchRoot;
            var associatedCsproj = projectFiles.FirstOrDefault(projectFile =>
                string.Equals(Path.GetDirectoryName(projectFile), projectFolder, StringComparison.OrdinalIgnoreCase));
            var projectName = associatedCsproj == null
                ? Path.GetFileName(projectFolder)
                : Path.GetFileNameWithoutExtension(associatedCsproj);

            if (associatedCsproj == null)
            {
                result.Projects.Add(new TechStackProject
                {
                    Name = projectName,
                    ProjectFilePath = godotProjectFile,
                    FolderPath = projectFolder
                });
            }

            progress?.Report($"Analyzing Godot project: {projectName}");
            await AnalyzeGodotProjectFileAsync(godotProjectFile, projectName, associatedCsproj ?? godotProjectFile,
                technologies, cancellationToken);
        }

        DetectWorkspaceInfrastructure(searchRoot, projectFiles, technologies);

        result.Technologies = technologies.Values
            .OrderBy(t => t.Category, StringComparer.OrdinalIgnoreCase)
            .ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(t => t.ProjectName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        progress?.Report($"Tech stack scan complete. {result.Technologies.Count} technology entries across {result.Projects.Count} project(s).");
        return result;
    }

    public static async Task<string> SaveAsync(TechStackScanResult scanResult, string workspaceFolderPath)
    {
        var scansDir = Path.Combine(workspaceFolderPath, ScansFolder);
        Directory.CreateDirectory(scansDir);

        var timestamp = scanResult.ScanTime.ToString("yyyyMMdd_HHmmss");
        var filePath = Path.Combine(scansDir, $"{timestamp}_techstack.json");

        await using var stream = File.Create(filePath);
        await JsonSerializer.SerializeAsync(stream, scanResult, JsonOptions);
        return filePath;
    }

    public static async Task<TechStackScanResult?> LoadAsync(string filePath)
    {
        if (!File.Exists(filePath))
            return null;

        await using var stream = File.OpenRead(filePath);
        var result = await JsonSerializer.DeserializeAsync<TechStackScanResult>(stream, JsonOptions);
        if (result == null)
            return null;

        result.Projects ??= new List<TechStackProject>();
        result.Technologies ??= new List<DetectedTechnology>();
        return result;
    }

    public static List<(string FilePath, DateTime ScanTime)> ListSavedScans(string workspaceFolderPath)
    {
        var scansDir = Path.Combine(workspaceFolderPath, ScansFolder);
        if (!Directory.Exists(scansDir))
            return new List<(string, DateTime)>();

        return Directory.EnumerateFiles(scansDir, "*_techstack.json")
            .Select(path => (FilePath: path, ScanTime: File.GetLastWriteTimeUtc(path)))
            .OrderByDescending(entry => entry.ScanTime)
            .ToList();
    }

    private static string GetSearchRoot(string sourcePath)
        => Directory.Exists(sourcePath)
            ? sourcePath
            : Path.GetDirectoryName(sourcePath) ?? sourcePath;

    private static IEnumerable<string> EnumerateProjectFiles(string searchRoot)
        => Directory.EnumerateFiles(searchRoot, "*.csproj", SearchOption.AllDirectories)
            .Where(path => !IsExcluded(path));

    private static IEnumerable<string> EnumerateGodotProjectFiles(string searchRoot)
        => Directory.EnumerateFiles(searchRoot, "project.godot", SearchOption.AllDirectories)
            .Where(path => !IsExcluded(path));

    private static async Task AnalyzeProjectFileAsync(
        string projectFilePath,
        string projectName,
        string projectFolder,
        Dictionary<string, DetectedTechnology> technologies,
        CancellationToken cancellationToken)
    {
        var projectXml = await File.ReadAllTextAsync(projectFilePath, cancellationToken);
        var document = XDocument.Parse(projectXml, LoadOptions.PreserveWhitespace);
        var project = document.Root;
        if (project == null)
            return;

        var sdkValue = project.Attribute("Sdk")?.Value?.Trim() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(sdkValue))
        {
            if (sdkValue.Contains("Godot.NET.Sdk", StringComparison.OrdinalIgnoreCase))
            {
                AddOrUpdateTechnology(technologies, new DetectedTechnology
                {
                    Name = "Godot",
                    Category = "Game Engine",
                    Confidence = "Likely",
                    ProjectName = projectName,
                    ProjectFilePath = projectFilePath,
                    Evidence = new List<TechnologyEvidence>
                    {
                        new()
                        {
                            Source = "ProjectSdk",
                            Description = $"Project SDK '{sdkValue}' indicates a Godot .NET project.",
                            SourceRef = projectFilePath
                        }
                    }
                });
            }

            if (sdkValue.Contains("Web", StringComparison.OrdinalIgnoreCase))
            {
                AddOrUpdateTechnology(technologies, new DetectedTechnology
                {
                    Name = "ASP.NET Core",
                    Category = "Web",
                    Confidence = "Likely",
                    ProjectName = projectName,
                    ProjectFilePath = projectFilePath,
                    Evidence = new List<TechnologyEvidence>
                    {
                        new()
                        {
                            Source = "ProjectSdk",
                            Description = $"Project SDK '{sdkValue}' indicates an ASP.NET Core project.",
                            SourceRef = projectFilePath
                        }
                    }
                });
            }

            if (sdkValue.Contains("Worker", StringComparison.OrdinalIgnoreCase))
            {
                AddOrUpdateTechnology(technologies, new DetectedTechnology
                {
                    Name = "Worker Service",
                    Category = "Runtime",
                    Confidence = "Likely",
                    ProjectName = projectName,
                    ProjectFilePath = projectFilePath,
                    Evidence = new List<TechnologyEvidence>
                    {
                        new()
                        {
                            Source = "ProjectSdk",
                            Description = $"Project SDK '{sdkValue}' indicates a worker service project.",
                            SourceRef = projectFilePath
                        }
                    }
                });
            }
        }

        var targetFrameworks = project.Descendants()
            .Where(node => string.Equals(node.Name.LocalName, "TargetFramework", StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(node.Name.LocalName, "TargetFrameworks", StringComparison.OrdinalIgnoreCase))
            .SelectMany(node => (node.Value ?? string.Empty)
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (targetFrameworks.Count > 0)
        {
            AddOrUpdateTechnology(technologies, new DetectedTechnology
            {
                Name = ".NET",
                Category = "Runtime",
                Confidence = "Likely",
                Version = string.Join(", ", targetFrameworks),
                ProjectName = projectName,
                ProjectFilePath = projectFilePath,
                Evidence = new List<TechnologyEvidence>
                {
                    new()
                    {
                        Source = "TargetFramework",
                        Description = $"Project targets {string.Join(", ", targetFrameworks)}.",
                        SourceRef = projectFilePath
                    }
                }
            });
        }

        foreach (var packageReference in project.Descendants().Where(node => string.Equals(node.Name.LocalName, "PackageReference", StringComparison.OrdinalIgnoreCase)))
        {
            var include = packageReference.Attribute("Include")?.Value?.Trim();
            if (string.IsNullOrWhiteSpace(include))
                continue;

            var version = packageReference.Attribute("Version")?.Value?.Trim() ??
                          packageReference.Elements().FirstOrDefault(node => string.Equals(node.Name.LocalName, "Version", StringComparison.OrdinalIgnoreCase))?.Value?.Trim() ??
                          string.Empty;

            foreach (var pattern in PackagePatterns.Where(pattern => MatchesPattern(include, pattern)))
            {
                AddOrUpdateTechnology(technologies, new DetectedTechnology
                {
                    Name = pattern.Name,
                    Category = pattern.Category,
                    Confidence = "Likely",
                    Version = version,
                    ProjectName = projectName,
                    ProjectFilePath = projectFilePath,
                    Evidence = new List<TechnologyEvidence>
                    {
                        new()
                        {
                            Source = "PackageReference",
                            Description = $"PackageReference '{include}' matched '{pattern.Name}'.",
                            SourceRef = projectFilePath
                        }
                    }
                });
            }

            if (include.StartsWith("Microsoft.Extensions.Hosting", StringComparison.OrdinalIgnoreCase))
            {
                AddOrUpdateTechnology(technologies, new DetectedTechnology
                {
                    Name = "Generic Host",
                    Category = "Runtime",
                    Confidence = "Likely",
                    Version = version,
                    ProjectName = projectName,
                    ProjectFilePath = projectFilePath,
                    Evidence = new List<TechnologyEvidence>
                    {
                        new()
                        {
                            Source = "PackageReference",
                            Description = $"PackageReference '{include}' indicates Microsoft Generic Host usage.",
                            SourceRef = projectFilePath
                        }
                    }
                });
            }
        }

        var useAppHost = project.Descendants().FirstOrDefault(node =>
            string.Equals(node.Name.LocalName, "UseAppHost", StringComparison.OrdinalIgnoreCase))?.Value?.Trim();
        if (string.Equals(useAppHost, "true", StringComparison.OrdinalIgnoreCase))
        {
            AddEvidenceOnly(technologies, projectName, projectFilePath, ".NET",
                "ProjectProperty",
                "Project property 'UseAppHost=true' indicates an executable .NET host.",
                projectFilePath);
        }

        _ = projectFolder;
    }

    private static async Task AnalyzeGodotProjectFileAsync(
        string godotProjectFilePath,
        string projectName,
        string projectFilePath,
        Dictionary<string, DetectedTechnology> technologies,
        CancellationToken cancellationToken)
    {
        var projectConfig = await File.ReadAllTextAsync(godotProjectFilePath, cancellationToken);
        var versionMatch = Regex.Match(projectConfig,
            @"^config/features\s*=\s*PackedStringArray\(\s*""(?<version>\d+(?:\.\d+)+)",
            RegexOptions.Multiline);
        var version = versionMatch.Success ? versionMatch.Groups["version"].Value : string.Empty;

        AddOrUpdateTechnology(technologies, new DetectedTechnology
        {
            Name = "Godot",
            Category = "Game Engine",
            Confidence = "Likely",
            Version = version,
            ProjectName = projectName,
            ProjectFilePath = projectFilePath,
            Evidence = new List<TechnologyEvidence>
            {
                new()
                {
                    Source = "ProjectFile",
                    Description = "Godot project configuration file 'project.godot' found.",
                    SourceRef = godotProjectFilePath
                }
            }
        });
    }

    private static void DetectProjectFiles(
        string projectName,
        string projectFilePath,
        string projectFolder,
        Dictionary<string, DetectedTechnology> technologies)
    {
        foreach (var filePath in EnumerateFilesSafe(projectFolder, "serilog*.json"))
        {
            AddOrUpdateTechnology(technologies, new DetectedTechnology
            {
                Name = "Serilog",
                Category = "Logging",
                Confidence = "Possible",
                ProjectName = projectName,
                ProjectFilePath = projectFilePath,
                Evidence = new List<TechnologyEvidence>
                {
                    new()
                    {
                        Source = "ConfigFile",
                        Description = $"Configuration file '{Path.GetFileName(filePath)}' suggests Serilog usage.",
                        SourceRef = filePath
                    }
                }
            });
        }

        foreach (var filePath in EnumerateFilesSafe(projectFolder, "nlog*.config"))
        {
            AddOrUpdateTechnology(technologies, new DetectedTechnology
            {
                Name = "NLog",
                Category = "Logging",
                Confidence = "Possible",
                ProjectName = projectName,
                ProjectFilePath = projectFilePath,
                Evidence = new List<TechnologyEvidence>
                {
                    new()
                    {
                        Source = "ConfigFile",
                        Description = $"Configuration file '{Path.GetFileName(filePath)}' suggests NLog usage.",
                        SourceRef = filePath
                    }
                }
            });
        }

        foreach (var filePath in EnumerateFilesSafe(projectFolder, "log4net*.config"))
        {
            AddOrUpdateTechnology(technologies, new DetectedTechnology
            {
                Name = "log4net",
                Category = "Logging",
                Confidence = "Possible",
                ProjectName = projectName,
                ProjectFilePath = projectFilePath,
                Evidence = new List<TechnologyEvidence>
                {
                    new()
                    {
                        Source = "ConfigFile",
                        Description = $"Configuration file '{Path.GetFileName(filePath)}' suggests log4net usage.",
                        SourceRef = filePath
                    }
                }
            });
        }

        foreach (var filePath in EnumerateFilesSafe(projectFolder, "Dockerfile"))
        {
            AddOrUpdateTechnology(technologies, new DetectedTechnology
            {
                Name = "Docker",
                Category = "Infrastructure",
                Confidence = "Likely",
                ProjectName = projectName,
                ProjectFilePath = projectFilePath,
                Evidence = new List<TechnologyEvidence>
                {
                    new()
                    {
                        Source = "InfrastructureFile",
                        Description = "Dockerfile found in the project folder.",
                        SourceRef = filePath
                    }
                }
            });
        }
    }

    private static void DetectWorkspaceInfrastructure(
        string searchRoot,
        List<string> projectFiles,
        Dictionary<string, DetectedTechnology> technologies)
    {
        var knownProjectDirectories = projectFiles
            .Select(path => Path.GetDirectoryName(path))
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => path!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var filePath in EnumerateFilesSafe(searchRoot, "docker-compose.yml")
            .Concat(EnumerateFilesSafe(searchRoot, "docker-compose.yaml")))
        {
            AddOrUpdateTechnology(technologies, new DetectedTechnology
            {
                Name = "docker-compose",
                Category = "Infrastructure",
                Confidence = "Likely",
                ProjectName = WorkspaceProjectName,
                ProjectFilePath = string.Empty,
                Evidence = new List<TechnologyEvidence>
                {
                    new()
                    {
                        Source = "InfrastructureFile",
                        Description = "docker-compose file found in the workspace.",
                        SourceRef = filePath
                    }
                }
            });
        }

        foreach (var filePath in EnumerateFilesSafe(searchRoot, "Dockerfile"))
        {
            var folder = Path.GetDirectoryName(filePath) ?? string.Empty;
            if (knownProjectDirectories.Contains(folder))
                continue;

            AddOrUpdateTechnology(technologies, new DetectedTechnology
            {
                Name = "Docker",
                Category = "Infrastructure",
                Confidence = "Possible",
                ProjectName = WorkspaceProjectName,
                ProjectFilePath = string.Empty,
                Evidence = new List<TechnologyEvidence>
                {
                    new()
                    {
                        Source = "InfrastructureFile",
                        Description = "Dockerfile found outside an individual project folder.",
                        SourceRef = filePath
                    }
                }
            });
        }
    }

    private static void AddEvidenceOnly(
        Dictionary<string, DetectedTechnology> technologies,
        string projectName,
        string projectFilePath,
        string technologyName,
        string source,
        string description,
        string sourceRef)
    {
        var existing = technologies.Values.FirstOrDefault(entry =>
            string.Equals(entry.Name, technologyName, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(entry.ProjectName, projectName, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(entry.ProjectFilePath, projectFilePath, StringComparison.OrdinalIgnoreCase));

        if (existing == null)
            return;

        AddEvidence(existing, source, description, sourceRef);
    }

    private static void AddOrUpdateTechnology(
        Dictionary<string, DetectedTechnology> technologies,
        DetectedTechnology candidate)
    {
        var key = BuildTechnologyKey(candidate.ProjectFilePath, candidate.ProjectName, candidate.Name);
        if (!technologies.TryGetValue(key, out var existing))
        {
            technologies[key] = candidate;
            return;
        }

        if (string.IsNullOrWhiteSpace(existing.Version) && !string.IsNullOrWhiteSpace(candidate.Version))
            existing.Version = candidate.Version;

        if (string.Equals(existing.Confidence, "Possible", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(candidate.Confidence, "Likely", StringComparison.OrdinalIgnoreCase))
        {
            existing.Confidence = candidate.Confidence;
        }

        foreach (var evidence in candidate.Evidence)
            AddEvidence(existing, evidence.Source, evidence.Description, evidence.SourceRef);
    }

    private static void AddEvidence(DetectedTechnology technology, string source, string description, string sourceRef)
    {
        if (technology.Evidence.Any(existing =>
            string.Equals(existing.Source, source, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(existing.Description, description, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(existing.SourceRef, sourceRef, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        technology.Evidence.Add(new TechnologyEvidence
        {
            Source = source,
            Description = description,
            SourceRef = sourceRef
        });
    }

    private static string BuildTechnologyKey(string projectFilePath, string projectName, string technologyName)
        => $"{projectFilePath}|{projectName}|{technologyName}".ToLowerInvariant();

    private static bool MatchesPattern(string packageName, TechnologyPattern pattern)
        => pattern.PrefixMatch
            ? packageName.StartsWith(pattern.PackageOrMarker, StringComparison.OrdinalIgnoreCase)
            : string.Equals(packageName, pattern.PackageOrMarker, StringComparison.OrdinalIgnoreCase);

    private static int CountKnownMarkers(string searchRoot)
        => MarkerFilePatterns.Sum(pattern => EnumerateFilesSafe(searchRoot, pattern).Count());

    private static IEnumerable<string> EnumerateFilesSafe(string root, string searchPattern)
    {
        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(root, searchPattern, SearchOption.AllDirectories)
                .Where(path => !IsExcluded(path));
        }
        catch
        {
            return Enumerable.Empty<string>();
        }

        return files;
    }

    private static bool IsExcluded(string path)
    {
        var normalized = path.Replace('\\', '/');
        return IgnoredDirectories.Any(directory =>
            normalized.Contains($"/{directory}/", StringComparison.OrdinalIgnoreCase));
    }
}
