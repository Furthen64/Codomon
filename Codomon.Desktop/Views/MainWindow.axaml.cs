using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Codomon.Desktop.Controls;
using Codomon.Desktop.Models;
using Codomon.Desktop.Models.SystemMap;
using Codomon.Desktop.Persistence;
using Codomon.Desktop.Services;
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

    // Tracks the LogReplayViewModel we have subscribed to, so we can unsubscribe correctly
    // when a new workspace (and therefore a new LogReplayViewModel) is loaded.
    private LogReplayViewModel? _subscribedReplay;

    // Tracks the LiveMonitorViewModel we have subscribed to.
    private LiveMonitorViewModel? _subscribedMonitor;

    // Timeline control instance; re-created when the workspace changes.
    private TimelineControl? _timelineControl;

    // Tracks whether the log list is currently bound to live-monitor entries.
    private bool _logListShowingLive;

    // Tracks whether the live log stream is paused (auto-scroll suppressed).
    private bool _logStreamPaused;

    // Tracks when live monitoring started, for the status bar runtime display.
    private DateTimeOffset? _liveMonitorStartTime;

    // Throttles timeline rebuilds during live monitoring (max once per LiveTimelineRebuildThrottleSeconds).
    private const double LiveTimelineRebuildThrottleSeconds = 2.0;
    private DateTimeOffset _lastLiveTimelineRebuild = DateTimeOffset.MinValue;
    private bool _firstRunConfigCheckDone;
    private bool _updatingAutoAlignPreset;

    // Tracks the currently active navigation tab.
    private string _activeNavTab = "Monitor";

    public MainWindow()
    {
        InitializeComponent();

        _vm = new MainViewModel();
        DataContext = _vm;

        // Set the window title with version and build date embedded at build time.
        Title = $"codomon {BuildInfo.AppVersion}  (build {BuildInfo.BuildDate})";

        SetupCanvas();
        SetupTreeView();
        SetupTimeline();
        InitializeGraphAutoAlignPanel();
        RefreshProfileComboBox();
        PopulateRecentWorkspaces();
        SetupReplaySpeedComboBox();

        _vm.Selection.PropertyChanged += (_, _) => UpdatePropertiesPanel();
        _vm.PropertyChanged += OnViewModelPropertyChanged;
        SubscribeToLogReplay(_vm.LogReplay);
        SubscribeToLiveMonitor(_vm.LiveMonitor);

        // Apply initial nav tab state.
        UpdateNavTabStyles();
        UpdateWorkspaceNameDisplay();

        // Intercept window close to warn about unsaved changes.
        Closing += OnWindowClosing;
        Opened += OnWindowOpened;

        AppLogger.Info("App started");
    }

    private async void OnWindowOpened(object? sender, EventArgs e)
    {
        if (_firstRunConfigCheckDone) return;
        _firstRunConfigCheckDone = true;

        if (UserConfigService.Exists()) return;

        var openSettings = await ShowInitialUserConfigPromptAsync();
        if (!openSettings) return;

        var dialog = new UserSettingsDialog();
        await dialog.ShowDialog(this);
    }


    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.HasWorkspace))
        {
            UpdateMainContentVisibility();
            UpdateWindowTitle();
            UpdateWorkspaceNameDisplay();
            RefreshLiveMonitorPanel();
        }
        else if (e.PropertyName == nameof(MainViewModel.Workspace))
        {
            _logListShowingLive = false;
            SetupCanvas();
            SetupTreeView();
            RefreshProfileComboBox();
            RefreshRoslynConnectionsPanel();
            UpdateWorkspaceNameDisplay();  // calls RefreshSidebar() internally
        }
        else if (e.PropertyName == nameof(MainViewModel.Timeline))
        {
            // A new workspace was loaded — replace the timeline control.
            SetupTimeline();
        }
        else if (e.PropertyName == nameof(MainViewModel.LogReplay))
        {
            // A new workspace was loaded — unsubscribe from the old VM and subscribe to the new one.
            SubscribeToLogReplay(_vm.LogReplay);
            RefreshLogReplayPanel();
        }
        else if (e.PropertyName == nameof(MainViewModel.LiveMonitor))
        {
            // A new workspace was loaded — re-subscribe to the new live monitor VM.
            SubscribeToLiveMonitor(_vm.LiveMonitor);
            RefreshLiveMonitorPanel();
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
        else if (e.PropertyName is nameof(MainViewModel.FileCount)
                                or nameof(MainViewModel.ClassCount)
                                or nameof(MainViewModel.MethodCount)
                                or nameof(MainViewModel.LogPointCount)
                                or nameof(MainViewModel.ScanStatus)
                                or nameof(MainViewModel.TotalEventCount))
        {
            RefreshStatusBar();
        }
        else if (e.PropertyName == nameof(MainViewModel.Profiles))
        {
            RefreshProfileComboBox();
        }
        else if (e.PropertyName == nameof(MainViewModel.ActiveProfileId))
        {
            SyncProfileComboBoxSelection();
            _vm.SystemMap.LoadFrom(_vm.Workspace.SystemMap, _vm.Workspace.ActiveProfile?.LayoutPositions);
            _vm.Graph.RefreshFromSystemMap(_vm.Workspace.SystemMap);
            RebuildTimeline();
        }
    }

    // ── Navigation tabs ─────────────────────────────────────────────────────

    private void OnNavTabClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        var tab = btn.Tag?.ToString() ?? "Monitor";
        SetActiveNavTab(tab);
    }

    private void SetActiveNavTab(string tab)
    {
        _activeNavTab = tab;
        UpdateMainContentVisibility();
        UpdateNavTabStyles();
    }

    /// <summary>
    /// Shows the correct main content panel for the active workspace + nav tab state.
    /// Called whenever workspace open/close state or active nav tab changes.
    /// </summary>
    private void UpdateMainContentVisibility()
    {
        var sidebarPanel   = this.FindControl<Border>("SidebarPanel");
        var workspaceGrid  = this.FindControl<Grid>("WorkspaceGrid");
        var welcomeOverlay = this.FindControl<Grid>("WelcomeOverlay");
        var designPanel    = this.FindControl<Grid>("DesignPanel");
        var scanPanel      = this.FindControl<Grid>("ScanPanel");
        var docsPanel      = this.FindControl<Grid>("DocsPanel");

        bool has = _vm.HasWorkspace;

        // Sidebar is always visible once a workspace is loaded, regardless of active nav tab.
        if (sidebarPanel   != null) sidebarPanel.IsVisible   = has;
        if (workspaceGrid  != null) workspaceGrid.IsVisible  = has && _activeNavTab is "Monitor" or "Graph";
        if (welcomeOverlay != null) welcomeOverlay.IsVisible = !has && _activeNavTab is "Monitor" or "Graph" or "Design" or "Docs";
        if (designPanel    != null) designPanel.IsVisible    = has && _activeNavTab == "Design";
        if (scanPanel      != null) scanPanel.IsVisible      = _activeNavTab == "Scan";
        if (docsPanel      != null) docsPanel.IsVisible      = has && _activeNavTab == "Docs";

        // Synchronise the CenterTabControl index when on Monitor or Graph nav tab.
        if (has && workspaceGrid?.IsVisible == true)
        {
            var centerTabs = this.FindControl<TabControl>("CenterTabControl");
            if (centerTabs != null)
                centerTabs.SelectedIndex = _activeNavTab == "Graph" ? 1 : 0;
        }
    }

    private const string NavTabActiveBackground = "#2A4A6A";
    private const string NavTabInactiveForeground = "#88AABB";

    /// <summary>Applies active/inactive visual styling to the five nav tab buttons.</summary>
    private void UpdateNavTabStyles()
    {
        var navTabs = new[]
        {
            ("NavDesignBtn",  "Design"),
            ("NavScanBtn",    "Scan"),
            ("NavMonitorBtn", "Monitor"),
            ("NavGraphBtn",   "Graph"),
            ("NavDocsBtn",    "Docs"),
        };

        foreach (var (name, tabName) in navTabs)
        {
            var btn = this.FindControl<Button>(name);
            if (btn == null) continue;

            if (tabName == _activeNavTab)
            {
                btn.Background = new Avalonia.Media.SolidColorBrush(
                    Avalonia.Media.Color.Parse(NavTabActiveBackground));
                btn.Foreground = new Avalonia.Media.SolidColorBrush(
                    Avalonia.Media.Colors.White);
            }
            else
            {
                btn.Background = Avalonia.Media.Brushes.Transparent;
                btn.Foreground = new Avalonia.Media.SolidColorBrush(
                    Avalonia.Media.Color.Parse(NavTabInactiveForeground));
            }
        }
    }

    /// <summary>Refreshes the workspace name displayed in the top-bar dropdown button and the sidebar.</summary>
    private void UpdateWorkspaceNameDisplay()
    {
        var nameText = this.FindControl<TextBlock>("WorkspaceNameText");
        if (nameText != null)
            nameText.Text = _vm.HasWorkspace ? _vm.Workspace.WorkspaceName : "No Workspace";

        RefreshSidebar();
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
            Title = "Open codomon Workspace Folder",
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

    private async void OnDeleteProfileClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!_vm.HasWorkspace) return;

        var activeProfile = _vm.Workspace.ActiveProfile;
        if (activeProfile == null) return;

        if (_vm.Workspace.Profiles.Count <= 1)
        {
            await ShowErrorAsync("Cannot delete the last profile. At least one profile must remain.");
            return;
        }

        bool confirmed = await ShowConfirmDeleteProfileAsync(activeProfile.ProfileName);
        if (!confirmed) return;

        await ExecuteSafeAsync(() =>
        {
            _vm.DeleteProfile(activeProfile.Id);
            UpdateWindowTitle();
            AppLogger.Info($"Profile deleted: {activeProfile.ProfileName}");
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

    private async Task<bool> ShowInitialUserConfigPromptAsync()
    {
        bool openSettings = false;

        var dialog = new Window
        {
            Title = "First Run Setup",
            Width = 520,
            Height = 220,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#111820"))
        };

        var openBtn = new Button { Content = "Setup LLM", Padding = new Avalonia.Thickness(20, 4) };
        var laterBtn = new Button { Content = "Later", Padding = new Avalonia.Thickness(20, 4) };

        openBtn.Click += (_, _) => { openSettings = true; dialog.Close(); };
        laterBtn.Click += (_, _) => dialog.Close();

        dialog.Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(20),
            Spacing = 16,
            Children =
            {
                new TextBlock
                {
                    Text = "No user settings file was found. Configure your default LLM endpoint and model now?",
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                    Foreground = Avalonia.Media.Brushes.White
                },
                new TextBlock
                {
                    Text = $"Expected path: {UserConfigService.GetConfigFilePath()}",
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                    Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#88AABB")),
                    FontSize = 11
                },
                new StackPanel
                {
                    Orientation = Avalonia.Layout.Orientation.Horizontal,
                    Spacing = 8,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                    Children = { openBtn, laterBtn }
                }
            }
        };

        await dialog.ShowDialog(this);
        return openSettings;
    }

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

    // ── Confirm-delete profile dialog ─────────────────────────────────────────

    private async Task<bool> ShowConfirmDeleteProfileAsync(string profileName)
    {
        bool confirmed = false;

        var dialog = new Window
        {
            Title = "Delete Profile",
            Width = 420,
            Height = 160,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#111820"))
        };

        var deleteBtn = new Button { Content = "Delete", Padding = new Avalonia.Thickness(20, 4) };
        var cancelBtn = new Button { Content = "Cancel", Padding = new Avalonia.Thickness(20, 4) };

        deleteBtn.Click += (_, _) => { confirmed = true; dialog.Close(); };
        cancelBtn.Click += (_, _) => dialog.Close();

        dialog.Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(20),
            Spacing = 16,
            Children =
            {
                new TextBlock
                {
                    Text = $"Delete profile \"{profileName}\"? This cannot be undone.",
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                    Foreground = Avalonia.Media.Brushes.White
                },
                new StackPanel
                {
                    Orientation = Avalonia.Layout.Orientation.Horizontal,
                    Spacing = 8,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                    Children = { deleteBtn, cancelBtn }
                }
            }
        };

        await dialog.ShowDialog(this);
        return confirmed;
    }

    // ── Welcome / Recent Workspaces ──────────────────────────────────────────

    private void PopulateRecentWorkspaces()
    {
        var listBox = this.FindControl<ListBox>("RecentWorkspacesListBox");
        if (listBox == null) return;

        listBox.Items.Clear();

        var entries = RecentWorkspacesService.Load();

        if (entries.Count == 0)
        {
            listBox.Items.Add(new ListBoxItem
            {
                Content = new TextBlock
                {
                    Text = "No recent workspaces. Create or open one to get started.",
                    Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#556677")),
                    FontSize = 13,
                    Margin = new Avalonia.Thickness(8, 12)
                },
                IsEnabled = false
            });
            return;
        }

        foreach (var entry in entries)
        {
            var lastMod = entry.LastModified;
            var lastModText = lastMod == default
                ? "—"
                : lastMod.ToString("yyyy-MM-dd  HH:mm", System.Globalization.CultureInfo.InvariantCulture);

            var item = new ListBoxItem
            {
                Tag = entry,
                Padding = new Avalonia.Thickness(12, 8),
                Content = new StackPanel
                {
                    Spacing = 2,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = entry.WorkspaceName,
                            FontSize = 15,
                            FontWeight = Avalonia.Media.FontWeight.SemiBold,
                            Foreground = Avalonia.Media.Brushes.White
                        },
                        new TextBlock
                        {
                            Text = entry.FolderPath,
                            FontSize = 11,
                            Foreground = new Avalonia.Media.SolidColorBrush(
                                Avalonia.Media.Color.Parse("#778899"))
                        },
                        new TextBlock
                        {
                            Text = $"Last updated: {lastModText}",
                            FontSize = 11,
                            Foreground = new Avalonia.Media.SolidColorBrush(
                                Avalonia.Media.Color.Parse("#556677"))
                        }
                    }
                }
            };
            listBox.Items.Add(item);
        }
    }

    private async void OnRecentWorkspaceSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count == 0 || e.AddedItems[0] is not ListBoxItem item) return;
        if (item.Tag is not RecentWorkspaceEntry entry) return;

        // Clear selection so the list doesn't stay highlighted.
        if (sender is ListBox lb) lb.SelectedItem = null;

        if (!System.IO.Directory.Exists(entry.FolderPath))
        {
            var remove = await ShowRemoveStaleRecentAsync(entry.WorkspaceName);
            if (remove)
            {
                RecentWorkspacesService.Remove(entry.FolderPath);
                PopulateRecentWorkspaces();
            }
            return;
        }

        await ExecuteSafeAsync(async () =>
        {
            await _vm.OpenWorkspaceAsync(entry.FolderPath);
            UpdateWindowTitle();
            AppLogger.Info($"Workspace opened from recent list: {entry.FolderPath}");
        });
    }

    private async Task<bool> ShowRemoveStaleRecentAsync(string workspaceName)
    {
        bool remove = false;

        var dialog = new Window
        {
            Title = "Workspace Not Found",
            Width = 440,
            Height = 180,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#111820"))
        };

        var removeBtn = new Button { Content = "Remove from List", Padding = new Avalonia.Thickness(20, 4) };
        var cancelBtn = new Button { Content = "Keep",             Padding = new Avalonia.Thickness(20, 4) };

        removeBtn.Click += (_, _) => { remove = true; dialog.Close(); };
        cancelBtn.Click += (_, _) => dialog.Close();

        dialog.Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(20),
            Spacing = 16,
            Children =
            {
                new TextBlock
                {
                    Text = $"The workspace \"{workspaceName}\" could not be found on disk. Remove it from the recent list?",
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                    Foreground = Avalonia.Media.Brushes.White
                },
                new StackPanel
                {
                    Orientation = Avalonia.Layout.Orientation.Horizontal,
                    Spacing = 8,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                    Children = { removeBtn, cancelBtn }
                }
            }
        };

        await dialog.ShowDialog(this);
        return remove;
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

    // ── Log import + replay handlers ─────────────────────────────────────────

    /// <summary>
    /// Unsubscribes from the previously tracked <see cref="LogReplayViewModel"/> (if any)
    /// and subscribes to <paramref name="replay"/>.
    /// </summary>
    private void SubscribeToLogReplay(LogReplayViewModel replay)
    {
        if (_subscribedReplay != null)
        {
            _subscribedReplay.PropertyChanged -= OnLogReplayPropertyChanged;
            _subscribedReplay.EntryActivated  -= OnLogEntryActivated;
        }
        _subscribedReplay = replay;
        _subscribedReplay.PropertyChanged += OnLogReplayPropertyChanged;
        _subscribedReplay.EntryActivated  += OnLogEntryActivated;
    }

    private async void OnImportLogClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!_vm.HasWorkspace)
        {
            await ShowErrorAsync("Please open or create a workspace before importing a log file.");
            return;
        }

        var wizard = new ImportWizardDialog();
        var result = await wizard.ShowDialog<ImportWizardViewModel?>(this);
        if (result == null) return;   // user cancelled the wizard

        await ExecuteSafeAsync(async () =>
        {
            await _vm.ImportLogsWithOptionsAsync(result.FilePath, result.BuildImportOptions());
            _logListShowingLive = false;
            RefreshLogReplayPanel();
            RefreshLiveMonitorPanel();
        });
    }

    private void OnReplayPlayClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => _vm.LogReplay.Play();

    private void OnReplayPauseClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => _vm.LogReplay.Pause();

    private void OnReplayStopClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => _vm.LogReplay.Stop();

    private void OnReplaySpeedChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox combo) return;
        if (combo.SelectedItem is not ComboBoxItem item) return;
        if (double.TryParse(item.Tag?.ToString(), out var speed))
            _vm.LogReplay.SpeedMultiplier = speed;
    }

    private void OnLogReplayPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LogReplayViewModel.IsPlaying) ||
            e.PropertyName == nameof(LogReplayViewModel.CurrentIndex))
        {
            RefreshLogReplayPanel();
            UpdateTimelineCursor();
            RefreshLiveMonitorPanel();
        }
    }

    private void OnLogEntryActivated(LogEntryModel entry)
    {
        if (_canvas == null) return;

        var match = LogMatcher.Match(entry, _vm.Workspace);

        if (match.Strength == MatchStrength.ModuleExact &&
            match.Module != null && match.System != null &&
            match.Module.IsVisible)
        {
            _canvas.HighlightModule(match.Module.Id, match.System.Id);
        }
        else if (match.Strength == MatchStrength.SystemOnly &&
                 match.System != null && match.System.IsVisible)
        {
            _canvas.HighlightSystem(match.System.Id);
        }

        // Show which rule/reason caused the match in the properties panel.
        ShowMatchInfo(match);

        // Scroll the log list to the current entry.
        var listBox = this.FindControl<ListBox>("ImportedLogsListBox");
        if (listBox != null)
        {
            var idx = _vm.LogReplay.CurrentIndex;
            if (idx >= 0 && idx < listBox.ItemCount)
            {
                var item = listBox.Items[idx];
                if (item != null)
                    listBox.ScrollIntoView(item);
            }
        }
    }

    /// <summary>
    /// Populates the ImportedLogsListBox and synchronises the replay toolbar state
    /// (button enabled states, status text).
    /// </summary>
    private void RefreshLogReplayPanel()
    {
        // When live monitoring is active, the log list belongs to the live monitor.
        if (_logListShowingLive) return;

        var replay   = _vm.LogReplay;
        var listBox  = this.FindControl<ListBox>("ImportedLogsListBox");
        var playBtn  = this.FindControl<Button>("ReplayPlayButton");
        var pauseBtn = this.FindControl<Button>("ReplayPauseButton");
        var stopBtn  = this.FindControl<Button>("ReplayStopButton");
        var statusTb = this.FindControl<TextBlock>("ReplayStatusText");

        bool hasEntries = replay.Entries.Count > 0;

        if (playBtn  != null) playBtn.IsEnabled  = hasEntries && !_vm.LiveMonitor.IsWatching;
        if (pauseBtn != null) pauseBtn.IsEnabled = replay.IsPlaying;
        if (stopBtn  != null) stopBtn.IsEnabled  = hasEntries;

        if (statusTb != null)
        {
            if (!hasEntries)
                statusTb.Text = "No log loaded";
            else if (replay.IsPlaying)
                statusTb.Text = $"Replaying… {replay.CurrentIndex + 1} / {replay.Entries.Count}";
            else if (replay.CurrentIndex < 0)
                statusTb.Text = $"{replay.Entries.Count} entries — press ▶ to replay";
            else
                statusTb.Text = $"Paused at {replay.CurrentIndex + 1} / {replay.Entries.Count}";
        }

        // (Re-)bind the log list if the entry set has changed.
        if (listBox != null && !ReferenceEquals(listBox.ItemsSource, replay.Entries))
        {
            listBox.ItemsSource = null;  // force reset
            listBox.ItemTemplate = BuildLogItemTemplate();
            listBox.ItemsSource = replay.Entries;

            // Entries have changed — rebuild the timeline and refresh sidebar log count.
            RebuildTimeline();
            RefreshSidebar();
        }
    }

    // ── Log column widths (must match the header in MainWindow.axaml) ─────────
    private const int LogColTime   = 80;
    private const int LogColLevel  = 52;
    private const int LogColSource = 140;

    // ── Log row background colours by level ──────────────────────────────────
    private static readonly Avalonia.Media.Color LogRowBgError   = Avalonia.Media.Color.Parse("#1A0808");
    private static readonly Avalonia.Media.Color LogRowBgWarn    = Avalonia.Media.Color.Parse("#1A1408");
    private static readonly Avalonia.Media.Color LogRowBgDefault = Avalonia.Media.Color.FromArgb(0, 0, 0, 0);

    private static Avalonia.Controls.Templates.FuncDataTemplate<LogEntryModel> BuildLogItemTemplate()
    {
        return new Avalonia.Controls.Templates.FuncDataTemplate<LogEntryModel>((entry, _) =>
        {
            if (entry == null) return new TextBlock();

            var levelUpper = entry.Level.ToUpperInvariant();

            // Row background tinted by level.
            var rowBg = levelUpper switch
            {
                "ERROR" => LogRowBgError,
                "WARN"  => LogRowBgWarn,
                _       => LogRowBgDefault
            };

            var levelColor = Avalonia.Media.Color.Parse(entry.LevelColor);

            var timeText = new TextBlock
            {
                Text         = entry.Timestamp?.ToString("HH:mm:ss.fff") ?? "—",
                FontFamily   = new Avalonia.Media.FontFamily("Monospace"),
                FontSize     = 10,
                Foreground   = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#667788")),
                Padding      = new Avalonia.Thickness(4, 1),
                TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis,
                Width        = LogColTime
            };

            var levelText = new TextBlock
            {
                Text       = levelUpper,
                FontFamily = new Avalonia.Media.FontFamily("Monospace"),
                FontSize   = 10,
                Foreground = new Avalonia.Media.SolidColorBrush(levelColor),
                Padding    = new Avalonia.Thickness(4, 1),
                Width      = LogColLevel
            };

            var sourceText = new TextBlock
            {
                Text         = entry.Source,
                FontFamily   = new Avalonia.Media.FontFamily("Monospace"),
                FontSize     = 10,
                Foreground   = new Avalonia.Media.SolidColorBrush(
                    levelUpper == "INFO"
                        ? Avalonia.Media.Color.Parse("#66CC88")
                        : levelColor),
                Padding      = new Avalonia.Thickness(4, 1),
                TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis,
                Width        = LogColSource
            };

            var msgText = new TextBlock
            {
                Text         = entry.IsParsed ? entry.Message : entry.RawLine,
                FontFamily   = new Avalonia.Media.FontFamily("Monospace"),
                FontSize     = 10,
                Foreground   = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#AABBCC")),
                Padding      = new Avalonia.Thickness(4, 1),
                TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis
            };

            var grid = new Grid
            {
                ColumnDefinitions = new Avalonia.Controls.ColumnDefinitions($"{LogColTime},{LogColLevel},{LogColSource},*"),
                Background = new Avalonia.Media.SolidColorBrush(rowBg)
            };
            Grid.SetColumn(timeText,   0);
            Grid.SetColumn(levelText,  1);
            Grid.SetColumn(sourceText, 2);
            Grid.SetColumn(msgText,    3);
            grid.Children.Add(timeText);
            grid.Children.Add(levelText);
            grid.Children.Add(sourceText);
            grid.Children.Add(msgText);

            return grid;
        });
    }

    private void SetupReplaySpeedComboBox()
    {
        var combo = this.FindControl<ComboBox>("ReplaySpeedComboBox");
        if (combo == null) return;

        var speeds = new[] { ("0.5×", 0.5), ("1×", 1.0), ("2×", 2.0), ("4×", 4.0), ("8×", 8.0) };
        foreach (var (label, value) in speeds)
        {
            combo.Items.Add(new ComboBoxItem
            {
                Content = label,
                Tag = value.ToString(System.Globalization.CultureInfo.InvariantCulture)
            });
        }

        // Select the speed closest to the user's default preference.
        var defaultSpeed = UserConfigService.Load().DefaultReplaySpeed;
        int bestIndex = 1; // fallback: 1×
        double bestDiff = double.MaxValue;
        for (int i = 0; i < speeds.Length; i++)
        {
            var diff = Math.Abs(speeds[i].Item2 - defaultSpeed);
            if (diff < bestDiff) { bestDiff = diff; bestIndex = i; }
        }
        combo.SelectedIndex = bestIndex;
    }

    // ── Live log monitoring handlers ──────────────────────────────────────────

    /// <summary>
    /// Unsubscribes from the previously tracked <see cref="LiveMonitorViewModel"/> (if any)
    /// and subscribes to <paramref name="monitor"/>.
    /// </summary>
    private void SubscribeToLiveMonitor(LiveMonitorViewModel monitor)
    {
        if (_subscribedMonitor != null)
        {
            _subscribedMonitor.PropertyChanged -= OnLiveMonitorPropertyChanged;
            _subscribedMonitor.EntryArrived    -= OnLiveEntryArrived;
            _subscribedMonitor.EntriesFlushed  -= OnLiveEntriesFlushed;
        }
        _subscribedMonitor = monitor;
        _subscribedMonitor.PropertyChanged += OnLiveMonitorPropertyChanged;
        _subscribedMonitor.EntryArrived    += OnLiveEntryArrived;
        _subscribedMonitor.EntriesFlushed  += OnLiveEntriesFlushed;
    }

    private async void OnWatchLogClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!_vm.HasWorkspace)
        {
            await ShowErrorAsync("Please open or create a workspace before starting live monitoring.");
            return;
        }

        var storageProvider = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storageProvider == null) return;

        // Start picker in the last-browsed folder when one is remembered.
        IStorageFolder? suggestedFolder = null;
        var lastFolder = _vm.Workspace.LastBrowsedFolder;
        if (!string.IsNullOrEmpty(lastFolder) && System.IO.Directory.Exists(lastFolder))
        {
            try
            {
                suggestedFolder = await storageProvider.TryGetFolderFromPathAsync(lastFolder);
            }
            catch { /* Ignore — fall back to the default starting location. */ }
        }

        var files = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select Log File to Watch",
            AllowMultiple = false,
            SuggestedStartLocation = suggestedFolder,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Log files") { Patterns = new[] { "*.log", "*.txt", "*.csv" } },
                new FilePickerFileType("All files") { Patterns = new[] { "*" } }
            }
        });

        if (files.Count == 0) return;

        var filePath = files[0].Path.LocalPath;

        await ExecuteSafeAsync(() =>
        {
            _vm.StartLiveMonitoring(filePath);
            // Switch the log list to show live entries.
            _logListShowingLive = true;
            _logStreamPaused    = false;
            _liveMonitorStartTime = DateTimeOffset.UtcNow;
            BindLogListToLiveMonitor();
            RefreshLiveMonitorPanel();
            return Task.CompletedTask;
        });
    }

    private void OnStopWatchClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _vm.StopLiveMonitoring();
        _liveMonitorStartTime = null;
        _logStreamPaused      = false;
        // Keep the log list on live entries so the user can review what arrived.
        RefreshLiveMonitorPanel();
    }

    private void OnLiveMonitorPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(LiveMonitorViewModel.IsWatching)
                           or nameof(LiveMonitorViewModel.ErrorMessage)
                           or nameof(LiveMonitorViewModel.WatchedFilePath))
        {
            RefreshLiveMonitorPanel();
        }
    }

    private void OnLiveEntryArrived(LogEntryModel entry)
    {
        if (_canvas == null) return;

        var match = LogMatcher.Match(entry, _vm.Workspace);

        if (match.Strength == MatchStrength.ModuleExact &&
            match.Module != null && match.System != null &&
            match.Module.IsVisible)
        {
            _canvas.HighlightModule(match.Module.Id, match.System.Id);
        }
        else if (match.Strength == MatchStrength.SystemOnly &&
                 match.System != null && match.System.IsVisible)
        {
            _canvas.HighlightSystem(match.System.Id);
        }

        ShowMatchInfo(match);
    }

    private void OnLiveEntriesFlushed()
    {
        // Update event count in the status bar.
        _vm.TotalEventCount = _vm.LiveMonitor.Entries.Count;

        // Update streaming indicator label with live count.
        var indicatorText = this.FindControl<TextBlock>("LogStreamIndicatorText");
        if (indicatorText != null && _vm.LiveMonitor.IsWatching)
        {
            var pausedSuffix = _logStreamPaused ? " (paused)" : string.Empty;
            indicatorText.Text = $"● Streaming…  {_vm.LiveMonitor.Entries.Count:N0} lines{pausedSuffix}";
        }

        // Auto-scroll to the latest entry unless stream is paused.
        if (!_logStreamPaused)
        {
            var listBox = this.FindControl<ListBox>("ImportedLogsListBox");
            if (listBox != null && listBox.ItemCount > 0)
            {
                var last = listBox.Items[listBox.ItemCount - 1];
                if (last != null)
                    listBox.ScrollIntoView(last);
            }
        }

        // Rebuild the timeline at most once every 2 seconds to avoid hammering it.
        var now = DateTimeOffset.UtcNow;
        if ((now - _lastLiveTimelineRebuild).TotalSeconds >= LiveTimelineRebuildThrottleSeconds)
        {
            _lastLiveTimelineRebuild = now;
            RebuildLiveTimeline();
        }
    }

    private void BindLogListToLiveMonitor()
    {
        var listBox = this.FindControl<ListBox>("ImportedLogsListBox");
        if (listBox == null) return;

        listBox.ItemsSource = null;
        listBox.ItemTemplate = BuildLogItemTemplate();
        listBox.ItemsSource = _vm.LiveMonitor.Entries;
    }

    /// <summary>Synchronises the Watch/Stop button states and streaming indicator.</summary>
    private void RefreshLiveMonitorPanel()
    {
        var watchBtn      = this.FindControl<Button>("WatchLogButton");
        var stopBtn       = this.FindControl<Button>("StopWatchButton");
        var pauseBtn      = this.FindControl<Button>("LogStreamPauseBtn");
        var indicatorBorder = this.FindControl<Border>("LogStreamIndicatorBorder");
        var indicatorText   = this.FindControl<TextBlock>("LogStreamIndicatorText");
        // Legacy hidden control (still updated for safety).
        var statusTb      = this.FindControl<TextBlock>("WatchStatusText");

        bool hasWorkspace  = _vm.HasWorkspace;
        bool isWatching    = _vm.LiveMonitor.IsWatching;
        bool replayPlaying = _vm.LogReplay.IsPlaying;

        if (watchBtn != null) watchBtn.IsEnabled = hasWorkspace && !replayPlaying;
        if (stopBtn  != null) stopBtn.IsEnabled  = isWatching;
        if (pauseBtn != null)
        {
            pauseBtn.IsEnabled = isWatching || (_logListShowingLive && _vm.LiveMonitor.Entries.Count > 0);
            pauseBtn.Content   = _logStreamPaused ? "▶ Resume" : "⏸ Pause";
        }

        // Update streaming indicator.
        if (indicatorText != null && indicatorBorder != null)
        {
            if (_vm.LiveMonitor.HasError)
            {
                indicatorText.Text       = $"⚠ Error";
                indicatorText.Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#FF7070"));
                indicatorBorder.Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#2A0F0F"));
            }
            else if (isWatching)
            {
                var pausedSuffix = _logStreamPaused ? " (paused)" : string.Empty;
                indicatorText.Text       = $"● Streaming…  {_vm.LiveMonitor.Entries.Count:N0} lines{pausedSuffix}";
                indicatorText.Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#44CC44"));
                indicatorBorder.Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#152A15"));
            }
            else if (_logListShowingLive && _vm.LiveMonitor.Entries.Count > 0)
            {
                indicatorText.Text       = $"○ Stopped  ({_vm.LiveMonitor.Entries.Count:N0} lines)";
                indicatorText.Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#778899"));
                indicatorBorder.Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#0F1E0F"));
            }
            else
            {
                indicatorText.Text       = "○ Idle";
                indicatorText.Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#556677"));
                indicatorBorder.Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#0F1E0F"));
            }
        }

        // Update legacy hidden WatchStatusText for any remaining code paths that read it.
        if (statusTb != null)
            statusTb.Text = isWatching ? $"● {_vm.LiveMonitor.WatchedFilePath}" : "Not watching";

        // Update top-bar running indicator.
        var runIndicator = this.FindControl<Border>("RunningIndicatorBorder");
        var topStopBtn   = this.FindControl<Button>("TopBarStopBtn");
        if (runIndicator != null) runIndicator.IsVisible = isWatching;
        if (topStopBtn   != null) topStopBtn.IsVisible   = isWatching;

        RefreshStatusBar();
    }

    private async void RebuildLiveTimeline()
    {
        if (!_vm.HasWorkspace) return;
        await _vm.Timeline.BuildAsync(_vm.LiveMonitor.Entries, _vm.Workspace);
    }

    private void OnLogStreamPauseClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _logStreamPaused = !_logStreamPaused;
        RefreshLiveMonitorPanel();
    }

    /// <summary>Refreshes all status-bar controls from the current view-model state.</summary>
    private void RefreshStatusBar()
    {
        // ── Scan-status dot + label ──────────────────────────────────────────
        var scanDot   = this.FindControl<TextBlock>("StatusScanDot");
        var scanLabel = this.FindControl<TextBlock>("StatusScanLabel");
        if (scanDot != null && scanLabel != null)
        {
            switch (_vm.ScanStatus)
            {
                case ScanStatusKind.Completed:
                    scanDot.Foreground   = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#44CC44"));
                    scanLabel.Text       = "Scan: Completed";
                    scanLabel.Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#556677"));
                    break;
                case ScanStatusKind.InProgress:
                    scanDot.Foreground   = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#FFAA44"));
                    scanLabel.Text       = "Scan: Scanning…";
                    scanLabel.Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#FFAA44"));
                    break;
                default:
                    scanDot.Foreground   = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#3A4A5A"));
                    scanLabel.Text       = "Idle";
                    scanLabel.Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#556677"));
                    break;
            }
        }

        // ── Right metric pills ───────────────────────────────────────────────
        bool hasScanStats = _vm.FileCount > 0 || _vm.ClassCount > 0;

        SetStatusPill("StatusBarFilesText",   hasScanStats, $"Files: {_vm.FileCount:N0}");
        SetStatusPill("StatusBarSep1",        hasScanStats, " | ");
        SetStatusPill("StatusBarClassesText", hasScanStats, $"Classes: {_vm.ClassCount:N0}");
        SetStatusPill("StatusBarSep2",        hasScanStats, " | ");
        SetStatusPill("StatusBarMethodsText", hasScanStats, $"Methods: {_vm.MethodCount:N0}");
        SetStatusPill("StatusBarSep3",        hasScanStats, " | ");
        SetStatusPill("StatusBarLogPtsText",  hasScanStats, $"Log Points: {_vm.LogPointCount:N0}");

        bool isWatching = _vm.LiveMonitor.IsWatching;

        if (isWatching && _liveMonitorStartTime.HasValue)
        {
            var elapsed = DateTimeOffset.UtcNow - _liveMonitorStartTime.Value;
            var runtimeStr = elapsed.TotalHours >= 1
                ? $"{(int)elapsed.TotalHours:D2}:{elapsed.Minutes:D2}:{elapsed.Seconds:D2}"
                : $"{elapsed.Minutes:D2}:{elapsed.Seconds:D2}";
            SetStatusPill("StatusBarSep4",       true, " | ");
            SetStatusPill("StatusBarRuntimeText", true, $"Runtime: {runtimeStr}");
        }
        else
        {
            SetStatusPill("StatusBarSep4",        false, string.Empty);
            SetStatusPill("StatusBarRuntimeText", false, string.Empty);
        }

        bool hasEvents = _vm.TotalEventCount > 0;
        SetStatusPill("StatusBarSep5",       hasEvents, " | ");
        SetStatusPill("StatusBarEventsText", hasEvents, $"Events: {_vm.TotalEventCount:N0}");
    }

    private void SetStatusPill(string controlName, bool visible, string text)
    {
        var tb = this.FindControl<TextBlock>(controlName);
        if (tb == null) return;
        tb.IsVisible = visible;
        if (visible) tb.Text = text;
    }

    // ── Roslyn Scan ───────────────────────────────────────────────────────────

    private async void OnScanClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!_vm.HasWorkspace)
        {
            await ShowErrorAsync("Please open or create a workspace before running a Roslyn scan.");
            return;
        }

        var scanVm = new ViewModels.RoslynScanViewModel(
            _vm.Workspace.SourceProjectPath,
            _vm.WorkspaceFolderPath,
            _vm.Workspace);

        var dialog = new RoslynScanDialog(scanVm);
        var result = await dialog.ShowDialog<ViewModels.RoslynScanViewModel?>(this);

        AppLogger.Debug($"[Roslyn] Dialog closed. result={(result == null ? "null (unexpected)" : "RoslynScanViewModel")}  " +
                        $"PromotedConnections={(result?.PromotedConnections.Count.ToString() ?? "n/a")}  " +
                        $"WasAddedToCanvas={(result?.WasAddedToCanvas.ToString() ?? "n/a")}");

        if (result == null) return;

        await ExecuteSafeAsync(async () =>
        {
            // Individual "Promote" connections from the results view are still
            // added to WorkspaceModel.Connections for backward compatibility.
            if (result.PromotedConnections.Count > 0)
            {
                AppLogger.Debug($"[Roslyn] Calling AddRoslynConnections with {result.PromotedConnections.Count} connection(s).");
                _vm.AddRoslynConnections(result.PromotedConnections);
                RefreshRoslynConnectionsPanel();
            }

            // "Import to System Map" pushes scan results through SystemDetector
            // and upserts project-level systems into the System Map, then
            // refreshes the Graph from the updated System Map data.
            if (result.WasAddedToCanvas && result.ScanResult != null)
            {
                AppLogger.Debug("[Roslyn] Applying scan results to System Map via SystemDetector.");
                await _vm.ApplyRoslynScanAsync(result.ScanResult);
                AppLogger.Debug("[Roslyn] System Map updated. Graph refreshed from System Map.");
                SetupTreeView();
                RefreshSidebar();
            }
            else if (result.PromotedConnections.Count > 0)
            {
                // Promoted class-level connections only (legacy "Promote" per-connection flow).
                // GraphViewModel.Refresh will use System Map data when it is populated, so
                // these workspace-level connections are intentionally not shown in the graph
                // once a System Map exists — only system-level relationships appear there.
                _vm.Graph.Refresh(_vm.Workspace);
            }
        });
    }

    // ── LLM Summaries ─────────────────────────────────────────────────────────

    private async void OnSummariesClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!_vm.HasWorkspace)
        {
            await ShowErrorAsync("Please open or create a workspace before using LLM Summaries.");
            return;
        }

        var summaryVm = new ViewModels.LlmSummaryViewModel(
            _vm.Workspace,
            _vm.WorkspaceFolderPath);

        var dialog = new LlmSummaryDialog(summaryVm);
        await dialog.ShowDialog(this);

        // Persist any settings changes the user made in the dialog.
        if (_vm.Workspace.LlmSettings.ApiEndpoint != summaryVm.ApiEndpoint ||
            _vm.Workspace.LlmSettings.ModelName   != summaryVm.ModelName)
        {
            summaryVm.SaveSettings();
            _vm.IsDirty = true;
        }
    }

    // ── Architecture Hypothesis ───────────────────────────────────────────────

    private async void OnHypothesisClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!_vm.HasWorkspace)
        {
            await ShowErrorAsync("Please open or create a workspace before using Architecture Hypothesis.");
            return;
        }

        var systemMapBefore = DescribeSystemMap(_vm.Workspace.SystemMap);
        AppLogger.Debug($"[Hypothesis] Opening Architecture dialog. Current System Map: {systemMapBefore}");

        var hypothesisVm = new ViewModels.ArchitectureHypothesisViewModel(
            _vm.Workspace,
            _vm.WorkspaceFolderPath);

        var dialog = new ArchitectureHypothesisDialog(hypothesisVm);
        await dialog.ShowDialog(this);

        var systemMapAfter = DescribeSystemMap(_vm.Workspace.SystemMap);
        AppLogger.Debug($"[Hypothesis] Architecture dialog closed. AcceptedCount={hypothesisVm.AcceptedCount}; AppliedSuggestionCount={hypothesisVm.AppliedSuggestionCount}; HasCanvasChanges={hypothesisVm.HasCanvasChanges}; System Map before='{systemMapBefore}'; after='{systemMapAfter}'.");

        // Sync both views that can render the System Map after the dialog mutates workspace state.
        AppLogger.Debug("[Hypothesis] Applying Architecture dialog results to System Map view model.");
        _vm.SystemMap.LoadFrom(_vm.Workspace.SystemMap, _vm.Workspace.ActiveProfile?.LayoutPositions);
        AppLogger.Debug("[Hypothesis] System Map view model refresh completed. Applying graph canvas refresh.");
        _vm.Graph.RefreshFromSystemMap(_vm.Workspace.SystemMap);
        AppLogger.Debug("[Hypothesis] Graph canvas refresh completed.");

        // Mark workspace dirty for accepted or cleared changes.
        if (hypothesisVm.HasCanvasChanges || !string.Equals(systemMapBefore, systemMapAfter, StringComparison.Ordinal))
            _vm.IsDirty = true;

        // Re-apply the latest saved Roslyn scan (if any) so that actual code-analysis
        // data — modules, code nodes, relationships — flows into the hypothesis-defined
        // systems.  This enriches the canvas without requiring the user to manually
        // re-run the scan after every Architecture hypothesis pass.
        if (hypothesisVm.HasCanvasChanges && !string.IsNullOrWhiteSpace(_vm.WorkspaceFolderPath))
        {
            // ListSavedScans returns results in descending chronological order,
            // so index 0 is always the most recent scan.
            var savedScans = RoslynScanService.ListSavedScans(_vm.WorkspaceFolderPath);
            if (savedScans.Any())
            {
                AppLogger.Info("[Hypothesis] Re-applying latest saved Roslyn scan to coordinate code analysis with hypothesis systems.");
                await ExecuteSafeAsync(async () =>
                {
                    var latestScan = await RoslynScanService.LoadAsync(savedScans[0].FilePath);
                    if (latestScan != null)
                    {
                        await _vm.ApplyRoslynScanAsync(latestScan);
                        AppLogger.Info("[Hypothesis] Roslyn scan re-applied — canvas enriched with code analysis data.");
                    }
                });
            }
        }

        AppLogger.Debug($"[Hypothesis] Apply-to-canvas flow finished. IsDirty={_vm.IsDirty}; Final System Map: {DescribeSystemMap(_vm.Workspace.SystemMap)}");
        SetupTreeView();
        RefreshSidebar();
    }

    private static string DescribeSystemMap(SystemMapModel map)
        => $"Systems={map.Systems.Count}, Modules={map.AllModules.Count()}, CodeNodes={map.AllCodeNodes.Count()}, ExternalSystems={map.ExternalSystems.Count}, Relationships={map.Relationships.Count}";

    // ── Connections panel (Roslyn-origin connections) ─────────────────────────

    /// <summary>
    /// Rebuilds the Connections tab list, showing all workspace connections with
    /// Roslyn-origin ones clearly badged as read-only.
    /// </summary>
    private void RefreshRoslynConnectionsPanel()
    {
        var listBox = this.FindControl<ListBox>("ConnectionsListBox");
        if (listBox == null) return;

        listBox.Items.Clear();

        foreach (var conn in _vm.Workspace.Connections)
        {
            var isRoslyn = conn.Origin == Models.ConnectionOrigin.Roslyn;
            var badge = isRoslyn
                ? (conn.IsReadOnly ? " [ROSLYN · read-only]" : " [ROSLYN · promoted]")
                : string.Empty;

            listBox.Items.Add(new ListBoxItem
            {
                Tag = conn,
                Padding = new Avalonia.Thickness(8, 4),
                Content = new StackPanel
                {
                    Orientation = Avalonia.Layout.Orientation.Horizontal,
                    Spacing = 8,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = conn.Name,
                            Foreground = Avalonia.Media.Brushes.LightGray,
                            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
                        },
                        new Border
                        {
                            IsVisible = isRoslyn,
                            Background = new Avalonia.Media.SolidColorBrush(
                                Avalonia.Media.Color.Parse("#1A3A5A")),
                            CornerRadius = new Avalonia.CornerRadius(3),
                            Padding = new Avalonia.Thickness(4, 1),
                            Child = new TextBlock
                            {
                                Text = conn.IsReadOnly ? "ROSLYN · read-only" : "ROSLYN · promoted",
                                FontSize = 9,
                                Foreground = new Avalonia.Media.SolidColorBrush(
                                    conn.IsReadOnly
                                        ? Avalonia.Media.Color.Parse("#4A9FBF")
                                        : Avalonia.Media.Color.Parse("#4ABF7A")),
                                FontWeight = Avalonia.Media.FontWeight.Bold,
                                LetterSpacing = 1
                            }
                        }
                    }
                }
            });
        }

        if (_vm.Workspace.Connections.Count == 0)
        {
            listBox.Items.Add(new ListBoxItem
            {
                IsEnabled = false,
                Content = new TextBlock
                {
                    Text = "No connections yet. Run a Roslyn scan or add connections manually.",
                    Foreground = new Avalonia.Media.SolidColorBrush(
                        Avalonia.Media.Color.Parse("#556677")),
                    FontSize = 12,
                    Margin = new Avalonia.Thickness(8, 6)
                }
            });
        }
    }

    private void OnConnectionsListSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        var promoteBtn = this.FindControl<Button>("PromoteConnectionButton");
        if (promoteBtn == null) return;

        if (e.AddedItems.Count > 0 && e.AddedItems[0] is ListBoxItem item &&
            item.Tag is Models.ConnectionModel conn)
        {
            promoteBtn.IsEnabled = conn.Origin == Models.ConnectionOrigin.Roslyn && conn.IsReadOnly;
        }
        else
        {
            promoteBtn.IsEnabled = false;
        }
    }

    private void OnPromoteConnectionClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var listBox = this.FindControl<ListBox>("ConnectionsListBox");
        if (listBox?.SelectedItem is not ListBoxItem item) return;
        if (item.Tag is not Models.ConnectionModel conn) return;

        _vm.PromoteConnectionToManual(conn.Id);
        RefreshRoslynConnectionsPanel();
    }

    // ── Dev Console ───────────────────────────────────────────────────────────

    private void OnDevConsoleClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
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

    private async void OnSettingsClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var dialog = new UserSettingsDialog();
        await dialog.ShowDialog(this);
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
        var baseTitle = $"codomon {BuildInfo.AppVersion}  (build {BuildInfo.BuildDate})";
        if (_vm.HasWorkspace)
        {
            var dirty = _vm.IsDirty ? " *" : string.Empty;
            Title = $"{baseTitle}  —  {_vm.Workspace.WorkspaceName}{dirty}";
        }
        else
        {
            Title = baseTitle;
        }
    }

    // ── Canvas / TreeView ────────────────────────────────────────────────────

    private void SetupCanvas()
    {
        _canvas = new MainCanvasControl(_vm.Workspace, _vm.Selection);
        _canvas.OnLayoutChanged = () => _vm.IsDirty = true;

        var graphView = new GraphView
        {
            DataContext = _vm.Graph
        };

        var host = this.FindControl<ContentControl>("CanvasHost");
        if (host != null)
            host.Content = graphView;

        SetupSystemMapView();

        AppLogger.Debug("Graph canvas (Nodify) initialized");
    }

    private void SetupSystemMapView()
    {
        var host = this.FindControl<ContentControl>("SystemMapHost");
        if (host == null) return;

        var view = new SystemMapView(_vm.SystemMap);
        view.ShowDetailedRelationshipsRequested += OnShowDetailedRelationshipsRequested;
        view.ClearCanvasRequested += OnSystemMapClearCanvasRequested;
        view.LayoutPositionChanged += OnSystemMapLayoutPositionChanged;
        view.RemoveRelationshipRequested += OnSystemMapRemoveRelationshipRequested;
        host.Content = view;
        AppLogger.Debug("System Map view initialized");
    }

    private void OnSystemMapLayoutPositionChanged(string itemId, bool isExternal, double x, double y)
    {
        if (!_vm.HasWorkspace) return;
        _vm.SaveSystemMapLayoutPosition(itemId, isExternal, x, y);
    }

    private void OnShowDetailedRelationshipsRequested(SystemItemVm system)
    {
        if (!_vm.HasWorkspace) return;

        AppLogger.Debug($"[SystemMap] Opening module relationship graph for system '{system.Name}' ({system.Id}).");
        _vm.Graph.RefreshModuleRelationshipsForSystem(_vm.Workspace.SystemMap, system.Id);

        // Switch to the Graph nav tab (which sets CenterTabControl to the Graph sub-tab).
        SetActiveNavTab("Graph");
    }

    private void OnSystemMapClearCanvasRequested()
    {
        if (!_vm.HasWorkspace) return;

        AppLogger.Info("[SystemMap] Clear Canvas requested from System Overview.");

        _vm.Workspace.SystemMap.Systems.Clear();
        _vm.Workspace.SystemMap.Modules.Clear();
        _vm.Workspace.SystemMap.ExternalSystems.Clear();
        _vm.Workspace.SystemMap.Relationships.Clear();

        _vm.SystemMap.LoadFrom(_vm.Workspace.SystemMap, _vm.Workspace.ActiveProfile?.LayoutPositions);
        _vm.Graph.RefreshFromSystemMap(_vm.Workspace.SystemMap);
        _vm.IsDirty = true;

        AppLogger.Debug("[SystemMap] Canvas cleared and views refreshed.");
    }

    private void OnSystemMapRemoveRelationshipRequested(string relationshipId)
    {
        if (!_vm.HasWorkspace) return;

        var map = _vm.Workspace.SystemMap;
        var rel = map.Relationships.FirstOrDefault(r =>
            string.Equals(r.Id, relationshipId, StringComparison.Ordinal));

        if (rel == null)
        {
            AppLogger.Warn($"[SystemMap] RemoveRelationship: relationship '{relationshipId}' not found. Skipped.");
            return;
        }

        // Record the override so it survives re-analysis.
        var override_ = new ManualOverrideModel
        {
            Id       = Guid.NewGuid().ToString(),
            Type     = ManualOverrideType.RemoveRelationship,
            TargetId = relationshipId,
        };
        map.ManualOverrides.Add(override_);
        map.Relationships.Remove(rel);

        AppLogger.Info($"[SystemMap] Relationship '{rel.FromId}' → '{rel.ToId}' ({rel.Kind}) removed via ManualOverride.");

        _vm.SystemMap.LoadFrom(map, _vm.Workspace.ActiveProfile?.LayoutPositions);
        _vm.Graph.RefreshFromSystemMap(map);
        _vm.IsDirty = true;
    }

    // ── Timeline ──────────────────────────────────────────────────────────────

    private void SetupTimeline()
    {
        _timelineControl = new TimelineControl(_vm.Timeline);
        _timelineControl.BucketSelected += OnTimelineBucketSelected;

        var host = this.FindControl<ContentControl>("TimelineHost");
        if (host != null)
            host.Content = _timelineControl;
    }

    private async void RebuildTimeline()
    {
        if (!_vm.HasWorkspace) return;
        await _vm.Timeline.BuildAsync(_vm.LogReplay.Entries, _vm.Workspace);
    }

    private void UpdateTimelineCursor()
    {
        var replay = _vm.LogReplay;
        if (replay == null) return;
        var entry = replay.CurrentEntry;
        _vm.Timeline.ReplayCursorTime = entry?.Timestamp?.TimeOfDay;
    }

    private void OnTimelineBucketSelected(TimelineBucket bucket)
    {
        // Scroll the log list to the first matching entry for this bucket.
        var listBox = this.FindControl<ListBox>("ImportedLogsListBox");
        if (listBox == null) return;

        var firstId = bucket.MatchingLogEntryIds.FirstOrDefault(-1);
        if (firstId < 0 || firstId >= listBox.ItemCount) return;

        var item = listBox.Items[firstId];
        if (item != null)
            listBox.ScrollIntoView(item);
    }

    private void UpdatePropertiesPanel()
    {
        var nameText = this.FindControl<TextBlock>("PropNameText");
        var typeText = this.FindControl<TextBlock>("PropTypeText");
        var rulesPanel = this.FindControl<StackPanel>("PropRulesPanel");
        var rulesCountText = this.FindControl<TextBlock>("PropRulesCountText");

        var sel = _vm.Selection;
        if (nameText != null)
            nameText.Text = string.IsNullOrEmpty(sel.SelectedName) ? "None" : sel.SelectedName;
        if (typeText != null)
            typeText.Text = string.IsNullOrEmpty(sel.SelectedType) ? "-" : sel.SelectedType;

        bool hasSelection = !string.IsNullOrEmpty(sel.SelectedId);
        if (rulesPanel != null)
            rulesPanel.IsVisible = hasSelection;

        if (hasSelection && rulesCountText != null)
        {
            var targetType = sel.SelectedType == "Module"
                ? RuleTargetType.Module
                : RuleTargetType.System;
            var count = _vm.Workspace.MappingRules
                .Count(r => r.TargetType == targetType && r.TargetId == sel.SelectedId);
            rulesCountText.Text = count == 0
                ? "No rules defined."
                : $"{count} rule{(count == 1 ? "" : "s")} defined.";
        }
    }

    private void InitializeGraphAutoAlignPanel()
    {
        var presetCombo = this.FindControl<ComboBox>("PropAutoAlignPresetComboBox");
        var sweeps = this.FindControl<NumericUpDown>("PropAutoAlignSweepsBox");
        var twoPass = this.FindControl<CheckBox>("PropAutoAlignTwoPassCheck");
        var columnGap = this.FindControl<NumericUpDown>("PropAutoAlignColumnGapBox");
        var rowGap = this.FindControl<NumericUpDown>("PropAutoAlignRowGapBox");
        var componentGap = this.FindControl<NumericUpDown>("PropAutoAlignComponentGapBox");

        if (presetCombo != null && sweeps != null && twoPass != null &&
            columnGap != null && rowGap != null && componentGap != null)
        {
            var cfg = UserConfigService.Load().GraphAutoAlignSettings;
            var presetKey = NormaliseAutoAlignPresetKey(cfg.PresetKey);

            _updatingAutoAlignPreset = true;
            presetCombo.SelectedIndex = presetKey switch
            {
                "dense" => 1,
                "separated" => 2,
                _ => 0
            };
            _updatingAutoAlignPreset = false;

            sweeps.Value = Math.Max(1, cfg.BarycentricSweeps);
            twoPass.IsChecked = cfg.RunTwoPassRefinement;
            columnGap.Value = (decimal)Math.Max(100, cfg.ColumnGap);
            rowGap.Value = (decimal)Math.Max(40, cfg.BaseRowGap);
            componentGap.Value = (decimal)Math.Max(20, cfg.ComponentGap);
        }

        UpdateGraphAutoAlignPanelVisibility();
    }

    private void OnPropAutoAlignPresetChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_updatingAutoAlignPreset) return;
        if (sender is not ComboBox combo) return;
        if (combo.SelectedItem is not ComboBoxItem item) return;

        var presetKey = item.Tag?.ToString();
        if (string.IsNullOrWhiteSpace(presetKey)) return;

        ApplyAutoAlignPreset(presetKey);
    }

    private static string NormaliseAutoAlignPresetKey(string? presetKey)
    {
        var key = (presetKey ?? string.Empty).Trim().ToLowerInvariant();
        return key is "dense" or "separated" ? key : "balanced";
    }

    private void ApplyAutoAlignPreset(string presetKey)
    {
        var sweeps = this.FindControl<NumericUpDown>("PropAutoAlignSweepsBox");
        var twoPass = this.FindControl<CheckBox>("PropAutoAlignTwoPassCheck");
        var columnGap = this.FindControl<NumericUpDown>("PropAutoAlignColumnGapBox");
        var rowGap = this.FindControl<NumericUpDown>("PropAutoAlignRowGapBox");
        var componentGap = this.FindControl<NumericUpDown>("PropAutoAlignComponentGapBox");

        if (sweeps == null || twoPass == null || columnGap == null || rowGap == null || componentGap == null)
            return;

        // Opinionated presets tuned for module relationship readability.
        switch (presetKey.Trim().ToLowerInvariant())
        {
            case "dense":
                sweeps.Value = 8;
                twoPass.IsChecked = true;
                columnGap.Value = 240;
                rowGap.Value = 84;
                componentGap.Value = 140;
                break;
            case "separated":
                sweeps.Value = 10;
                twoPass.IsChecked = true;
                columnGap.Value = 360;
                rowGap.Value = 120;
                componentGap.Value = 260;
                break;
            default:
                sweeps.Value = 6;
                twoPass.IsChecked = false;
                columnGap.Value = 280;
                rowGap.Value = 96;
                componentGap.Value = 180;
                break;
        }
    }

    private void UpdateGraphAutoAlignPanelVisibility(TabControl? tabs = null)
    {
        StackPanel? panel;
        try
        {
            panel = this.FindControl<StackPanel>("PropAutoAlignPanel");
        }
        catch (InvalidOperationException)
        {
            // Can happen during XAML initialization before the parent name scope exists.
            return;
        }

        if (tabs == null)
        {
            try
            {
                tabs = this.FindControl<TabControl>("CenterTabControl");
            }
            catch (InvalidOperationException)
            {
                return;
            }
        }

        if (panel == null || tabs == null) return;

        panel.IsVisible = tabs.SelectedIndex == 1;
    }

    private void OnCenterTabSelectionChanged(object? sender, SelectionChangedEventArgs e)
        => UpdateGraphAutoAlignPanelVisibility(sender as TabControl);

    private void OnPropAutoAlignApplyClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var defaults = _vm.Graph.CreateAutoAlignDefaults();

        var sweeps = this.FindControl<NumericUpDown>("PropAutoAlignSweepsBox")?.Value;
        var twoPass = this.FindControl<CheckBox>("PropAutoAlignTwoPassCheck")?.IsChecked == true;
        var columnGap = this.FindControl<NumericUpDown>("PropAutoAlignColumnGapBox")?.Value;
        var rowGap = this.FindControl<NumericUpDown>("PropAutoAlignRowGapBox")?.Value;
        var componentGap = this.FindControl<NumericUpDown>("PropAutoAlignComponentGapBox")?.Value;

        var options = new GraphViewModel.AutoAlignOptions
        {
            StartX = defaults.StartX,
            StartY = defaults.StartY,
            BarycentricSweeps = Math.Max(1, (int)(sweeps ?? defaults.BarycentricSweeps)),
            RunTwoPassRefinement = twoPass,
            ColumnGap = Math.Max(100, (double)(columnGap ?? (decimal)defaults.ColumnGap)),
            BaseRowGap = Math.Max(40, (double)(rowGap ?? (decimal)defaults.BaseRowGap)),
            ComponentGap = Math.Max(20, (double)(componentGap ?? (decimal)defaults.ComponentGap))
        };

        _vm.Graph.AutoAlign(options);

        var presetKey = "balanced";
        if (this.FindControl<ComboBox>("PropAutoAlignPresetComboBox")?.SelectedItem is ComboBoxItem selectedPreset)
            presetKey = NormaliseAutoAlignPresetKey(selectedPreset.Tag?.ToString());

        var cfg = UserConfigService.Load();
        cfg.GraphAutoAlignSettings.PresetKey = presetKey;
        cfg.GraphAutoAlignSettings.BarycentricSweeps = options.BarycentricSweeps;
        cfg.GraphAutoAlignSettings.RunTwoPassRefinement = options.RunTwoPassRefinement;
        cfg.GraphAutoAlignSettings.ColumnGap = options.ColumnGap;
        cfg.GraphAutoAlignSettings.BaseRowGap = options.BaseRowGap;
        cfg.GraphAutoAlignSettings.ComponentGap = options.ComponentGap;
        UserConfigService.Save(cfg);

        var statusText = this.FindControl<TextBlock>("StatusText");
        if (statusText != null)
            statusText.Text = "Graph auto-align applied and saved.";
    }

    private async void OnEditRulesClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var sel = _vm.Selection;
        if (string.IsNullOrEmpty(sel.SelectedId)) return;

        var targetType = sel.SelectedType == "Module" ? RuleTargetType.Module : RuleTargetType.System;

        var dialog = new MappingRulesDialog(
            _vm.Workspace.MappingRules,
            targetType,
            sel.SelectedId,
            sel.SelectedName);

        await dialog.ShowDialog(this);

        if (dialog.HasChanges)
        {
            _vm.IsDirty = true;
            UpdatePropertiesPanel();
            AppLogger.Info($"Mapping rules updated for {sel.SelectedType} '{sel.SelectedName}'");
        }
    }

    private void ShowMatchInfo(MatchResult match)
    {
        var matchPanel = this.FindControl<StackPanel>("PropMatchPanel");
        var matchText  = this.FindControl<TextBlock>("PropMatchText");

        if (matchPanel == null || matchText == null) return;

        if (match.Strength == MatchStrength.None)
        {
            matchPanel.IsVisible = false;
            return;
        }

        matchPanel.IsVisible = true;
        matchText.Text = match.MatchReason;
    }

    // ── Sidebar ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Refreshes all sidebar controls: workspace name, source path, analysis counts,
    /// and the user profile strip. Call after the workspace is opened, changed, or
    /// when log entries are loaded.
    /// </summary>
    private void RefreshSidebar()
    {
        var workspaceNameText  = this.FindControl<TextBlock>("SidebarWorkspaceNameText");
        var sourcePathText     = this.FindControl<TextBlock>("SidebarSourcePathText");
        var sourceFilesCount   = this.FindControl<TextBlock>("AnalysisSourceFilesCountText");
        var mdDocsCount        = this.FindControl<TextBlock>("AnalysisMarkdownDocsCountText");
        var logPointsCount     = this.FindControl<TextBlock>("AnalysisLogPointsCountText");
        var profileInitial     = this.FindControl<TextBlock>("ProfileInitialText");
        var profileDisplayName = this.FindControl<TextBlock>("ProfileDisplayNameText");

        if (!_vm.HasWorkspace)
        {
            if (workspaceNameText != null) workspaceNameText.Text = "—";
            if (sourcePathText    != null) sourcePathText.Text    = string.Empty;
            if (sourceFilesCount  != null) sourceFilesCount.Text  = string.Empty;
            if (mdDocsCount       != null) mdDocsCount.Text       = string.Empty;
            if (logPointsCount    != null) logPointsCount.Text    = string.Empty;
            return;
        }

        // WORKSPACE section
        if (workspaceNameText != null)
            workspaceNameText.Text = _vm.Workspace.WorkspaceName;

        if (sourcePathText != null)
        {
            var slnFile = string.IsNullOrWhiteSpace(_vm.Workspace.SourceProjectPath)
                ? string.Empty
                : Path.GetFileName(_vm.Workspace.SourceProjectPath);
            sourcePathText.Text = string.IsNullOrEmpty(slnFile)
                ? _vm.WorkspaceFolderPath
                : $"{slnFile}  ({Path.GetDirectoryName(_vm.Workspace.SourceProjectPath)})";
        }

        // ANALYSIS counts
        if (sourceFilesCount != null)
        {
            int count = _vm.Workspace.SystemMap.AllCodeNodes
                .Select(n => n.FilePath)
                .Where(p => !string.IsNullOrEmpty(p))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();
            sourceFilesCount.Text = count > 0 ? count.ToString("N0") : string.Empty;
        }

        if (mdDocsCount != null)
        {
            int count = CountMarkdownDocs(_vm.WorkspaceFolderPath);
            mdDocsCount.Text = count > 0 ? count.ToString("N0") : string.Empty;
        }

        if (logPointsCount != null)
        {
            int count = _vm.LogReplay.Entries.Count;
            logPointsCount.Text = count > 0 ? count.ToString("N0") : string.Empty;
        }

        // Profile strip — use OS user name for a simple display
        var osUser = Environment.UserName;
        var initial = string.IsNullOrEmpty(osUser) ? "?" : osUser[0].ToString().ToUpperInvariant();
        if (profileInitial     != null) profileInitial.Text     = initial;
        if (profileDisplayName != null) profileDisplayName.Text = osUser;
    }

    private static int CountMarkdownDocs(string workspaceFolderPath)
    {
        if (string.IsNullOrEmpty(workspaceFolderPath)) return 0;
        var summariesRoot = Path.Combine(workspaceFolderPath, "summaries");
        if (!Directory.Exists(summariesRoot)) return 0;
        try
        {
            return Directory.EnumerateFiles(summariesRoot, "*.md", SearchOption.AllDirectories).Count();
        }
        catch
        {
            return 0;
        }
    }

    // ── Analysis sidebar row click handlers ────────────────────────────────

    private void OnAnalysisScanSummaryClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => SetActiveNavTab("Scan");

    private void OnAnalysisSourceFilesClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        SetActiveNavTab("Monitor");
        if (_vm.HasWorkspace)
            _vm.SystemMap.SetActiveView(ViewModels.SystemMapViewKind.CodeDetailView);
    }

    private void OnAnalysisMarkdownDocsClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => SetActiveNavTab("Docs");

    private void OnAnalysisAstGraphClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => SetActiveNavTab("Graph");

    private void OnAnalysisLogPointsClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => SetActiveNavTab("Monitor");

    private async void OnAddArchComponentClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!_vm.HasWorkspace) return;

        var name = await ShowInputDialogAsync(
            "Add Architecture Component",
            "Enter a name for the new system/component:",
            string.Empty);
        if (string.IsNullOrWhiteSpace(name)) return;

        var newSystem = new Models.SystemMap.SystemModel
        {
            Id          = Guid.NewGuid().ToString(),
            Name        = name.Trim(),
            Kind        = Models.SystemMap.SystemKind.Unknown,
            Confidence  = Models.SystemMap.ConfidenceLevel.Manual,
            IdentityKey = Services.SystemMapIdentity.CreateSystemKey(
                name.Trim(), nameof(Models.SystemMap.SystemKind.Unknown))
        };

        _vm.Workspace.SystemMap.Systems.Add(newSystem);
        _vm.SystemMap.LoadFrom(_vm.Workspace.SystemMap, _vm.Workspace.ActiveProfile?.LayoutPositions);
        _vm.Graph.RefreshFromSystemMap(_vm.Workspace.SystemMap);
        _vm.IsDirty = true;
        SetupTreeView();
        AppLogger.Info($"Architecture component added: {name.Trim()}");
    }

    // ── Canvas / TreeView ────────────────────────────────────────────────────

    private void SetupTreeView()
    {
        var tree = this.FindControl<TreeView>("ArchTreeView");
        if (tree == null) return;

        tree.SelectionChanged -= OnTreeSelectionChanged;

        var layers = new[]
        {
            (ViewModels.ArchitectureLayerKind.Presentation,   "Presentation Layer",   "#9B59B6"),
            (ViewModels.ArchitectureLayerKind.Application,    "Application Layer",    "#27AE60"),
            (ViewModels.ArchitectureLayerKind.Domain,         "Domain Layer",         "#F39C12"),
            (ViewModels.ArchitectureLayerKind.Infrastructure, "Infrastructure Layer", "#2980B9"),
        };

        var items = new List<TreeViewItem>();

        // Group classified systems (from SystemMap view-model) by layer.
        var systemsByLayer = _vm.SystemMap.Systems
            .GroupBy(s => s.LayerKind)
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var (layerKind, layerLabel, layerColor) in layers)
        {
            var layerSystems = systemsByLayer.TryGetValue(layerKind, out var ls)
                ? ls
                : new List<ViewModels.SystemItemVm>();

            var dotColor = Avalonia.Media.Color.Parse(layerColor);
            var dotBrush = new Avalonia.Media.SolidColorBrush(dotColor);

            var groupHeader = new Avalonia.Controls.StackPanel
            {
                Orientation = Avalonia.Layout.Orientation.Horizontal,
                Spacing = 6
            };
            groupHeader.Children.Add(new Avalonia.Controls.Shapes.Ellipse
            {
                Width  = 8,
                Height = 8,
                Fill   = dotBrush,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
            });
            groupHeader.Children.Add(new TextBlock
            {
                Text      = layerLabel,
                Foreground = dotBrush,
                FontSize  = 11,
                FontWeight = Avalonia.Media.FontWeight.SemiBold,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
            });

            var layerNode = new TreeViewItem
            {
                Header     = groupHeader,
                IsExpanded = layerSystems.Count > 0,
                ItemsSource = layerSystems.Select(sys => new TreeViewItem
                {
                    Header = CreateSystemLeafHeader(sys.Name),
                    Tag    = sys
                }).ToList()
            };

            items.Add(layerNode);
        }

        // Add any legacy WorkspaceModel systems not yet classified in the SystemMap
        // (e.g. workspaces that pre-date the SystemMap) as an "Other" group.
        var classifiedIds = new HashSet<string>(
            _vm.SystemMap.Systems.Select(s => s.Id), StringComparer.Ordinal);

        var unclassified = _vm.Workspace.Systems
            .Where(ws => !classifiedIds.Contains(ws.Id))
            .Select(ws => new ViewModels.SystemItemVm { Id = ws.Id, Name = ws.Name })
            .ToList();

        if (unclassified.Count > 0)
        {
            var otherNode = new TreeViewItem
            {
                Header = new TextBlock
                {
                    Text       = "Other",
                    Foreground = new Avalonia.Media.SolidColorBrush(
                        Avalonia.Media.Color.Parse("#778899")),
                    FontSize   = 11,
                    FontWeight = Avalonia.Media.FontWeight.SemiBold
                },
                IsExpanded  = true,
                ItemsSource = unclassified.Select(sys => new TreeViewItem
                {
                    Header = CreateSystemLeafHeader(sys.Name),
                    Tag    = sys
                }).ToList()
            };
            items.Add(otherNode);
        }

        tree.ItemsSource = items;
        tree.SelectionChanged += OnTreeSelectionChanged;
    }

    private void OnTreeSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count == 0 || e.AddedItems[0] is not TreeViewItem item)
            return;

        // Only leaf items (system nodes) carry a SystemItemVm tag; layer group nodes do not.
        if (item.Tag is not ViewModels.SystemItemVm sys) return;

        // Highlight the matching card on the System Map canvas.
        _vm.SystemMap.SelectSystem(sys);

        // Also update the shared selection state used by the Properties panel.
        _vm.Workspace.ClearSelection();
        var wsSystem = _vm.Workspace.Systems.FirstOrDefault(s => s.Id == sys.Id);
        if (wsSystem != null) wsSystem.IsSelected = true;

        _vm.Selection.SelectedId   = sys.Id;
        _vm.Selection.SelectedType = "System";
        _vm.Selection.SelectedName = sys.Name;

        // Switch to Monitor/System Map so the user can see the highlighted card.
        if (_activeNavTab != "Monitor")
            SetActiveNavTab("Monitor");
    }

    private static Avalonia.Controls.StackPanel CreateSystemLeafHeader(string name)
    {
        var panel = new Avalonia.Controls.StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            Spacing = 6
        };
        panel.Children.Add(new TextBlock
        {
            Text      = "·",
            Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#556677")),
            FontSize  = 13,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        });
        panel.Children.Add(new TextBlock
        {
            Text      = name,
            Foreground = Avalonia.Media.Brushes.LightGray,
            FontSize  = 11,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        });
        return panel;
    }

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
