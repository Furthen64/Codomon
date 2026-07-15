using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Codomon.Desktop.Models;
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
    public string Name             { get; set; }  = string.Empty;
    public string KindLabel        { get; init; } = string.Empty;
    public string StartupMechanism { get; init; } = string.Empty;
    public ConfidenceLevel Confidence { get; init; }
    public int ModuleCount         { get; init; }
    public double X                { get; set; }
    public double Y                { get; set; }
    public ArchitectureLayerKind LayerKind { get; init; }
    /// <summary>Short human-readable description sourced from the system's Notes field.</summary>
    public string Description { get; init; } = string.Empty;
    /// <summary>Top module kinds for this system, in descending order of count.</summary>
    public IReadOnlyList<(string Kind, int Count)> ModuleKindCounts { get; init; } = Array.Empty<(string, int)>();

    /// <summary>Primary source file path (first entry-point candidate), or empty.</summary>
    public string SourceFile { get; init; } = string.Empty;
    /// <summary>First source line (0 = unknown).</summary>
    public int SourceLineStart { get; init; }
    /// <summary>Last source line (0 = unknown).</summary>
    public int SourceLineEnd { get; init; }
    /// <summary>Bullet-list responsibilities; populated from manual notes or empty.</summary>
    public IReadOnlyList<string> Responsibilities { get; init; } = Array.Empty<string>();

    /// <summary>True when this system is a class-library rather than a runnable app.</summary>
    public bool IsLibrary =>
        string.Equals(KindLabel, nameof(SystemKind.LibraryOnly), StringComparison.Ordinal) ||
        string.Equals(StartupMechanism, "Class Library", StringComparison.OrdinalIgnoreCase);
}

/// <summary>Item view-model for an External System.</summary>
public class ExternalSystemItemVm
{
    public string Id   { get; init; } = string.Empty;
    public string Name { get; set; }  = string.Empty;
    public string Kind { get; init; } = string.Empty;
    public ConfidenceLevel Confidence { get; init; }
    public double X { get; set; }
    public double Y { get; set; }
}

/// <summary>Item view-model for a Module.</summary>
public class ModuleItemVm
{
    public string Id           { get; init; } = string.Empty;
    public string Name         { get; set; }  = string.Empty;
    public string KindLabel    { get; init; } = string.Empty;
    public ConfidenceLevel Confidence { get; init; }
    public int CodeNodeCount   { get; init; }
    public string SystemId     { get; init; } = string.Empty;
    public HashSet<string> SystemIds { get; init; } = new(StringComparer.Ordinal);
    public int OutboundRelationshipCount { get; init; }
    public int InboundRelationshipCount { get; init; }
    public IReadOnlyList<string> OutboundHighlights { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> InboundHighlights { get; init; } = Array.Empty<string>();
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
    public bool IsNoisy          { get; init; }
    public bool HideFromOverview { get; init; }
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

/// <summary>The architectural tier a System belongs to in the layered architecture view.</summary>
public enum ArchitectureLayerKind
{
    Presentation,
    Application,
    Domain,
    Infrastructure
}

/// <summary>
/// Item view-model for a visible typed relationship between two top-level entities
/// (System → System, System → External System, or External System → System).
/// </summary>
public class RelationshipItemVm
{
    public string Id             { get; init; } = string.Empty;
    public string FromId         { get; init; } = string.Empty;
    public string ToId           { get; init; } = string.Empty;
    public RelationshipKind Kind { get; init; }
    public string Label          { get; init; } = string.Empty;
    public ConfidenceLevel Confidence { get; init; }
    public string Notes          { get; init; } = string.Empty;
    public string FromName       { get; init; } = string.Empty;
    public string ToName         { get; init; } = string.Empty;
}

/// <summary>
/// View-model that drives the four System Map views (System Overview, Module View,
/// Code Detail View, Startup View) and their shared inspector panel and filters.
/// </summary>
public class SystemMapViewModel : INotifyPropertyChanged
{
    private const string LayoutPrefix = "systemmap:";
    private const double CardStartX = 24;
    private const double CardStartY = 18;
    private const double CardGapX = 248;
    private const double CardGapY = 220;
    private const int CardsPerRow = 4;
    /// <summary>
    /// Default base row for external system card layout on the unified canvas.
    /// Keeps externals below the typical system area without hard-coding a pixel offset.
    /// </summary>
    internal const int ExternalBaseRow = 4;

    /// <summary>Characters treated as token separators when parsing names in CleanupNames.</summary>
    private static readonly char[] NameSeparators = { '.', '-', '_', ' ' };

    // High-value code node kinds shown when ShowOnlyHighValueCodeNodes is active.
    private static readonly HashSet<CodeNodeKind> HighValueKinds = new()
    {
        CodeNodeKind.EntryPoint, CodeNodeKind.Service, CodeNodeKind.ViewModel,
        CodeNodeKind.View, CodeNodeKind.Dialog, CodeNodeKind.Repository
    };

    private SystemMapViewKind _activeView = SystemMapViewKind.SystemOverview;
    private SystemItemVm? _selectedSystem;
    private ModuleItemVm? _selectedModule;
    private RelationshipItemVm? _selectedRelationship;
    private bool _showExternalSystems    = true;
    private bool _showStartupRelationships = false;
    private bool _showLowConfidenceItems = true;
    private bool _showOnlyHighValueCodeNodes = false;
    private SystemMapModel? _currentModel;

    // Full unfiltered data — kept so filters can be re-applied without a full reload.
    private List<SystemItemVm>         _allSystems         = new();
    private List<ExternalSystemItemVm> _allExternalSystems = new();
    private List<ModuleItemVm>         _allModules         = new();
    private List<CodeNodeItemVm>       _allCodeNodes       = new();
    private List<StartupItemVm>        _allStartupItems    = new();
    private List<RelationshipItemVm>   _allRelationships   = new();

    // ── Collections bound to the view ─────────────────────────────────────

    public ObservableCollection<SystemItemVm>         Systems                    { get; } = new();
    public ObservableCollection<ExternalSystemItemVm> ExternalSystems            { get; } = new();
    public ObservableCollection<ModuleItemVm>         ModulesForSelectedSystem   { get; } = new();
    public ObservableCollection<RelationshipItemVm>   ModuleRelationshipsForSelectedSystem { get; } = new();
    public ObservableCollection<CodeNodeItemVm>       CodeNodesForSelectedScope  { get; } = new();
    public ObservableCollection<StartupItemVm>        StartupItems               { get; } = new();
    public ObservableCollection<RelationshipItemVm>   VisibleRelationships       { get; } = new();

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

    /// <summary>The relationship currently shown in the inspector, or null if none selected.</summary>
    public RelationshipItemVm? SelectedRelationship
    {
        get => _selectedRelationship;
        private set { _selectedRelationship = value; OnPropertyChanged(); }
    }

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

    // ── Extended Component-tab inspector state ─────────────────────────────

    private bool   _inspectorIsSystemSelected;
    private string _inspectorLayerLabel      = string.Empty;
    private string _inspectorLayerColor      = "#4A6A8A";
    private string _inspectorDescription     = string.Empty;
    private string _inspectorSourceFile      = string.Empty;
    private string _inspectorSourceLineRange = string.Empty;
    private List<string>             _inspectorResponsibilities   = new();
    private List<RelationshipItemVm> _inspectorInboundConnections  = new();
    private List<RelationshipItemVm> _inspectorOutboundConnections = new();

    /// <summary>True when a System (not a relationship/module/code node) is selected.</summary>
    public bool InspectorIsSystemSelected
    {
        get => _inspectorIsSystemSelected;
        private set { _inspectorIsSystemSelected = value; OnPropertyChanged(); }
    }

    /// <summary>Human-readable layer label for the selected system (e.g. "Application Layer").</summary>
    public string InspectorLayerLabel
    {
        get => _inspectorLayerLabel;
        private set { _inspectorLayerLabel = value; OnPropertyChanged(); }
    }

    /// <summary>Accent colour hex for the selected system's architectural layer.</summary>
    public string InspectorLayerColor
    {
        get => _inspectorLayerColor;
        private set { _inspectorLayerColor = value; OnPropertyChanged(); }
    }

    /// <summary>Short description text for the selected component.</summary>
    public string InspectorDescription
    {
        get => _inspectorDescription;
        private set { _inspectorDescription = value; OnPropertyChanged(); }
    }

    /// <summary>Primary source file path for the selected system, or empty.</summary>
    public string InspectorSourceFile
    {
        get => _inspectorSourceFile;
        private set { _inspectorSourceFile = value; OnPropertyChanged(); }
    }

    /// <summary>Formatted source line range (e.g. "Lines: 45–247") or empty.</summary>
    public string InspectorSourceLineRange
    {
        get => _inspectorSourceLineRange;
        private set { _inspectorSourceLineRange = value; OnPropertyChanged(); }
    }

    /// <summary>Responsibility bullet items for the selected system.</summary>
    public List<string> InspectorResponsibilities
    {
        get => _inspectorResponsibilities;
        private set { _inspectorResponsibilities = value; OnPropertyChanged(); }
    }

    /// <summary>Inbound relationships targeting the selected system.</summary>
    public List<RelationshipItemVm> InspectorInboundConnections
    {
        get => _inspectorInboundConnections;
        private set { _inspectorInboundConnections = value; OnPropertyChanged(); }
    }

    /// <summary>Outbound relationships originating from the selected system.</summary>
    public List<RelationshipItemVm> InspectorOutboundConnections
    {
        get => _inspectorOutboundConnections;
        private set { _inspectorOutboundConnections = value; OnPropertyChanged(); }
    }


    // ── Public API ─────────────────────────────────────────────────────────

    public void SetActiveView(SystemMapViewKind view) => ActiveView = view;

    /// <summary>
    /// Resets the map to its top-level landing state used from the Overview page.
    /// This ensures the user sees the system card canvas rather than stale deeper selections.
    /// </summary>
    public void OpenOverviewLanding()
    {
        SelectedSystem = null;
        SelectedModule = null;
        SelectedRelationship = null;
        ClearInspector();
        ActiveView = SystemMapViewKind.SystemOverview;
    }

    public void SelectSystem(SystemItemVm? sys)
    {
        SelectedSystem = sys;
        SelectedModule = null;
        SelectedRelationship = null;
        UpdateInspectorForSystem(sys);
    }

    public void SelectModule(ModuleItemVm? mod)
    {
        SelectedModule = mod;
        SelectedRelationship = null;
        UpdateInspectorForModule(mod);
    }

    public void SelectExternalSystem(ExternalSystemItemVm? ext)
    {
        SelectedRelationship = null;
        UpdateInspectorForExternalSystem(ext);
    }

    public void SelectCodeNode(CodeNodeItemVm? node)
    {
        SelectedRelationship = null;
        UpdateInspectorForCodeNode(node);
    }

    public void SelectRelationship(RelationshipItemVm rel)
    {
        SelectedRelationship = rel;
        UpdateInspectorForRelationship(rel);
    }

    /// <summary>
    /// Rebuilds all collections from <paramref name="model"/>.
    /// Call this whenever the workspace's <see cref="SystemMapModel"/> changes.
    /// </summary>
    public void LoadFrom(SystemMapModel model, IReadOnlyDictionary<string, LayoutPosition>? layoutPositions = null)
    {
        _currentModel = model;
        AppLogger.Debug($"[SystemMapViewModel] LoadFrom starting. Systems={model.Systems.Count}, Modules={model.AllModules.Count()}, CodeNodes={model.AllCodeNodes.Count()}, ExternalSystems={model.ExternalSystems.Count}, Relationships={model.Relationships.Count}");

        // Pre-compute the base row for library systems so they are placed after all non-library
        // rows, preventing overlap when there are more than CardsPerRow non-library systems.
        // Only systems without a saved layout position contribute to the auto-layout grid.
        int autoLayoutNonLibCount = model.Systems.Count(s =>
            s.Kind != SystemKind.LibraryOnly &&
            !string.Equals(s.StartupMechanism, "Class Library", StringComparison.OrdinalIgnoreCase) &&
            !TryGetSavedPosition(GetLayoutPositionKey(s.Id, isExternal: false), layoutPositions, out _));
        int nonLibraryRowCount = autoLayoutNonLibCount == 0 ? 0
            : (autoLayoutNonLibCount + CardsPerRow - 1) / CardsPerRow;
        int libraryBaseRow = nonLibraryRowCount == 0 ? 0 : nonLibraryRowCount + 1;

        int topLaneIndex = 0;
        int lowerLaneIndex = 0;

        _allSystems = model.Systems.Select(s =>
        {
            var position = GetSystemPosition(s, layoutPositions, ref topLaneIndex, ref lowerLaneIndex, libraryBaseRow);
            return new SystemItemVm
            {
                Id               = s.Id,
                Name             = s.Name,
                KindLabel        = s.Kind.ToString(),
                StartupMechanism = s.StartupMechanism,
                Confidence       = s.Confidence,
                ModuleCount      = CountModulesForSystem(model, s),
                LayerKind        = ClassifySystemLayer(s.Kind, s.StartupMechanism),
                ModuleKindCounts = GetModuleKindCounts(model, s),
                Description      = s.Notes,
                SourceFile       = s.EntryPointCandidates.Count > 0 ? s.EntryPointCandidates[0] : string.Empty,
                SourceLineStart  = 0,
                SourceLineEnd    = 0,
                Responsibilities = Array.Empty<string>(),
                X                = position.X,
                Y                = position.Y
            };
        }).ToList();

        int externalIndex = 0;

        _allExternalSystems = model.ExternalSystems.Select(e =>
        {
            var position = GetExternalPosition(e.Id, layoutPositions, externalIndex++);
            return new ExternalSystemItemVm
            {
                Id         = e.Id,
                Name       = e.Name,
                Kind       = e.Kind,
                Confidence = e.Confidence,
                X          = position.X,
                Y          = position.Y
            };
        }).ToList();

        _allModules = model.AllModules.Select(m =>
        {
            var ownerSystemIds = ResolveSystemIdsForModule(model, m);
            return new ModuleItemVm
            {
                Id            = m.Id,
                Name          = m.Name,
                KindLabel     = m.Kind.ToString(),
                Confidence    = m.Confidence,
                CodeNodeCount = m.CodeNodes.Count,
                SystemId      = ownerSystemIds.FirstOrDefault() ?? string.Empty,
                SystemIds     = ownerSystemIds
            };
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
                IsHighValue    = cn.IsHighValue || HighValueKinds.Contains(cn.Kind),
                IsNoisy        = cn.IsNoisy,
                HideFromOverview = cn.HideFromOverview,
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

        // Build relationship view-models for System↔System and System↔ExternalSystem pairs.
        var systemIdSet   = _allSystems.ToLookup(s => s.Id, StringComparer.Ordinal);
        var externalIdSet = _allExternalSystems.ToLookup(e => e.Id, StringComparer.Ordinal);

        _allRelationships = model.Relationships
            .Where(r => (systemIdSet.Contains(r.FromId)   || externalIdSet.Contains(r.FromId)) &&
                        (systemIdSet.Contains(r.ToId)     || externalIdSet.Contains(r.ToId)))
            .Select(r =>
            {
                string fromName = systemIdSet[r.FromId].FirstOrDefault()?.Name
                    ?? externalIdSet[r.FromId].FirstOrDefault()?.Name
                    ?? r.FromId;
                string toName   = systemIdSet[r.ToId].FirstOrDefault()?.Name
                    ?? externalIdSet[r.ToId].FirstOrDefault()?.Name
                    ?? r.ToId;
                return new RelationshipItemVm
                {
                    Id         = r.Id,
                    FromId     = r.FromId,
                    ToId       = r.ToId,
                    Kind       = r.Kind,
                    Label      = r.Kind.ToString(),
                    Confidence = r.Confidence,
                    Notes      = r.Notes,
                    FromName   = fromName,
                    ToName     = toName
                };
            }).ToList();

        // Reset selection state.
        _selectedSystem = null;
        _selectedModule = null;
        _selectedRelationship = null;
        OnPropertyChanged(nameof(SelectedSystemName));
        OnPropertyChanged(nameof(SelectedModuleName));
        OnPropertyChanged(nameof(SelectedRelationship));

        ClearInspector();
        CleanupNames(applyFiltersAfter: false); // strip common prefix before first render
        ApplyFilters();

        AppLogger.Debug($"[SystemMapViewModel] LoadFrom completed. VisibleSystems={Systems.Count}, CachedModules={_allModules.Count}, CachedCodeNodes={_allCodeNodes.Count}, VisibleExternalSystems={ExternalSystems.Count}, VisibleStartupItems={StartupItems.Count}");
    }

    /// <summary>
    /// Detects common leading token(s) shared across all card names
    /// (Systems, External Systems and Modules) and strips them from every name that
    /// carries them.  Names are first split into tokens on the separators
    /// <c>.</c>, <c>-</c>, <c>_</c> and <c> </c>, and any leading token that
    /// appears identically in every name (that still has remaining tokens after
    /// stripping) is removed.  Typical use-case: a company prefix such as
    /// <c>LTD.Customer.Simulation.App</c> → <c>Customer.Simulation.App</c>.
    /// </summary>
    /// <param name="applyFiltersAfter">
    /// When <c>true</c> (the default), <see cref="ApplyFilters"/> is called after renaming
    /// so the UI collections reflect the cleaned names immediately.  Pass <c>false</c> when
    /// called from <see cref="LoadFrom"/> to avoid a redundant extra filter pass.
    /// </param>
    public void CleanupNames(bool applyFiltersAfter = true)
    {
        AppLogger.Debug("[SystemMapViewModel.CleanupNames] Starting cleanup. " +
            $"Systems={_allSystems.Count}, ExternalSystems={_allExternalSystems.Count}, Modules={_allModules.Count}");

        try
        {
            // Gather all current display names.
            var allNames = _allSystems.Select(s => s.Name)
                .Concat(_allExternalSystems.Select(e => e.Name))
                .Concat(_allModules.Select(m => m.Name))
                .Where(n => !string.IsNullOrEmpty(n))
                .ToList();

            AppLogger.Debug($"[SystemMapViewModel.CleanupNames] Total non-empty names collected: {allNames.Count}");

            if (allNames.Count < 2)
            {
                AppLogger.Info("[SystemMapViewModel.CleanupNames] Fewer than 2 names — nothing to clean up.");
                return;
            }

            // Tokenise every name and find how many leading tokens are universal.
            var tokenRows = allNames.Select(TokenizeName).ToList();
            int prefixTokenCount = FindCommonPrefixTokenCount(tokenRows);

            if (prefixTokenCount == 0)
            {
                AppLogger.Info("[SystemMapViewModel.CleanupNames] No common prefix tokens found — nothing to strip.");
                return;
            }

            // Build a human-readable representation of the tokens being stripped.
            string prefixLabel = tokenRows.Count > 0
                ? string.Join(".", tokenRows[0].Take(prefixTokenCount))
                : string.Empty;
            AppLogger.Debug($"[SystemMapViewModel.CleanupNames] Common prefix token(s) detected: \"{prefixLabel}\" ({prefixTokenCount} token(s))");

            int renamed = 0;

            foreach (var item in _allSystems)
            {
                string trimmed = StripLeadingTokens(item.Name, prefixTokenCount);
                if (!string.IsNullOrWhiteSpace(trimmed) && trimmed != item.Name)
                {
                    AppLogger.Debug($"[SystemMapViewModel.CleanupNames] System rename: \"{item.Name}\" → \"{trimmed}\"");
                    item.Name = trimmed;
                    renamed++;
                }
            }
            foreach (var item in _allExternalSystems)
            {
                string trimmed = StripLeadingTokens(item.Name, prefixTokenCount);
                if (!string.IsNullOrWhiteSpace(trimmed) && trimmed != item.Name)
                {
                    AppLogger.Debug($"[SystemMapViewModel.CleanupNames] ExternalSystem rename: \"{item.Name}\" → \"{trimmed}\"");
                    item.Name = trimmed;
                    renamed++;
                }
            }
            foreach (var item in _allModules)
            {
                string trimmed = StripLeadingTokens(item.Name, prefixTokenCount);
                if (!string.IsNullOrWhiteSpace(trimmed) && trimmed != item.Name)
                {
                    AppLogger.Debug($"[SystemMapViewModel.CleanupNames] Module rename: \"{item.Name}\" → \"{trimmed}\"");
                    item.Name = trimmed;
                    renamed++;
                }
            }

            AppLogger.Info($"[SystemMapViewModel.CleanupNames] Done. Renamed {renamed} item(s) by stripping prefix \"{prefixLabel}\".");

            if (applyFiltersAfter)
                ApplyFilters();
        }
        catch (Exception ex)
        {
            AppLogger.Error($"[SystemMapViewModel.CleanupNames] Unexpected error: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
        }
    }

    /// <summary>
    /// Splits a name into tokens by breaking on the separators
    /// <c>.</c>, <c>-</c>, <c>_</c> and <c> </c>.
    /// </summary>
    private static string[] TokenizeName(string name) =>
        name.Split(NameSeparators, StringSplitOptions.RemoveEmptyEntries);

    /// <summary>
    /// Returns how many leading token positions are identical (case-insensitive)
    /// across all rows that have at least one further token remaining after that
    /// position (to avoid stripping a name down to nothing).  Requires at least
    /// two such eligible rows to agree before a position is counted.
    /// </summary>
    private static int FindCommonPrefixTokenCount(List<string[]> tokenRows)
    {
        if (tokenRows.Count == 0) return 0;

        int common = 0;
        while (true)
        {
            int position = common;

            // Eligible rows: have a token at 'position' AND at least one more token
            // after it, so stripping won't leave the name empty.
            var eligible = tokenRows
                .Where(r => r.Length > position + 1)
                .ToList();

            if (eligible.Count < 2) break;

            // All eligible names must share the same token at this position.
            string first = eligible[0][position];
            bool allMatch = eligible.All(r =>
                string.Equals(r[position], first, StringComparison.OrdinalIgnoreCase));

            if (allMatch)
                common++;
            else
                break;
        }

        return common;
    }

    /// <summary>
    /// Removes the first <paramref name="tokenCount"/> token(s) from the start of
    /// <paramref name="name"/>, together with the separator characters that
    /// immediately follow each token.  The original separators within the remaining
    /// portion of the name are preserved unchanged.
    /// </summary>
    private static string StripLeadingTokens(string name, int tokenCount)
    {
        if (tokenCount <= 0) return name;

        int tokensRemoved = 0;
        int i = 0;
        while (i < name.Length && tokensRemoved < tokenCount)
        {
            // Advance past the current token characters.
            while (i < name.Length && Array.IndexOf(NameSeparators, name[i]) < 0)
                i++;
            tokensRemoved++;
            // Advance past any trailing separator(s).
            while (i < name.Length && Array.IndexOf(NameSeparators, name[i]) >= 0)
                i++;
        }

        return i < name.Length ? name[i..] : string.Empty;
    }

    /// <summary>
    /// Sorts systems by kind sections in this order: DesktopApp, BackendService,
    /// LibraryOnly, then all other kinds. Each section is alphabetised and separated
    /// by one empty row. Returns the list of (id, newX, newY) updates.
    /// </summary>
    public List<(string Id, double X, double Y)> SortAll()
    {
        var desktopApps = _allSystems
            .Where(s => GetSortAllSectionRank(s) == 0)
            .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var backendServices = _allSystems
            .Where(s => GetSortAllSectionRank(s) == 1)
            .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var libraries = _allSystems
            .Where(s => GetSortAllSectionRank(s) == 2)
            .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var others = _allSystems
            .Where(s => GetSortAllSectionRank(s) == 3)
            .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var sections = new List<List<SystemItemVm>> { desktopApps, backendServices, libraries, others };
        var updates = new List<(string, double, double)>();
        int currentBaseRow = 0;

        foreach (var section in sections)
        {
            if (section.Count == 0) continue;

            for (int i = 0; i < section.Count; i++)
            {
                var pos = CreateGridPosition(i, baseRow: currentBaseRow);
                section[i].X = pos.X;
                section[i].Y = pos.Y;
                updates.Add((section[i].Id, pos.X, pos.Y));
            }

            int rowsUsed = (section.Count + CardsPerRow - 1) / CardsPerRow;
            currentBaseRow += rowsUsed + 1;
        }

        ApplyFilters();
        return updates;
    }

    internal static bool IsDesktopAppKind(SystemItemVm s) =>
        string.Equals(s.KindLabel, nameof(SystemKind.DesktopApp), StringComparison.Ordinal);

    internal static bool IsBackendServiceKind(SystemItemVm s) =>
        string.Equals(s.KindLabel, nameof(SystemKind.BackendService), StringComparison.Ordinal);

    internal static bool IsLibraryOnlyKind(SystemItemVm s) =>
        string.Equals(s.KindLabel, nameof(SystemKind.LibraryOnly), StringComparison.Ordinal);

    internal static int GetSortAllSectionRank(SystemItemVm s)
    {
        if (IsDesktopAppKind(s)) return 0;
        if (IsBackendServiceKind(s)) return 1;
        if (IsLibraryOnlyKind(s)) return 2;
        return 3;
    }

    /// <summary>
    /// Sorts all system cards alphabetically by name and reassigns their grid positions
    /// in that order.  Returns the list of (id, newX, newY) updates for persistence.
    /// </summary>
    public List<(string Id, double X, double Y)> ArrangeAlphabetically()
    {
        var sorted = _allSystems
            .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var updates = new List<(string, double, double)>();

        for (int i = 0; i < sorted.Count; i++)
        {
            var pos = CreateGridPosition(i, baseRow: 0);
            sorted[i].X = pos.X;
            sorted[i].Y = pos.Y;
            updates.Add((sorted[i].Id, sorted[i].X, sorted[i].Y));
        }

        ApplyFilters();
        return updates;
    }

    /// <summary>
    /// Sorts system cards into four architectural layer rows
    /// (Presentation → Application → Domain → Infrastructure), each layer occupying
    /// its own row group separated by a blank row.
    /// Returns (position updates, whether two or more distinct layers are present).
    /// </summary>
    public (List<(string Id, double X, double Y)> Updates, bool HasMultipleLayers) SortByLayers()
    {
        var layerOrder = new[]
        {
            ArchitectureLayerKind.Presentation,
            ArchitectureLayerKind.Application,
            ArchitectureLayerKind.Domain,
            ArchitectureLayerKind.Infrastructure
        };

        var updates = new List<(string Id, double X, double Y)>();
        int currentBaseRow = 0;
        int nonEmptyLayerCount = 0;

        foreach (var layer in layerOrder)
        {
            var systemsInLayer = _allSystems
                .Where(s => s.LayerKind == layer)
                .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (systemsInLayer.Count == 0) continue;
            nonEmptyLayerCount++;

            for (int i = 0; i < systemsInLayer.Count; i++)
            {
                var pos = CreateGridPosition(i, baseRow: currentBaseRow);
                systemsInLayer[i].X = pos.X;
                systemsInLayer[i].Y = pos.Y;
                updates.Add((systemsInLayer[i].Id, pos.X, pos.Y));
            }

            // Advance past this layer's rows and leave one blank row as a visual gap.
            int rowsUsed = (systemsInLayer.Count + CardsPerRow - 1) / CardsPerRow;
            currentBaseRow += rowsUsed + 1;
        }

        ApplyFilters();
        return (updates, nonEmptyLayerCount > 1);
    }

    public void SetOverviewPosition(string itemId, double x, double y, bool isExternal)
    {
        if (isExternal)
        {
            var item = _allExternalSystems.FirstOrDefault(ext => string.Equals(ext.Id, itemId, StringComparison.Ordinal));
            if (item == null) return;
            item.X = x;
            item.Y = y;
            return;
        }

        var system = _allSystems.FirstOrDefault(sys => string.Equals(sys.Id, itemId, StringComparison.Ordinal));
        if (system == null) return;
        system.X = x;
        system.Y = y;
    }

    public static string GetLayoutPositionKey(string itemId, bool isExternal)
        => $"{LayoutPrefix}{(isExternal ? "external" : "system")}:{itemId}";

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

        // Relationships are visible when both endpoints are in the currently visible sets.
        // Depends-kind relationships are treated as "startup edges" and hidden unless
        // the ShowStartupRelationships filter is active (avoids cluttering the canvas by default).
        var visibleSystemIds   = Systems.Select(s => s.Id).ToHashSet(StringComparer.Ordinal);
        var visibleExternalIds = ExternalSystems.Select(e => e.Id).ToHashSet(StringComparer.Ordinal);

        SyncCollection(VisibleRelationships,
            _allRelationships
                .Where(r =>
                    (visibleSystemIds.Contains(r.FromId) || visibleExternalIds.Contains(r.FromId)) &&
                    (visibleSystemIds.Contains(r.ToId)   || visibleExternalIds.Contains(r.ToId))   &&
                    (ShowStartupRelationships || r.Kind != RelationshipKind.Depends))
                .ToList());

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
        var selectedModules = _allModules
            .Where(m => _selectedSystem == null
                        || m.SystemIds.Contains(_selectedSystem.Id)
                        || m.SystemId == _selectedSystem.Id)
            .Where(m => lowConf || IsHighConfidence(m.Confidence))
            .ToList();

        var moduleRelationshipData = BuildModuleRelationshipData(selectedModules, lowConf);

        SyncCollection(ModulesForSelectedSystem,
            selectedModules
                .Select(m =>
                {
                    moduleRelationshipData.SummariesByModuleId.TryGetValue(m.Id, out var summary);
                    return new ModuleItemVm
                    {
                        Id = m.Id,
                        Name = m.Name,
                        KindLabel = m.KindLabel,
                        Confidence = m.Confidence,
                        CodeNodeCount = m.CodeNodeCount,
                        SystemId = m.SystemId,
                        SystemIds = new HashSet<string>(m.SystemIds, StringComparer.Ordinal),
                        OutboundRelationshipCount = summary?.OutboundCount ?? 0,
                        InboundRelationshipCount = summary?.InboundCount ?? 0,
                        OutboundHighlights = summary?.OutboundHighlights ?? Array.Empty<string>(),
                        InboundHighlights = summary?.InboundHighlights ?? Array.Empty<string>()
                    };
                })
                .ToList());

        SyncCollection(
            ModuleRelationshipsForSelectedSystem,
            moduleRelationshipData.Relationships);
    }

    private ModuleRelationshipData BuildModuleRelationshipData(
        IReadOnlyList<ModuleItemVm> selectedModules,
        bool includeLowConfidence)
    {
        var summaries = selectedModules.ToDictionary(
            m => m.Id,
            _ => new ModuleRelationshipSummary(),
            StringComparer.Ordinal);
        var relationships = new List<RelationshipItemVm>();

        if (_currentModel == null || selectedModules.Count == 0)
        {
            return new ModuleRelationshipData(summaries, relationships);
        }

        var selectedModuleIds = selectedModules
            .Select(m => m.Id)
            .ToHashSet(StringComparer.Ordinal);
        var moduleNamesById = selectedModules.ToDictionary(m => m.Id, m => m.Name, StringComparer.Ordinal);
        var codeNodeToModule = _currentModel.AllModules
            .SelectMany(m => m.CodeNodes.Select(cn => (CodeNodeId: cn.Id, ModuleId: m.Id)))
            .ToDictionary(x => x.CodeNodeId, x => x.ModuleId, StringComparer.Ordinal);

        string? ResolveModuleId(string entityId)
        {
            if (selectedModuleIds.Contains(entityId))
                return entityId;

            if (codeNodeToModule.TryGetValue(entityId, out var ownerModuleId) &&
                selectedModuleIds.Contains(ownerModuleId))
            {
                return ownerModuleId;
            }

            return null;
        }

        var grouped = new Dictionary<(string FromId, string ToId), Dictionary<RelationshipKind, int>>();

        foreach (var relationship in _currentModel.Relationships)
        {
            if (!includeLowConfidence && !IsHighConfidence(relationship.Confidence))
                continue;

            var fromModuleId = ResolveModuleId(relationship.FromId);
            var toModuleId = ResolveModuleId(relationship.ToId);
            if (fromModuleId == null || toModuleId == null)
                continue;
            if (string.Equals(fromModuleId, toModuleId, StringComparison.Ordinal))
                continue;

            var key = (fromModuleId, toModuleId);
            if (!grouped.TryGetValue(key, out var kindCounts))
            {
                kindCounts = new Dictionary<RelationshipKind, int>();
                grouped[key] = kindCounts;
            }

            kindCounts.TryGetValue(relationship.Kind, out var count);
            kindCounts[relationship.Kind] = count + 1;
        }

        foreach (var pair in grouped)
        {
            var fromId = pair.Key.FromId;
            var toId = pair.Key.ToId;
            var label = FormatRelationshipKinds(pair.Value);
            var counterpartName = moduleNamesById.TryGetValue(toId, out var outboundName) ? outboundName : toId;
            var sourceName = moduleNamesById.TryGetValue(fromId, out var inboundName) ? inboundName : fromId;
            var totalCount = pair.Value.Values.Sum();

            summaries[fromId].OutboundCount++;
            summaries[fromId].OutboundEntries.Add(new ModuleRelationshipEntry(
                $"{label} -> {counterpartName}",
                totalCount));

            summaries[toId].InboundCount++;
            summaries[toId].InboundEntries.Add(new ModuleRelationshipEntry(
                $"{label} <- {sourceName}",
                totalCount));

            relationships.Add(new RelationshipItemVm
            {
                Id = $"module:{fromId}:{toId}:{label}",
                FromId = fromId,
                ToId = toId,
                Kind = pair.Value
                    .OrderByDescending(x => x.Value)
                    .ThenBy(x => x.Key.ToString(), StringComparer.Ordinal)
                    .First().Key,
                Label = label,
                Confidence = ConfidenceLevel.Likely,
                Notes = $"{sourceName} -> {counterpartName}",
                FromName = sourceName,
                ToName = counterpartName
            });
        }

        foreach (var summary in summaries.Values)
        {
            summary.OutboundHighlights = summary.OutboundEntries
                .OrderByDescending(x => x.Weight)
                .ThenBy(x => x.Label, StringComparer.Ordinal)
                .Take(2)
                .Select(x => x.Label)
                .ToList();
            summary.InboundHighlights = summary.InboundEntries
                .OrderByDescending(x => x.Weight)
                .ThenBy(x => x.Label, StringComparer.Ordinal)
                .Take(2)
                .Select(x => x.Label)
                .ToList();
        }

        return new ModuleRelationshipData(summaries, relationships);
    }

    private static string FormatRelationshipKinds(Dictionary<RelationshipKind, int> kindCounts)
        => string.Join(", ", kindCounts
            .OrderByDescending(x => x.Value)
            .ThenBy(x => x.Key.ToString(), StringComparer.Ordinal)
            .Select(x => x.Value > 1 ? $"{x.Key}x{x.Value}" : x.Key.ToString()));

    private sealed class ModuleRelationshipSummary
    {
        public int OutboundCount { get; set; }
        public int InboundCount { get; set; }
        public List<ModuleRelationshipEntry> OutboundEntries { get; } = new();
        public List<ModuleRelationshipEntry> InboundEntries { get; } = new();
        public IReadOnlyList<string> OutboundHighlights { get; set; } = Array.Empty<string>();
        public IReadOnlyList<string> InboundHighlights { get; set; } = Array.Empty<string>();
    }

    private sealed record ModuleRelationshipData(
        Dictionary<string, ModuleRelationshipSummary> SummariesByModuleId,
        List<RelationshipItemVm> Relationships);

    private sealed record ModuleRelationshipEntry(string Label, int Weight);

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
                .Where(m => m.SystemIds.Contains(_selectedSystem.Id)
                            || m.SystemId == _selectedSystem.Id)
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

    private static LayoutPosition GetSystemPosition(
        SystemModel system,
        IReadOnlyDictionary<string, LayoutPosition>? layoutPositions,
        ref int topLaneIndex,
        ref int lowerLaneIndex,
        int libraryBaseRow)
    {
        if (TryGetSavedPosition(GetLayoutPositionKey(system.Id, isExternal: false), layoutPositions, out var saved))
            return saved;

        bool lowerLane = system.Kind == SystemKind.LibraryOnly ||
            string.Equals(system.StartupMechanism, "Class Library", StringComparison.OrdinalIgnoreCase);

        return CreateGridPosition(lowerLane ? lowerLaneIndex++ : topLaneIndex++, lowerLane ? libraryBaseRow : 0);
    }

    private static LayoutPosition GetExternalPosition(
        string externalSystemId,
        IReadOnlyDictionary<string, LayoutPosition>? layoutPositions,
        int index)
    {
        if (TryGetSavedPosition(GetLayoutPositionKey(externalSystemId, isExternal: true), layoutPositions, out var saved))
        {
            // A position saved before the unified-canvas migration would have a small Y value
            // (it was relative to the now-removed ExternalSystemsCanvas, which started at row 0).
            // Use the original pre-migration baseline as the threshold so that positions saved
            // with any later CardGapY value are not incorrectly shifted.
            // Value = ExternalBaseRow (4) × original CardGapY (148) = 592.
            const double migrationBaseline = 592.0;
            if (saved.Y < migrationBaseline)
                return new LayoutPosition { X = saved.X, Y = saved.Y + (ExternalBaseRow * CardGapY) };
            return saved;
        }

        return CreateGridPosition(index, baseRow: ExternalBaseRow);
    }

    private static bool TryGetSavedPosition(
        string key,
        IReadOnlyDictionary<string, LayoutPosition>? layoutPositions,
        out LayoutPosition position)
    {
        if (layoutPositions != null && layoutPositions.TryGetValue(key, out position!))
            return true;

        position = new LayoutPosition();
        return false;
    }

    private static LayoutPosition CreateGridPosition(int index, int baseRow)
    {
        int row = index / CardsPerRow;
        int column = index % CardsPerRow;
        return new LayoutPosition
        {
            X = CardStartX + (column * CardGapX),
            Y = CardStartY + ((baseRow + row) * CardGapY)
        };
    }

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
        InspectorName               = "Nothing selected";
        InspectorType               = string.Empty;
        InspectorKind               = string.Empty;
        InspectorNotes              = string.Empty;
        InspectorConfidence         = string.Empty;
        InspectorDetails            = new List<string>();
        InspectorIsSystemSelected   = false;
        InspectorLayerLabel         = string.Empty;
        InspectorLayerColor         = "#4A6A8A";
        InspectorDescription        = string.Empty;
        InspectorSourceFile         = string.Empty;
        InspectorSourceLineRange    = string.Empty;
        InspectorResponsibilities   = new List<string>();
        InspectorInboundConnections  = new List<RelationshipItemVm>();
        InspectorOutboundConnections = new List<RelationshipItemVm>();
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

        // Component-tab extended fields
        InspectorIsSystemSelected   = true;
        InspectorLayerLabel         = $"{sys.LayerKind} Layer";
        InspectorLayerColor         = LayerAccentColorHex(sys.LayerKind);
        InspectorDescription        = sys.Description;
        InspectorSourceFile         = sys.SourceFile;
        InspectorSourceLineRange    = sys.SourceLineStart > 0 && sys.SourceLineEnd > 0
                                          ? $"Lines: {sys.SourceLineStart}–{sys.SourceLineEnd}"
                                          : string.Empty;
        InspectorResponsibilities   = sys.Responsibilities.ToList();

        // Build connection lists from the currently visible relationships.
        InspectorInboundConnections  = VisibleRelationships
            .Where(r => string.Equals(r.ToId,   sys.Id, StringComparison.Ordinal))
            .ToList();
        InspectorOutboundConnections = VisibleRelationships
            .Where(r => string.Equals(r.FromId, sys.Id, StringComparison.Ordinal))
            .ToList();
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

        InspectorIsSystemSelected   = false;
        InspectorLayerLabel         = string.Empty;
        InspectorLayerColor         = "#4A6A8A";
        InspectorDescription        = string.Empty;
        InspectorSourceFile         = string.Empty;
        InspectorSourceLineRange    = string.Empty;
        InspectorResponsibilities   = new List<string>();
        InspectorInboundConnections  = new List<RelationshipItemVm>();
        InspectorOutboundConnections = new List<RelationshipItemVm>();
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

        InspectorIsSystemSelected   = false;
        InspectorLayerLabel         = string.Empty;
        InspectorLayerColor         = "#4A6A8A";
        InspectorDescription        = string.Empty;
        InspectorSourceFile         = string.Empty;
        InspectorSourceLineRange    = string.Empty;
        InspectorResponsibilities   = new List<string>();
        // External systems can also participate in relationships.
        InspectorInboundConnections  = VisibleRelationships
            .Where(r => string.Equals(r.ToId,   ext.Id, StringComparison.Ordinal))
            .ToList();
        InspectorOutboundConnections = VisibleRelationships
            .Where(r => string.Equals(r.FromId, ext.Id, StringComparison.Ordinal))
            .ToList();
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

        InspectorIsSystemSelected   = false;
        InspectorLayerLabel         = string.Empty;
        InspectorLayerColor         = "#4A6A8A";
        InspectorDescription        = string.Empty;
        InspectorSourceFile         = node.FilePath;
        InspectorSourceLineRange    = string.Empty;
        InspectorResponsibilities   = new List<string>();
        InspectorInboundConnections  = new List<RelationshipItemVm>();
        InspectorOutboundConnections = new List<RelationshipItemVm>();
    }

    private void UpdateInspectorForRelationship(RelationshipItemVm rel)
    {
        InspectorName       = $"{rel.FromName} → {rel.ToName}";
        InspectorType       = "Relationship";
        InspectorKind       = rel.Kind.ToString();
        InspectorNotes      = rel.Notes;
        InspectorConfidence = rel.Confidence.ToString();
        InspectorDetails    = new List<string>
        {
            $"From: {rel.FromName}",
            $"To: {rel.ToName}"
        };

        InspectorIsSystemSelected   = false;
        InspectorLayerLabel         = string.Empty;
        InspectorLayerColor         = "#4A6A8A";
        InspectorDescription        = string.Empty;
        InspectorSourceFile         = string.Empty;
        InspectorSourceLineRange    = string.Empty;
        InspectorResponsibilities   = new List<string>();
        InspectorInboundConnections  = new List<RelationshipItemVm>();
        InspectorOutboundConnections = new List<RelationshipItemVm>();
    }

    /// <summary>Returns the vision accent colour hex for a given architectural layer.</summary>
    private static string LayerAccentColorHex(ArchitectureLayerKind layer) => layer switch
    {
        ArchitectureLayerKind.Presentation   => "#9B59B6",
        ArchitectureLayerKind.Application    => "#27AE60",
        ArchitectureLayerKind.Domain         => "#F39C12",
        ArchitectureLayerKind.Infrastructure => "#2980B9",
        _                                    => "#4A6A8A"
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

    private static HashSet<string> ResolveSystemIdsForModule(SystemMapModel map, ModuleModel module)
    {
        var systemIds = new HashSet<string>(module.SystemIds, StringComparer.Ordinal);

        foreach (var system in map.Systems)
        {
            if (system.Modules.Any(m => string.Equals(m.Id, module.Id, StringComparison.Ordinal)))
                systemIds.Add(system.Id);
        }

        return systemIds;
    }

    /// <summary>Maps a system's <see cref="SystemKind"/> to its architectural layer tier.</summary>
    private static ArchitectureLayerKind ClassifySystemLayer(SystemKind kind, string startupMechanism)
    {
        if (string.Equals(startupMechanism, "Class Library", StringComparison.OrdinalIgnoreCase))
            return ArchitectureLayerKind.Infrastructure;

        return kind switch
        {
            SystemKind.DesktopApp or SystemKind.WebApp or SystemKind.CliTool
                => ArchitectureLayerKind.Presentation,
            SystemKind.BackendService or SystemKind.WorkerService or SystemKind.ScheduledJob
                => ArchitectureLayerKind.Application,
            SystemKind.DatabaseProcess or SystemKind.LibraryOnly
                => ArchitectureLayerKind.Infrastructure,
            _ => ArchitectureLayerKind.Domain
        };
    }

    /// <summary>
    /// Returns the top module kinds for a system in descending order of count.
    /// At most four distinct kinds are returned.
    /// </summary>
    private static IReadOnlyList<(string Kind, int Count)> GetModuleKindCounts(
        SystemMapModel map, SystemModel system)
    {
        var relevant = map.AllModules.Where(m =>
            system.Modules.Any(sm => string.Equals(sm.Id, m.Id, StringComparison.Ordinal)) ||
            m.SystemIds.Any(sid => string.Equals(sid, system.Id, StringComparison.Ordinal)));

        return relevant
            .GroupBy(m => m.Kind.ToString())
            .OrderByDescending(g => g.Count())
            .Take(4)
            .Select(g => (g.Key, g.Count()))
            .ToList();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
