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
        //
        // IsConnected must be true on each connector so that Nodify's UpdateAnchorOptimized
        // recalculates the anchor when the parent node is moved (it is a no-op when false).
        Connect(app.OutputConnector,                 mainWindowViewModel.InputConnector);
        Connect(mainWindowViewModel.OutputConnector, roslynScanner.InputConnector);
        Connect(roslynScanner.OutputConnector,       workspaceService.InputConnector);
    }

    private void Connect(ConnectorViewModel source, ConnectorViewModel target)
    {
        source.IsConnected = true;
        target.IsConnected = true;
        Connections.Add(new ConnectionViewModel(source, target));
    }
}
