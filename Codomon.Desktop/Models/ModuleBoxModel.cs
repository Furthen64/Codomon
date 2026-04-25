using Avalonia;

namespace Codomon.Desktop.Models;

public class ModuleBoxModel
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ParentSystemId { get; set; } = string.Empty;
    public Rect Bounds { get; set; }
}
