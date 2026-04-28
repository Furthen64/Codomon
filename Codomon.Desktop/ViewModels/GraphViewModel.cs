using System.Collections.ObjectModel;
using Codomon.Desktop.Services.Graph;

namespace Codomon.Desktop.ViewModels;

public class GraphViewModel
{
    public ObservableCollection<NodeViewModel> Nodes { get; } = new();
    public ObservableCollection<ConnectionViewModel> Connections { get; } = new();

    public GraphViewModel()
    {
        var graph   = FakeCodomonGraphFactory.Create();
        var adapted = CodomonGraphAdapter.ToViewModel(graph);

        foreach (var node in adapted.Nodes)
            Nodes.Add(node);

        foreach (var connection in adapted.Connections)
            Connections.Add(connection);
    }
}
