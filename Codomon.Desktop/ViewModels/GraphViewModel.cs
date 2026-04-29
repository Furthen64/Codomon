using Avalonia;
using Codomon.Desktop.Models;
using Codomon.Desktop.Services.Graph;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Codomon.Desktop.ViewModels;

public class GraphViewModel
{
    public ObservableCollection<NodeViewModel> Nodes { get; } = new();
    public ObservableCollection<ConnectionViewModel> Connections { get; } = new();

    // Directed edges between node view-models, used by AutoAlign for topological layout.
    private readonly List<(NodeViewModel From, NodeViewModel To)> _nodeEdges = new();

    /// <summary>
    /// Parameterless constructor — loads the fake demo graph, useful for design-time previews.
    /// Call <see cref="Refresh"/> immediately after construction to load real workspace data.
    /// </summary>
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
    /// Clears all current nodes and connections, then rebuilds the graph from
    /// <paramref name="workspace"/> data: one node per <see cref="SystemBoxModel"/>,
    /// one connection per <see cref="ConnectionModel"/> whose FromId/ToId resolve
    /// to known system nodes.
    /// </summary>
    public void Refresh(WorkspaceModel workspace)
    {
        Nodes.Clear();
        Connections.Clear();
        _nodeEdges.Clear();

        // Build a node per system box.
        var nodeMap = new Dictionary<string, NodeViewModel>(workspace.Systems.Count, StringComparer.Ordinal);
        double autoX = 80;
        const double autoY   = 200;
        const double autoGap = 220;

        foreach (var sys in workspace.Systems)
        {
            // Use the saved position when either coordinate is non-zero, otherwise auto-layout.
            bool hasSavedPosition = sys.X != 0 || sys.Y != 0;
            double x = hasSavedPosition ? sys.X : autoX;
            double y = hasSavedPosition ? sys.Y : autoY;

            var node = new NodeViewModel
            {
                Title    = sys.Name,
                Location = new Point(x, y),
            };

            nodeMap[sys.Id] = node;
            Nodes.Add(node);
            autoX += autoGap;
        }

        // Build connections from workspace connection models.
        foreach (var conn in workspace.Connections)
        {
            if (!nodeMap.TryGetValue(conn.FromId, out var fromNode))
            {
                AppLogger.Debug($"[Graph] Skipping connection '{conn.Name}' — FromId='{conn.FromId}' not found in node map (Origin={conn.Origin}).");
                continue;
            }
            if (!nodeMap.TryGetValue(conn.ToId, out var toNode))
            {
                AppLogger.Debug($"[Graph] Skipping connection '{conn.Name}' — ToId='{conn.ToId}' not found in node map (Origin={conn.Origin}).");
                continue;
            }

            fromNode.OutputConnector.IsConnected = true;
            toNode.InputConnector.IsConnected    = true;

            Connections.Add(new ConnectionViewModel(fromNode.OutputConnector, toNode.InputConnector));
            _nodeEdges.Add((fromNode, toNode));
        }

        AppLogger.Debug($"[Graph] Refresh complete. Nodes={Nodes.Count}  Connections={Connections.Count}  " +
                        $"(workspace had {workspace.Connections.Count} connection(s) total).");
    }

    /// <summary>
    /// Arranges all nodes using a layered (hierarchical) layout: nodes are grouped into
    /// columns by their longest-path depth in the DAG, and stacked vertically within each
    /// column. Nodes in cycles or with no edges are placed in a final column.
    /// </summary>
    public void AutoAlign()
    {
        const double startX    = 80;
        const double startY    = 80;
        const double columnGap = 220;
        const double rowGap    = 120;

        var layers = ComputeLayers();

        for (int col = 0; col < layers.Count; col++)
        {
            var layer = layers[col];
            double x = startX + col * columnGap;

            for (int row = 0; row < layer.Count; row++)
                layer[row].Location = new Point(x, startY + row * rowGap);
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

    /// <summary>
    /// Groups nodes into layers by their longest-path depth in the DAG (Kahn's BFS).
    /// Nodes at depth 0 are roots; each subsequent layer is one step further from the roots.
    /// Nodes that are part of cycles are collected into a final layer.
    /// </summary>
    private List<List<NodeViewModel>> ComputeLayers()
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

        // Assign each node its depth = longest path from any root.
        var depth   = Nodes.ToDictionary(n => n, _ => 0);
        var queue   = new Queue<NodeViewModel>(Nodes.Where(n => inDegree[n] == 0));
        var visited = new HashSet<NodeViewModel>(Nodes.Count);

        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            if (!visited.Add(node)) continue;

            foreach (var succ in successors[node])
            {
                if (depth[succ] < depth[node] + 1)
                    depth[succ] = depth[node] + 1;
                if (--inDegree[succ] == 0)
                    queue.Enqueue(succ);
            }
        }

        // Collect cycle nodes (never dequeued) into a layer after all acyclic nodes.
        int maxDepth   = visited.Count > 0 ? visited.Max(n => depth[n]) : 0;
        int cycleDepth = maxDepth + 1;
        bool hasCycles = false;
        foreach (var node in Nodes)
        {
            if (!visited.Contains(node))
            {
                depth[node] = cycleDepth;
                hasCycles   = true;
            }
        }

        int numLayers = (hasCycles ? cycleDepth : maxDepth) + 1;
        var layers    = Enumerable.Range(0, numLayers).Select(_ => new List<NodeViewModel>()).ToList();

        foreach (var node in Nodes)
            layers[depth[node]].Add(node);

        return layers;
    }
}
