using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Codomon.Desktop.Models;

public class SelectionStateModel : INotifyPropertyChanged
{
    private string _selectedId = string.Empty;
    private string _selectedType = string.Empty;
    private string _selectedName = string.Empty;

    public string SelectedId
    {
        get => _selectedId;
        set { _selectedId = value; OnPropertyChanged(); }
    }

    public string SelectedType
    {
        get => _selectedType;
        set { _selectedType = value; OnPropertyChanged(); }
    }

    public string SelectedName
    {
        get => _selectedName;
        set { _selectedName = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
