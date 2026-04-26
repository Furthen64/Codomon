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

    // Keep at most one Dev Console open at a time.
    private DevConsoleWindow? _devConsole;

    // Guards against re-entrant profile ComboBox updates.
    private bool _updatingProfileComboBox;

    public MainWindow()
    {
        InitializeComponent();

        _vm = new MainViewModel();
        DataContext = _vm;

        // Set the window title with version and build date embedded at build time.
        Title = $"Codomon {BuildInfo.AppVersion}  (build {BuildInfo.BuildDate})";

        SetupCanvas();
        SetupTreeView();
        RefreshProfileComboBox();

        _vm.Selection.PropertyChanged += (_, _) => UpdatePropertiesPanel();
        _vm.PropertyChanged += OnViewModelPropertyChanged;

        // Intercept window close to warn about unsaved changes.
        Closing += OnWindowClosing;

        AppLogger.Info("App started");
    }


    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.Workspace))
        {
            SetupCanvas();
            SetupTreeView();
            RefreshProfileComboBox();
        }
        else if (e.PropertyName == nameof(MainViewModel.StatusMessage))
        {
            var statusText = this.FindControl<TextBlock>("StatusText");
            if (statusText != null)
                statusText.Text = _vm.StatusMessage;
        }
        else if (e.PropertyName == nameof(MainViewModel.IsDirty))
        {
            UpdateWindowTitle();
        }
        else if (e.PropertyName == nameof(MainViewModel.Profiles))
        {
            RefreshProfileComboBox();
        }
        else if (e.PropertyName == nameof(MainViewModel.ActiveProfileId))
        {
            SyncProfileComboBoxSelection();
            _canvas?.InvalidateVisual();
        }
    }

    // ── Toolbar handlers ────────────────────────────────────────────────────

    private async void OnNewWorkspaceClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var wizard = new SetupWizardDialog();
        var result = await wizard.ShowDialog<SetupWizardViewModel?>(this);
        if (result == null) return;

        await ExecuteSafeAsync(async () =>
        {
            await _vm.NewWorkspaceAsync(
                result.WorkspaceFolderPath,
                result.WorkspaceName,
                result.SourceProjectPath,
                result.ProfileName,
                result.SystemNames);
            UpdateWindowTitle();
            AppLogger.Info($"New workspace created: {result.WorkspaceName}");
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
            AppLogger.Info($"Workspace opened: {folderPath}");
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
        AppLogger.Info($"Workspace saved: {_vm.WorkspaceFolderPath}");
    }

    private async void OnSaveAsWorkspaceClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        await SaveAsAsync();
    }

    private async void OnLoadAutosaveClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_vm.WorkspaceFolderPath))
        {
            await ShowErrorAsync("No workspace is open. Please open a workspace before loading an autosave.");
            return;
        }

        var entries = _vm.GetAutosaveEntries();
        if (entries.Count == 0)
        {
            await ShowErrorAsync("No autosaves found for the current workspace.");
            return;
        }

        var selected = await ShowAutosavePickerAsync(entries);
        if (selected == null) return;

        var confirmed = await ShowAutosaveWarningAsync(selected.DisplayName);
        if (!confirmed) return;

        await ExecuteSafeAsync(async () =>
        {
            await _vm.LoadAutosaveAsync(selected.Path);
            UpdateWindowTitle();
            AppLogger.Info($"Autosave loaded: {selected.DisplayName}");
        });
    }

    // ── Profile toolbar handlers ─────────────────────────────────────────────

    private void OnProfileComboBoxSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_updatingProfileComboBox) return;

        var combo = this.FindControl<ComboBox>("ProfileComboBox");
        if (combo?.SelectedItem is not ProfileModel profile) return;

        _vm.SwitchProfile(profile.Id);
    }

    private async void OnNewProfileClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var name = await ShowInputDialogAsync("New Profile", "Enter a name for the new profile:", "New Profile");
        if (string.IsNullOrWhiteSpace(name)) return;

        _vm.CreateProfile(name.Trim());
        UpdateWindowTitle();
        AppLogger.Info($"Profile created: {name}");
    }

    private async void OnRenameProfileClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var activeProfile = _vm.Workspace.ActiveProfile;
        if (activeProfile == null) return;

        var name = await ShowInputDialogAsync("Rename Profile", "Enter a new name for the profile:", activeProfile.ProfileName);
        if (string.IsNullOrWhiteSpace(name)) return;

        _vm.RenameProfile(activeProfile.Id, name.Trim());
        UpdateWindowTitle();
        AppLogger.Info($"Profile renamed to: {name}");
    }

    private async void OnDuplicateProfileClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var activeProfile = _vm.Workspace.ActiveProfile;
        if (activeProfile == null) return;

        var name = await ShowInputDialogAsync(
            "Duplicate Profile",
            "Enter a name for the duplicate profile:",
            $"Copy of {activeProfile.ProfileName}");
        if (string.IsNullOrWhiteSpace(name)) return;

        await ExecuteSafeAsync(() =>
        {
            _vm.DuplicateProfile(activeProfile.Id, name.Trim());
            UpdateWindowTitle();
            AppLogger.Info($"Profile duplicated as: {name}");
            return Task.CompletedTask;
        });
    }

    // ── Profile ComboBox helpers ─────────────────────────────────────────────

    private void RefreshProfileComboBox()
    {
        var combo = this.FindControl<ComboBox>("ProfileComboBox");
        if (combo == null) return;

        _updatingProfileComboBox = true;
        try
        {
            // Only re-bind ItemsSource when the workspace (and therefore the collection) has
            // changed. For profile additions within the same workspace the ObservableCollection
            // notifies Avalonia incrementally, avoiding a full reset that would fire spurious
            // SelectionChanged events after the guard flag is cleared.
            if (!ReferenceEquals(combo.ItemsSource, _vm.Profiles))
                combo.ItemsSource = _vm.Profiles;

            combo.DisplayMemberBinding = new Avalonia.Data.Binding(nameof(ProfileModel.ProfileName));
            SyncProfileComboBoxSelectionCore(combo);
        }
        finally
        {
            _updatingProfileComboBox = false;
        }
    }

    private void SyncProfileComboBoxSelection()
    {
        var combo = this.FindControl<ComboBox>("ProfileComboBox");
        if (combo == null) return;

        _updatingProfileComboBox = true;
        try
        {
            SyncProfileComboBoxSelectionCore(combo);
        }
        finally
        {
            _updatingProfileComboBox = false;
        }
    }

    private void SyncProfileComboBoxSelectionCore(ComboBox combo)
    {
        var activeId = _vm.ActiveProfileId;
        var match = _vm.Profiles.FirstOrDefault(p => p.Id == activeId);
        combo.SelectedItem = match;
    }

    // ── Input dialog ─────────────────────────────────────────────────────────

    private async Task<string?> ShowInputDialogAsync(string title, string prompt, string defaultValue = "")
    {
        string? result = null;

        var dialog = new Window
        {
            Title = title,
            Width = 420,
            Height = 160,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#111820"))
        };

        var inputBox = new TextBox
        {
            Text = defaultValue,
            Foreground = Avalonia.Media.Brushes.White,
            Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#1A2435")),
            Padding = new Avalonia.Thickness(6, 4)
        };

        var okBtn = new Button
        {
            Content = "OK",
            Padding = new Avalonia.Thickness(20, 4),
            IsDefault = true
        };
        var cancelBtn = new Button
        {
            Content = "Cancel",
            Padding = new Avalonia.Thickness(20, 4),
            IsCancel = true
        };

        okBtn.Click += (_, _) => { result = inputBox.Text; dialog.Close(); };
        cancelBtn.Click += (_, _) => dialog.Close();
        inputBox.KeyDown += (_, ke) =>
        {
            if (ke.Key == Avalonia.Input.Key.Enter) { result = inputBox.Text; dialog.Close(); }
        };

        dialog.Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(16),
            Spacing = 10,
            Children =
            {
                new TextBlock
                {
                    Text = prompt,
                    Foreground = Avalonia.Media.Brushes.White,
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap
                },
                inputBox,
                new StackPanel
                {
                    Orientation = Avalonia.Layout.Orientation.Horizontal,
                    Spacing = 8,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                    Children = { okBtn, cancelBtn }
                }
            }
        };

        await dialog.ShowDialog(this);
        return result;
    }

    // ── Autosave dialogs ─────────────────────────────────────────────────────

    private async Task<Codomon.Desktop.Persistence.AutosaveEntry?> ShowAutosavePickerAsync(
        List<Codomon.Desktop.Persistence.AutosaveEntry> entries)
    {
        Codomon.Desktop.Persistence.AutosaveEntry? result = null;

        var dialog = new Window
        {
            Title = "Load Autosave",
            Width = 480,
            Height = 320,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#111820"))
        };

        var listBox = new ListBox
        {
            Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#1A2435")),
            Margin = new Avalonia.Thickness(0, 0, 0, 8)
        };

        foreach (var entry in entries)
        {
            listBox.Items.Add(new ListBoxItem
            {
                Content = new TextBlock
                {
                    Text = entry.DisplayName,
                    Foreground = Avalonia.Media.Brushes.White,
                    FontFamily = new Avalonia.Media.FontFamily("Monospace"),
                    Padding = new Avalonia.Thickness(4, 2)
                },
                Tag = entry
            });
        }

        var loadBtn = new Button
        {
            Content = "Load Selected",
            Padding = new Avalonia.Thickness(20, 4),
            IsEnabled = false
        };
        var cancelBtn = new Button
        {
            Content = "Cancel",
            Padding = new Avalonia.Thickness(20, 4)
        };

        listBox.SelectionChanged += (_, _) =>
            loadBtn.IsEnabled = listBox.SelectedItem != null;

        loadBtn.Click += (_, _) =>
        {
            if (listBox.SelectedItem is ListBoxItem item &&
                item.Tag is Codomon.Desktop.Persistence.AutosaveEntry entry)
                result = entry;
            dialog.Close();
        };
        cancelBtn.Click += (_, _) => dialog.Close();

        dialog.Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(16),
            Spacing = 8,
            Children =
            {
                new TextBlock
                {
                    Text = "Select an autosave to restore:",
                    Foreground = Avalonia.Media.Brushes.White,
                    FontWeight = Avalonia.Media.FontWeight.SemiBold
                },
                listBox,
                new StackPanel
                {
                    Orientation = Avalonia.Layout.Orientation.Horizontal,
                    Spacing = 8,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                    Children = { loadBtn, cancelBtn }
                }
            }
        };

        await dialog.ShowDialog(this);
        return result;
    }

    private async Task<bool> ShowAutosaveWarningAsync(string autosaveName)
    {
        bool confirmed = false;

        var dialog = new Window
        {
            Title = "Restore Autosave",
            Width = 440,
            Height = 180,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#111820"))
        };

        var restoreBtn = new Button { Content = "Restore", Padding = new Avalonia.Thickness(20, 4) };
        var cancelBtn = new Button { Content = "Cancel", Padding = new Avalonia.Thickness(20, 4) };

        restoreBtn.Click += (_, _) => { confirmed = true; dialog.Close(); };
        cancelBtn.Click += (_, _) => dialog.Close();

        dialog.Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(20),
            Spacing = 16,
            Children =
            {
                new TextBlock
                {
                    Text = $"Restoring autosave \"{autosaveName}\" will overwrite the current workspace metadata and profile settings. This cannot be undone.\n\nContinue?",
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                    Foreground = Avalonia.Media.Brushes.White
                },
                new StackPanel
                {
                    Orientation = Avalonia.Layout.Orientation.Horizontal,
                    Spacing = 8,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                    Children = { restoreBtn, cancelBtn }
                }
            }
        };

        await dialog.ShowDialog(this);
        return confirmed;
    }

    private async void OnDevConsoleClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_devConsole != null)
        {
            _devConsole.Activate();
            return;
        }

        _devConsole = new DevConsoleWindow();
        _devConsole.Closed += (_, _) => _devConsole = null;
        _devConsole.Show(this);
    }

    private async void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (!_vm.IsDirty) return;

        // Cancel the close first; re-open after the user answers.
        e.Cancel = true;

        var save = await ShowUnsavedChangesDialogAsync();
        if (save == null) return;   // user chose Cancel — do nothing

        if (save == true)
        {
            if (string.IsNullOrEmpty(_vm.WorkspaceFolderPath))
            {
                await SaveAsAsync();
                if (_vm.IsDirty) return;   // save was not completed (folder picker cancelled or error)
            }
            else
            {
                await ExecuteSafeAsync(_vm.SaveWorkspaceAsync);
            }
        }

        // "Discard" or save succeeded — close for real.
        _vm.IsDirty = false;
        Close();
    }

    private async Task<bool?> ShowUnsavedChangesDialogAsync()
    {
        bool? result = null;

        var dialog = new Window
        {
            Title = "Unsaved Changes",
            Width = 400,
            Height = 160,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#111820"))
        };

        var saveBtn = new Button { Content = "Save", Padding = new Avalonia.Thickness(20, 4) };
        var discardBtn = new Button { Content = "Discard", Padding = new Avalonia.Thickness(20, 4) };
        var cancelBtn = new Button { Content = "Cancel", Padding = new Avalonia.Thickness(20, 4) };

        saveBtn.Click += (_, _) => { result = true; dialog.Close(); };
        discardBtn.Click += (_, _) => { result = false; dialog.Close(); };
        cancelBtn.Click += (_, _) => { result = null; dialog.Close(); };

        dialog.Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(20),
            Spacing = 16,
            Children =
            {
                new TextBlock
                {
                    Text = "You have unsaved changes. Save before closing?",
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                    Foreground = Avalonia.Media.Brushes.White
                },
                new StackPanel
                {
                    Orientation = Avalonia.Layout.Orientation.Horizontal,
                    Spacing = 8,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                    Children = { saveBtn, discardBtn, cancelBtn }
                }
            }
        };

        await dialog.ShowDialog(this);
        return result;
    }

    private async Task SaveAsAsync()
    {
        var storageProvider = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storageProvider == null) return;

        var folders = await storageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Save Workspace As — Choose or Create Folder",
            AllowMultiple = false
        });

        if (folders.Count == 0) return;

        var folderPath = folders[0].Path.LocalPath;

        await ExecuteSafeAsync(async () =>
        {
            await _vm.SaveWorkspaceAsAsync(folderPath);
            UpdateWindowTitle();
            AppLogger.Info($"Workspace saved as: {folderPath}");
        });
    }

    private void UpdateWindowTitle()
    {
        var workspaceSuffix = _vm.IsDirty
            ? $" — {_vm.Workspace.WorkspaceName} *"
            : $" — {_vm.Workspace.WorkspaceName}";
        Title = $"Codomon {BuildInfo.AppVersion}  (build {BuildInfo.BuildDate}){workspaceSuffix}";
    }

    // ── Canvas / TreeView ────────────────────────────────────────────────────

    private void SetupCanvas()
    {
        _canvas = new MainCanvasControl(_vm.Workspace, _vm.Selection);
        _canvas.OnLayoutChanged = () => _vm.IsDirty = true;

        var host = this.FindControl<ContentControl>("CanvasHost");
        if (host != null)
            host.Content = _canvas;

        AppLogger.Debug("Canvas initialized");
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
