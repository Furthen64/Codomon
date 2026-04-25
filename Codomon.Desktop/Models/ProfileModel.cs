namespace Codomon.Desktop.Models;

public class ProfileModel
{
    public string ProfileName { get; set; } = "Default";
    public Dictionary<string, object> LayoutOverrides { get; set; } = new();
    public Dictionary<string, bool> CheckboxFilterState { get; set; } = new();
    public Dictionary<string, object> VisualSettings { get; set; } = new();
}
