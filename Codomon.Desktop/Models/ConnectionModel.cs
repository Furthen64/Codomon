namespace Codomon.Desktop.Models;

public class ConnectionModel
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public string FromId { get; set; } = string.Empty;
    public string ToId { get; set; } = string.Empty;
    public ConnectionOrigin Origin { get; set; } = ConnectionOrigin.Manual;
}
