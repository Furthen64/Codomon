using Codomon.Desktop.Models;
using Codomon.Desktop.Models.SystemMap;

namespace Codomon.Desktop.Services;

/// <summary>
/// Analyses a <see cref="RoslynScanResult"/> to detect candidate <see cref="SystemModel"/>
/// entries — one per discovered project — using hard facts such as project SDK, entry-point
/// files, config files, Dockerfiles, and Roslyn-extracted base-type information.
/// </summary>
public static class SystemDetector
{
    // ── Evidence source labels ────────────────────────────────────────────────

    private const string SrcProjectFile = "ProjectFile";
    private const string SrcEntryPoint = "EntryPoint";
    private const string SrcRoslyn = "Roslyn";
    private const string SrcConfigFile = "ConfigFile";
    private const string SrcDocker = "Docker";

    // ── Known base-type names that signal a Worker/Background service ─────────

    private static readonly HashSet<string> BackgroundServiceBaseTypes =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "BackgroundService", "IHostedService"
        };

    // ── Scheduled job framework markers ──────────────────────────────────────

    private static readonly string[] ScheduledJobTypeHints =
    {
        "IJob", "ICronJob", "IScheduledTask", "RecurringJob", "CronJob",
        "QuartzJob", "HangfireJob"
    };

    // ── Logging config file name patterns ────────────────────────────────────

    private static readonly string[] LogConfigPatterns =
    {
        "nlog.config", "nlog.*.config",
        "log4net.config", "log4net.*.config",
        "serilog.json", "serilog.*.json"
    };

    // ── Public entry point ────────────────────────────────────────────────────

    /// <summary>
    /// Detects <see cref="SystemModel"/> candidates from the supplied scan result.
    /// Returns one candidate per project; candidates with no evidence are omitted.
    /// </summary>
    public static async Task<List<SystemModel>> DetectAsync(
        RoslynScanResult scan,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        // Build a fast lookup: absolute file path → ScannedFile so detectors can
        // access Roslyn-extracted class information without re-reading the files.
        var fileLookup = scan.Files.ToDictionary(
            f => f.FilePath,
            f => f,
            StringComparer.OrdinalIgnoreCase);

        var systems = new List<SystemModel>();

        foreach (var project in scan.Projects)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report($"Detecting system: {project.Name}…");

            var system = await DetectProjectAsync(project, fileLookup, cancellationToken);
            if (system != null)
                systems.Add(system);
        }

        progress?.Report($"System detection complete. {systems.Count} candidate(s) found.");
        return systems;
    }

    // ── Per-project detection ─────────────────────────────────────────────────

    private static async Task<SystemModel?> DetectProjectAsync(
        ScannedProject project,
        Dictionary<string, ScannedFile> fileLookup,
        CancellationToken ct)
    {
        var evidence = new List<EvidenceModel>();
        var entryPoints = new List<string>();
        var configFiles = new List<string>();
        var logFiles = new List<string>();

        // Detection flags
        bool isExecutable = false;
        bool isWebSdk = false;
        bool isWorkerSdk = false;
        bool hasAppXaml = false;
        bool hasWindowsService = false;
        bool hasHostedService = false;
        bool hasWebApp = false;
        bool hasScheduledJob = false;
        bool hasStaThread = false;
        string startupMechanism = string.Empty;

        // 1 ── Analyse .csproj ─────────────────────────────────────────────────
        if (!string.IsNullOrEmpty(project.ProjectFilePath) &&
            File.Exists(project.ProjectFilePath))
        {
            var csprojContent = await ReadFileSafeAsync(project.ProjectFilePath, ct);
            if (csprojContent != null)
                AnalyseCsproj(csprojContent, project.ProjectFilePath, evidence,
                    ref isExecutable, ref isWebSdk, ref isWorkerSdk);
        }

        // 2 ── Inspect file paths known from the scan ──────────────────────────
        foreach (var filePath in project.FilePaths)
        {
            var fileName = Path.GetFileName(filePath);

            if (string.Equals(fileName, "Program.cs", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(fileName, "Startup.cs", StringComparison.OrdinalIgnoreCase))
            {
                entryPoints.Add(filePath);
                evidence.Add(new EvidenceModel
                {
                    Source = SrcEntryPoint,
                    Description = $"Found entry-point file: {fileName}",
                    SourceRef = filePath
                });
            }
        }

        // 3 ── Enumerate non-.cs files in the project folder ──────────────────
        if (!string.IsNullOrEmpty(project.FolderPath) &&
            Directory.Exists(project.FolderPath))
        {
            FindAppXamlFiles(project.FolderPath, entryPoints, evidence, ref hasAppXaml);
            FindConfigFiles(project.FolderPath, configFiles, evidence);
            FindLogConfigFiles(project.FolderPath, logFiles, evidence);
            FindDockerFiles(project.FolderPath, evidence);
        }

        // 4 ── Analyse Program.cs content ─────────────────────────────────────
        var programCsPath = entryPoints.FirstOrDefault(f =>
            string.Equals(Path.GetFileName(f), "Program.cs", StringComparison.OrdinalIgnoreCase));

        if (programCsPath != null)
        {
            var content = await ReadFileSafeAsync(programCsPath, ct);
            if (content != null)
            {
                AnalyseProgramCs(content, programCsPath, evidence,
                    ref hasWebApp, ref hasWindowsService, ref hasHostedService,
                    ref hasStaThread, ref startupMechanism);
            }
        }

        // 5 ── Use Roslyn class data (base types) ──────────────────────────────
        foreach (var filePath in project.FilePaths)
        {
            if (!fileLookup.TryGetValue(filePath, out var scannedFile)) continue;

            foreach (var cls in scannedFile.Classes)
            {
                if (!hasHostedService)
                {
                    var matchedBaseType = cls.BaseTypes.FirstOrDefault(bt =>
                        BackgroundServiceBaseTypes.Contains(bt));

                    if (matchedBaseType != null)
                    {
                        hasHostedService = true;
                        evidence.Add(new EvidenceModel
                        {
                            Source = SrcRoslyn,
                            Description =
                                $"Class '{cls.SimpleName}' inherits from / implements '{matchedBaseType}' — hosted/background service.",
                            SourceRef = filePath
                        });
                    }
                }

                if (!hasScheduledJob &&
                    cls.BaseTypes.Any(bt => ScheduledJobTypeHints.Any(h =>
                        bt.Contains(h, StringComparison.OrdinalIgnoreCase))))
                {
                    hasScheduledJob = true;
                    evidence.Add(new EvidenceModel
                    {
                        Source = SrcRoslyn,
                        Description =
                            $"Class '{cls.SimpleName}' implements a scheduled-job interface.",
                        SourceRef = filePath
                    });
                }
            }
        }

        // 6 ── Determine kind and confidence ───────────────────────────────────
        var (kind, signals) = DetermineKind(
            isExecutable, isWebSdk, isWorkerSdk,
            hasAppXaml, hasWebApp, hasWindowsService,
            hasHostedService, hasScheduledJob, hasStaThread,
            entryPoints);

        if (string.IsNullOrEmpty(startupMechanism))
            startupMechanism = InferStartupMechanism(kind, hasAppXaml, isWorkerSdk, entryPoints);

        var confidence = signals >= 2 ? ConfidenceLevel.Likely
            : signals == 1 ? ConfidenceLevel.Possible
            : evidence.Count > 0 ? ConfidenceLevel.Possible
            : ConfidenceLevel.Unknown;

        // Skip projects that are completely opaque (no evidence at all).
        if (kind == SystemKind.Unknown && evidence.Count == 0)
            return null;

        return new SystemModel
        {
            Id = Guid.NewGuid().ToString(),
            Name = project.Name,
            Kind = kind,
            StartupMechanism = startupMechanism,
            EntryPointCandidates = entryPoints,
            ConfigFileCandidates = configFiles,
            LogFileCandidates = logFiles,
            Confidence = confidence,
            Evidence = evidence
        };
    }

    // ── Detection helpers ─────────────────────────────────────────────────────

    private static void AnalyseCsproj(
        string content, string projectFilePath,
        List<EvidenceModel> evidence,
        ref bool isExecutable, ref bool isWebSdk, ref bool isWorkerSdk)
    {
        // OutputType element
        var outputTypeStart = content.IndexOf("<OutputType>", StringComparison.OrdinalIgnoreCase);
        if (outputTypeStart >= 0)
        {
            var valueStart = outputTypeStart + "<OutputType>".Length;
            var valueEnd = content.IndexOf("</OutputType>", valueStart, StringComparison.OrdinalIgnoreCase);
            if (valueEnd > valueStart)
            {
                var outputType = content[valueStart..valueEnd].Trim();
                if (string.Equals(outputType, "Exe", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(outputType, "WinExe", StringComparison.OrdinalIgnoreCase))
                {
                    isExecutable = true;
                    evidence.Add(new EvidenceModel
                    {
                        Source = SrcProjectFile,
                        Description = $"OutputType is '{outputType}' — executable project.",
                        SourceRef = projectFilePath
                    });
                }
                else if (string.Equals(outputType, "Library", StringComparison.OrdinalIgnoreCase))
                {
                    evidence.Add(new EvidenceModel
                    {
                        Source = SrcProjectFile,
                        Description = "OutputType is 'Library' — class library, not directly startable.",
                        SourceRef = projectFilePath
                    });
                }
            }
        }

        // SDK attribute — governs kind even when OutputType is absent
        if (content.Contains("Microsoft.NET.Sdk.Web", StringComparison.OrdinalIgnoreCase))
        {
            isWebSdk = true;
            isExecutable = true;
            evidence.Add(new EvidenceModel
            {
                Source = SrcProjectFile,
                Description = "Project SDK is 'Microsoft.NET.Sdk.Web' — ASP.NET Core web application.",
                SourceRef = projectFilePath
            });
        }
        else if (content.Contains("Microsoft.NET.Sdk.Worker", StringComparison.OrdinalIgnoreCase))
        {
            isWorkerSdk = true;
            isExecutable = true;
            evidence.Add(new EvidenceModel
            {
                Source = SrcProjectFile,
                Description = "Project SDK is 'Microsoft.NET.Sdk.Worker' — Worker Service project.",
                SourceRef = projectFilePath
            });
        }

        // Service installer / install-util markers
        if (content.Contains("ServiceInstaller", StringComparison.OrdinalIgnoreCase) ||
            content.Contains("ServiceProcessInstaller", StringComparison.OrdinalIgnoreCase))
        {
            evidence.Add(new EvidenceModel
            {
                Source = SrcProjectFile,
                Description = "Project references service-installer types — Windows Service candidate.",
                SourceRef = projectFilePath
            });
        }
    }

    private static void AnalyseProgramCs(
        string content, string filePath,
        List<EvidenceModel> evidence,
        ref bool hasWebApp, ref bool hasWindowsService,
        ref bool hasHostedService, ref bool hasStaThread,
        ref string startupMechanism)
    {
        if (content.Contains("WebApplication.CreateBuilder", StringComparison.Ordinal) ||
            content.Contains("WebHost.CreateDefaultBuilder", StringComparison.Ordinal))
        {
            hasWebApp = true;
            startupMechanism = "ASP.NET Core";
            evidence.Add(new EvidenceModel
            {
                Source = SrcEntryPoint,
                Description = "Program.cs calls WebApplication.CreateBuilder — ASP.NET Core web application.",
                SourceRef = filePath
            });
        }

        if (content.Contains(".AddWindowsService(", StringComparison.Ordinal) ||
            content.Contains(".UseWindowsService(", StringComparison.Ordinal))
        {
            hasWindowsService = true;
            if (string.IsNullOrEmpty(startupMechanism))
                startupMechanism = "Windows Service";
            evidence.Add(new EvidenceModel
            {
                Source = SrcEntryPoint,
                Description = "Program.cs registers a Windows Service lifetime — Windows Service.",
                SourceRef = filePath
            });
        }

        if (!hasWebApp &&
            (content.Contains(".AddHostedService<", StringComparison.Ordinal) ||
             content.Contains("Host.CreateDefaultBuilder", StringComparison.Ordinal) ||
             content.Contains("IHostBuilder", StringComparison.Ordinal)))
        {
            hasHostedService = true;
            if (string.IsNullOrEmpty(startupMechanism))
                startupMechanism = "Generic Host";
            evidence.Add(new EvidenceModel
            {
                Source = SrcEntryPoint,
                Description = "Program.cs uses Generic Host / AddHostedService — Worker or Background Service.",
                SourceRef = filePath
            });
        }

        if (content.Contains("[STAThread]", StringComparison.Ordinal))
        {
            hasStaThread = true;
            if (string.IsNullOrEmpty(startupMechanism))
                startupMechanism = "WinForms Application";
            evidence.Add(new EvidenceModel
            {
                Source = SrcEntryPoint,
                Description = "Program.cs has [STAThread] — Windows Forms or native desktop application.",
                SourceRef = filePath
            });
        }
    }

    private static void FindAppXamlFiles(
        string folderPath, List<string> entryPoints,
        List<EvidenceModel> evidence, ref bool hasAppXaml)
    {
        var patterns = new[] { "App.xaml", "App.xaml.cs", "App.axaml", "App.axaml.cs" };
        foreach (var pattern in patterns)
        {
            var found = Directory
                .EnumerateFiles(folderPath, pattern, SearchOption.AllDirectories)
                .Where(f => !IsExcluded(f))
                .FirstOrDefault();

            if (found == null) continue;

            if (!hasAppXaml)
            {
                hasAppXaml = true;
                evidence.Add(new EvidenceModel
                {
                    Source = SrcEntryPoint,
                    Description =
                        $"Found desktop application entry point: {Path.GetFileName(found)}",
                    SourceRef = found
                });
            }

            entryPoints.Add(found);
        }
    }

    private static void FindConfigFiles(
        string folderPath, List<string> configFiles,
        List<EvidenceModel> evidence)
    {
        var patterns = new[] { "appsettings.json", "appsettings.*.json" };
        bool reported = false;
        foreach (var pattern in patterns)
        {
            foreach (var f in Directory
                .EnumerateFiles(folderPath, pattern, SearchOption.AllDirectories)
                .Where(f => !IsExcluded(f)))
            {
                configFiles.Add(f);
                if (!reported)
                {
                    reported = true;
                    evidence.Add(new EvidenceModel
                    {
                        Source = SrcConfigFile,
                        Description =
                            $"Found ASP.NET/Worker configuration file: {Path.GetFileName(f)}",
                        SourceRef = f
                    });
                }
            }
        }
    }

    private static void FindLogConfigFiles(
        string folderPath, List<string> logFiles,
        List<EvidenceModel> evidence)
    {
        bool reported = false;
        foreach (var pattern in LogConfigPatterns)
        {
            foreach (var f in Directory
                .EnumerateFiles(folderPath, pattern, SearchOption.AllDirectories)
                .Where(f => !IsExcluded(f)))
            {
                logFiles.Add(f);
                if (!reported)
                {
                    reported = true;
                    evidence.Add(new EvidenceModel
                    {
                        Source = SrcConfigFile,
                        Description =
                            $"Found logging configuration file: {Path.GetFileName(f)}",
                        SourceRef = f
                    });
                }
            }
        }
    }

    private static void FindDockerFiles(
        string folderPath, List<EvidenceModel> evidence)
    {
        var dockerCandidates = new[] { "Dockerfile", "docker-compose.yml", "docker-compose.yaml" };

        // Check both the project folder and its parent (monorepo layout).
        var searchDirs = new[]
        {
            folderPath,
            Path.GetDirectoryName(folderPath) ?? folderPath
        }.Distinct().ToArray();

        foreach (var dir in searchDirs)
        {
            foreach (var name in dockerCandidates)
            {
                var path = Path.Combine(dir, name);
                if (!File.Exists(path)) continue;

                evidence.Add(new EvidenceModel
                {
                    Source = SrcDocker,
                    Description =
                        $"Found Docker configuration file '{name}' — this system may be containerised.",
                    SourceRef = path
                });
                return; // one Docker evidence entry is enough per project
            }
        }
    }

    // ── Kind determination ────────────────────────────────────────────────────

    private static (SystemKind Kind, int Signals) DetermineKind(
        bool isExecutable, bool isWebSdk, bool isWorkerSdk,
        bool hasAppXaml, bool hasWebApp, bool hasWindowsService,
        bool hasHostedService, bool hasScheduledJob, bool hasStaThread,
        List<string> entryPoints)
    {
        if (hasAppXaml || hasStaThread)
            return (SystemKind.DesktopApp, (hasAppXaml ? 1 : 0) + (hasStaThread ? 1 : 0));

        if (hasWebApp || isWebSdk)
            return (SystemKind.WebApp, (hasWebApp ? 1 : 0) + (isWebSdk ? 1 : 0));

        if (hasScheduledJob)
            return (SystemKind.ScheduledJob, 1);

        if (hasWindowsService)
            return (SystemKind.BackendService, 1 + (hasHostedService ? 1 : 0));

        if (hasHostedService || isWorkerSdk)
            return (SystemKind.WorkerService, (hasHostedService ? 1 : 0) + (isWorkerSdk ? 1 : 0));

        if (!isExecutable)
            // OutputType Library or SDK default — clearly not startable.
            return (SystemKind.LibraryOnly, 2);

        if (entryPoints.Count > 0)
            // Executable with a recognised entry-point file but no framework signals.
            return (SystemKind.BackendService, 1);

        return (SystemKind.Unknown, 0);
    }

    private static string InferStartupMechanism(
        SystemKind kind, bool hasAppXaml, bool isWorkerSdk,
        List<string> entryPoints)
    {
        return kind switch
        {
            SystemKind.DesktopApp => entryPoints.Any(f =>
                f.EndsWith(".axaml", StringComparison.OrdinalIgnoreCase) ||
                f.EndsWith(".axaml.cs", StringComparison.OrdinalIgnoreCase))
                    ? "Avalonia Application"
                    : hasAppXaml ? "WPF Application" : "Desktop Application",
            SystemKind.WebApp => "ASP.NET Core",
            SystemKind.WorkerService => isWorkerSdk ? "Worker SDK" : "Generic Host",
            SystemKind.BackendService => "Console Application",
            SystemKind.ScheduledJob => "Scheduled Job",
            SystemKind.CliTool => "Console Application",
            SystemKind.LibraryOnly => "Class Library",
            _ => string.Empty
        };
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static bool IsExcluded(string filePath)
    {
        var norm = filePath.Replace('\\', '/');
        return norm.Contains("/obj/") || norm.Contains("/bin/");
    }

    private static async Task<string?> ReadFileSafeAsync(string path, CancellationToken ct)
    {
        try
        {
            return await File.ReadAllTextAsync(path, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }
}
