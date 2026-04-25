using Codomon.Desktop.Models;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Codomon.Desktop.ViewModels;

public class MainViewModel : INotifyPropertyChanged
{
    public WorkspaceModel Workspace { get; } = DemoData.Workspace;
    public SelectionStateModel Selection { get; } = new SelectionStateModel();

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
