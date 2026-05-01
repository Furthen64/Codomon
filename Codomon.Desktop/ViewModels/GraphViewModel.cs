using Avalonia;
using Codomon.Desktop.Models;
using Codomon.Desktop.Models.SystemMap;
using Codomon.Desktop.Services.Graph;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

namespace Codomon.Desktop.ViewModels;

public class GraphViewModel : INotifyPropertyChanged
{
    public ObservableCollection<NodeViewModel> Nodes { get; } = new();
    public ObservableCollection<ConnectionViewModel> Connections { get; } = new();

    // Directed edges between node view-models, used by AutoAlign for topological layout.
    private readonly List<(NodeViewModel From, NodeViewModel To)> _nodeEdges = new();

    // Cached source data so filters can be re-applied without a full external reload.
    private SystemMapModel? _currentSystemMap;
    private WorkspaceModel? _currentWorkspace;

    // ── Filters ───────────────────────────────────────────────────────────────

    private bool _showLowConfidenceItems = false;
    private bool _showCallsRelationships     = true;
    private bool _showDependsRelationships   = true;
    private bool _showImportsRelationships   = true;
    private bool _showOtherRelationships     = true;

    /// <summary>
    /// When <c>false</c> (default) nodes and relationships with
    /// <see cref="ConfidenceLevel.Unknown"/> confidence are hidden.
    /// </summary>
    public bool ShowLowConfidenceItems
    {
        get => _showLowConfidenceItems;
        set { if (_showLowConfidenceItems == value) return; _showLowConfidenceItems = value; OnPropertyChanged(); ApplyFilters(); }
    }

    /// <summary>Show / hide edges of kind <see cref="RelationshipKind.Calls"/>.</summary>
    public bool ShowCallsRelationships
    {
        get => _showCallsRelationships;
        set { if (_showCallsRelationships == value) return; _showCallsRelationships = value; OnPropertyChanged(); ApplyFilters(); }
    }

    /// <summary>Show / hide edges of kind <see cref="RelationshipKind.Depends"/>.</summary>
    public bool ShowDependsRelationships
    {
        get => _showDependsRelationships;
        set { if (_showDependsRelationships == value) return; _showDependsRelationships = value; OnPropertyChanged(); ApplyFilters(); }
    }

    /// <summary>Show / hide edges of kind <see cref="RelationshipKind.Imports"/>.</summary>
    public bool ShowImportsRelationships
    {
        get => _showImportsRelationships;
        set { if (_showImportsRelationships == value) return; _showImportsRelationships = value; OnPropertyChanged(); ApplyFilters(); }
    }

    /// <summary>
    /// Show / hide edges whose kind is none of the individually-toggled kinds
    /// (Configures, Logs, Publishes, Subscribes, Reads, Writes, Hosts, Other).
    /// </summary>
    public bool ShowOtherRelationships
    {
        get => _showOtherRelationships;
        set { if (_showOtherRelationships == value) return; _showOtherRelationships = value; OnPropertyChanged(); ApplyFilters(); }
    }

    // ── Construction ──────────────────────────────────────────────────────────

    /// <summary>
    /// Parameterless constructor — loads the fake demo graph, useful for design-time previews.
    /// Call <see cref="Refresh"/> or <see cref="RefreshFromSystemMap"/> after construction
    /// to load real workspace data.
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

    // ── Public Refresh API ────────────────────────────────────────────────────

    /// <summary>
    /// Rebuilds the graph from the <see cref="SystemMapModel"/> data inside
    /// <paramref name="workspace"/>. When the System Map has no systems, falls back
    /// to the legacy workspace-connections rendering.
    /// </summary>
    public void Refresh(WorkspaceModel workspace)
    {
        _currentWorkspace  = workspace;
        _currentSystemMap  = workspace.SystemMap.Systems.Count > 0 ? workspace.SystemMap : null;

        if (_currentSystemMap != null)
            ApplyFilters();
        else
            BuildFromWorkspaceConnections(workspace);
    }

    /// <summary>
    /// Rebuilds the graph directly from a <see cref="SystemMapModel"/>:
    /// one node per <see cref="SystemModel"/> / <see cref="ExternalSystemModel"/>,
    /// one connection per <see cref="RelationshipModel"/>.
    /// Current filter settings are applied immediately.
    /// </summary>
    public void RefreshFromSystemMap(SystemMapModel map)
    {
        _currentSystemMap = map;
        ApplyFilters();
    }

    // ── Private render helpers ────────────────────────────────────────────────

    /// <summary>
    /// Re-renders nodes and connections using <see cref="_currentSystemMap"/> and
    /// the active filter settings. No-ops when no system map has been loaded.
    /// Note: filter changes made before the first <see cref="Refresh"/> or
    /// <see cref="RefreshFromSystemMap"/> call have no visual effect — the
    /// design-time demo graph loaded in the constructor is unaffected by filters.
    /// </summary>
    private void ApplyFilters()
    {
        if (_currentSystemMap != null)
            BuildFromSystemMap(_currentSystemMap);
        else if (_currentWorkspace != null)
            BuildFromWorkspaceConnections(_currentWorkspace);
        // If neither is set (design-time demo graph), do nothing.
    }

    private void BuildFromSystemMap(SystemMapModel map)
    {
        Nodes.Clear();
        Connections.Clear();
        _nodeEdges.Clear();

        bool lowConf = ShowLowConfidenceItems;

        // ── Nodes ─────────────────────────────────────────────────────────────

        var nodeMap = new Dictionary<string, NodeViewModel>(
            map.Systems.Count + map.ExternalSystems.Count, StringComparer.Ordinal);

        double autoX = 80;
        const double autoY   = 200;
        const double autoGap = 220;

        foreach (var sys in map.Systems)
        {
            if (!lowConf && sys.Confidence == ConfidenceLevel.Unknown) continue;

            var node = new NodeViewModel { Title = sys.Name, Location = new Point(autoX, autoY) };
            nodeMap[sys.Id] = node;
            Nodes.Add(node);
            autoX += autoGap;
        }

        foreach (var ext in map.ExternalSystems)
        {
            if (!lowConf && ext.Confidence == ConfidenceLevel.Unknown) continue;

            var node = new NodeViewModel { Title = $"[ext] {ext.Name}", Location = new Point(autoX, autoY + 160) };
            nodeMap[ext.Id] = node;
            Nodes.Add(node);
            autoX += autoGap;
        }

        // ── Connections ───────────────────────────────────────────────────────

        foreach (var rel in map.Relationships)
        {
            if (!lowConf && rel.Confidence == ConfidenceLevel.Unknown) continue;
            if (!IsKindVisible(rel.Kind)) continue;

            if (!nodeMap.TryGetValue(rel.FromId, out var fromNode) ||
                !nodeMap.TryGetValue(rel.ToId,   out var toNode))
                continue;

            fromNode.OutputConnector.IsConnected = true;
            toNode.InputConnector.IsConnected    = true;

            Connections.Add(new ConnectionViewModel(
                fromNode.OutputConnector, toNode.InputConnector, rel.Kind.ToString()));
            _nodeEdges.Add((fromNode, toNode));
        }

        // Set ChildCount (outgoing edge count) on each node.
        foreach (var node in Nodes) node.ChildCount = 0;
        foreach (var (from, _) in _nodeEdges) from.ChildCount++;

        AppLogger.Debug($"[Graph] BuildFromSystemMap complete. " +
                        $"Nodes={Nodes.Count}  Connections={Connections.Count}");
    }

    private bool IsKindVisible(RelationshipKind kind) => kind switch
    {
        RelationshipKind.Calls   => ShowCallsRelationships,
        RelationshipKind.Depends => ShowDependsRelationships,
        RelationshipKind.Imports => ShowImportsRelationships,
        _                        => ShowOtherRelationships,
    };

    /// <summary>
    /// Legacy render path: one node per <see cref="SystemBoxModel"/>,
    /// one edge per <see cref="ConnectionModel"/>. Used when no System Map data
    /// is available.
    /// </summary>
    private void BuildFromWorkspaceConnections(WorkspaceModel workspace)
    {
        Nodes.Clear();
        Connections.Clear();
        _nodeEdges.Clear();

        var nodeMap = new Dictionary<string, NodeViewModel>(workspace.Systems.Count, StringComparer.Ordinal);
        double autoX = 80;
        const double autoY   = 200;
        const double autoGap = 220;

        foreach (var sys in workspace.Systems)
        {
            bool hasSavedPosition = sys.X != 0 || sys.Y != 0;
            double x = hasSavedPosition ? sys.X : autoX;
            double y = hasSavedPosition ? sys.Y : autoY;

            var node = new NodeViewModel { Title = sys.Name, Location = new Point(x, y) };
            nodeMap[sys.Id] = node;
            Nodes.Add(node);
            autoX += autoGap;
        }

        foreach (var conn in workspace.Connections)
        {
            if (!nodeMap.TryGetValue(conn.FromId, out var fromNode))
            {
                AppLogger.Debug($"[Graph] Skipping connection '{conn.Name}' — FromId='{conn.FromId}' not found (Origin={conn.Origin}).");
                continue;
            }
            if (!nodeMap.TryGetValue(conn.ToId, out var toNode))
            {
                AppLogger.Debug($"[Graph] Skipping connection '{conn.Name}' — ToId='{conn.ToId}' not found (Origin={conn.Origin}).");
                continue;
            }

            fromNode.OutputConnector.IsConnected = true;
            toNode.InputConnector.IsConnected    = true;

            Connections.Add(new ConnectionViewModel(fromNode.OutputConnector, toNode.InputConnector));
            _nodeEdges.Add((fromNode, toNode));
        }

        foreach (var node in Nodes) node.ChildCount = 0;
        foreach (var (from, _) in _nodeEdges) from.ChildCount++;

        AppLogger.Debug($"[Graph] BuildFromWorkspaceConnections complete. Nodes={Nodes.Count}  Connections={Connections.Count}  " +
                        $"(workspace had {workspace.Connections.Count} connection(s) total).");
    }

    /// <summary>
    /// Arranges all nodes using a layered (hierarchical) layout: nodes are grouped into
    /// columns by their longest-path depth in the DAG, and stacked vertically within each
    /// column. Nodes in cycles or with no edges are placed in a final column.
    /// A second pass then promotes hub nodes (many incoming connections) and their
    /// dedicated callers to new columns on the right to reduce visual clutter.
    /// </summary>
    public void AutoAlign()
    {
        const double startX       = 80;
        const double startY       = 80;
        const double columnGap    = 220;
        const double rowGap       = 120;
        const int    hubThreshold = 3;

        var layers = ComputeLayers();
        PromoteHubs(layers, hubThreshold);

        for (int col = 0; col < layers.Count; col++)
        {
            var layer = layers[col];
            double x = startX + col * columnGap;

            for (int row = 0; row < layer.Count; row++)
                layer[row].Location = new Point(x, startY + row * rowGap);
        }
    }

    /// <summary>
    /// Second-pass heuristic: hub nodes (in-degree &gt;= <paramref name="hubThreshold"/>)
    /// are moved to a new rightmost column, and any node whose every successor is a hub
    /// ("dedicated callers") is moved to the column just before the hub column.
    /// This reduces clutter when many nodes all converge on a single target.
    /// </summary>
    private void PromoteHubs(List<List<NodeViewModel>> layers, int hubThreshold)
    {
        var inDegree   = Nodes.ToDictionary(n => n, _ => 0);
        var successors = Nodes.ToDictionary(n => n, _ => new List<NodeViewModel>());

        foreach (var (from, to) in _nodeEdges)
        {
            if (inDegree.ContainsKey(to) && successors.ContainsKey(from))
            {
                inDegree[to]++;
                successors[from].Add(to);
            }
        }

        var hubs = new HashSet<NodeViewModel>(Nodes.Where(n => inDegree[n] >= hubThreshold));
        if (hubs.Count == 0) return;

        // Dedicated callers: nodes outside the hub set whose every outgoing edge leads to a hub.
        var dedicatedCallers = Nodes
            .Where(n => !hubs.Contains(n)
                     && successors[n].Count > 0
                     && successors[n].All(s => hubs.Contains(s)))
            .ToList();

        // Remove promoted nodes from their original layers.
        var promoted = new HashSet<NodeViewModel>(hubs.Concat(dedicatedCallers));
        foreach (var layer in layers)
            layer.RemoveAll(promoted.Contains);

        // Append: dedicated callers layer (if any), then the hub layer.
        if (dedicatedCallers.Count > 0)
            layers.Add(dedicatedCallers);
        layers.Add(hubs.ToList());
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

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
