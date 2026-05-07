using Avalonia;
using Codomon.Desktop.Models;
using Codomon.Desktop.Models.SystemMap;
using Codomon.Desktop.Services;
using Codomon.Desktop.Services.Graph;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;

namespace Codomon.Desktop.ViewModels;

public enum GraphRenderMode
{
    SystemMap,
    ModuleRelationships,
    CodeNodeRelationships
}

public sealed class GraphNodeFileVm
{
    public string DisplayName { get; init; } = string.Empty;
    public string FullPath { get; init; } = string.Empty;
}

public sealed class GraphCallerVm
{
    public string CallerName { get; init; } = string.Empty;
    public string CallerContext { get; init; } = string.Empty;
    public string ModuleId { get; init; } = string.Empty;
    public string CallerCodeNodeId { get; init; } = string.Empty;
    public string RelationshipLabel { get; init; } = string.Empty;
}

public class GraphViewModel : INotifyPropertyChanged
{
    /// <summary>Entity type string assigned to code-node <see cref="NodeViewModel"/> instances.</summary>
    private const string CodeNodeEntityType = "Code Node";

    public sealed class AutoAlignOptions
    {
        public double StartX { get; set; } = 80;
        public double StartY { get; set; } = 80;
        public double ColumnGap { get; set; } = 280;
        public double BaseRowGap { get; set; } = 96;
        public double ComponentGap { get; set; } = 180;
        public int BarycentricSweeps { get; set; } = 6;
        public bool RunTwoPassRefinement { get; set; }
    }

    public ObservableCollection<NodeViewModel> Nodes { get; } = new();
    public ObservableCollection<ConnectionViewModel> Connections { get; } = new();

    // Directed edges between node view-models, used by AutoAlign for topological layout.
    private readonly List<(NodeViewModel From, NodeViewModel To)> _nodeEdges = new();

    // Cached source data so filters can be re-applied without a full external reload.
    private SystemMapModel? _currentSystemMap;
    private WorkspaceModel? _currentWorkspace;
    private GraphRenderMode _renderMode = GraphRenderMode.SystemMap;
    private string? _moduleSystemId;
    private string? _codeNodeModuleId;
    private readonly Dictionary<string, Point> _savedPositions = new(StringComparer.Ordinal);
    private string _breadcrumbSystemLabel = "System Map";
    private string _breadcrumbModuleLabel = "Module";
    private string _breadcrumbCodeNodesLabel = "Code nodes";
    private string? _breadcrumbSystemId;
    private string? _breadcrumbModuleId;
    private NodeViewModel? _selectedNode;
    private string _selectedNodeType = string.Empty;
    private string _selectedNodeKind = string.Empty;
    private string _selectedNodeFullName = string.Empty;
    private string _selectedNodeConfidence = string.Empty;
    private string _selectedNodeModule = string.Empty;
    private string _selectedNodeSystem = string.Empty;
    private string _selectedNodeSummaryFirstParagraph = string.Empty;
    private readonly ObservableCollection<GraphNodeFileVm> _selectedNodeFiles = new();
    private readonly ObservableCollection<GraphCallerVm> _selectedNodeCallers = new();
    private readonly HashSet<string> _callerOverlayNodeKeys = new(StringComparer.Ordinal);
    private readonly List<ConnectionViewModel> _callerOverlayConnections = new();
    private SystemMapModel? _callerLookupMap;
    private readonly Dictionary<string, ModuleModel> _moduleByCodeNodeId = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CodeNodeModel> _codeNodeById = new(StringComparer.Ordinal);
    private string _workspaceFolderPath = string.Empty;

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

    public string BreadcrumbSystemLabel
    {
        get => _breadcrumbSystemLabel;
        private set { _breadcrumbSystemLabel = value; OnPropertyChanged(); }
    }

    public string BreadcrumbModuleLabel
    {
        get => _breadcrumbModuleLabel;
        private set { _breadcrumbModuleLabel = value; OnPropertyChanged(); }
    }

    public string BreadcrumbCodeNodesLabel
    {
        get => _breadcrumbCodeNodesLabel;
        private set { _breadcrumbCodeNodesLabel = value; OnPropertyChanged(); }
    }

    public bool ShowModuleBreadcrumb => _renderMode is GraphRenderMode.ModuleRelationships or GraphRenderMode.CodeNodeRelationships;
    public bool ShowCodeNodesBreadcrumb => _renderMode == GraphRenderMode.CodeNodeRelationships;
    public bool CanNavigateToModule => !string.IsNullOrWhiteSpace(_breadcrumbSystemId);
    public bool CanNavigateToCodeNodes => !string.IsNullOrWhiteSpace(_breadcrumbModuleId);
    public string? BreadcrumbSystemId => _breadcrumbSystemId;
    public string? BreadcrumbModuleId => _breadcrumbModuleId;

    public NodeViewModel? SelectedNode
    {
        get => _selectedNode;
        private set
        {
            if (ReferenceEquals(_selectedNode, value)) return;
            _selectedNode = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSelectedNode));
            OnPropertyChanged(nameof(SelectedNodeName));
            UpdateSelectedNodeDetails();
        }
    }

    public bool HasSelectedNode => SelectedNode != null;
    public string SelectedNodeName => SelectedNode?.Title ?? "Select a node";
    public string SelectedNodeType
    {
        get => _selectedNodeType;
        private set { _selectedNodeType = value; OnPropertyChanged(); }
    }
    public string SelectedNodeKind
    {
        get => _selectedNodeKind;
        private set { _selectedNodeKind = value; OnPropertyChanged(); }
    }
    public string SelectedNodeFullName
    {
        get => _selectedNodeFullName;
        private set { _selectedNodeFullName = value; OnPropertyChanged(); }
    }
    public string SelectedNodeConfidence
    {
        get => _selectedNodeConfidence;
        private set { _selectedNodeConfidence = value; OnPropertyChanged(); }
    }
    public string SelectedNodeModule
    {
        get => _selectedNodeModule;
        private set { _selectedNodeModule = value; OnPropertyChanged(); }
    }
    public string SelectedNodeSystem
    {
        get => _selectedNodeSystem;
        private set { _selectedNodeSystem = value; OnPropertyChanged(); }
    }
    public string SelectedNodeSummaryFirstParagraph
    {
        get => _selectedNodeSummaryFirstParagraph;
        private set { _selectedNodeSummaryFirstParagraph = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasSelectedNodeSummary)); }
    }
    public bool HasSelectedNodeSummary => !string.IsNullOrEmpty(_selectedNodeSummaryFirstParagraph);
    public ObservableCollection<GraphNodeFileVm> SelectedNodeFiles => _selectedNodeFiles;
    public ObservableCollection<GraphCallerVm> SelectedNodeCallers => _selectedNodeCallers;
    public bool HasSelectedNodeCallers => _selectedNodeCallers.Count > 0;

    // ── Workspace context ─────────────────────────────────────────────────────

    /// <summary>
    /// Path to the workspace folder on disk. Used to locate LLM-generated summaries for
    /// code nodes in the Node Details panel. Set by the caller whenever a workspace is opened.
    /// </summary>
    public string WorkspaceFolderPath
    {
        get => _workspaceFolderPath;
        set { _workspaceFolderPath = value ?? string.Empty; OnPropertyChanged(); }
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
        _renderMode = GraphRenderMode.SystemMap;
        _moduleSystemId = null;
        _codeNodeModuleId = null;

        if (_currentSystemMap != null)
            ApplyFilters();
        else
            BuildFromWorkspaceConnections(workspace);

        UpdateBreadcrumbs(null, null);
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
        _renderMode = GraphRenderMode.SystemMap;
        _moduleSystemId = null;
        _codeNodeModuleId = null;
        ApplyFilters();
        UpdateBreadcrumbs(null, null);
    }

    /// <summary>
    /// Rebuilds the graph as module-to-module relationships for a single selected system.
    /// Endpoints represented as code-node IDs are resolved to their owner modules first.
    /// </summary>
    public void RefreshModuleRelationshipsForSystem(SystemMapModel map, string systemId)
    {
        _currentSystemMap = map;
        _renderMode = GraphRenderMode.ModuleRelationships;
        _moduleSystemId = systemId;
        _codeNodeModuleId = null;
        ApplyFilters();
    }

    /// <summary>
    /// Rebuilds the graph as code-node relationships for a single selected module.
    /// Only relationships where both endpoints are code nodes in the selected module are shown.
    /// </summary>
    public void RefreshCodeNodeRelationshipsForModule(SystemMapModel map, string moduleId)
    {
        _currentSystemMap = map;
        _renderMode = GraphRenderMode.CodeNodeRelationships;
        _moduleSystemId = null;
        _codeNodeModuleId = moduleId;
        ApplyFilters();
    }

    public void SelectNode(NodeViewModel? node)
        => SelectedNode = node;

    public string ResolveFilePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return path;
        if (Path.IsPathRooted(path)) return Path.GetFullPath(path);

        var sourceProjectDir = Path.GetDirectoryName(_currentWorkspace?.SourceProjectPath ?? string.Empty);
        if (string.IsNullOrWhiteSpace(sourceProjectDir)) return path;

        var rootFullPath = Path.GetFullPath(sourceProjectDir);
        var candidate = Path.Combine(rootFullPath, path);
        var candidateFullPath = Path.GetFullPath(candidate);

        var normalizedRoot = Path.TrimEndingDirectorySeparator(rootFullPath);
        var rootPrefix = Path.EndsInDirectorySeparator(normalizedRoot)
            ? normalizedRoot
            : normalizedRoot + Path.DirectorySeparatorChar;

        if (!candidateFullPath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
            return string.Empty;

        return candidateFullPath;
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
        SavePositions();
        if (_currentSystemMap != null)
        {
            if (_renderMode == GraphRenderMode.ModuleRelationships)
                BuildModuleRelationshipsForSystem(_currentSystemMap, _moduleSystemId);
            else if (_renderMode == GraphRenderMode.CodeNodeRelationships)
                BuildCodeNodeRelationshipsForModule(_currentSystemMap, _codeNodeModuleId);
            else
                BuildFromSystemMap(_currentSystemMap);
        }
        else if (_currentWorkspace != null)
            BuildFromWorkspaceConnections(_currentWorkspace);
        // If neither is set (design-time demo graph), do nothing.
    }

    private void BuildModuleRelationshipsForSystem(SystemMapModel map, string? systemId)
    {
        Nodes.Clear();
        Connections.Clear();
        _nodeEdges.Clear();

        if (string.IsNullOrWhiteSpace(systemId))
        {
            AppLogger.Warn("[Graph] Module relationship view requested without a system id. Falling back to System Map graph.");
            BuildFromSystemMap(map);
            return;
        }

        bool lowConf = ShowLowConfidenceItems;

        var system = map.Systems.FirstOrDefault(s => string.Equals(s.Id, systemId, StringComparison.Ordinal));
        if (system == null)
        {
            AppLogger.Warn($"[Graph] Module relationship view requested for unknown system id={systemId}. Falling back to System Map graph.");
            BuildFromSystemMap(map);
            return;
        }
        UpdateBreadcrumbs(system, null);

        var modules = GetModulesForSystem(map, system);
        if (modules.Count == 0)
        {
            AppLogger.Debug($"[Graph] Module relationship view: system '{system.Name}' has no modules.");
            return;
        }

        var moduleById = modules.ToDictionary(m => m.Id, StringComparer.Ordinal);
        var codeNodeToModule = modules
            .SelectMany(m => m.CodeNodes.Select(cn => (CodeNodeId: cn.Id, ModuleId: m.Id)))
            .ToDictionary(x => x.CodeNodeId, x => x.ModuleId, StringComparer.Ordinal);

        double autoX = 80;
        const double autoY = 180;
        const double autoGap = 220;

        var nodeMap = new Dictionary<string, NodeViewModel>(modules.Count, StringComparer.Ordinal);
        foreach (var module in modules)
        {
            if (!lowConf && module.Confidence == ConfidenceLevel.Unknown) continue;

            var node = new NodeViewModel
            {
                Key = module.Id,
                Title = module.Name,
                Subtitle = $"{module.CodeNodes.Count} code node(s)",
                KindLabel = module.Kind.ToString(),
                KindBadgeBackground = "#1E3A5F",
                KindBadgeForeground = "#8BD4FF",
                EntityType = "Module",
                Confidence = module.Confidence.ToString(),
                ModuleName = module.Name,
                SystemName = system.Name,
                RelatedFiles = module.CodeNodes
                    .Select(n => n.FilePath)
                    .Where(p => !string.IsNullOrWhiteSpace(p))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                Location = _savedPositions.TryGetValue(module.Id, out var savedPosition)
                    ? savedPosition
                    : new Point(autoX, autoY)
            };

            nodeMap[module.Id] = node;
            Nodes.Add(node);
            autoX += autoGap;
        }

        string? ResolveModuleId(string entityId)
        {
            if (moduleById.ContainsKey(entityId)) return entityId;
            if (codeNodeToModule.TryGetValue(entityId, out var owner)) return owner;
            return null;
        }

        var relCounts = new Dictionary<(string From, string To, RelationshipKind Kind), int>();

        foreach (var rel in map.Relationships)
        {
            if (!lowConf && rel.Confidence == ConfidenceLevel.Unknown) continue;
            if (!IsKindVisible(rel.Kind)) continue;

            var fromModuleId = ResolveModuleId(rel.FromId);
            var toModuleId   = ResolveModuleId(rel.ToId);
            if (fromModuleId == null || toModuleId == null) continue;
            if (!nodeMap.ContainsKey(fromModuleId) || !nodeMap.ContainsKey(toModuleId)) continue;
            if (string.Equals(fromModuleId, toModuleId, StringComparison.Ordinal)) continue;

            var key = (fromModuleId, toModuleId, rel.Kind);
            relCounts.TryGetValue(key, out var count);
            relCounts[key] = count + 1;
        }

        foreach (var kvp in relCounts)
        {
            var (fromId, toId, kind) = kvp.Key;
            var count = kvp.Value;

            var fromNode = nodeMap[fromId];
            var toNode   = nodeMap[toId];

            fromNode.OutputConnector.IsConnected = true;
            toNode.InputConnector.IsConnected    = true;

            var label = count > 1 ? $"{kind} x{count}" : kind.ToString();
            Connections.Add(new ConnectionViewModel(fromNode.OutputConnector, toNode.InputConnector, label));
            _nodeEdges.Add((fromNode, toNode));
        }

        foreach (var node in Nodes) node.ChildCount = 0;
        foreach (var (from, _) in _nodeEdges) from.ChildCount++;
        foreach (var node in Nodes) node.IsCodeLeaf = false;
        EnsureSelectedNodeIsValid();

        AppLogger.Debug($"[Graph] BuildModuleRelationshipsForSystem complete. System='{system.Name}' Modules={Nodes.Count} Connections={Connections.Count}");
    }

    private void BuildCodeNodeRelationshipsForModule(SystemMapModel map, string? moduleId)
    {
        const double defaultCodeNodeStartX = 80;
        const double defaultCodeNodeStartY = 180;
        const double codeNodeHorizontalGap = 220;

        Nodes.Clear();
        Connections.Clear();
        _nodeEdges.Clear();

        if (string.IsNullOrWhiteSpace(moduleId))
        {
            AppLogger.Warn("[Graph] Code-node relationship view requested without a module id. Falling back to System Map graph.");
            BuildFromSystemMap(map);
            return;
        }

        bool lowConf = ShowLowConfidenceItems;

        var module = map.AllModules.FirstOrDefault(m => string.Equals(m.Id, moduleId, StringComparison.Ordinal));
        if (module == null)
        {
            AppLogger.Warn($"[Graph] Code-node relationship view requested for unknown module id={moduleId}. Falling back to System Map graph.");
            BuildFromSystemMap(map);
            return;
        }

        var ownerSystem = map.Systems.FirstOrDefault(s =>
            s.Modules.Any(m => string.Equals(m.Id, module.Id, StringComparison.Ordinal)) ||
            module.SystemIds.Any(id => string.Equals(id, s.Id, StringComparison.Ordinal)));
        UpdateBreadcrumbs(ownerSystem, module);

        var codeNodes = module.CodeNodes;
        if (codeNodes.Count == 0)
        {
            AppLogger.Debug($"[Graph] Code-node relationship view: module '{module.Name}' has no code nodes.");
            return;
        }

        double autoX = defaultCodeNodeStartX;

        var nodeMap = new Dictionary<string, NodeViewModel>(codeNodes.Count, StringComparer.Ordinal);
        foreach (var codeNode in codeNodes)
        {
            if (!lowConf && codeNode.Confidence == ConfidenceLevel.Unknown) continue;

            var node = new NodeViewModel
            {
                Key = codeNode.Id,
                Title = codeNode.Name,
                Subtitle = codeNode.FullName,
                KindLabel = codeNode.Kind.ToString(),
                KindBadgeBackground = KindBadgeBackgroundForCodeNode(codeNode.Kind),
                KindBadgeForeground = KindBadgeForegroundForCodeNode(codeNode.Kind),
                EntityType = CodeNodeEntityType,
                Confidence = codeNode.Confidence.ToString(),
                FullName = codeNode.FullName,
                ModuleName = module.Name,
                SystemName = ownerSystem?.Name ?? string.Empty,
                RelatedFiles = string.IsNullOrWhiteSpace(codeNode.FilePath)
                    ? Array.Empty<string>()
                    : new[] { codeNode.FilePath },
                Location = _savedPositions.TryGetValue(codeNode.Id, out var savedPosition)
                    ? savedPosition
                    : new Point(autoX, defaultCodeNodeStartY)
            };

            nodeMap[codeNode.Id] = node;
            Nodes.Add(node);
            autoX += codeNodeHorizontalGap;
        }

        foreach (var rel in map.Relationships)
        {
            if (!lowConf && rel.Confidence == ConfidenceLevel.Unknown) continue;
            if (!IsKindVisible(rel.Kind)) continue;

            if (!nodeMap.TryGetValue(rel.FromId, out var fromNode) ||
                !nodeMap.TryGetValue(rel.ToId, out var toNode))
                continue;

            if (string.Equals(rel.FromId, rel.ToId, StringComparison.Ordinal))
                continue;

            fromNode.OutputConnector.IsConnected = true;
            toNode.InputConnector.IsConnected    = true;

            Connections.Add(new ConnectionViewModel(
                fromNode.OutputConnector, toNode.InputConnector, rel.Kind.ToString()));
            _nodeEdges.Add((fromNode, toNode));
        }

        foreach (var node in Nodes) node.ChildCount = 0;
        foreach (var (from, _) in _nodeEdges) from.ChildCount++;
        foreach (var node in Nodes) node.IsCodeLeaf = node.ChildCount == 0;
        EnsureSelectedNodeIsValid();

        AppLogger.Debug($"[Graph] BuildCodeNodeRelationshipsForModule complete. Module='{module.Name}' CodeNodes={Nodes.Count} Connections={Connections.Count}");
    }

    private void BuildFromSystemMap(SystemMapModel map)
    {
        Nodes.Clear();
        Connections.Clear();
        _nodeEdges.Clear();
        UpdateBreadcrumbs(null, null);

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

            var moduleCount = CountModulesForSystem(map, sys);
            var node = new NodeViewModel
            {
                Key = sys.Id,
                Title = sys.Name,
                Subtitle = $"{moduleCount} module(s)",
                KindLabel = sys.Kind.ToString(),
                KindBadgeBackground = "#1F4335",
                KindBadgeForeground = "#7FE0B1",
                EntityType = "System",
                Confidence = sys.Confidence.ToString(),
                RelatedFiles = sys.EntryPointCandidates
                    .Where(p => !string.IsNullOrWhiteSpace(p))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                Location = _savedPositions.TryGetValue(sys.Id, out var savedPosition)
                    ? savedPosition
                    : new Point(autoX, autoY)
            };
            nodeMap[sys.Id] = node;
            Nodes.Add(node);
            autoX += autoGap;
        }

        foreach (var ext in map.ExternalSystems)
        {
            if (!lowConf && ext.Confidence == ConfidenceLevel.Unknown) continue;

            var node = new NodeViewModel
            {
                Key = ext.Id,
                Title = $"[ext] {ext.Name}",
                Subtitle = string.IsNullOrWhiteSpace(ext.Kind) ? "External System" : ext.Kind,
                KindLabel = "External",
                KindBadgeBackground = "#3A2C5F",
                KindBadgeForeground = "#C5A9FF",
                EntityType = "External System",
                Confidence = ext.Confidence.ToString(),
                Location = _savedPositions.TryGetValue(ext.Id, out var savedPosition)
                    ? savedPosition
                    : new Point(autoX, autoY + 160)
            };
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
        foreach (var node in Nodes) node.IsCodeLeaf = false;
        EnsureSelectedNodeIsValid();

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

    private static int CountModulesForSystem(SystemMapModel map, SystemModel system)
    {
        var moduleIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var module in system.Modules)
            moduleIds.Add(module.Id);

        foreach (var module in map.Modules)
        {
            if (module.SystemIds.Any(id => string.Equals(id, system.Id, StringComparison.Ordinal)))
                moduleIds.Add(module.Id);
        }

        return moduleIds.Count;
    }

    private static List<ModuleModel> GetModulesForSystem(SystemMapModel map, SystemModel system)
    {
        var modules = new List<ModuleModel>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var module in system.Modules)
        {
            if (seen.Add(module.Id))
                modules.Add(module);
        }

        foreach (var module in map.Modules)
        {
            if (module.SystemIds.Any(id => string.Equals(id, system.Id, StringComparison.Ordinal))
                && seen.Add(module.Id))
            {
                modules.Add(module);
            }
        }

        return modules;
    }

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
        UpdateBreadcrumbs(null, null);

        var nodeMap = new Dictionary<string, NodeViewModel>(workspace.Systems.Count, StringComparer.Ordinal);
        double autoX = 80;
        const double autoY   = 200;
        const double autoGap = 220;

        foreach (var sys in workspace.Systems)
        {
            bool hasSavedPosition = sys.X != 0 || sys.Y != 0;
            double x = hasSavedPosition ? sys.X : autoX;
            double y = hasSavedPosition ? sys.Y : autoY;

            var node = new NodeViewModel { Key = sys.Id, Title = sys.Name, Location = new Point(x, y) };
            node.Subtitle = "System";
            node.KindLabel = "System";
            node.KindBadgeBackground = "#1F4335";
            node.KindBadgeForeground = "#7FE0B1";
            node.EntityType = "System";
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
        foreach (var node in Nodes) node.IsCodeLeaf = false;
        EnsureSelectedNodeIsValid();

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
    public AutoAlignOptions CreateAutoAlignDefaults()
    {
        if (_renderMode is GraphRenderMode.ModuleRelationships or GraphRenderMode.CodeNodeRelationships)
        {
            return new AutoAlignOptions
            {
                StartX = 80,
                StartY = 80,
                ColumnGap = 280,
                BaseRowGap = 96,
                ComponentGap = 180,
                BarycentricSweeps = 6,
                RunTwoPassRefinement = false
            };
        }

        return new AutoAlignOptions
        {
            StartX = 80,
            StartY = 80,
            ColumnGap = 220,
            BaseRowGap = 120,
            ComponentGap = 180,
            BarycentricSweeps = 1,
            RunTwoPassRefinement = false
        };
    }

    public void AutoAlign(AutoAlignOptions? options = null)
    {
        options ??= CreateAutoAlignDefaults();

        if (_renderMode is GraphRenderMode.ModuleRelationships or GraphRenderMode.CodeNodeRelationships)
        {
            AutoAlignModuleRelationships(options);
            return;
        }

        var startX = Math.Max(0, options.StartX);
        var startY = Math.Max(0, options.StartY);
        var columnGap = Math.Max(80, options.ColumnGap);
        var rowGap = Math.Max(40, options.BaseRowGap);
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
        SavePositions();
    }

    /// <summary>
    /// Smarter auto-layout for dense module relationship graphs:
    /// split into weakly connected components, use layered layout per component,
    /// reorder each layer with barycentric sweeps, and apply adaptive vertical spacing
    /// so one-to-many hubs are easier to read.
    /// </summary>
    private void AutoAlignModuleRelationships(AutoAlignOptions options)
    {
        var startX = Math.Max(0, options.StartX);
        var startY = Math.Max(0, options.StartY);
        var columnGap = Math.Max(100, options.ColumnGap);
        var baseRowGap = Math.Max(40, options.BaseRowGap);
        var componentGap = Math.Max(20, options.ComponentGap);
        var sweeps = Math.Max(1, options.BarycentricSweeps);
        var passes = options.RunTwoPassRefinement ? 2 : 1;

        if (Nodes.Count == 0)
            return;

        BuildAdjacency(out var successors, out var predecessors, out var undirected);
        var connected = Nodes
            .Where(n => successors[n].Count > 0 || predecessors[n].Count > 0)
            .ToHashSet();

        var components = ComputeWeaklyConnectedComponents(connected, undirected)
            .OrderByDescending(c => c.Sum(n => successors[n].Count + predecessors[n].Count))
            .ThenByDescending(c => c.Count)
            .ToList();

        // Keep truly isolated modules out of the main signal path.
        var isolates = Nodes.Where(n => !connected.Contains(n)).ToList();
        if (isolates.Count > 0)
            components.Add(new HashSet<NodeViewModel>(isolates));

        double componentY = startY;

        foreach (var component in components)
        {
            var layers = ComputeLayersForSubset(component, successors);
            RemoveEmptyLayers(layers);
            if (layers.Count == 0)
                continue;

            for (int pass = 0; pass < passes; pass++)
                OrderLayersByBarycenter(layers, predecessors, successors, sweeps);

            double componentBottom = componentY;

            for (int col = 0; col < layers.Count; col++)
            {
                var layer = layers[col];
                double x = startX + col * columnGap;
                double y = componentY;

                foreach (var node in layer)
                {
                    node.Location = new Point(x, y);
                    var degree = successors[node].Count + predecessors[node].Count;
                    var adaptiveGap = baseRowGap + Math.Min(48, degree * 8);
                    y += adaptiveGap;
                }

                if (y > componentBottom)
                    componentBottom = y;
            }

            componentY = componentBottom + componentGap;
        }

        SavePositions();
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
        SavePositions();
    }

    public void SavePositions()
    {
        foreach (var node in Nodes)
        {
            if (!string.IsNullOrWhiteSpace(node.Key))
                _savedPositions[node.Key] = node.Location;
        }
    }

    private void UpdateBreadcrumbs(SystemModel? system, ModuleModel? module)
    {
        _breadcrumbSystemId = system?.Id;
        _breadcrumbModuleId = module?.Id;
        BreadcrumbSystemLabel = "System Map";
        BreadcrumbModuleLabel = system?.Name ?? "Module";
        BreadcrumbCodeNodesLabel = module?.Name ?? "Code nodes";
        OnPropertyChanged(nameof(ShowModuleBreadcrumb));
        OnPropertyChanged(nameof(ShowCodeNodesBreadcrumb));
        OnPropertyChanged(nameof(CanNavigateToModule));
        OnPropertyChanged(nameof(CanNavigateToCodeNodes));
    }

    private void EnsureSelectedNodeIsValid()
    {
        if (SelectedNode == null) return;
        if (Nodes.Contains(SelectedNode)) return;
        SelectedNode = null;
    }

    private void UpdateSelectedNodeDetails()
    {
        ClearCallerOverlay();

        var node = SelectedNode;
        if (node == null)
        {
            SelectedNodeType = string.Empty;
            SelectedNodeKind = string.Empty;
            SelectedNodeFullName = string.Empty;
            SelectedNodeConfidence = string.Empty;
            SelectedNodeModule = string.Empty;
            SelectedNodeSystem = string.Empty;
            SelectedNodeSummaryFirstParagraph = string.Empty;
            _selectedNodeFiles.Clear();
            _selectedNodeCallers.Clear();
            OnPropertyChanged(nameof(HasSelectedNodeCallers));
            return;
        }

        SelectedNodeType = node.EntityType;
        SelectedNodeKind = node.KindLabel;
        SelectedNodeFullName = node.FullName;
        SelectedNodeConfidence = node.Confidence;
        SelectedNodeModule = node.ModuleName;
        SelectedNodeSystem = node.SystemName;
        _selectedNodeFiles.Clear();
        foreach (var file in node.RelatedFiles
                     .Where(p => !string.IsNullOrWhiteSpace(p))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            _selectedNodeFiles.Add(new GraphNodeFileVm
            {
                DisplayName = Path.GetFileName(file),
                FullPath = file
            });
        }
        PopulateSelectedNodeCallers(node);
        PopulateCallerOverlay(node);

        // Populate summary first paragraph for code nodes when a workspace is loaded.
        string summaryText = string.Empty;
        if (string.Equals(node.EntityType, CodeNodeEntityType, StringComparison.Ordinal)
            && !string.IsNullOrWhiteSpace(_workspaceFolderPath))
        {
            var filePath = node.RelatedFiles.FirstOrDefault(p => !string.IsNullOrWhiteSpace(p));
            if (!string.IsNullOrWhiteSpace(filePath))
                summaryText = LlmSummaryService.GetSummaryFirstParagraph(_workspaceFolderPath, filePath) ?? string.Empty;
        }
        SelectedNodeSummaryFirstParagraph = summaryText;
    }

    private void PopulateSelectedNodeCallers(NodeViewModel node)
    {
        _selectedNodeCallers.Clear();

        if (!string.Equals(node.EntityType, CodeNodeEntityType, StringComparison.Ordinal))
        {
            OnPropertyChanged(nameof(HasSelectedNodeCallers));
            return;
        }

        var callerGroups = BuildCallersForCodeNode(node.Key);

        foreach (var caller in callerGroups)
            _selectedNodeCallers.Add(caller);

        OnPropertyChanged(nameof(HasSelectedNodeCallers));
    }

    private List<GraphCallerVm> BuildCallersForCodeNode(string codeNodeId)
    {
        if (_currentSystemMap == null || string.IsNullOrWhiteSpace(codeNodeId))
            return new List<GraphCallerVm>();

        var map = _currentSystemMap;
        EnsureCallerLookups(map);

        return map.Relationships
            .Where(rel => string.Equals(rel.ToId, codeNodeId, StringComparison.Ordinal))
            .Where(rel => !string.Equals(rel.FromId, rel.ToId, StringComparison.Ordinal))
            .Where(rel => ShowLowConfidenceItems || rel.Confidence != ConfidenceLevel.Unknown)
            .Where(rel => IsKindVisible(rel.Kind))
            .GroupBy(rel => rel.FromId, StringComparer.Ordinal)
            .Select(group =>
            {
                if (!_moduleByCodeNodeId.TryGetValue(group.Key, out var callerModule)
                    || !_codeNodeById.TryGetValue(group.Key, out var callerNode))
                    return null;

                var ownerSystemName = ResolveOwnerSystemName(map, callerModule);
                var relationKinds = group
                    .Select(rel => rel.Kind.ToString())
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(kind => kind, StringComparer.Ordinal)
                    .ToArray();
                var relationLabel = string.Join(", ", relationKinds);

                return new GraphCallerVm
                {
                    CallerName = callerNode.Name,
                    CallerContext = $"{callerModule.Name} · {ownerSystemName} · {relationLabel}",
                    ModuleId = callerModule.Id,
                    CallerCodeNodeId = callerNode.Id,
                    RelationshipLabel = relationLabel
                };
            })
            .Where(caller => caller != null)
            .Cast<GraphCallerVm>()
            .OrderBy(caller => caller.CallerName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(caller => caller.ModuleId, StringComparer.Ordinal)
            .ToList();
    }

    private void PopulateCallerOverlay(NodeViewModel selectedNode)
    {
        if (_renderMode != GraphRenderMode.CodeNodeRelationships) return;
        if (!string.Equals(selectedNode.EntityType, CodeNodeEntityType, StringComparison.Ordinal)) return;

        var callers = BuildCallersForCodeNode(selectedNode.Key);
        if (callers.Count == 0) return;

        var existingNodeKeys = new HashSet<string>(Nodes.Select(n => n.Key), StringComparer.Ordinal);
        var callersToRender = callers
            .Where(caller => !string.IsNullOrWhiteSpace(caller.CallerCodeNodeId))
            .Where(caller => !existingNodeKeys.Contains(caller.CallerCodeNodeId))
            .ToList();

        if (callersToRender.Count == 0) return;

        const double overlayXOffset = 360;
        const double overlayYStep = 130;
        var startY = selectedNode.Location.Y - ((callersToRender.Count - 1) * overlayYStep / 2d);

        for (var i = 0; i < callersToRender.Count; i++)
        {
            var caller = callersToRender[i];
            var overlayNodeKey = $"__caller_overlay__:{selectedNode.Key}:{caller.CallerCodeNodeId}";
            var overlayNode = new NodeViewModel
            {
                Key = overlayNodeKey,
                Title = caller.CallerName,
                Subtitle = caller.CallerContext,
                KindLabel = "Caller",
                KindBadgeBackground = "#2A354A",
                KindBadgeForeground = "#C9D8EA",
                EntityType = "Caller",
                Confidence = string.Empty,
                FullName = string.Empty,
                ModuleName = string.Empty,
                SystemName = string.Empty,
                RelatedFiles = Array.Empty<string>(),
                Location = new Point(selectedNode.Location.X - overlayXOffset, startY + (i * overlayYStep))
            };

            overlayNode.OutputConnector.IsConnected = true;
            selectedNode.InputConnector.IsConnected = true;

            var overlayConnection = new ConnectionViewModel(
                overlayNode.OutputConnector,
                selectedNode.InputConnector,
                caller.RelationshipLabel)
            {
                Stroke = "#E0A84E",
                StrokeThickness = 2.25
            };

            Nodes.Add(overlayNode);
            Connections.Add(overlayConnection);
            _nodeEdges.Add((overlayNode, selectedNode));
            _callerOverlayNodeKeys.Add(overlayNodeKey);
            _callerOverlayConnections.Add(overlayConnection);
        }

        RecalculateNodeEdgeStats();
    }

    private void ClearCallerOverlay()
    {
        if (_callerOverlayNodeKeys.Count == 0 && _callerOverlayConnections.Count == 0)
            return;

        _nodeEdges.RemoveAll(edge =>
            _callerOverlayNodeKeys.Contains(edge.From.Key) || _callerOverlayNodeKeys.Contains(edge.To.Key));

        if (_callerOverlayConnections.Count > 0)
        {
            foreach (var connection in _callerOverlayConnections)
                Connections.Remove(connection);
            _callerOverlayConnections.Clear();
        }

        if (_callerOverlayNodeKeys.Count > 0)
        {
            for (var index = Nodes.Count - 1; index >= 0; index--)
            {
                if (_callerOverlayNodeKeys.Contains(Nodes[index].Key))
                    Nodes.RemoveAt(index);
            }
            _callerOverlayNodeKeys.Clear();
        }

        RecalculateNodeEdgeStats();
    }

    private void RecalculateNodeEdgeStats()
    {
        foreach (var node in Nodes) node.ChildCount = 0;
        foreach (var (from, _) in _nodeEdges) from.ChildCount++;
        foreach (var node in Nodes)
            node.IsCodeLeaf = string.Equals(node.EntityType, CodeNodeEntityType, StringComparison.Ordinal) && node.ChildCount == 0;
    }

    private void EnsureCallerLookups(SystemMapModel map)
    {
        if (ReferenceEquals(_callerLookupMap, map))
            return;

        _callerLookupMap = map;
        _moduleByCodeNodeId.Clear();
        _codeNodeById.Clear();

        foreach (var module in map.AllModules)
        {
            foreach (var codeNode in module.CodeNodes)
            {
                _moduleByCodeNodeId[codeNode.Id] = module;
                _codeNodeById[codeNode.Id] = codeNode;
            }
        }
    }

    private static string ResolveOwnerSystemName(SystemMapModel map, ModuleModel module)
    {
        var ownerSystemId = module.SystemIds.FirstOrDefault()
            ?? map.Systems.FirstOrDefault(system =>
                system.Modules.Any(systemModule => string.Equals(systemModule.Id, module.Id, StringComparison.Ordinal)))?.Id;
        if (string.IsNullOrWhiteSpace(ownerSystemId))
            return "(unknown system)";

        return map.Systems
            .FirstOrDefault(system => string.Equals(system.Id, ownerSystemId, StringComparison.Ordinal))
            ?.Name
            ?? "(unknown system)";
    }

    private static string KindBadgeBackgroundForCodeNode(CodeNodeKind kind) => kind switch
    {
        CodeNodeKind.Service => "#2D1C52",
        CodeNodeKind.Class => "#183A66",
        CodeNodeKind.Interface => "#164C4C",
        _ => "#1E3A5F"
    };

    private static string KindBadgeForegroundForCodeNode(CodeNodeKind kind) => kind switch
    {
        CodeNodeKind.Service => "#D6B8FF",
        CodeNodeKind.Class => "#9FCEFF",
        CodeNodeKind.Interface => "#9FE7E7",
        _ => "#8BD4FF"
    };

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

    private void BuildAdjacency(
        out Dictionary<NodeViewModel, List<NodeViewModel>> successors,
        out Dictionary<NodeViewModel, List<NodeViewModel>> predecessors,
        out Dictionary<NodeViewModel, List<NodeViewModel>> undirected)
    {
        successors = Nodes.ToDictionary(n => n, _ => new List<NodeViewModel>());
        predecessors = Nodes.ToDictionary(n => n, _ => new List<NodeViewModel>());
        undirected = Nodes.ToDictionary(n => n, _ => new List<NodeViewModel>());

        foreach (var (from, to) in _nodeEdges)
        {
            if (!successors.ContainsKey(from) || !successors.ContainsKey(to))
                continue;

            successors[from].Add(to);
            predecessors[to].Add(from);

            undirected[from].Add(to);
            undirected[to].Add(from);
        }
    }

    private static List<HashSet<NodeViewModel>> ComputeWeaklyConnectedComponents(
        IEnumerable<NodeViewModel> nodes,
        IReadOnlyDictionary<NodeViewModel, List<NodeViewModel>> undirected)
    {
        var remaining = new HashSet<NodeViewModel>(nodes);
        var result = new List<HashSet<NodeViewModel>>();

        while (remaining.Count > 0)
        {
            var seed = remaining.First();
            var queue = new Queue<NodeViewModel>();
            var component = new HashSet<NodeViewModel>();

            queue.Enqueue(seed);
            remaining.Remove(seed);
            component.Add(seed);

            while (queue.Count > 0)
            {
                var node = queue.Dequeue();
                foreach (var next in undirected[node])
                {
                    if (!remaining.Remove(next)) continue;
                    component.Add(next);
                    queue.Enqueue(next);
                }
            }

            result.Add(component);
        }

        return result;
    }

    private static List<List<NodeViewModel>> ComputeLayersForSubset(
        IReadOnlySet<NodeViewModel> subset,
        IReadOnlyDictionary<NodeViewModel, List<NodeViewModel>> successors)
    {
        var inDegree = subset.ToDictionary(n => n, _ => 0);

        foreach (var node in subset)
        {
            foreach (var succ in successors[node])
            {
                if (inDegree.ContainsKey(succ))
                    inDegree[succ]++;
            }
        }

        var depth = subset.ToDictionary(n => n, _ => 0);
        var queue = new Queue<NodeViewModel>(subset.Where(n => inDegree[n] == 0));
        var visited = new HashSet<NodeViewModel>();

        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            if (!visited.Add(node))
                continue;

            foreach (var succ in successors[node])
            {
                if (!inDegree.ContainsKey(succ)) continue;

                if (depth[succ] < depth[node] + 1)
                    depth[succ] = depth[node] + 1;

                if (--inDegree[succ] == 0)
                    queue.Enqueue(succ);
            }
        }

        int maxDepth = visited.Count > 0 ? visited.Max(n => depth[n]) : 0;
        int cycleDepth = maxDepth + 1;
        bool hasCycles = false;

        foreach (var node in subset)
        {
            if (visited.Contains(node)) continue;
            depth[node] = cycleDepth;
            hasCycles = true;
        }

        int layerCount = (hasCycles ? cycleDepth : maxDepth) + 1;
        var layers = Enumerable.Range(0, layerCount)
            .Select(_ => new List<NodeViewModel>())
            .ToList();

        foreach (var node in subset)
            layers[depth[node]].Add(node);

        return layers;
    }

    private static void RemoveEmptyLayers(List<List<NodeViewModel>> layers)
        => layers.RemoveAll(layer => layer.Count == 0);

    private static void OrderLayersByBarycenter(
        List<List<NodeViewModel>> layers,
        IReadOnlyDictionary<NodeViewModel, List<NodeViewModel>> predecessors,
        IReadOnlyDictionary<NodeViewModel, List<NodeViewModel>> successors,
        int sweeps)
    {
        if (layers.Count <= 1)
            return;

        sweeps = Math.Max(1, sweeps);

        for (int sweep = 0; sweep < sweeps; sweep++)
        {
            for (int i = 1; i < layers.Count; i++)
                SortLayerByNeighborOrder(layers[i], layers[i - 1], predecessors);

            for (int i = layers.Count - 2; i >= 0; i--)
                SortLayerByNeighborOrder(layers[i], layers[i + 1], successors);
        }
    }

    private static void SortLayerByNeighborOrder(
        List<NodeViewModel> layer,
        List<NodeViewModel> referenceLayer,
        IReadOnlyDictionary<NodeViewModel, List<NodeViewModel>> neighborLookup)
    {
        if (layer.Count <= 1 || referenceLayer.Count == 0)
            return;

        var refIndex = referenceLayer
            .Select((node, index) => (node, index))
            .ToDictionary(x => x.node, x => (double)x.index);

        var withBarycenter = layer
            .Select((node, index) =>
            {
                var neighbors = neighborLookup[node]
                    .Where(refIndex.ContainsKey)
                    .ToList();

                var barycenter = neighbors.Count > 0
                    ? neighbors.Average(n => refIndex[n])
                    : (double)index;

                var degree = neighborLookup[node].Count;
                return (node, barycenter, degree, index);
            })
            .OrderBy(x => x.barycenter)
            .ThenByDescending(x => x.degree)
            .ThenBy(x => x.index)
            .Select(x => x.node)
            .ToList();

        layer.Clear();
        layer.AddRange(withBarycenter);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
