using Avalonia;
using Codomon.Desktop.Models.Graph;
using Codomon.Desktop.ViewModels;

namespace Codomon.Desktop.Services.Graph;

public static class CodomonGraphAdapter
{
    public static (IReadOnlyList<NodeViewModel> Nodes,
                   IReadOnlyList<ConnectionViewModel> Connections,
                   IReadOnlyList<(NodeViewModel From, NodeViewModel To)> Edges)
        ToViewModel(CodomonGraphModel model)
    {
        // Build node view-models, keyed by domain node ID
        var nodeMap = new Dictionary<string, NodeViewModel>(model.Nodes.Count);

        foreach (var domainNode in model.Nodes)
        {
            var vm = new NodeViewModel
            {
                Title    = domainNode.Title,
                Location = new Point(domainNode.X, domainNode.Y),
            };

            nodeMap[domainNode.Id] = vm;
        }

        // Build connection view-models from domain edges; skip edges with unknown IDs
        var connections = new List<ConnectionViewModel>();
        var edges = new List<(NodeViewModel From, NodeViewModel To)>();

        foreach (var edge in model.Edges)
        {
            if (!nodeMap.TryGetValue(edge.FromNodeId, out var fromNode))
                continue;

            if (!nodeMap.TryGetValue(edge.ToNodeId, out var toNode))
                continue;

            fromNode.OutputConnector.IsConnected = true;
            toNode.InputConnector.IsConnected    = true;

            connections.Add(new ConnectionViewModel(fromNode.OutputConnector, toNode.InputConnector));
            edges.Add((fromNode, toNode));
        }

        // Set ChildCount (outgoing edge count) for each node.
        foreach (var edge in edges)
            edge.From.ChildCount++;

        return (nodeMap.Values.ToList(), connections, edges);
    }
}
