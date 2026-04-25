using Avalonia.Controls;
using Codomon.Desktop.Controls;
using Codomon.Desktop.Models;
using Codomon.Desktop.ViewModels;
using System.Collections.Generic;
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
    }

    private void SetupCanvas()
    {
        _canvas = new MainCanvasControl(DemoData.Systems, DemoData.Connections);
        _canvas.SelectionChanged += OnCanvasSelectionChanged;

        var host = this.FindControl<ContentControl>("CanvasHost");
        if (host != null)
            host.Content = _canvas;
    }

    private void OnCanvasSelectionChanged(object? item)
    {
        _vm.OnSelectionChanged(item);

        var nameText = this.FindControl<TextBlock>("PropNameText");
        var typeText = this.FindControl<TextBlock>("PropTypeText");

        if (nameText != null) nameText.Text = _vm.SelectedName;
        if (typeText != null) typeText.Text = _vm.SelectedType;
    }

    private void SetupTreeView()
    {
        var tree = this.FindControl<TreeView>("ArchTreeView");
        if (tree == null) return;

        var items = new List<TreeViewItem>();

        foreach (var sys in DemoData.Systems)
        {
            var sysNode = new TreeViewItem
            {
                Header = CreateNodeHeader(sys.Name, "☐"),
                IsExpanded = true,
                ItemsSource = sys.Modules.Select(mod => new TreeViewItem
                {
                    Header = CreateNodeHeader(mod.Name, "  ☐")
                }).ToList()
            };

            items.Add(sysNode);
        }

        tree.ItemsSource = items;
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
