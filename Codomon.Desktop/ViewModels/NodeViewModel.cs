using Avalonia;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Codomon.Desktop.ViewModels;

public class NodeViewModel : INotifyPropertyChanged
{
    private string _title = string.Empty;
    private Point _location;

    public string Title
    {
        get => _title;
        set { _title = value; OnPropertyChanged(); }
    }

    public Point Location
    {
        get => _location;
        set { _location = value; OnPropertyChanged(); }
    }

    /// <summary>Input connectors displayed on the left side of the node.</summary>
    public ObservableCollection<ConnectorViewModel> Inputs { get; } = new() { new ConnectorViewModel() };

    /// <summary>Output connectors displayed on the right side of the node.</summary>
    public ObservableCollection<ConnectorViewModel> Outputs { get; } = new() { new ConnectorViewModel() };

    /// <summary>Convenience accessor for the single input connector.</summary>
    public ConnectorViewModel InputConnector => Inputs[0];

    /// <summary>Convenience accessor for the single output connector.</summary>
    public ConnectorViewModel OutputConnector => Outputs[0];

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
