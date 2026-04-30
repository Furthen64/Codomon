using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Codomon.Desktop.Controls;
using Codomon.Desktop.Models;
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

    // Graph view-model for the Nodify canvas; kept so Refresh can be called on demand.
    private ViewModels.GraphViewModel? _graphVm;

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

    // Throttles timeline rebuilds during live monitoring (max once per LiveTimelineRebuildThrottleSeconds).
    private const double LiveTimelineRebuildThrottleSeconds = 2.0;
    private DateTimeOffset _lastLiveTimelineRebuild = DateTimeOffset.MinValue;

    public MainWindow()
    {
        InitializeComponent();

        _vm = new MainViewModel();
        DataContext = _vm;

        // Set the window title with version and build date embedded at build time.
        Title = $"Codomon {BuildInfo.AppVersion}  (build {BuildInfo.BuildDate})";

        SetupCanvas();
        SetupTreeView();
        SetupTimeline();
        RefreshProfileComboBox();
        PopulateRecentWorkspaces();
        SetupReplaySpeedComboBox();

        _vm.Selection.PropertyChanged += (_, _) => UpdatePropertiesPanel();
        _vm.PropertyChanged += OnViewModelPropertyChanged;
        SubscribeToLogReplay(_vm.LogReplay);
        SubscribeToLiveMonitor(_vm.LiveMonitor);

        // Intercept window close to warn about unsaved changes.
        Closing += OnWindowClosing;

        AppLogger.Info("App started");
    }


    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.HasWorkspace))
        {
            var workspaceGrid = this.FindControl<Grid>("WorkspaceGrid");
            var welcomeOverlay = this.FindControl<Grid>("WelcomeOverlay");
            bool has = _vm.HasWorkspace;
            if (workspaceGrid != null) workspaceGrid.IsVisible = has;
            if (welcomeOverlay != null) welcomeOverlay.IsVisible = !has;
            UpdateWindowTitle();
            RefreshLiveMonitorPanel();
        }
        else if (e.PropertyName == nameof(MainViewModel.Workspace))
        {
            _logListShowingLive = false;
            SetupCanvas();
            SetupTreeView();
            RefreshProfileComboBox();
            RefreshRoslynConnectionsPanel();
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
        else if (e.PropertyName == nameof(MainViewModel.Profiles))
        {
            RefreshProfileComboBox();
        }
        else if (e.PropertyName == nameof(MainViewModel.ActiveProfileId))
        {
            SyncProfileComboBoxSelection();
            // TODO (Phase 02): notify the Nodify graph to refresh when the profile changes.
            RebuildTimeline();
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

            // Entries have changed — rebuild the timeline.
            RebuildTimeline();
        }
    }

    private static Avalonia.Controls.Templates.FuncDataTemplate<LogEntryModel> BuildLogItemTemplate()
    {
        return new Avalonia.Controls.Templates.FuncDataTemplate<LogEntryModel>((entry, _) =>
        {
            if (entry == null) return new TextBlock();
            return new TextBlock
            {
                Text = entry.Formatted,
                FontFamily = new Avalonia.Media.FontFamily("Monospace"),
                FontSize = 11,
                Foreground = new Avalonia.Media.SolidColorBrush(
                    Avalonia.Media.Color.Parse(entry.LevelColor)),
                Padding = new Avalonia.Thickness(4, 1)
            };
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
        combo.SelectedIndex = 1;  // 1× default
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
            BindLogListToLiveMonitor();
            RefreshLiveMonitorPanel();
            return Task.CompletedTask;
        });
    }

    private void OnStopWatchClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _vm.StopLiveMonitoring();
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
        // Scroll the log list to the latest entry.
        var listBox = this.FindControl<ListBox>("ImportedLogsListBox");
        if (listBox != null && listBox.ItemCount > 0)
        {
            var last = listBox.Items[listBox.ItemCount - 1];
            if (last != null)
                listBox.ScrollIntoView(last);
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

    /// <summary>Synchronises the Watch/Stop button states and status text.</summary>
    private void RefreshLiveMonitorPanel()
    {
        var watchBtn  = this.FindControl<Button>("WatchLogButton");
        var stopBtn   = this.FindControl<Button>("StopWatchButton");
        var statusTb  = this.FindControl<TextBlock>("WatchStatusText");

        bool hasWorkspace  = _vm.HasWorkspace;
        bool isWatching    = _vm.LiveMonitor.IsWatching;
        bool replayPlaying = _vm.LogReplay.IsPlaying;

        if (watchBtn != null) watchBtn.IsEnabled = hasWorkspace && !replayPlaying;
        if (stopBtn  != null) stopBtn.IsEnabled  = isWatching;

        if (statusTb != null)
        {
            if (_vm.LiveMonitor.HasError)
            {
                statusTb.Text       = $"⚠ {_vm.LiveMonitor.ErrorMessage}";
                statusTb.Foreground = new Avalonia.Media.SolidColorBrush(
                    Avalonia.Media.Color.Parse("#FF7070"));
            }
            else if (isWatching)
            {
                statusTb.Text       = $"● {System.IO.Path.GetFileName(_vm.LiveMonitor.WatchedFilePath)} ({_vm.LiveMonitor.Entries.Count} lines)";
                statusTb.Foreground = new Avalonia.Media.SolidColorBrush(
                    Avalonia.Media.Color.Parse("#66EE88"));
            }
            else if (_logListShowingLive && _vm.LiveMonitor.Entries.Count > 0)
            {
                statusTb.Text       = $"Stopped ({_vm.LiveMonitor.Entries.Count} lines captured)";
                statusTb.Foreground = new Avalonia.Media.SolidColorBrush(
                    Avalonia.Media.Color.Parse("#778899"));
            }
            else
            {
                statusTb.Text       = "Not watching";
                statusTb.Foreground = new Avalonia.Media.SolidColorBrush(
                    Avalonia.Media.Color.Parse("#556677"));
            }
        }
    }

    private async void RebuildLiveTimeline()
    {
        if (!_vm.HasWorkspace) return;
        await _vm.Timeline.BuildAsync(_vm.LiveMonitor.Entries, _vm.Workspace);
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

        AppLogger.Debug($"[Roslyn] Dialog closed. result={(result == null ? "null (unexpected — dialog may have been cancelled before the X-button fix applied)" : "RoslynScanViewModel")}  " +
                        $"PromotedConnections={(result?.PromotedConnections.Count.ToString() ?? "n/a")}  " +
                        $"WasAddedToCanvas={(result?.WasAddedToCanvas.ToString() ?? "n/a")}");

        if (result == null || (result.PromotedConnections.Count == 0 && !result.WasAddedToCanvas))
        {
            AppLogger.Debug("[Roslyn] No promoted connections and canvas was not modified — canvas not updated.");
            return;
        }

        await ExecuteSafeAsync(() =>
        {
            if (result.PromotedConnections.Count > 0)
            {
                AppLogger.Debug($"[Roslyn] Calling AddRoslynConnections with {result.PromotedConnections.Count} connection(s).");
                _vm.AddRoslynConnections(result.PromotedConnections);
                RefreshRoslynConnectionsPanel();
            }
            else
            {
                // Systems were added via "Add all to Canvas" but no connections were promoted
                // (e.g. scan found no inter-class relationships). Still mark dirty.
                _vm.IsDirty = true;
            }
            AppLogger.Debug($"[Roslyn] Calling GraphViewModel.Refresh. _graphVm is {(_graphVm == null ? "null — canvas will NOT update!" : "set")}.");
            _graphVm?.Refresh(_vm.Workspace);
            AppLogger.Debug("[Roslyn] GraphViewModel.Refresh complete. Canvas should now reflect all added entities.");
            return Task.CompletedTask;
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

        var hypothesisVm = new ViewModels.ArchitectureHypothesisViewModel(
            _vm.Workspace,
            _vm.WorkspaceFolderPath);

        var dialog = new ArchitectureHypothesisDialog(hypothesisVm);
        await dialog.ShowDialog(this);

        // If any suggestions were accepted into the System Map mark the workspace dirty.
        if (_vm.Workspace.SystemMap.Systems.Count > 0 ||
            _vm.Workspace.SystemMap.AllModules.Any())
        {
            _vm.IsDirty = true;
        }
    }

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
        var baseTitle = $"Codomon {BuildInfo.AppVersion}  (build {BuildInfo.BuildDate})";
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

        _graphVm = new ViewModels.GraphViewModel();
        if (_vm.HasWorkspace)
            _graphVm.Refresh(_vm.Workspace);

        var graphView = new GraphView
        {
            DataContext = _graphVm
        };

        var host = this.FindControl<ContentControl>("CanvasHost");
        if (host != null)
            host.Content = graphView;

        AppLogger.Debug("Graph canvas (Nodify) initialized");
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
