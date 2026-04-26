namespace Codomon.Desktop.Persistence.Dto;

public class WorkspaceFileDto
{
    public string Schema { get; set; } = "codomon-workspace/1";
    public string Name { get; set; } = string.Empty;
    public string SourceProjectPath { get; set; } = string.Empty;
    public string ActiveProfileId { get; set; } = string.Empty;
    public string ParserSettingsPlaceholder { get; set; } = string.Empty;
    public string LogSourceSettingsPlaceholder { get; set; } = string.Empty;
}

public class SystemEntryDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public double DefaultWidth { get; set; } = 220;
    public double DefaultHeight { get; set; } = 200;
}

public class SystemsFileDto
{
    public string Schema { get; set; } = "codomon-systems/1";
    public List<SystemEntryDto> Systems { get; set; } = new();
}

public class ModuleEntryDto
{
    public string Id { get; set; } = string.Empty;
    public string SystemId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public double DefaultWidth { get; set; } = 80;
    public double DefaultHeight { get; set; } = 40;
}

public class ModulesFileDto
{
    public string Schema { get; set; } = "codomon-modules/1";
    public List<ModuleEntryDto> Modules { get; set; } = new();
}

public class ConnectionEntryDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public string FromId { get; set; } = string.Empty;
    public string ToId { get; set; } = string.Empty;
    public string Origin { get; set; } = "Manual";
}

public class ConnectionsFileDto
{
    public string Schema { get; set; } = "codomon-connections/1";
    public List<ConnectionEntryDto> Connections { get; set; } = new();
}

public class MappingRuleEntryDto
{
    public string Id { get; set; } = string.Empty;
    public string TargetType { get; set; } = "System";
    public string TargetId { get; set; } = string.Empty;
    public string RuleType { get; set; } = string.Empty;
    public string Pattern { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
    public string Notes { get; set; } = string.Empty;
}

public class MappingRulesFileDto
{
    public string Schema { get; set; } = "codomon-rules/1";
    public List<MappingRuleEntryDto> Rules { get; set; } = new();
}

public class LayoutPositionDto
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
}

public class ProfileFileDto
{
    public string Schema { get; set; } = "codomon-profile/1";
    public string ProfileName { get; set; } = "Default";
    public Dictionary<string, LayoutPositionDto> LayoutPositions { get; set; } = new();
    public Dictionary<string, bool> CheckboxFilterState { get; set; } = new();
    public Dictionary<string, string> VisualSettings { get; set; } = new();
    public string Notes { get; set; } = string.Empty;
}
