using Avalonia;
using System.Collections.ObjectModel;

namespace Codomon.Desktop.ViewModels;

public class GraphViewModel
{
    private const double NodeY             = 200;
    private const double NodeStartX        = 100;
    private const double NodeHorizontalGap = 300;

    public ObservableCollection<NodeViewModel> Nodes { get; } = new();
    public ObservableCollection<ConnectionViewModel> Connections { get; } = new();

    public GraphViewModel()
    {
        // Test nodes — horizontal layout: App(100,200) → MainWindowVM(400,200) → RoslynScanner(700,200) → WorkspaceService(1000,200)
        var app                 = new NodeViewModel { Title = "App",                 Location = new Point(NodeStartX,                             NodeY) };
        var mainWindowViewModel = new NodeViewModel { Title = "MainWindowViewModel", Location = new Point(NodeStartX + NodeHorizontalGap,         NodeY) };
        var roslynScanner       = new NodeViewModel { Title = "RoslynScanner",       Location = new Point(NodeStartX + NodeHorizontalGap * 2,     NodeY) };
        var workspaceService    = new NodeViewModel { Title = "WorkspaceService",    Location = new Point(NodeStartX + NodeHorizontalGap * 3,     NodeY) };

        Nodes.Add(app);
        Nodes.Add(mainWindowViewModel);
        Nodes.Add(roslynScanner);
        Nodes.Add(workspaceService);

        // Connections from each node's output connector to the next node's input connector.
        // ConnectorViewModel.Anchor is updated by the NodeOutput/NodeInput Connector controls
        // in the view (via OneWayToSource binding), then ConnectionViewModel reacts to those
        // anchor changes so the bezier curves track nodes as they are dragged.
        Connections.Add(new ConnectionViewModel(app.OutputConnector,                 mainWindowViewModel.InputConnector));
        Connections.Add(new ConnectionViewModel(mainWindowViewModel.OutputConnector, roslynScanner.InputConnector));
        Connections.Add(new ConnectionViewModel(roslynScanner.OutputConnector,       workspaceService.InputConnector));
    }
}
