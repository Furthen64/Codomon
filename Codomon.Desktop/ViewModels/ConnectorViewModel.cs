using Avalonia;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Codomon.Desktop.ViewModels;

public class ConnectorViewModel : INotifyPropertyChanged
{
    private Point _anchor;

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

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
