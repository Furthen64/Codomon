using Avalonia;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Codomon.Desktop.ViewModels;

public class ConnectionViewModel : INotifyPropertyChanged
{
    private Point _source;
    private Point _target;

    public Point Source
    {
        get => _source;
        set { _source = value; OnPropertyChanged(); }
    }

    public Point Target
    {
        get => _target;
        set { _target = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
