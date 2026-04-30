namespace Codomon.Desktop.Persistence.Dto;

// ── System Map DTOs ───────────────────────────────────────────────────────────
// These types mirror Models.SystemMap but use plain string fields so they remain
// stable across schema changes and are safe for JSON round-trips.

public class EvidenceDto
{
    public string Source { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string SourceRef { get; set; } = string.Empty;
}

public class CodeNodeDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Kind { get; set; } = "Class";
    public string FullName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public string Confidence { get; set; } = "Unknown";
    public List<EvidenceDto> Evidence { get; set; } = new();
    public bool IsHighValue { get; set; }
    public bool IsNoisy { get; set; }
    public bool HideFromOverview { get; set; }
}

public class ModuleDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Kind { get; set; } = "Other";
    public string Notes { get; set; } = string.Empty;
    public string Confidence { get; set; } = "Unknown";
    public List<string> SystemIds { get; set; } = new();
    public List<CodeNodeDto> CodeNodes { get; set; } = new();
    public List<EvidenceDto> Evidence { get; set; } = new();
}

public class SystemDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Kind { get; set; } = "Unknown";
    public string Notes { get; set; } = string.Empty;
    public string Confidence { get; set; } = "Unknown";
    public string StartupMechanism { get; set; } = string.Empty;
    public List<string> EntryPointCandidates { get; set; } = new();
    public List<string> ConfigFileCandidates { get; set; } = new();
    public List<string> LogFileCandidates { get; set; } = new();
    public List<ModuleDto> Modules { get; set; } = new();
    public List<EvidenceDto> Evidence { get; set; } = new();
}

public class ExternalSystemDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public string Confidence { get; set; } = "Unknown";
    public List<EvidenceDto> Evidence { get; set; } = new();
}

public class RelationshipDto
{
    public string Id { get; set; } = string.Empty;
    public string Kind { get; set; } = "Depends";
    public string FromId { get; set; } = string.Empty;
    public string ToId { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public string Confidence { get; set; } = "Unknown";
    public List<EvidenceDto> Evidence { get; set; } = new();
}

public class ManualOverrideDto
{
    public string Id { get; set; } = string.Empty;
    public string TargetId { get; set; } = string.Empty;
    /// <summary>String representation of <see cref="ManualOverrideType"/> for JSON stability.</summary>
    public string OverrideType { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class SystemMapFileDto
{
    public string Schema { get; set; } = "codomon-systemmap/1";
    public string Id { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public List<SystemDto> Systems { get; set; } = new();
    /// <summary>Top-level modules not embedded inside a system (may be shared across systems).</summary>
    public List<ModuleDto> Modules { get; set; } = new();
    public List<ExternalSystemDto> ExternalSystems { get; set; } = new();
    public List<RelationshipDto> Relationships { get; set; } = new();
    public List<ManualOverrideDto> ManualOverrides { get; set; } = new();
}
