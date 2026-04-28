using Avalonia;
using System.Collections.ObjectModel;

namespace Codomon.Desktop.ViewModels;

public class GraphViewModel
{
    private const double NodeY             = 200;
    private const double NodeStartX        = 100;
    private const double NodeHorizontalGap = 240;

    public ObservableCollection<NodeViewModel> Nodes { get; } = new();
    public ObservableCollection<ConnectionViewModel> Connections { get; } = new();

    public GraphViewModel()
    {
        // Test nodes
        var app                 = new NodeViewModel { Title = "App",                 Location = new Point(NodeStartX,                             NodeY) };
        var mainWindowViewModel = new NodeViewModel { Title = "MainWindowViewModel", Location = new Point(NodeStartX + NodeHorizontalGap,         NodeY) };
        var roslynScanner       = new NodeViewModel { Title = "RoslynScanner",       Location = new Point(NodeStartX + NodeHorizontalGap * 2,     NodeY) };
        var workspaceService    = new NodeViewModel { Title = "WorkspaceService",    Location = new Point(NodeStartX + NodeHorizontalGap * 3,     NodeY) };

        Nodes.Add(app);
        Nodes.Add(mainWindowViewModel);
        Nodes.Add(roslynScanner);
        Nodes.Add(workspaceService);

        // Test connections: each ConnectionViewModel watches its nodes for location changes
        // so that connection endpoints update automatically when nodes are dragged.
        Connections.Add(new ConnectionViewModel(app,                 mainWindowViewModel));
        Connections.Add(new ConnectionViewModel(mainWindowViewModel, roslynScanner));
        Connections.Add(new ConnectionViewModel(roslynScanner,       workspaceService));
    }
}
