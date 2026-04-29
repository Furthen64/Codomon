using Avalonia;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Codomon.Desktop.ViewModels;

public class ConnectorViewModel : INotifyPropertyChanged
{
    private Point _anchor;
    private bool _isConnected;

    /// <summary>
    /// The canvas position of this connector.
    /// Bind with <c>Mode=OneWayToSource</c> to <see cref="Nodify.Connector.Anchor"/>
    /// so the control pushes its layout position back into the view-model.
    /// </summary>
    public Point Anchor
    {
        get => _anchor;
        set { _anchor = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Whether this connector is part of an active connection.
    /// Must be <c>true</c> so Nodify's <see cref="Nodify.Connector.UpdateAnchorOptimized"/>
    /// recalculates the anchor when the parent node is moved.
    /// Bind to <see cref="Nodify.Connector.IsConnected"/>.
    /// </summary>
    public bool IsConnected
    {
        get => _isConnected;
        set { _isConnected = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
