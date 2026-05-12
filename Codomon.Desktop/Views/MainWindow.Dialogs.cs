using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Codomon.Desktop.Persistence;
using Codomon.Desktop.Services;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Codomon.Desktop.Views;

// Partial class that contains all small dialog-builder helpers for MainWindow.
// Keeping these separate prevents the main code-behind from growing too large.
public partial class MainWindow
{
    // ── About dialog ─────────────────────────────────────────────────────────

    private async void OnAboutClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var dialog = new Window
        {
            Title = "About codomon",
            Width = 340,
            Height = 180,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Background = new SolidColorBrush(Color.Parse("#111820"))
        };

        var closeBtn = new Button
        {
            Content = "Close",
            Padding = new Avalonia.Thickness(20, 4),
            IsDefault = true
        };
        closeBtn.Click += (_, _) => dialog.Close();

        dialog.Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(24, 20),
            Spacing = 10,
            Children =
            {
                new TextBlock
                {
                    Text = $"codomon {BuildInfo.AppVersion}",
                    FontSize = 16,
                    FontWeight = FontWeight.Bold,
                    Foreground = Brushes.White
                },
                new TextBlock
                {
                    Text = $"Build: {BuildInfo.BuildDate}",
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Color.Parse("#88AABB"))
                },
                new TextBlock
                {
                    Text = "Architecture intelligence for software teams.",
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.Parse("#556677")),
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap
                },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Children = { closeBtn }
                }
            }
        };

        await dialog.ShowDialog(this);
    }

    // ── First-run user-config prompt ─────────────────────────────────────────

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
            Background = new SolidColorBrush(Color.Parse("#111820"))
        };

        var openBtn  = new Button { Content = "Setup LLM", Padding = new Avalonia.Thickness(20, 4) };
        var laterBtn = new Button { Content = "Later",     Padding = new Avalonia.Thickness(20, 4) };

        openBtn.Click  += (_, _) => { openSettings = true; dialog.Close(); };
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
                    Foreground = Brushes.White
                },
                new TextBlock
                {
                    Text = $"Expected path: {UserConfigService.GetConfigFilePath()}",
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                    Foreground = new SolidColorBrush(Color.Parse("#88AABB")),
                    FontSize = 11
                },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Children = { openBtn, laterBtn }
                }
            }
        };

        await dialog.ShowDialog(this);
        return openSettings;
    }

    // ── Generic input dialog ─────────────────────────────────────────────────

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
            Background = new SolidColorBrush(Color.Parse("#111820"))
        };

        var inputBox = new TextBox
        {
            Text = defaultValue,
            Foreground = Brushes.White,
            Background = new SolidColorBrush(Color.Parse("#1A2435")),
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

        okBtn.Click    += (_, _) => { result = inputBox.Text; dialog.Close(); };
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
                    Foreground = Brushes.White,
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap
                },
                inputBox,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    HorizontalAlignment = HorizontalAlignment.Right,
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
            Background = new SolidColorBrush(Color.Parse("#111820"))
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
                    Foreground = Brushes.White
                },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Children = { deleteBtn, cancelBtn }
                }
            }
        };

        await dialog.ShowDialog(this);
        return confirmed;
    }

    // ── Remove stale recent workspace dialog ─────────────────────────────────

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
            Background = new SolidColorBrush(Color.Parse("#111820"))
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
                    Foreground = Brushes.White
                },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Children = { removeBtn, cancelBtn }
                }
            }
        };

        await dialog.ShowDialog(this);
        return remove;
    }

    // ── Autosave picker dialog ────────────────────────────────────────────────

    private async Task<AutosaveEntry?> ShowAutosavePickerAsync(List<AutosaveEntry> entries)
    {
        AutosaveEntry? result = null;

        var dialog = new Window
        {
            Title = "Load Autosave",
            Width = 480,
            Height = 320,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Background = new SolidColorBrush(Color.Parse("#111820"))
        };

        var listBox = new ListBox
        {
            Background = new SolidColorBrush(Color.Parse("#1A2435")),
            Margin = new Avalonia.Thickness(0, 0, 0, 8)
        };

        foreach (var entry in entries)
        {
            listBox.Items.Add(new ListBoxItem
            {
                Content = new TextBlock
                {
                    Text = entry.DisplayName,
                    Foreground = Brushes.White,
                    FontFamily = new FontFamily("Monospace"),
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
                item.Tag is AutosaveEntry entry)
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
                    Foreground = Brushes.White,
                    FontWeight = FontWeight.SemiBold
                },
                listBox,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Children = { loadBtn, cancelBtn }
                }
            }
        };

        await dialog.ShowDialog(this);
        return result;
    }

    // ── Autosave restore warning dialog ──────────────────────────────────────

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
            Background = new SolidColorBrush(Color.Parse("#111820"))
        };

        var restoreBtn = new Button { Content = "Restore", Padding = new Avalonia.Thickness(20, 4) };
        var cancelBtn  = new Button { Content = "Cancel",  Padding = new Avalonia.Thickness(20, 4) };

        restoreBtn.Click += (_, _) => { confirmed = true; dialog.Close(); };
        cancelBtn.Click  += (_, _) => dialog.Close();

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
                    Foreground = Brushes.White
                },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Children = { restoreBtn, cancelBtn }
                }
            }
        };

        await dialog.ShowDialog(this);
        return confirmed;
    }

    // ── Unsaved-changes dialog ────────────────────────────────────────────────

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
            Background = new SolidColorBrush(Color.Parse("#111820"))
        };

        var saveBtn    = new Button { Content = "Save",    Padding = new Avalonia.Thickness(20, 4) };
        var discardBtn = new Button { Content = "Discard", Padding = new Avalonia.Thickness(20, 4) };
        var cancelBtn  = new Button { Content = "Cancel",  Padding = new Avalonia.Thickness(20, 4) };

        saveBtn.Click    += (_, _) => { result = true;  dialog.Close(); };
        discardBtn.Click += (_, _) => { result = false; dialog.Close(); };
        cancelBtn.Click  += (_, _) => { result = null;  dialog.Close(); };

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
                    Foreground = Brushes.White
                },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Children = { saveBtn, discardBtn, cancelBtn }
                }
            }
        };

        await dialog.ShowDialog(this);
        return result;
    }

    // ── Error dialog ──────────────────────────────────────────────────────────

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
            HorizontalAlignment = HorizontalAlignment.Center,
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
                    Foreground = Brushes.White
                },
                okButton
            }
        };

        await dialog.ShowDialog(this);
    }
}
