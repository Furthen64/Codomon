namespace Codomon.Desktop.Models.SystemMap;

/// <summary>How confident Codomon is that an item belongs where it has been placed.</summary>
public enum ConfidenceLevel
{
    /// <summary>Placement was set explicitly by a human and must not be overridden automatically.</summary>
    Manual,
    /// <summary>Placement has been verified by human review.</summary>
    Confirmed,
    /// <summary>Strong evidence suggests this placement is correct.</summary>
    Likely,
    /// <summary>Some evidence suggests this placement, but it is uncertain.</summary>
    Possible,
    /// <summary>No reliable evidence is available; placement is a guess.</summary>
    Unknown
}

/// <summary>The high-level type of a <see cref="SystemModel"/>.</summary>
public enum SystemKind
{
    DesktopApp,
    WebApp,
    Service,
    Worker,
    ScheduledJob,
    MaintenanceProcess,
    Other
}

/// <summary>The functional role of a <see cref="ModuleModel"/>.</summary>
public enum ModuleKind
{
    DataAccess,
    BusinessLogic,
    Presentation,
    Api,
    Infrastructure,
    Configuration,
    Utility,
    Integration,
    Other
}

/// <summary>The kind of concrete artefact represented by a <see cref="CodeNodeModel"/>.</summary>
public enum CodeNodeKind
{
    Class,
    Interface,
    Enum,
    Record,
    SourceFile,
    ConfigFile,
    EntryPoint,
    Script,
    Other
}

/// <summary>The nature of a <see cref="RelationshipModel"/> between two entities.</summary>
public enum RelationshipKind
{
    Calls,
    Imports,
    Depends,
    Configures,
    Logs,
    Publishes,
    Subscribes,
    Reads,
    Writes,
    Hosts,
    Other
}
