using Avalonia;

namespace Codomon.Desktop.Models;

public class SystemBoxModel
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public Rect Bounds { get; set; }
    public List<ModuleBoxModel> Modules { get; set; } = new();
}
