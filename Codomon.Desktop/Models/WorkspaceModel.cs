using System.Collections.ObjectModel;
using Codomon.Desktop.Models.SystemMap;

namespace Codomon.Desktop.Models;

public class WorkspaceModel
{
    public string WorkspaceName { get; set; } = string.Empty;
    public string SourceProjectPath { get; set; } = string.Empty;
    public List<SystemBoxModel> Systems { get; set; } = new();
    public List<ConnectionModel> Connections { get; set; } = new();
    public ObservableCollection<ProfileModel> Profiles { get; set; } = new();
    public string ActiveProfileId { get; set; } = string.Empty;

    /// <summary>Workspace-level rules that map log data to Systems and Modules.</summary>
    public List<MappingRuleModel> MappingRules { get; set; } = new();

    /// <summary>
    /// The last folder path the user browsed to when picking a log file.
    /// Remembered across sessions so the picker opens in a familiar location.
    /// </summary>
    public string LastBrowsedFolder { get; set; } = string.Empty;

    /// <summary>Log file paths configured for live monitoring in this workspace.</summary>
    public List<string> WatchedLogPaths { get; set; } = new();

    /// <summary>LLM API configuration for summary generation.</summary>
    public LlmSettingsModel LlmSettings { get; set; } = new();

    /// <summary>
    /// Codomon's interpreted model of the whole codebase hierarchy: Systems, Modules,
    /// Code Nodes, External Systems, Relationships, and Manual Overrides.
    /// </summary>
    public SystemMapModel SystemMap { get; set; } = new();

    /// <summary>The currently active profile, or null if no profiles exist.</summary>
    public ProfileModel? ActiveProfile =>
        Profiles.FirstOrDefault(p => p.Id == ActiveProfileId) ?? Profiles.FirstOrDefault();

    /// <summary>Flat enumeration of all modules across all systems.</summary>
    public IEnumerable<ModuleBoxModel> Modules => Systems.SelectMany(s => s.Modules);

    public void ClearSelection()
    {
        foreach (var sys in Systems)
        {
            sys.IsSelected = false;
            foreach (var mod in sys.Modules)
                mod.IsSelected = false;
        }
    }
}
