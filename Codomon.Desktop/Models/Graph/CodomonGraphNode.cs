namespace Codomon.Desktop.Models.Graph;

public sealed class CodomonGraphNode
{
    public required string Id { get; init; }
    public required string Title { get; init; }

    public string? Subtitle { get; init; }
    public string? Kind { get; init; }

    public double X { get; init; }
    public double Y { get; init; }
}
