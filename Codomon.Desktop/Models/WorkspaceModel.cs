namespace Codomon.Desktop.Models;

public class WorkspaceModel
{
    public string WorkspaceName { get; set; } = string.Empty;
    public string SourceProjectPath { get; set; } = string.Empty;
    public List<SystemBoxModel> Systems { get; set; } = new();
    public List<ConnectionModel> Connections { get; set; } = new();
    public ProfileModel ActiveProfile { get; set; } = new();

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
