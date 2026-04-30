namespace Codomon.Desktop.Models.ArchitectureHypothesis;

/// <summary>
/// The role-signal that caused an LLM to flag a code node as high-value.
/// </summary>
public enum HighValueNodeSignal
{
    EntryPoint,
    Orchestrator,
    CentralStateModel,
    ServiceBoundary,
    SerializationBoundary,
    IntegrationBoundary,
    RuntimeHeavy,
    ErrorProne,
    BridgeBetweenClusters,
    PersistenceBoundary,
    Other
}
