using Avalonia.Controls;
using Codomon.Desktop.Controls;
using Codomon.Desktop.Models;
using Codomon.Desktop.ViewModels;
using System.Linq;

namespace Codomon.Desktop.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private MainCanvasControl? _canvas;

    public MainWindow()
    {
        InitializeComponent();

        _vm = new MainViewModel();
        DataContext = _vm;

        SetupCanvas();
        SetupTreeView();

        _vm.Selection.PropertyChanged += (_, _) => UpdatePropertiesPanel();
    }

    private void SetupCanvas()
    {
        _canvas = new MainCanvasControl(_vm.Workspace, _vm.Selection);

        var host = this.FindControl<ContentControl>("CanvasHost");
        if (host != null)
            host.Content = _canvas;
    }

    private void UpdatePropertiesPanel()
    {
        var nameText = this.FindControl<TextBlock>("PropNameText");
        var typeText = this.FindControl<TextBlock>("PropTypeText");

        var sel = _vm.Selection;
        if (nameText != null)
            nameText.Text = string.IsNullOrEmpty(sel.SelectedName) ? "None" : sel.SelectedName;
        if (typeText != null)
            typeText.Text = string.IsNullOrEmpty(sel.SelectedType) ? "-" : sel.SelectedType;
    }

    private void SetupTreeView()
    {
        var tree = this.FindControl<TreeView>("ArchTreeView");
        if (tree == null) return;

        var items = _vm.Workspace.Systems.Select(sys =>
        {
            var sysNode = new TreeViewItem
            {
                Header = CreateNodeHeader(sys.Name, "☐"),
                IsExpanded = true,
                Tag = sys,
                ItemsSource = sys.Modules.Select(mod => new TreeViewItem
                {
                    Header = CreateNodeHeader(mod.Name, "  ☐"),
                    Tag = mod
                }).ToList()
            };
            return sysNode;
        }).ToList();

        tree.ItemsSource = items;

        tree.SelectionChanged += OnTreeSelectionChanged;
    }

    private void OnTreeSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count == 0 || e.AddedItems[0] is not TreeViewItem item)
            return;

        _vm.Workspace.ClearSelection();

        if (item.Tag is SystemBoxModel sys)
        {
            sys.IsSelected = true;
            _vm.Selection.SelectedId = sys.Id;
            _vm.Selection.SelectedType = "System";
            _vm.Selection.SelectedName = sys.Name;
        }
        else if (item.Tag is ModuleBoxModel mod)
        {
            mod.IsSelected = true;
            _vm.Selection.SelectedId = mod.Id;
            _vm.Selection.SelectedType = "Module";
            _vm.Selection.SelectedName = mod.Name;
        }
    }

    private static Avalonia.Controls.StackPanel CreateNodeHeader(string name, string icon)
    {
        var panel = new Avalonia.Controls.StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            Spacing = 6
        };
        panel.Children.Add(new TextBlock { Text = icon, Foreground = Avalonia.Media.Brushes.Gray });
        panel.Children.Add(new TextBlock { Text = name, Foreground = Avalonia.Media.Brushes.LightGray });
        return panel;
    }
}
