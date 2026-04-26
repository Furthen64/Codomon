namespace Codomon.Desktop.Models;

public class ProfileModel
{
    public string Id { get; set; } = string.Empty;
    public string ProfileName { get; set; } = "Default";
    public Dictionary<string, LayoutPosition> LayoutPositions { get; set; } = new();
    public Dictionary<string, bool> CheckboxFilterState { get; set; } = new();
    public Dictionary<string, string> VisualSettings { get; set; } = new();
    public string Notes { get; set; } = string.Empty;
}
