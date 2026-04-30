using System.IO;
using System.Text.Json;
using Codomon.Desktop.Models;
using Codomon.Desktop.Models.SystemMap;
using Codomon.Desktop.Persistence.Dto;
using Codomon.Desktop.Services;

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
    private const string RulesFile = "rules.json";
    private const string SystemMapFile = "systemmap.json";
    private const string ProfilesFolder = "profiles";
    private const string VersionFile = ".wsversion";

    private const string DefaultHypothesisPrompt =
        """
        You are an expert software architect. Below are Markdown summaries of C# source files from a codebase.

        Analyze these summaries and produce a JSON architecture hypothesis following the exact schema below.
        Only output the JSON object — no prose, no markdown fences.

        Schema:
        {
          "systems": [
            {
              "name": "string",
              "kind": "DesktopApp|WebApp|BackendService|WorkerService|ScheduledJob|CliTool|DatabaseProcess|LibraryOnly|Unknown",
              "confidence": "Likely|Possible|Unknown",
              "evidence": ["string"],
              "modules": [
                {
                  "name": "string",
                  "confidence": "Likely|Possible|Unknown",
                  "highValueNodes": ["string"]
                }
              ]
            }
          ],
          "highValueNodes": [
            {
              "name": "string",
              "reason": "string",
              "signal": "EntryPoint|Orchestrator|CentralStateModel|ServiceBoundary|SerializationBoundary|IntegrationBoundary|RuntimeHeavy|ErrorProne|BridgeBetweenClusters|Other",
              "confidence": "Likely|Possible|Unknown"
            }
          ],
          "startup": [
            {
              "system": "string",
              "mechanism": "string",
              "entryPointCandidates": ["string"],
              "confidence": "Likely|Possible|Unknown"
            }
          ],
          "uncertainAreas": ["string"]
        }

        --- SUMMARIES ---
        {Summaries}
        """;

    private const string DefaultSummaryPrompt =
        """
        Please analyze the following C# source file and write a concise Markdown summary covering:

        - **Purpose and responsibilities** of the file
        - **Classes, interfaces, or records** defined
        - **Key public methods** and their intent
        - **Notable dependencies or patterns**

        Source file path: `{FilePath}`

        ```csharp
        {SourceCode}
        ```
        """;

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
        Directory.CreateDirectory(Path.Combine(folderPath, "summaries"));
        Directory.CreateDirectory(Path.Combine(folderPath, "hypotheses"));

        // Write workspace version file so we can detect incompatible workspaces later.
        var versionContent = $"codomon-version={BuildInfo.AppVersion}{Environment.NewLine}build-date={BuildInfo.BuildDate}{Environment.NewLine}";
        await File.WriteAllTextAsync(Path.Combine(folderPath, VersionFile), versionContent);

        // Create the default summary prompt file if it does not already exist.
        var promptPath = Path.Combine(folderPath, "summary_prompt.md");
        if (!File.Exists(promptPath))
            await File.WriteAllTextAsync(promptPath, DefaultSummaryPrompt);

        // Create the default hypothesis prompt file if it does not already exist.
        var hypothesisPromptPath = Path.Combine(folderPath, "hypothesis_prompt.md");
        if (!File.Exists(hypothesisPromptPath))
            await File.WriteAllTextAsync(hypothesisPromptPath, DefaultHypothesisPrompt);

        var workspaceDto = new WorkspaceFileDto
        {
            Name = workspace.WorkspaceName,
            SourceProjectPath = workspace.SourceProjectPath,
            ActiveProfileId = workspace.ActiveProfileId,
            LastBrowsedFolder = workspace.LastBrowsedFolder,
            WatchedLogPaths = new List<string>(workspace.WatchedLogPaths),
            LlmSettings = new Dto.LlmSettingsDto
            {
                ApiEndpoint = workspace.LlmSettings.ApiEndpoint,
                ModelName = workspace.LlmSettings.ModelName
            }
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
                Origin = c.Origin.ToString(),
                IsReadOnly = c.IsReadOnly
            }).ToList()
        };
        await WriteJsonAsync(Path.Combine(folderPath, ConnectionsFile), connectionsDto);

        var rulesDto = new MappingRulesFileDto
        {
            Rules = workspace.MappingRules.Select(r => new MappingRuleEntryDto
            {
                Id = r.Id,
                TargetType = r.TargetType.ToString(),
                TargetId = r.TargetId,
                RuleType = r.RuleType.ToString(),
                Pattern = r.Pattern,
                IsEnabled = r.IsEnabled,
                Notes = r.Notes
            }).ToList()
        };
        await WriteJsonAsync(Path.Combine(folderPath, RulesFile), rulesDto);

        var systemMapDto = SystemMapToDto(workspace.SystemMap);
        await WriteJsonAsync(Path.Combine(folderPath, SystemMapFile), systemMapDto);

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

        // rules.json is optional (backward-compatible with pre-Phase-08 workspaces).
        var rulesPath = Path.Combine(folderPath, RulesFile);
        var rulesDto = File.Exists(rulesPath)
            ? await ReadJsonAsync<MappingRulesFileDto>(rulesPath)
            : new MappingRulesFileDto();

        // systemmap.json is optional (backward-compatible with pre-Phase-01 workspaces).
        var systemMapPath = Path.Combine(folderPath, SystemMapFile);
        var systemMapDto = File.Exists(systemMapPath)
            ? await ReadJsonAsync<SystemMapFileDto>(systemMapPath)
            : new SystemMapFileDto();

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
                : ConnectionOrigin.Manual,
            IsReadOnly = c.IsReadOnly
        }).ToList();

        var workspace = new WorkspaceModel
        {
            WorkspaceName = workspaceDto.Name,
            SourceProjectPath = workspaceDto.SourceProjectPath,
            ActiveProfileId = activeProfileId,
            LastBrowsedFolder = workspaceDto.LastBrowsedFolder ?? string.Empty
        };

        foreach (var p in profiles) workspace.Profiles.Add(p);
        workspace.Systems.AddRange(systems);
        workspace.Connections.AddRange(connections);

        var rules = rulesDto.Rules.Select(r => new MappingRuleModel
        {
            Id = r.Id,
            TargetType = Enum.TryParse<RuleTargetType>(r.TargetType, out var tt) ? tt : RuleTargetType.System,
            TargetId = r.TargetId,
            RuleType = Enum.TryParse<RuleType>(r.RuleType, out var rt) ? rt : RuleType.LogSourcePattern,
            Pattern = r.Pattern,
            IsEnabled = r.IsEnabled,
            Notes = r.Notes
        }).ToList();
        workspace.MappingRules.AddRange(rules);

        if (workspaceDto.WatchedLogPaths != null)
            workspace.WatchedLogPaths.AddRange(workspaceDto.WatchedLogPaths);

        workspace.LlmSettings = new Models.LlmSettingsModel
        {
            ApiEndpoint = workspaceDto.LlmSettings?.ApiEndpoint ?? "http://localhost:8080/v1",
            ModelName = workspaceDto.LlmSettings?.ModelName ?? string.Empty
        };

        workspace.SystemMap = DtoToSystemMap(systemMapDto);

        // Re-apply any stored manual overrides so that human curation is always
        // the final authority, regardless of which analysis pass last ran.
        ManualOverrideService.Apply(workspace.SystemMap, workspace.SystemMap.ManualOverrides);

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

        // Re-validate that the folder is still empty at creation time (guards against
        // TOCTOU races between wizard validation and workspace creation).
        if (Directory.Exists(folderPath) &&
            Directory.EnumerateFileSystemEntries(folderPath).Any())
        {
            throw new InvalidOperationException(
                $"The workspace folder '{folderPath}' is not empty. " +
                "Please choose an empty folder.");
        }

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

    // ── System Map Dto conversion helpers ────────────────────────────────────

    private static SystemMapFileDto SystemMapToDto(SystemMapModel map) => new()
    {
        Id = map.Id,
        CreatedAt = map.CreatedAt,
        UpdatedAt = map.UpdatedAt,
        Systems = map.Systems.Select(SystemToDto).ToList(),
        Modules = map.Modules.Select(ModuleToDto).ToList(),
        ExternalSystems = map.ExternalSystems.Select(ExternalSystemToDto).ToList(),
        Relationships = map.Relationships.Select(RelationshipToDto).ToList(),
        ManualOverrides = map.ManualOverrides.Select(OverrideToDto).ToList()
    };

    private static SystemMapModel DtoToSystemMap(SystemMapFileDto dto) => new()
    {
        Id = string.IsNullOrEmpty(dto.Id) ? Guid.NewGuid().ToString() : dto.Id,
        CreatedAt = dto.CreatedAt,
        UpdatedAt = dto.UpdatedAt,
        Systems = dto.Systems.Select(DtoToSystem).ToList(),
        Modules = dto.Modules.Select(DtoToModule).ToList(),
        ExternalSystems = dto.ExternalSystems.Select(DtoToExternalSystem).ToList(),
        Relationships = dto.Relationships.Select(DtoToRelationship).ToList(),
        ManualOverrides = dto.ManualOverrides.Select(DtoToOverride).ToList()
    };

    private static SystemDto SystemToDto(SystemModel s) => new()
    {
        Id = s.Id,
        Name = s.Name,
        Kind = s.Kind.ToString(),
        Notes = s.Notes,
        Confidence = s.Confidence.ToString(),
        StartupMechanism = s.StartupMechanism,
        EntryPointCandidates = new List<string>(s.EntryPointCandidates),
        ConfigFileCandidates = new List<string>(s.ConfigFileCandidates),
        LogFileCandidates = new List<string>(s.LogFileCandidates),
        Modules = s.Modules.Select(ModuleToDto).ToList(),
        Evidence = s.Evidence.Select(EvidenceToDto).ToList()
    };

    private static SystemModel DtoToSystem(SystemDto dto) => new()
    {
        Id = dto.Id,
        Name = dto.Name,
        Kind = Enum.TryParse<SystemKind>(dto.Kind, out var sk) ? sk : SystemKind.Unknown,
        Notes = dto.Notes,
        Confidence = Enum.TryParse<ConfidenceLevel>(dto.Confidence, out var sc) ? sc : ConfidenceLevel.Unknown,
        StartupMechanism = dto.StartupMechanism,
        EntryPointCandidates = new List<string>(dto.EntryPointCandidates),
        ConfigFileCandidates = new List<string>(dto.ConfigFileCandidates),
        LogFileCandidates = new List<string>(dto.LogFileCandidates),
        Modules = dto.Modules.Select(DtoToModule).ToList(),
        Evidence = dto.Evidence.Select(DtoToEvidence).ToList()
    };

    private static ModuleDto ModuleToDto(ModuleModel m) => new()
    {
        Id = m.Id,
        Name = m.Name,
        Kind = m.Kind.ToString(),
        Notes = m.Notes,
        Confidence = m.Confidence.ToString(),
        SystemIds = new List<string>(m.SystemIds),
        CodeNodes = m.CodeNodes.Select(CodeNodeToDto).ToList(),
        Evidence = m.Evidence.Select(EvidenceToDto).ToList()
    };

    private static ModuleModel DtoToModule(ModuleDto dto) => new()
    {
        Id = dto.Id,
        Name = dto.Name,
        Kind = Enum.TryParse<ModuleKind>(dto.Kind, out var mk) ? mk : ModuleKind.Other,
        Notes = dto.Notes,
        Confidence = Enum.TryParse<ConfidenceLevel>(dto.Confidence, out var mc) ? mc : ConfidenceLevel.Unknown,
        SystemIds = new List<string>(dto.SystemIds),
        CodeNodes = dto.CodeNodes.Select(DtoToCodeNode).ToList(),
        Evidence = dto.Evidence.Select(DtoToEvidence).ToList()
    };

    private static CodeNodeDto CodeNodeToDto(CodeNodeModel n) => new()
    {
        Id = n.Id,
        Name = n.Name,
        Kind = n.Kind.ToString(),
        FullName = n.FullName,
        FilePath = n.FilePath,
        Notes = n.Notes,
        Confidence = n.Confidence.ToString(),
        Evidence = n.Evidence.Select(EvidenceToDto).ToList(),
        IsHighValue = n.IsHighValue,
        IsNoisy = n.IsNoisy,
        HideFromOverview = n.HideFromOverview
    };

    private static CodeNodeModel DtoToCodeNode(CodeNodeDto dto) => new()
    {
        Id = dto.Id,
        Name = dto.Name,
        Kind = Enum.TryParse<CodeNodeKind>(dto.Kind, out var nk) ? nk : CodeNodeKind.Other,
        FullName = dto.FullName,
        FilePath = dto.FilePath,
        Notes = dto.Notes,
        Confidence = Enum.TryParse<ConfidenceLevel>(dto.Confidence, out var nc) ? nc : ConfidenceLevel.Unknown,
        Evidence = dto.Evidence.Select(DtoToEvidence).ToList(),
        IsHighValue = dto.IsHighValue,
        IsNoisy = dto.IsNoisy,
        HideFromOverview = dto.HideFromOverview
    };

    private static ExternalSystemDto ExternalSystemToDto(ExternalSystemModel e) => new()
    {
        Id = e.Id,
        Name = e.Name,
        Kind = e.Kind,
        Notes = e.Notes,
        Confidence = e.Confidence.ToString(),
        Evidence = e.Evidence.Select(EvidenceToDto).ToList()
    };

    private static ExternalSystemModel DtoToExternalSystem(ExternalSystemDto dto) => new()
    {
        Id = dto.Id,
        Name = dto.Name,
        Kind = dto.Kind,
        Notes = dto.Notes,
        Confidence = Enum.TryParse<ConfidenceLevel>(dto.Confidence, out var ec) ? ec : ConfidenceLevel.Unknown,
        Evidence = dto.Evidence.Select(DtoToEvidence).ToList()
    };

    private static RelationshipDto RelationshipToDto(RelationshipModel r) => new()
    {
        Id = r.Id,
        Kind = r.Kind.ToString(),
        FromId = r.FromId,
        ToId = r.ToId,
        Notes = r.Notes,
        Confidence = r.Confidence.ToString(),
        Evidence = r.Evidence.Select(EvidenceToDto).ToList()
    };

    private static RelationshipModel DtoToRelationship(RelationshipDto dto) => new()
    {
        Id = dto.Id,
        Kind = Enum.TryParse<RelationshipKind>(dto.Kind, out var rk) ? rk : RelationshipKind.Other,
        FromId = dto.FromId,
        ToId = dto.ToId,
        Notes = dto.Notes,
        Confidence = Enum.TryParse<ConfidenceLevel>(dto.Confidence, out var rc) ? rc : ConfidenceLevel.Unknown,
        Evidence = dto.Evidence.Select(DtoToEvidence).ToList()
    };

    private static ManualOverrideDto OverrideToDto(ManualOverrideModel o) => new()
    {
        Id = o.Id,
        TargetId = o.TargetId,
        OverrideType = o.Type.ToString(),
        Value = o.Value,
        Notes = o.Notes,
        CreatedAt = o.CreatedAt
    };

    private static ManualOverrideModel DtoToOverride(ManualOverrideDto dto)
    {
        if (!Enum.TryParse<ManualOverrideType>(dto.OverrideType, out var ot))
        {
            AppLogger.Warn($"[WorkspaceSerializer] Unrecognised ManualOverrideType '{dto.OverrideType}' " +
                           $"on override id='{dto.Id}'. Defaulting to Unknown; override will be skipped during Apply.");
            ot = ManualOverrideType.Unknown;
        }

        return new ManualOverrideModel
        {
            Id        = dto.Id,
            TargetId  = dto.TargetId,
            Type      = ot,
            Value     = dto.Value,
            Notes     = dto.Notes,
            CreatedAt = dto.CreatedAt
        };
    }

    private static EvidenceDto EvidenceToDto(EvidenceModel e) => new()
    {
        Source = e.Source,
        Description = e.Description,
        SourceRef = e.SourceRef
    };

    private static EvidenceModel DtoToEvidence(EvidenceDto dto) => new()
    {
        Source = dto.Source,
        Description = dto.Description,
        SourceRef = dto.SourceRef
    };
}
