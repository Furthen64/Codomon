using Avalonia;

namespace Codomon.Desktop.Models;

public static class DemoData
{
    public static List<SystemBoxModel> Systems { get; } = new();
    public static List<ConnectionModel> Connections { get; } = new();

    static DemoData()
    {
        var sysA = new SystemBoxModel
        {
            Id = "sysA",
            Name = "System A",
            Bounds = new Rect(40, 40, 220, 200),
            Modules = new List<ModuleBoxModel>
            {
                new() { Id = "modA1", Name = "Module A1", ParentSystemId = "sysA", Bounds = new Rect(60, 80, 80, 40) },
                new() { Id = "modA2", Name = "Module A2", ParentSystemId = "sysA", Bounds = new Rect(60, 140, 80, 40) },
            }
        };

        var sysB = new SystemBoxModel
        {
            Id = "sysB",
            Name = "System B",
            Bounds = new Rect(320, 40, 220, 200),
            Modules = new List<ModuleBoxModel>
            {
                new() { Id = "modB1", Name = "Module B1", ParentSystemId = "sysB", Bounds = new Rect(340, 80, 80, 40) },
                new() { Id = "modB2", Name = "Module B2", ParentSystemId = "sysB", Bounds = new Rect(340, 140, 80, 40) },
            }
        };

        var sysC = new SystemBoxModel
        {
            Id = "sysC",
            Name = "System C",
            Bounds = new Rect(180, 300, 220, 160),
            Modules = new List<ModuleBoxModel>
            {
                new() { Id = "modC1", Name = "Module C1", ParentSystemId = "sysC", Bounds = new Rect(200, 340, 80, 40) },
            }
        };

        Systems.Add(sysA);
        Systems.Add(sysB);
        Systems.Add(sysC);

        Connections.Add(new ConnectionModel { FromId = "sysA", ToId = "sysB", Label = "calls" });
        Connections.Add(new ConnectionModel { FromId = "sysB", ToId = "sysC", Label = "feeds" });
    }
}
