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
            SourceProjectPath = workspace.SourceProjectPath,
            ActiveProfileId = workspace.ActiveProfileId
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

        // Capture live layout into the active profile before saving.
        var activeProfile = workspace.ActiveProfile;
        if (activeProfile != null)
            CaptureLayoutIntoProfile(workspace, activeProfile);

        // Save all profiles to profiles/{id}.json
        var profilesDir = Path.Combine(folderPath, ProfilesFolder);
        var savedIds = new HashSet<string>();
        foreach (var profile in workspace.Profiles)
        {
            var profileDto = ProfileToDto(profile);
            await WriteJsonAsync(Path.Combine(profilesDir, $"{profile.Id}.json"), profileDto);
            savedIds.Add(profile.Id);
        }

        // Remove profile files that no longer exist in the workspace.
        foreach (var file in Directory.GetFiles(profilesDir, "*.json"))
        {
            var id = Path.GetFileNameWithoutExtension(file);
            if (!savedIds.Contains(id))
                File.Delete(file);
        }
    }

    public static async Task<WorkspaceModel> LoadAsync(string folderPath)
    {
        ValidateFolder(folderPath);

        var workspaceDto = await ReadJsonAsync<WorkspaceFileDto>(Path.Combine(folderPath, WorkspaceFile));
        var systemsDto = await ReadJsonAsync<SystemsFileDto>(Path.Combine(folderPath, SystemsFile));
        var modulesDto = await ReadJsonAsync<ModulesFileDto>(Path.Combine(folderPath, ModulesFile));
        var connectionsDto = await ReadJsonAsync<ConnectionsFileDto>(Path.Combine(folderPath, ConnectionsFile));

        // Load all profiles from profiles/*.json
        var profiles = new List<ProfileModel>();
        var profilesDir = Path.Combine(folderPath, ProfilesFolder);
        if (Directory.Exists(profilesDir))
        {
            foreach (var file in Directory.GetFiles(profilesDir, "*.json").OrderBy(f => f))
            {
                var dto = await ReadJsonAsync<ProfileFileDto>(file);
                var id = Path.GetFileNameWithoutExtension(file);
                profiles.Add(DtoToProfile(id, dto));
            }
        }

        // Ensure at least one profile exists (migration / empty workspaces).
        if (profiles.Count == 0)
            profiles.Add(new ProfileModel { Id = "default", ProfileName = "Default" });

        // Resolve active profile ID: prefer stored value, fall back to first profile.
        var activeProfileId = !string.IsNullOrEmpty(workspaceDto.ActiveProfileId)
            && profiles.Any(p => p.Id == workspaceDto.ActiveProfileId)
                ? workspaceDto.ActiveProfileId
                : profiles[0].Id;

        var activeProfile = profiles.First(p => p.Id == activeProfileId);

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

        // Apply the active profile's layout positions to the live systems/modules.
        ApplyProfileLayout(activeProfile, systems);

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

        var workspace = new WorkspaceModel
        {
            WorkspaceName = workspaceDto.Name,
            SourceProjectPath = workspaceDto.SourceProjectPath,
            ActiveProfileId = activeProfileId
        };

        workspace.Profiles.AddRange(profiles);
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

        var defaultProfileId = Guid.NewGuid().ToString();
        var workspace = new WorkspaceModel
        {
            WorkspaceName = workspaceName,
            SourceProjectPath = sourceProjectPath,
            ActiveProfileId = defaultProfileId
        };
        workspace.Profiles.Add(new ProfileModel { Id = defaultProfileId, ProfileName = profileName });

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

    // ── Layout helpers (shared with MainViewModel) ────────────────────────────

    /// <summary>
    /// Captures the live layout positions and visibility (checkbox) states from
    /// <paramref name="workspace"/> systems and modules into <paramref name="profile"/>.
    /// </summary>
    public static void CaptureLayoutIntoProfile(WorkspaceModel workspace, ProfileModel profile)
    {
        var positions = new Dictionary<string, LayoutPosition>();
        var checkboxState = new Dictionary<string, bool>();

        foreach (var sys in workspace.Systems)
        {
            positions[sys.Id] = new LayoutPosition
            {
                X = sys.X, Y = sys.Y, Width = sys.Width, Height = sys.Height
            };
            checkboxState[sys.Id] = sys.IsVisible;

            foreach (var mod in sys.Modules)
            {
                positions[mod.Id] = new LayoutPosition
                {
                    X = mod.RelativeX, Y = mod.RelativeY, Width = mod.Width, Height = mod.Height
                };
                checkboxState[mod.Id] = mod.IsVisible;
            }
        }

        profile.LayoutPositions = positions;
        profile.CheckboxFilterState = checkboxState;
    }

    /// <summary>Applies <paramref name="profile"/> layout positions to <paramref name="systems"/>.</summary>
    public static void ApplyProfileLayout(ProfileModel profile, IEnumerable<SystemBoxModel> systems)
    {
        foreach (var sys in systems)
        {
            if (profile.LayoutPositions.TryGetValue(sys.Id, out var pos))
            {
                sys.X = pos.X;
                sys.Y = pos.Y;
                sys.Width = pos.Width;
                sys.Height = pos.Height;
            }
            if (profile.CheckboxFilterState.TryGetValue(sys.Id, out var vis))
                sys.IsVisible = vis;

            foreach (var mod in sys.Modules)
            {
                if (profile.LayoutPositions.TryGetValue(mod.Id, out var mPos))
                {
                    mod.RelativeX = mPos.X;
                    mod.RelativeY = mPos.Y;
                    mod.Width = mPos.Width;
                    mod.Height = mPos.Height;
                }
                if (profile.CheckboxFilterState.TryGetValue(mod.Id, out var mVis))
                    mod.IsVisible = mVis;
            }
        }
    }

    // ── Dto conversion helpers ────────────────────────────────────────────────

    private static ProfileFileDto ProfileToDto(ProfileModel profile) => new()
    {
        ProfileName = profile.ProfileName,
        LayoutPositions = profile.LayoutPositions.ToDictionary(
            kvp => kvp.Key,
            kvp => new LayoutPositionDto
            {
                X = kvp.Value.X, Y = kvp.Value.Y,
                Width = kvp.Value.Width, Height = kvp.Value.Height
            }),
        CheckboxFilterState = profile.CheckboxFilterState,
        VisualSettings = profile.VisualSettings,
        Notes = profile.Notes
    };

    private static ProfileModel DtoToProfile(string id, ProfileFileDto dto) => new()
    {
        Id = id,
        ProfileName = dto.ProfileName,
        LayoutPositions = dto.LayoutPositions.ToDictionary(
            kvp => kvp.Key,
            kvp => new LayoutPosition
            {
                X = kvp.Value.X, Y = kvp.Value.Y,
                Width = kvp.Value.Width, Height = kvp.Value.Height
            }),
        CheckboxFilterState = dto.CheckboxFilterState,
        VisualSettings = dto.VisualSettings,
        Notes = dto.Notes
    };

    // ── JSON helpers ──────────────────────────────────────────────────────────

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
