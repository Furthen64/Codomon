using System.Text.RegularExpressions;

namespace Codomon.Desktop.Services;

/// <summary>
/// Produces stable, deterministic identity keys for System Map entities.
/// Keys are used to detect duplicates and drive upsert/merge logic.
/// </summary>
public static class SystemMapIdentity
{
    /// <summary>
    /// Creates a stable key for a System from its name and kind.
    /// Example: name="Clearview.Desktop", kind="DesktopApp" → "clearview.desktop|desktopapp"
    /// </summary>
    public static string CreateSystemKey(string name, string kind)
        => $"{NormalizeKeyPart(name)}|{NormalizeKeyPart(kind)}";

    /// <summary>
    /// Creates a stable key for a Module, scoped under its parent system key.
    /// </summary>
    public static string CreateModuleKey(string? systemKey, string moduleName)
        => string.IsNullOrWhiteSpace(systemKey)
            ? NormalizeKeyPart(moduleName)
            : $"{systemKey}::{NormalizeKeyPart(moduleName)}";

    /// <summary>
    /// Creates a stable key for a Code Node.
    /// Uses the fully-qualified name when available; otherwise falls back to
    /// project path + file path + name.
    /// </summary>
    public static string CreateCodeNodeKey(
        string? fullyQualifiedName,
        string? projectPath,
        string? filePath,
        string name)
    {
        if (!string.IsNullOrWhiteSpace(fullyQualifiedName))
            return NormalizeKeyPart(fullyQualifiedName);

        var proj = NormalizeKeyPart(projectPath ?? string.Empty);
        var file = NormalizeKeyPart(filePath ?? string.Empty);
        return $"{proj}::{file}::{NormalizeKeyPart(name)}";
    }

    /// <summary>Creates a stable key for an External System from its name and kind.</summary>
    public static string CreateExternalSystemKey(string name, string kind)
        => $"{NormalizeKeyPart(name)}|{NormalizeKeyPart(kind)}";

    /// <summary>Creates a stable key for a Relationship from source, target, and kind.</summary>
    public static string CreateRelationshipKey(string sourceKey, string targetKey, string relationshipKind)
        => $"{sourceKey}→{targetKey}|{NormalizeKeyPart(relationshipKind)}";

    /// <summary>
    /// Normalizes a single key component: trims, lowercases, collapses whitespace,
    /// and collapses repeated separators.
    /// </summary>
    public static string NormalizeKeyPart(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var s = value.Trim().ToLowerInvariant();
        s = Regex.Replace(s, @"\s+", " ");
        s = Regex.Replace(s, @"[|:]{2,}", "|");
        return s;
    }
}
