using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Codomon.Desktop.Controls;
using Codomon.Desktop.Models;
using Codomon.Desktop.ViewModels;
using System;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;

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
        _vm.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.Workspace))
        {
            SetupCanvas();
            SetupTreeView();
        }
        else if (e.PropertyName == nameof(MainViewModel.StatusMessage))
        {
            var statusText = this.FindControl<TextBlock>("StatusText");
            if (statusText != null)
                statusText.Text = _vm.StatusMessage;
        }
    }

    // ── Toolbar handlers ────────────────────────────────────────────────────

    private async void OnNewWorkspaceClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var storageProvider = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storageProvider == null) return;

        var file = await storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Create New Workspace",
            SuggestedFileName = "NewWorkspace",
            FileTypeChoices = new[]
            {
                new FilePickerFileType("Codomon Workspace") { Patterns = new[] { "*.codomon" } }
            }
        });

        if (file == null) return;

        var folderPath = file.Path.LocalPath;

        // SaveFilePicker may create an empty file at that path — remove it so we can create a directory.
        if (System.IO.File.Exists(folderPath))
            System.IO.File.Delete(folderPath);

        var workspaceName = System.IO.Path.GetFileNameWithoutExtension(folderPath);

        await ExecuteSafeAsync(async () =>
        {
            await _vm.NewWorkspaceAsync(folderPath, workspaceName);
            UpdateWindowTitle();
        });
    }

    private async void OnOpenWorkspaceClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var storageProvider = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storageProvider == null) return;

        var folders = await storageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Open Codomon Workspace Folder",
            AllowMultiple = false
        });

        if (folders.Count == 0) return;

        var folderPath = folders[0].Path.LocalPath;

        await ExecuteSafeAsync(async () =>
        {
            await _vm.OpenWorkspaceAsync(folderPath);
            UpdateWindowTitle();
        });
    }

    private async void OnSaveWorkspaceClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_vm.WorkspaceFolderPath))
        {
            // No folder set yet — fall through to Save As behaviour.
            await SaveAsAsync();
            return;
        }

        await ExecuteSafeAsync(_vm.SaveWorkspaceAsync);
    }

    private async Task SaveAsAsync()
    {
        var storageProvider = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storageProvider == null) return;

        var file = await storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save Workspace As",
            SuggestedFileName = _vm.Workspace.WorkspaceName,
            FileTypeChoices = new[]
            {
                new FilePickerFileType("Codomon Workspace") { Patterns = new[] { "*.codomon" } }
            }
        });

        if (file == null) return;

        var folderPath = file.Path.LocalPath;

        if (System.IO.File.Exists(folderPath))
            System.IO.File.Delete(folderPath);

        await ExecuteSafeAsync(async () =>
        {
            await _vm.SaveWorkspaceAsAsync(folderPath);
            UpdateWindowTitle();
        });
    }

    private void UpdateWindowTitle()
        => this.Title = $"Codomon — {_vm.Workspace.WorkspaceName}";

    // ── Canvas / TreeView ────────────────────────────────────────────────────

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

        tree.SelectionChanged -= OnTreeSelectionChanged;

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

    // ── Error handling ───────────────────────────────────────────────────────

    private async Task ExecuteSafeAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            await ShowErrorAsync(ex.Message);
        }
    }

    private async Task ShowErrorAsync(string message)
    {
        var dialog = new Window
        {
            Title = "Error",
            Width = 440,
            Height = 160,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false
        };

        var okButton = new Button
        {
            Content = "OK",
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            Padding = new Avalonia.Thickness(24, 4)
        };
        okButton.Click += (_, _) => dialog.Close();

        dialog.Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(16),
            Spacing = 16,
            Children =
            {
                new TextBlock
                {
                    Text = message,
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                    Foreground = Avalonia.Media.Brushes.White
                },
                okButton
            }
        };

        await dialog.ShowDialog(this);
    }
}
