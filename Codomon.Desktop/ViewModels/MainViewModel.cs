using Codomon.Desktop.Models;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Codomon.Desktop.ViewModels;

public class MainViewModel : INotifyPropertyChanged
{
    private object? _selectedItem;
    private string _selectedName = "None";
    private string _selectedType = "-";

    public List<SystemBoxModel> Systems => DemoData.Systems;
    public List<ConnectionModel> Connections => DemoData.Connections;

    public string SelectedName
    {
        get => _selectedName;
        set { _selectedName = value; OnPropertyChanged(); }
    }

    public string SelectedType
    {
        get => _selectedType;
        set { _selectedType = value; OnPropertyChanged(); }
    }

    public void OnSelectionChanged(object? item)
    {
        _selectedItem = item;
        if (item is SystemBoxModel sys)
        {
            SelectedName = sys.Name;
            SelectedType = "System";
        }
        else if (item is ModuleBoxModel mod)
        {
            SelectedName = mod.Name;
            SelectedType = "Module";
        }
        else
        {
            SelectedName = "None";
            SelectedType = "-";
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
