using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Codomon.Desktop.Models;

public class SystemBoxModel : INotifyPropertyChanged
{
    private string _id = string.Empty;
    private string _name = string.Empty;
    private string _notes = string.Empty;
    private double _x;
    private double _y;
    private double _width = 220;
    private double _height = 200;
    private bool _isVisible = true;
    private bool _isSelected;

    public string Id
    {
        get => _id;
        set { _id = value; OnPropertyChanged(); }
    }

    public string Name
    {
        get => _name;
        set { _name = value; OnPropertyChanged(); }
    }

    public string Notes
    {
        get => _notes;
        set { _notes = value; OnPropertyChanged(); }
    }

    public double X
    {
        get => _x;
        set { _x = value; OnPropertyChanged(); }
    }

    public double Y
    {
        get => _y;
        set { _y = value; OnPropertyChanged(); }
    }

    public double Width
    {
        get => _width;
        set { _width = value; OnPropertyChanged(); }
    }

    public double Height
    {
        get => _height;
        set { _height = value; OnPropertyChanged(); }
    }

    public bool IsVisible
    {
        get => _isVisible;
        set { _isVisible = value; OnPropertyChanged(); }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set { _isSelected = value; OnPropertyChanged(); }
    }

    public List<ModuleBoxModel> Modules { get; set; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
