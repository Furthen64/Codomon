using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Codomon.Desktop.Models;
using System.Collections.Specialized;

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
        var list = this.FindControl<ItemsControl>("LogItems");
        if (list == null) return;

        list.Items.Clear();

        var filterLower = _filter.ToLowerInvariant();

        foreach (var entry in AppLogger.Entries)
        {
            if (!string.IsNullOrEmpty(filterLower) &&
                !entry.Formatted.ToLowerInvariant().Contains(filterLower))
                continue;

            var tb = new TextBlock
            {
                Text = entry.Formatted,
                FontFamily = new FontFamily("Monospace"),
                FontSize = 12,
                Foreground = new SolidColorBrush(Avalonia.Media.Color.Parse(entry.LevelColor)),
                Padding = new Avalonia.Thickness(8, 2),
                TextWrapping = TextWrapping.NoWrap
            };
            list.Items.Add(tb);
        }

        // Auto-scroll to bottom when no filter is active.
        if (string.IsNullOrEmpty(_filter))
        {
            var scroller = this.FindControl<ScrollViewer>("LogScroller");
            scroller?.ScrollToEnd();
        }
    }
}
