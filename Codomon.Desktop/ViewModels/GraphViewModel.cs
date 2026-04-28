using Avalonia;
using System.Collections.ObjectModel;

namespace Codomon.Desktop.ViewModels;

public class GraphViewModel
{
    public ObservableCollection<NodeViewModel> Nodes { get; } = new();
    public ObservableCollection<ConnectionViewModel> Connections { get; } = new();

    public GraphViewModel()
    {
        // Test nodes
        var app                  = new NodeViewModel { Title = "App",                  Location = new Point(100, 200) };
        var mainWindowViewModel  = new NodeViewModel { Title = "MainWindowViewModel",  Location = new Point(340, 200) };
        var roslynScanner        = new NodeViewModel { Title = "RoslynScanner",        Location = new Point(580, 200) };
        var workspaceService     = new NodeViewModel { Title = "WorkspaceService",     Location = new Point(820, 200) };

        Nodes.Add(app);
        Nodes.Add(mainWindowViewModel);
        Nodes.Add(roslynScanner);
        Nodes.Add(workspaceService);

        // Test connections: use the node locations as anchor points.
        // Source/Target Point values represent positions on the canvas.
        Connections.Add(new ConnectionViewModel { Source = app.Location,                 Target = mainWindowViewModel.Location });
        Connections.Add(new ConnectionViewModel { Source = mainWindowViewModel.Location, Target = roslynScanner.Location       });
        Connections.Add(new ConnectionViewModel { Source = roslynScanner.Location,       Target = workspaceService.Location    });
    }
}
