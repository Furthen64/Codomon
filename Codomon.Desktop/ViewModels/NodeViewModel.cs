using Avalonia;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Codomon.Desktop.ViewModels;

public class NodeViewModel : INotifyPropertyChanged
{
    private string _title = string.Empty;
    private Point _location;
    private int _childCount;

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

    /// <summary>Number of outgoing edges (children) this node has.</summary>
    public int ChildCount
    {
        get => _childCount;
        set
        {
            _childCount = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(BorderThickness));
        }
    }

    /// <summary>
    /// Visual border thickness that scales with the number of children:
    /// 1 px for 0 children, up to 6 px for 5 or more children.
    /// </summary>
    public Thickness BorderThickness => new(Math.Clamp(_childCount + 1, 1, 6));

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
