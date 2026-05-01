using Avalonia;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Codomon.Desktop.ViewModels;

public class ConnectionViewModel : INotifyPropertyChanged
{
    private readonly ConnectorViewModel _source;
    private readonly ConnectorViewModel _target;

    public ConnectionViewModel(ConnectorViewModel source, ConnectorViewModel target, string label = "")
    {
        _source = source;
        _target = target;
        Label = label;

        _source.PropertyChanged += OnConnectorPropertyChanged;
        _target.PropertyChanged += OnConnectorPropertyChanged;
    }

    /// <summary>Canvas position of the source (output) connector.</summary>
    public Point Source => _source.Anchor;

    /// <summary>Canvas position of the target (input) connector.</summary>
    public Point Target => _target.Anchor;

    /// <summary>Short label shown as a tooltip on the edge (e.g. the relationship kind).</summary>
    public string Label { get; }

    private void OnConnectorPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ConnectorViewModel.Anchor))
        {
            if (ReferenceEquals(sender, _source))
                OnPropertyChanged(nameof(Source));
            else
                OnPropertyChanged(nameof(Target));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
