using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Codomon.Desktop.Models.SystemMap;

namespace Codomon.Desktop.ViewModels;

/// <summary>Which of the four System Map views is currently active.</summary>
public enum SystemMapViewKind
{
    SystemOverview,
    ModuleView,
    CodeDetailView,
    StartupView
}

/// <summary>Item view-model for a System in the System Map.</summary>
public class SystemItemVm
{
    public string Id               { get; init; } = string.Empty;
    public string Name             { get; init; } = string.Empty;
    public string KindLabel        { get; init; } = string.Empty;
    public string StartupMechanism { get; init; } = string.Empty;
    public ConfidenceLevel Confidence { get; init; }
    public int ModuleCount         { get; init; }
}

/// <summary>Item view-model for an External System.</summary>
public class ExternalSystemItemVm
{
    public string Id   { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Kind { get; init; } = string.Empty;
    public ConfidenceLevel Confidence { get; init; }
}

/// <summary>Item view-model for a Module.</summary>
public class ModuleItemVm
{
    public string Id           { get; init; } = string.Empty;
    public string Name         { get; init; } = string.Empty;
    public string KindLabel    { get; init; } = string.Empty;
    public ConfidenceLevel Confidence { get; init; }
    public int CodeNodeCount   { get; init; }
    public string SystemId     { get; init; } = string.Empty;
}

/// <summary>Item view-model for a Code Node.</summary>
public class CodeNodeItemVm
{
    public string Id             { get; init; } = string.Empty;
    public string Name           { get; init; } = string.Empty;
    public string KindLabel      { get; init; } = string.Empty;
    public ConfidenceLevel Confidence { get; init; }
    public string FullName       { get; init; } = string.Empty;
    public string FilePath       { get; init; } = string.Empty;
    public string ModuleName     { get; init; } = string.Empty;
    public bool IsHighValue      { get; init; }
    public string SourceModuleId { get; init; } = string.Empty;
}

/// <summary>Item view-model for the Startup view.</summary>
public class StartupItemVm
{
    public string Id               { get; init; } = string.Empty;
    public string Name             { get; init; } = string.Empty;
    public string KindLabel        { get; init; } = string.Empty;
    public string StartupMechanism { get; init; } = string.Empty;
    public int StartOrder          { get; init; }
    public List<string> EntryPoints { get; init; } = new();
}

/// <summary>
/// View-model that drives the four System Map views (System Overview, Module View,
/// Code Detail View, Startup View) and their shared inspector panel and filters.
/// </summary>
public class SystemMapViewModel : INotifyPropertyChanged
{
    // High-value code node kinds shown when ShowOnlyHighValueCodeNodes is active.
    private static readonly HashSet<CodeNodeKind> HighValueKinds = new()
    {
        CodeNodeKind.EntryPoint, CodeNodeKind.Service, CodeNodeKind.ViewModel,
        CodeNodeKind.View, CodeNodeKind.Dialog, CodeNodeKind.Repository
    };

    private SystemMapViewKind _activeView = SystemMapViewKind.SystemOverview;
    private SystemItemVm? _selectedSystem;
    private ModuleItemVm? _selectedModule;
    private bool _showExternalSystems    = true;
    private bool _showStartupRelationships = false;
    private bool _showLowConfidenceItems = true;
    private bool _showOnlyHighValueCodeNodes = false;

    // Full unfiltered data — kept so filters can be re-applied without a full reload.
    private List<SystemItemVm>         _allSystems         = new();
    private List<ExternalSystemItemVm> _allExternalSystems = new();
    private List<ModuleItemVm>         _allModules         = new();
    private List<CodeNodeItemVm>       _allCodeNodes       = new();
    private List<StartupItemVm>        _allStartupItems    = new();

    // ── Collections bound to the view ─────────────────────────────────────

    public ObservableCollection<SystemItemVm>         Systems                    { get; } = new();
    public ObservableCollection<ExternalSystemItemVm> ExternalSystems            { get; } = new();
    public ObservableCollection<ModuleItemVm>         ModulesForSelectedSystem   { get; } = new();
    public ObservableCollection<CodeNodeItemVm>       CodeNodesForSelectedScope  { get; } = new();
    public ObservableCollection<StartupItemVm>        StartupItems               { get; } = new();

    // ── Active view ────────────────────────────────────────────────────────

    public SystemMapViewKind ActiveView
    {
        get => _activeView;
        set
        {
            if (_activeView == value) return;
            _activeView = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsSystemOverviewActive));
            OnPropertyChanged(nameof(IsModuleViewActive));
            OnPropertyChanged(nameof(IsCodeDetailViewActive));
            OnPropertyChanged(nameof(IsStartupViewActive));
        }
    }

    public bool IsSystemOverviewActive => _activeView == SystemMapViewKind.SystemOverview;
    public bool IsModuleViewActive     => _activeView == SystemMapViewKind.ModuleView;
    public bool IsCodeDetailViewActive => _activeView == SystemMapViewKind.CodeDetailView;
    public bool IsStartupViewActive    => _activeView == SystemMapViewKind.StartupView;

    // ── Filters ────────────────────────────────────────────────────────────

    public bool ShowExternalSystems
    {
        get => _showExternalSystems;
        set { _showExternalSystems = value; OnPropertyChanged(); ApplyFilters(); }
    }

    public bool ShowStartupRelationships
    {
        get => _showStartupRelationships;
        set { _showStartupRelationships = value; OnPropertyChanged(); ApplyFilters(); }
    }

    public bool ShowLowConfidenceItems
    {
        get => _showLowConfidenceItems;
        set { _showLowConfidenceItems = value; OnPropertyChanged(); ApplyFilters(); }
    }

    public bool ShowOnlyHighValueCodeNodes
    {
        get => _showOnlyHighValueCodeNodes;
        set { _showOnlyHighValueCodeNodes = value; OnPropertyChanged(); ApplyFilters(); }
    }

    // ── Selection & context ───────────────────────────────────────────────

    public SystemItemVm? SelectedSystem
    {
        get => _selectedSystem;
        private set
        {
            _selectedSystem = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedSystemName));
            RebuildModulesForSelectedSystem();
            RebuildCodeNodesForSelectedScope();
        }
    }

    public ModuleItemVm? SelectedModule
    {
        get => _selectedModule;
        private set
        {
            _selectedModule = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedModuleName));
            RebuildCodeNodesForSelectedScope();
        }
    }

    public string SelectedSystemName => _selectedSystem?.Name ?? "(none selected)";
    public string SelectedModuleName => _selectedModule?.Name ?? "(none selected)";

    // ── Inspector ──────────────────────────────────────────────────────────

    private string       _inspectorName       = "Nothing selected";
    private string       _inspectorType       = string.Empty;
    private string       _inspectorKind       = string.Empty;
    private string       _inspectorNotes      = string.Empty;
    private string       _inspectorConfidence = string.Empty;
    private List<string> _inspectorDetails    = new();

    public string InspectorName
    {
        get => _inspectorName;
        private set { _inspectorName = value; OnPropertyChanged(); }
    }

    public string InspectorType
    {
        get => _inspectorType;
        private set { _inspectorType = value; OnPropertyChanged(); }
    }

    public string InspectorKind
    {
        get => _inspectorKind;
        private set { _inspectorKind = value; OnPropertyChanged(); }
    }

    public string InspectorNotes
    {
        get => _inspectorNotes;
        private set { _inspectorNotes = value; OnPropertyChanged(); }
    }

    public string InspectorConfidence
    {
        get => _inspectorConfidence;
        private set { _inspectorConfidence = value; OnPropertyChanged(); }
    }

    public List<string> InspectorDetails
    {
        get => _inspectorDetails;
        private set { _inspectorDetails = value; OnPropertyChanged(); }
    }

    // ── Public API ─────────────────────────────────────────────────────────

    public void SetActiveView(SystemMapViewKind view) => ActiveView = view;

    public void SelectSystem(SystemItemVm? sys)
    {
        SelectedSystem = sys;
        SelectedModule = null;
        UpdateInspectorForSystem(sys);
    }

    public void SelectModule(ModuleItemVm? mod)
    {
        SelectedModule = mod;
        UpdateInspectorForModule(mod);
    }

    public void SelectExternalSystem(ExternalSystemItemVm? ext)
        => UpdateInspectorForExternalSystem(ext);

    public void SelectCodeNode(CodeNodeItemVm? node)
        => UpdateInspectorForCodeNode(node);

    /// <summary>
    /// Rebuilds all collections from <paramref name="model"/>.
    /// Call this whenever the workspace's <see cref="SystemMapModel"/> changes.
    /// </summary>
    public void LoadFrom(SystemMapModel model)
    {
        _allSystems = model.Systems.Select(s => new SystemItemVm
        {
            Id               = s.Id,
            Name             = s.Name,
            KindLabel        = s.Kind.ToString(),
            StartupMechanism = s.StartupMechanism,
            Confidence       = s.Confidence,
            ModuleCount      = s.Modules.Count
        }).ToList();

        _allExternalSystems = model.ExternalSystems.Select(e => new ExternalSystemItemVm
        {
            Id         = e.Id,
            Name       = e.Name,
            Kind       = e.Kind,
            Confidence = e.Confidence
        }).ToList();

        _allModules = model.AllModules.Select(m => new ModuleItemVm
        {
            Id            = m.Id,
            Name          = m.Name,
            KindLabel     = m.Kind.ToString(),
            Confidence    = m.Confidence,
            CodeNodeCount = m.CodeNodes.Count,
            SystemId      = m.SystemIds.FirstOrDefault() ?? string.Empty
        }).ToList();

        _allCodeNodes = model.AllCodeNodes.Select(cn =>
        {
            var ownerModule = model.AllModules
                .FirstOrDefault(m => m.CodeNodes.Any(c => c.Id == cn.Id));
            return new CodeNodeItemVm
            {
                Id             = cn.Id,
                Name           = cn.Name,
                KindLabel      = cn.Kind.ToString(),
                Confidence     = cn.Confidence,
                FullName       = cn.FullName,
                FilePath       = cn.FilePath,
                ModuleName     = ownerModule?.Name ?? string.Empty,
                IsHighValue    = HighValueKinds.Contains(cn.Kind),
                SourceModuleId = ownerModule?.Id ?? string.Empty
            };
        }).ToList();

        var startupOrder = ComputeStartupOrder(model);
        _allStartupItems = model.Systems.Select(s =>
        {
            int order = startupOrder.TryGetValue(s.Id, out var o) ? o : 0;
            return new StartupItemVm
            {
                Id               = s.Id,
                Name             = s.Name,
                KindLabel        = s.Kind.ToString(),
                StartupMechanism = s.StartupMechanism,
                StartOrder       = order,
                EntryPoints      = s.EntryPointCandidates.Take(3).ToList()
            };
        }).OrderBy(i => i.StartOrder).ToList();

        // Reset selection state.
        _selectedSystem = null;
        _selectedModule = null;
        OnPropertyChanged(nameof(SelectedSystemName));
        OnPropertyChanged(nameof(SelectedModuleName));

        ClearInspector();
        ApplyFilters();
    }

    /// <summary>Re-applies the current filter settings to all collections.</summary>
    public void ApplyFilters()
    {
        bool lowConf = ShowLowConfidenceItems;

        SyncCollection(Systems,
            _allSystems
                .Where(s => lowConf || IsHighConfidence(s.Confidence))
                .ToList());

        SyncCollection(ExternalSystems,
            ShowExternalSystems
                ? _allExternalSystems
                    .Where(e => lowConf || IsHighConfidence(e.Confidence))
                    .ToList()
                : new List<ExternalSystemItemVm>());

        SyncCollection(StartupItems,
            _allStartupItems
                .Where(s =>
                {
                    var sys = _allSystems.FirstOrDefault(x => x.Id == s.Id);
                    return sys == null || lowConf || IsHighConfidence(sys.Confidence);
                })
                .ToList());

        RebuildModulesForSelectedSystem();
        RebuildCodeNodesForSelectedScope();
    }

    // ── Private helpers ────────────────────────────────────────────────────

    private void RebuildModulesForSelectedSystem()
    {
        bool lowConf = ShowLowConfidenceItems;
        SyncCollection(ModulesForSelectedSystem,
            _allModules
                .Where(m => _selectedSystem == null || m.SystemId == _selectedSystem.Id)
                .Where(m => lowConf || IsHighConfidence(m.Confidence))
                .ToList());
    }

    private void RebuildCodeNodesForSelectedScope()
    {
        bool lowConf  = ShowLowConfidenceItems;
        bool highOnly = ShowOnlyHighValueCodeNodes;

        var filtered = _allCodeNodes.AsEnumerable();

        if (_selectedModule != null)
        {
            filtered = filtered.Where(c => c.SourceModuleId == _selectedModule.Id);
        }
        else if (_selectedSystem != null)
        {
            var moduleIds = _allModules
                .Where(m => m.SystemId == _selectedSystem.Id)
                .Select(m => m.Id)
                .ToHashSet(StringComparer.Ordinal);
            filtered = filtered.Where(c => moduleIds.Contains(c.SourceModuleId));
        }

        if (!lowConf)
            filtered = filtered.Where(c => IsHighConfidence(c.Confidence));

        if (highOnly)
            filtered = filtered.Where(c => c.IsHighValue);

        SyncCollection(CodeNodesForSelectedScope, filtered.ToList());
    }

    private static bool IsHighConfidence(ConfidenceLevel c)
        => c is ConfidenceLevel.Manual or ConfidenceLevel.Confirmed or ConfidenceLevel.Likely;

    private static Dictionary<string, int> ComputeStartupOrder(SystemMapModel model)
    {
        // Nodes depended on by others start earlier (lower order number).
        var depth = model.Systems.ToDictionary(s => s.Id, _ => 0, StringComparer.Ordinal);
        bool changed = true;
        for (int iter = 0; iter < model.Systems.Count && changed; iter++)
        {
            changed = false;
            foreach (var rel in model.Relationships.Where(r => r.Kind == RelationshipKind.Depends))
            {
                if (depth.TryGetValue(rel.FromId, out int from) &&
                    depth.TryGetValue(rel.ToId,   out int to)   &&
                    from <= to)
                {
                    depth[rel.FromId] = to + 1;
                    changed = true;
                }
            }
        }
        return depth;
    }

    private static void SyncCollection<T>(ObservableCollection<T> col, List<T> items)
    {
        col.Clear();
        foreach (var item in items)
            col.Add(item);
    }

    private void ClearInspector()
    {
        InspectorName       = "Nothing selected";
        InspectorType       = string.Empty;
        InspectorKind       = string.Empty;
        InspectorNotes      = string.Empty;
        InspectorConfidence = string.Empty;
        InspectorDetails    = new List<string>();
    }

    private void UpdateInspectorForSystem(SystemItemVm? sys)
    {
        if (sys == null) { ClearInspector(); return; }
        InspectorName       = sys.Name;
        InspectorType       = "System";
        InspectorKind       = sys.KindLabel;
        InspectorNotes      = !string.IsNullOrEmpty(sys.StartupMechanism)
                                  ? $"Startup: {sys.StartupMechanism}"
                                  : string.Empty;
        InspectorConfidence = sys.Confidence.ToString();
        InspectorDetails    = sys.ModuleCount > 0
            ? new List<string> { $"{sys.ModuleCount} module(s)" }
            : new List<string>();
    }

    private void UpdateInspectorForModule(ModuleItemVm? mod)
    {
        if (mod == null) { ClearInspector(); return; }
        InspectorName       = mod.Name;
        InspectorType       = "Module";
        InspectorKind       = mod.KindLabel;
        InspectorNotes      = string.Empty;
        InspectorConfidence = mod.Confidence.ToString();
        InspectorDetails    = mod.CodeNodeCount > 0
            ? new List<string> { $"{mod.CodeNodeCount} code node(s)" }
            : new List<string>();
    }

    private void UpdateInspectorForExternalSystem(ExternalSystemItemVm? ext)
    {
        if (ext == null) { ClearInspector(); return; }
        InspectorName       = ext.Name;
        InspectorType       = "External System";
        InspectorKind       = ext.Kind;
        InspectorNotes      = string.Empty;
        InspectorConfidence = ext.Confidence.ToString();
        InspectorDetails    = new List<string>();
    }

    private void UpdateInspectorForCodeNode(CodeNodeItemVm? node)
    {
        if (node == null) { ClearInspector(); return; }
        InspectorName       = node.Name;
        InspectorType       = "Code Node";
        InspectorKind       = node.KindLabel;
        InspectorNotes      = !string.IsNullOrEmpty(node.FilePath) ? node.FilePath : string.Empty;
        InspectorConfidence = node.Confidence.ToString();
        InspectorDetails    = !string.IsNullOrEmpty(node.FullName)
            ? new List<string> { node.FullName }
            : new List<string>();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
