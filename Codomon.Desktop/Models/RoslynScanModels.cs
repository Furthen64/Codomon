namespace Codomon.Desktop.Models;

// ── Roslyn scan data models ───────────────────────────────────────────────────

/// <summary>A logging call detected inside a method body.</summary>
public class LoggingCallLocation
{
    public string LoggerExpression { get; set; } = string.Empty;
    public string LogLevel { get; set; } = string.Empty;
    public int LineNumber { get; set; }
}

/// <summary>A scanned C# method.</summary>
public class ScannedMethod
{
    public string Name { get; set; } = string.Empty;
    public string ReturnType { get; set; } = string.Empty;
    public string Accessibility { get; set; } = string.Empty;
    public int LineNumber { get; set; }
    public List<string> CalledClasses { get; set; } = new();
    public List<LoggingCallLocation> LoggingCalls { get; set; } = new();
}

/// <summary>A scanned C# class or interface.</summary>
public class ScannedClass
{
    public string SimpleName { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Namespace { get; set; } = string.Empty;
    public string Kind { get; set; } = "class";
    public List<ScannedMethod> Methods { get; set; } = new();
}

/// <summary>A scanned C# file.</summary>
public class ScannedFile
{
    public string FilePath { get; set; } = string.Empty;
    public string RelativePath { get; set; } = string.Empty;
    public List<ScannedClass> Classes { get; set; } = new();
}

/// <summary>A project discovered under the source path.</summary>
public class ScannedProject
{
    public string Name { get; set; } = string.Empty;
    public string ProjectFilePath { get; set; } = string.Empty;
    public string FolderPath { get; set; } = string.Empty;
    public List<string> FilePaths { get; set; } = new();
}

/// <summary>
/// A connection suggested by Roslyn based on inter-class method calls.
/// These are stored in the scan result and can be promoted to the workspace.
/// </summary>
public class SuggestedConnection
{
    public string Id { get; set; } = string.Empty;
    public string FromClass { get; set; } = string.Empty;
    public string ToClass { get; set; } = string.Empty;
    public int CallCount { get; set; }
    public List<string> CallSites { get; set; } = new();
    public bool IsPromoted { get; set; } = false;
}

/// <summary>The top-level result of a Roslyn scan.</summary>
public class RoslynScanResult
{
    public string Schema { get; set; } = "codomon-scan/1";
    public DateTime ScanTime { get; set; }
    public string SourcePath { get; set; } = string.Empty;
    public List<ScannedProject> Projects { get; set; } = new();
    public List<ScannedFile> Files { get; set; } = new();
    public List<SuggestedConnection> SuggestedConnections { get; set; } = new();

    /// <summary>IDs of workspace connections that were promoted from this scan.</summary>
    public List<string> PromotedConnectionIds { get; set; } = new();
}
