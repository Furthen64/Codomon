using Codomon.Desktop.Models;
using Codomon.Desktop.Persistence;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Codomon.Desktop.ViewModels;

public class MainViewModel : INotifyPropertyChanged
{
    private WorkspaceModel _workspace = DemoData.Workspace;
    private string _workspaceFolderPath = string.Empty;
    private string _statusMessage = "Demo workspace loaded.";

    public WorkspaceModel Workspace
    {
        get => _workspace;
        private set { _workspace = value; OnPropertyChanged(); }
    }

    public string WorkspaceFolderPath
    {
        get => _workspaceFolderPath;
        private set { _workspaceFolderPath = value; OnPropertyChanged(); }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set { _statusMessage = value; OnPropertyChanged(); }
    }

    public SelectionStateModel Selection { get; } = new SelectionStateModel();

    public async Task NewWorkspaceAsync(
        string folderPath,
        string workspaceName,
        string sourceProjectPath = "",
        string profileName = "Default",
        IEnumerable<string>? initialSystemNames = null)
    {
        var workspace = await WorkspaceSerializer.CreateNewAsync(
            folderPath, workspaceName, sourceProjectPath, profileName, initialSystemNames);
        Workspace = workspace;
        WorkspaceFolderPath = folderPath;
        ClearSelection();
        StatusMessage = $"New workspace created: {folderPath}";
    }

    public async Task OpenWorkspaceAsync(string folderPath)
    {
        var workspace = await WorkspaceSerializer.LoadAsync(folderPath);
        Workspace = workspace;
        WorkspaceFolderPath = folderPath;
        ClearSelection();
        StatusMessage = $"Opened: {folderPath}";
    }

    public async Task SaveWorkspaceAsync()
    {
        if (string.IsNullOrEmpty(WorkspaceFolderPath))
            throw new InvalidOperationException("No workspace folder is set. Use Save As.");

        await WorkspaceSerializer.SaveAsync(Workspace, WorkspaceFolderPath);
        StatusMessage = $"Saved: {WorkspaceFolderPath}";
    }

    public async Task SaveWorkspaceAsAsync(string folderPath)
    {
        await WorkspaceSerializer.SaveAsync(Workspace, folderPath);
        WorkspaceFolderPath = folderPath;
        StatusMessage = $"Saved: {folderPath}";
    }

    private void ClearSelection()
    {
        Selection.SelectedId = string.Empty;
        Selection.SelectedType = string.Empty;
        Selection.SelectedName = string.Empty;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
