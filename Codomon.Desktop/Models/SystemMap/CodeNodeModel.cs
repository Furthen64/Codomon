namespace Codomon.Desktop.Models.SystemMap;

/// <summary>
/// A concrete item discovered in the codebase, such as a class, source file, interface,
/// enum, config file, or entry point.
/// </summary>
public class CodeNodeModel
{
    /// <summary>Unique identifier for this code node.</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>Display name (e.g. simple class name or file name).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The kind of artefact this node represents.</summary>
    public CodeNodeKind Kind { get; set; } = CodeNodeKind.Class;

    /// <summary>Fully-qualified name where applicable (e.g. namespace + class name).</summary>
    public string FullName { get; set; } = string.Empty;

    /// <summary>Relative path to the source file within the codebase.</summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>Optional human-provided notes.</summary>
    public string Notes { get; set; } = string.Empty;

    /// <summary>Confidence that this node belongs in its assigned location.</summary>
    public ConfidenceLevel Confidence { get; set; } = ConfidenceLevel.Unknown;

    /// <summary>Evidence that supports this node's placement.</summary>
    public List<EvidenceModel> Evidence { get; set; } = new();
}
