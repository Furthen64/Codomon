using Codomon.Desktop.Models.Graph;

namespace Codomon.Desktop.Services.Graph;

public static class FakeCodomonGraphFactory
{
    public static CodomonGraphModel Create()
    {
        var model = new CodomonGraphModel();

        model.Nodes.Add(new CodomonGraphNode { Id = "app",                 Title = "App",                 Kind = "Application", X = 100,  Y = 200 });
        model.Nodes.Add(new CodomonGraphNode { Id = "mainWindowViewModel", Title = "MainWindowViewModel", Kind = "ViewModel",   X = 400,  Y = 200 });
        model.Nodes.Add(new CodomonGraphNode { Id = "roslynScanner",       Title = "RoslynScanner",       Kind = "Service",     X = 700,  Y = 200 });
        model.Nodes.Add(new CodomonGraphNode { Id = "workspaceService",    Title = "WorkspaceService",    Kind = "Service",     X = 1000, Y = 200 });

        model.Edges.Add(new CodomonGraphEdge { Id = "e1", FromNodeId = "app",                 ToNodeId = "mainWindowViewModel" });
        model.Edges.Add(new CodomonGraphEdge { Id = "e2", FromNodeId = "mainWindowViewModel", ToNodeId = "roslynScanner" });
        model.Edges.Add(new CodomonGraphEdge { Id = "e3", FromNodeId = "roslynScanner",       ToNodeId = "workspaceService" });

        return model;
    }
}
