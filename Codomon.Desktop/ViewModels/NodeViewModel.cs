using Avalonia;
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

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
