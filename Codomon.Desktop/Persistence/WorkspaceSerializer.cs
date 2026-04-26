using System.IO;
using System.Text.Json;
using Codomon.Desktop.Models;
using Codomon.Desktop.Persistence.Dto;

namespace Codomon.Desktop.Persistence;

public static class WorkspaceSerializer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private const string WorkspaceFile = "workspace.json";
    private const string SystemsFile = "systems.json";
    private const string ModulesFile = "modules.json";
    private const string ConnectionsFile = "connections.json";
    private const string ProfilesFolder = "profiles";
    private const string DefaultProfileFile = "default.json";
    private const string VersionFile = ".wsversion";

    private static readonly string[] RequiredFiles =
    {
        WorkspaceFile, SystemsFile, ModulesFile, ConnectionsFile
    };

    public static async Task SaveAsync(WorkspaceModel workspace, string folderPath)
    {
        Directory.CreateDirectory(folderPath);
        Directory.CreateDirectory(Path.Combine(folderPath, ProfilesFolder));
        Directory.CreateDirectory(Path.Combine(folderPath, "scans"));
        Directory.CreateDirectory(Path.Combine(folderPath, "logs", "raw"));
        Directory.CreateDirectory(Path.Combine(folderPath, "logs", "imported"));
        Directory.CreateDirectory(Path.Combine(folderPath, "autosaves"));

        // Write workspace version file so we can detect incompatible workspaces later.
        var versionContent = $"codomon-version={BuildInfo.AppVersion}{Environment.NewLine}build-date={BuildInfo.BuildDate}{Environment.NewLine}";
        await File.WriteAllTextAsync(Path.Combine(folderPath, VersionFile), versionContent);

        var workspaceDto = new WorkspaceFileDto
        {
            Name = workspace.WorkspaceName,
            SourceProjectPath = workspace.SourceProjectPath
        };
        await WriteJsonAsync(Path.Combine(folderPath, WorkspaceFile), workspaceDto);

        var systemsDto = new SystemsFileDto
        {
            Systems = workspace.Systems.Select(s => new SystemEntryDto
            {
                Id = s.Id,
                Name = s.Name,
                Notes = s.Notes,
                DefaultWidth = s.Width,
                DefaultHeight = s.Height
            }).ToList()
        };
        await WriteJsonAsync(Path.Combine(folderPath, SystemsFile), systemsDto);

        var modulesDto = new ModulesFileDto
        {
            Modules = workspace.Modules.Select(m => new ModuleEntryDto
            {
                Id = m.Id,
                SystemId = m.SystemId,
                Name = m.Name,
                Notes = m.Notes,
                DefaultWidth = m.Width,
                DefaultHeight = m.Height
            }).ToList()
        };
        await WriteJsonAsync(Path.Combine(folderPath, ModulesFile), modulesDto);

        var connectionsDto = new ConnectionsFileDto
        {
            Connections = workspace.Connections.Select(c => new ConnectionEntryDto
            {
                Id = c.Id,
                Name = c.Name,
                Type = c.Type,
                Notes = c.Notes,
                FromId = c.FromId,
                ToId = c.ToId,
                Origin = c.Origin.ToString()
            }).ToList()
        };
        await WriteJsonAsync(Path.Combine(folderPath, ConnectionsFile), connectionsDto);

        var layoutPositions = new Dictionary<string, LayoutPositionDto>();
        foreach (var sys in workspace.Systems)
        {
            layoutPositions[sys.Id] = new LayoutPositionDto
            {
                X = sys.X,
                Y = sys.Y,
                Width = sys.Width,
                Height = sys.Height
            };
            foreach (var mod in sys.Modules)
            {
                layoutPositions[mod.Id] = new LayoutPositionDto
                {
                    X = mod.RelativeX,
                    Y = mod.RelativeY,
                    Width = mod.Width,
                    Height = mod.Height
                };
            }
        }

        var profileDto = new ProfileFileDto
        {
            ProfileName = workspace.ActiveProfile.ProfileName,
            LayoutPositions = layoutPositions,
            CheckboxFilterState = workspace.ActiveProfile.CheckboxFilterState
        };
        await WriteJsonAsync(Path.Combine(folderPath, ProfilesFolder, DefaultProfileFile), profileDto);
    }

    public static async Task<WorkspaceModel> LoadAsync(string folderPath)
    {
        ValidateFolder(folderPath);

        var workspaceDto = await ReadJsonAsync<WorkspaceFileDto>(Path.Combine(folderPath, WorkspaceFile));
        var systemsDto = await ReadJsonAsync<SystemsFileDto>(Path.Combine(folderPath, SystemsFile));
        var modulesDto = await ReadJsonAsync<ModulesFileDto>(Path.Combine(folderPath, ModulesFile));
        var connectionsDto = await ReadJsonAsync<ConnectionsFileDto>(Path.Combine(folderPath, ConnectionsFile));

        ProfileFileDto? profileDto = null;
        var profilePath = Path.Combine(folderPath, ProfilesFolder, DefaultProfileFile);
        if (File.Exists(profilePath))
            profileDto = await ReadJsonAsync<ProfileFileDto>(profilePath);

        var modulesBySystem = modulesDto.Modules
            .GroupBy(m => m.SystemId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var systems = systemsDto.Systems.Select(s =>
        {
            var sys = new SystemBoxModel
            {
                Id = s.Id,
                Name = s.Name,
                Notes = s.Notes,
                Width = s.DefaultWidth,
                Height = s.DefaultHeight
            };

            if (modulesBySystem.TryGetValue(s.Id, out var mods))
            {
                foreach (var m in mods)
                {
                    sys.Modules.Add(new ModuleBoxModel
                    {
                        Id = m.Id,
                        SystemId = m.SystemId,
                        Name = m.Name,
                        Notes = m.Notes,
                        Width = m.DefaultWidth,
                        Height = m.DefaultHeight
                    });
                }
            }

            return sys;
        }).ToList();

        if (profileDto != null)
        {
            foreach (var sys in systems)
            {
                if (profileDto.LayoutPositions.TryGetValue(sys.Id, out var pos))
                {
                    sys.X = pos.X;
                    sys.Y = pos.Y;
                    sys.Width = pos.Width;
                    sys.Height = pos.Height;
                }
                foreach (var mod in sys.Modules)
                {
                    if (profileDto.LayoutPositions.TryGetValue(mod.Id, out var mPos))
                    {
                        mod.RelativeX = mPos.X;
                        mod.RelativeY = mPos.Y;
                        mod.Width = mPos.Width;
                        mod.Height = mPos.Height;
                    }
                }
            }
        }

        var connections = connectionsDto.Connections.Select(c => new ConnectionModel
        {
            Id = c.Id,
            Name = c.Name,
            Type = c.Type,
            Notes = c.Notes,
            FromId = c.FromId,
            ToId = c.ToId,
            Origin = Enum.TryParse<ConnectionOrigin>(c.Origin, out var origin)
                ? origin
                : ConnectionOrigin.Manual
        }).ToList();

        var profile = new ProfileModel
        {
            ProfileName = profileDto?.ProfileName ?? "Default",
            CheckboxFilterState = profileDto?.CheckboxFilterState ?? new Dictionary<string, bool>()
        };

        var workspace = new WorkspaceModel
        {
            WorkspaceName = workspaceDto.Name,
            SourceProjectPath = workspaceDto.SourceProjectPath,
            ActiveProfile = profile
        };

        workspace.Systems.AddRange(systems);
        workspace.Connections.AddRange(connections);

        return workspace;
    }

    public static async Task<WorkspaceModel> CreateNewAsync(
        string folderPath,
        string workspaceName,
        string sourceProjectPath = "",
        string profileName = "Default",
        IEnumerable<string>? initialSystemNames = null)
    {
        const double InitialXOffset = 40;
        const double InitialYOffset = 40;
        const double SystemHorizontalSpacing = 260;

        var workspace = new WorkspaceModel
        {
            WorkspaceName = workspaceName,
            SourceProjectPath = sourceProjectPath,
            ActiveProfile = new ProfileModel { ProfileName = profileName }
        };

        if (initialSystemNames != null)
        {
            double xOffset = InitialXOffset;
            foreach (var name in initialSystemNames)
            {
                workspace.Systems.Add(new Models.SystemBoxModel
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = name,
                    X = xOffset,
                    Y = InitialYOffset
                });
                xOffset += SystemHorizontalSpacing;
            }
        }

        await SaveAsync(workspace, folderPath);
        return workspace;
    }

    public static void ValidateFolder(string folderPath)
    {
        var missing = RequiredFiles
            .Where(f => !File.Exists(Path.Combine(folderPath, f)))
            .ToList();

        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                $"The selected folder is not a valid Codomon workspace. " +
                $"Missing required files: {string.Join(", ", missing)}");
        }
    }

    private static async Task WriteJsonAsync<T>(string path, T value)
    {
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, value, JsonOptions);
    }

    private static async Task<T> ReadJsonAsync<T>(string path) where T : new()
    {
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions)
            ?? throw new InvalidOperationException($"Failed to deserialize '{path}': file is empty or contains null.");
    }
}
