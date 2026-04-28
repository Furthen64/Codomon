using Avalonia;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Codomon.Desktop.ViewModels;

public class ConnectionViewModel : INotifyPropertyChanged
{
    private readonly NodeViewModel _sourceNode;
    private readonly NodeViewModel _targetNode;

    public ConnectionViewModel(NodeViewModel source, NodeViewModel target)
    {
        _sourceNode = source;
        _targetNode = target;

        _sourceNode.PropertyChanged += OnNodePropertyChanged;
        _targetNode.PropertyChanged += OnNodePropertyChanged;
    }

    public Point Source => _sourceNode.Location;
    public Point Target => _targetNode.Location;

    private void OnNodePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(NodeViewModel.Location))
        {
            if (ReferenceEquals(sender, _sourceNode))
                OnPropertyChanged(nameof(Source));
            else
                OnPropertyChanged(nameof(Target));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
