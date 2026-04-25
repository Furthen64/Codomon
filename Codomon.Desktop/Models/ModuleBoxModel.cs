using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Codomon.Desktop.Models;

public class ModuleBoxModel : INotifyPropertyChanged
{
    private string _id = string.Empty;
    private string _systemId = string.Empty;
    private string _name = string.Empty;
    private string _notes = string.Empty;
    private double _relativeX;
    private double _relativeY;
    private double _width = 80;
    private double _height = 40;
    private bool _isVisible = true;
    private bool _isSelected;

    public string Id
    {
        get => _id;
        set { _id = value; OnPropertyChanged(); }
    }

    public string SystemId
    {
        get => _systemId;
        set { _systemId = value; OnPropertyChanged(); }
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

    public double RelativeX
    {
        get => _relativeX;
        set { _relativeX = value; OnPropertyChanged(); }
    }

    public double RelativeY
    {
        get => _relativeY;
        set { _relativeY = value; OnPropertyChanged(); }
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

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
