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
    BackendService,
    WorkerService,
    ScheduledJob,
    CliTool,
    DatabaseProcess,
    LibraryOnly,
    Unknown
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
    /// <summary>An MVVM ViewModel (name ends with <c>ViewModel</c>).</summary>
    ViewModel,
    /// <summary>A dialog window (name ends with <c>Dialog</c>).</summary>
    Dialog,
    /// <summary>A view / window (name ends with <c>Window</c>).</summary>
    View,
    /// <summary>A service class (name ends with <c>Service</c>).</summary>
    Service,
    /// <summary>A domain model (name ends with <c>Model</c>).</summary>
    Model,
    /// <summary>A data-transfer object (name ends with <c>Dto</c>).</summary>
    Dto,
    /// <summary>A repository (name ends with <c>Repository</c>).</summary>
    Repository,
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
