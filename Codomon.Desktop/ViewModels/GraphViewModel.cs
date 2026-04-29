using Avalonia;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Codomon.Desktop.Services.Graph;

namespace Codomon.Desktop.ViewModels;

public class GraphViewModel
{
    public ObservableCollection<NodeViewModel> Nodes { get; } = new();
    public ObservableCollection<ConnectionViewModel> Connections { get; } = new();

    // Directed edges between node view-models, used by AutoAlign for topological layout.
    private readonly List<(NodeViewModel From, NodeViewModel To)> _nodeEdges = new();

    public GraphViewModel()
    {
        var graph   = FakeCodomonGraphFactory.Create();
        var adapted = CodomonGraphAdapter.ToViewModel(graph);

        foreach (var node in adapted.Nodes)
            Nodes.Add(node);

        foreach (var connection in adapted.Connections)
            Connections.Add(connection);

        foreach (var edge in adapted.Edges)
            _nodeEdges.Add(edge);
    }

    /// <summary>
    /// Arranges all nodes in a left-to-right row using a topological order derived from
    /// the graph edges. Nodes in cycles or with no edges appear after the ordered nodes.
    /// </summary>
    public void AutoAlign()
    {
        const double startX     = 80;
        const double startY     = 200;
        const double columnGap  = 220;

        var order = TopologicalOrder();

        double x = startX;
        foreach (var node in order)
        {
            node.Location = new Point(x, startY);
            x += columnGap;
        }
    }

    /// <summary>
    /// Evenly distributes <paramref name="nodes"/> along the horizontal axis.
    /// The leftmost and rightmost nodes (by current X position) are kept in place;
    /// all others are repositioned to create equal gaps.
    /// At least three nodes must be supplied for spacing to have any effect.
    /// </summary>
    public void DistributeHorizontally(IList<NodeViewModel> nodes)
    {
        if (nodes.Count < 3) return;

        var sorted = nodes.OrderBy(n => n.Location.X).ToList();
        double leftX  = sorted[0].Location.X;
        double rightX = sorted[^1].Location.X;
        double gap    = (rightX - leftX) / (sorted.Count - 1);

        for (int i = 1; i < sorted.Count - 1; i++)
            sorted[i].Location = new Point(leftX + i * gap, sorted[i].Location.Y);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    /// <summary>Returns nodes in topological (Kahn's BFS) order; remaining cycle nodes appended last.</summary>
    private List<NodeViewModel> TopologicalOrder()
    {
        var successors = Nodes.ToDictionary(n => n, _ => new List<NodeViewModel>());
        var inDegree   = Nodes.ToDictionary(n => n, _ => 0);

        foreach (var (from, to) in _nodeEdges)
        {
            if (successors.ContainsKey(from) && inDegree.ContainsKey(to))
            {
                successors[from].Add(to);
                inDegree[to]++;
            }
        }

        var queue  = new Queue<NodeViewModel>(Nodes.Where(n => inDegree[n] == 0));
        var result  = new List<NodeViewModel>(Nodes.Count);
        var inResult = new HashSet<NodeViewModel>(Nodes.Count);

        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            result.Add(node);
            inResult.Add(node);
            foreach (var succ in successors[node])
                if (--inDegree[succ] == 0)
                    queue.Enqueue(succ);
        }

        // Append any remaining nodes that are part of cycles.
        foreach (var node in Nodes)
            if (!inResult.Contains(node))
                result.Add(node);

        return result;
    }
}
