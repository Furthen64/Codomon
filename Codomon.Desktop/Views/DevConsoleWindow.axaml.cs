using Avalonia.Controls;
using Avalonia.Threading;
using Codomon.Desktop.Models;
using System.Collections.Specialized;
using System.Text;

namespace Codomon.Desktop.Views;

public partial class DevConsoleWindow : Window
{
    private string _filter = string.Empty;

    public DevConsoleWindow()
    {
        InitializeComponent();

        // Populate current entries.
        RefreshList();

        // Subscribe to new entries while the window is open.
        AppLogger.Entries.CollectionChanged += OnEntriesChanged;

        Closed += (_, _) => AppLogger.Entries.CollectionChanged -= OnEntriesChanged;
    }

    private void OnEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // Marshal to the UI thread — CollectionChanged may be raised from any thread.
        Dispatcher.UIThread.Post(RefreshList);
    }

    // ── Toolbar handlers ─────────────────────────────────────────────────────

    private void OnClearClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        AppLogger.Entries.Clear();
        RefreshList();
    }

    private void OnFilterChanged(object? sender, TextChangedEventArgs e)
    {
        _filter = (sender as TextBox)?.Text ?? string.Empty;
        RefreshList();
    }

    // ── List rendering ───────────────────────────────────────────────────────

    private void RefreshList()
    {
        var logBox = this.FindControl<TextBox>("LogBox");
        if (logBox == null) return;

        var filterLower = _filter.ToLowerInvariant();
        var sb = new StringBuilder();

        foreach (var entry in AppLogger.Entries)
        {
            if (!string.IsNullOrEmpty(filterLower) &&
                !entry.Formatted.ToLowerInvariant().Contains(filterLower))
                continue;

            sb.AppendLine(entry.Formatted);
        }

        logBox.Text = sb.ToString();

        // Auto-scroll to bottom when no filter is active.
        if (string.IsNullOrEmpty(_filter))
            logBox.CaretIndex = logBox.Text?.Length ?? 0;
    }
}
