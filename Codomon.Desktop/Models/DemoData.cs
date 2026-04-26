namespace Codomon.Desktop.Models;

public static class DemoData
{
    public static WorkspaceModel Workspace { get; } = CreateWorkspace();

    private static WorkspaceModel CreateWorkspace()
    {
        var sysA = new SystemBoxModel { Id = "sysA", Name = "System A", X = 40, Y = 40, Width = 220, Height = 200 };
        sysA.Modules.Add(new ModuleBoxModel { Id = "modA1", SystemId = "sysA", Name = "Module A1", RelativeX = 20, RelativeY = 40, Width = 80, Height = 40 });
        sysA.Modules.Add(new ModuleBoxModel { Id = "modA2", SystemId = "sysA", Name = "Module A2", RelativeX = 20, RelativeY = 100, Width = 80, Height = 40 });

        var sysB = new SystemBoxModel { Id = "sysB", Name = "System B", X = 320, Y = 40, Width = 220, Height = 200 };
        sysB.Modules.Add(new ModuleBoxModel { Id = "modB1", SystemId = "sysB", Name = "Module B1", RelativeX = 20, RelativeY = 40, Width = 80, Height = 40 });
        sysB.Modules.Add(new ModuleBoxModel { Id = "modB2", SystemId = "sysB", Name = "Module B2", RelativeX = 20, RelativeY = 100, Width = 80, Height = 40 });

        var sysC = new SystemBoxModel { Id = "sysC", Name = "System C", X = 180, Y = 300, Width = 220, Height = 160 };
        sysC.Modules.Add(new ModuleBoxModel { Id = "modC1", SystemId = "sysC", Name = "Module C1", RelativeX = 20, RelativeY = 40, Width = 80, Height = 40 });

        var defaultProfile = new ProfileModel { Id = "default", ProfileName = "Default" };

        var workspace = new WorkspaceModel
        {
            WorkspaceName = "Demo Workspace",
            SourceProjectPath = string.Empty,
            ActiveProfileId = "default"
        };

        workspace.Profiles.Add(defaultProfile);
        workspace.Systems.Add(sysA);
        workspace.Systems.Add(sysB);
        workspace.Systems.Add(sysC);

        workspace.Connections.Add(new ConnectionModel { Id = "conn1", Name = "calls", FromId = "sysA", ToId = "sysB", Origin = ConnectionOrigin.Manual });
        workspace.Connections.Add(new ConnectionModel { Id = "conn2", Name = "feeds", FromId = "sysB", ToId = "sysC", Origin = ConnectionOrigin.Manual });

        return workspace;
    }
}
