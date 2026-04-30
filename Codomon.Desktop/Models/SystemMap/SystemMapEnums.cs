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

/// <summary>
/// The kind of manual correction recorded in a <see cref="ManualOverrideModel"/>.
/// </summary>
public enum ManualOverrideType
{
    /// <summary>Unrecognised or unset override type. Used as a safe fall-back during deserialisation.</summary>
    Unknown = 0,

    /// <summary>Rename a System, Module, or Code Node. <c>Value</c> = new name.</summary>
    Rename,

    /// <summary>Assign a Module or Code Node to a System. <c>Value</c> = target system ID.</summary>
    AssignToSystem,

    /// <summary>Move a Code Node to a different Module. <c>Value</c> = target module ID.</summary>
    AssignToModule,

    /// <summary>Mark a Code Node as high-value. <c>Value</c> = "true" or "false".</summary>
    MarkHighValue,

    /// <summary>Mark a Code Node as noisy/supporting (low signal). <c>Value</c> = "true" or "false".</summary>
    MarkNoisy,

    /// <summary>Exclude a Code Node from the overview. <c>Value</c> = "true" or "false".</summary>
    HideFromOverview,

    /// <summary>Set the startup mechanism on a System. <c>Value</c> = mechanism string.</summary>
    SetStartupMechanism,

    /// <summary>
    /// Add a typed relationship between two entities.
    /// <c>TargetId</c> = from-entity ID; <c>Value</c> = "to-entity-id|RelationshipKind".
    /// </summary>
    AddRelationship,

    /// <summary>Remove a relationship from the System Map. <c>TargetId</c> = relationship ID.</summary>
    RemoveRelationship,

    /// <summary>
    /// Pin the canvas position of an entity.
    /// <c>TargetId</c> = entity ID; <c>Value</c> = "x,y".
    /// </summary>
    PinPosition,

    /// <summary>Accept an LLM-generated suggestion. <c>TargetId</c> = suggestion reference.</summary>
    AcceptSuggestion,

    /// <summary>Reject an LLM-generated suggestion. <c>TargetId</c> = suggestion reference.</summary>
    RejectSuggestion,
}
