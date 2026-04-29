namespace Codomon.Desktop.Models.Graph;

public sealed class CodomonGraphEdge
{
    public required string Id { get; init; }
    public required string FromNodeId { get; init; }
    public required string ToNodeId { get; init; }

    public string? Kind { get; init; }
    public string? Label { get; init; }
}
