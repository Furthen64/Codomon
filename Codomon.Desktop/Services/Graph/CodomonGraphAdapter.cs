using Avalonia;
using Codomon.Desktop.Models.Graph;
using Codomon.Desktop.ViewModels;

namespace Codomon.Desktop.Services.Graph;

public static class CodomonGraphAdapter
{
    public static (IReadOnlyList<NodeViewModel> Nodes, IReadOnlyList<ConnectionViewModel> Connections)
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

        foreach (var edge in model.Edges)
        {
            if (!nodeMap.TryGetValue(edge.FromNodeId, out var fromNode))
                continue;

            if (!nodeMap.TryGetValue(edge.ToNodeId, out var toNode))
                continue;

            fromNode.OutputConnector.IsConnected = true;
            toNode.InputConnector.IsConnected    = true;

            connections.Add(new ConnectionViewModel(fromNode.OutputConnector, toNode.InputConnector));
        }

        return (nodeMap.Values.ToList(), connections);
    }
}
