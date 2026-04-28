namespace Codomon.Desktop.Models.Graph;

public sealed class CodomonGraphModel
{
    public List<CodomonGraphNode> Nodes { get; } = new();
    public List<CodomonGraphEdge> Edges { get; } = new();
}
