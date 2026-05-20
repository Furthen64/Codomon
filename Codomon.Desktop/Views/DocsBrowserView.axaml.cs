using Avalonia.Controls;
using Codomon.Desktop.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Codomon.Desktop.Views;

public partial class DocsBrowserView : UserControl
{
    private const int MaxPreviewCharacters = 200_000;

    private string _workspaceFolderPath = string.Empty;
    private List<SummaryEntry> _allEntries = new();
    private string _filterText = string.Empty;

    public DocsBrowserView()
    {
        InitializeComponent();
        this.FindControl<Button>("RefreshDocsButton")!.Click += (_, _) => Reload();
    }

    /// <summary>
    /// Loads (or reloads) the summary list for the given workspace folder.
    /// Pass <c>null</c> or an empty string to clear the view.
    /// </summary>
    public void LoadWorkspace(string? workspaceFolderPath)
    {
        _workspaceFolderPath = workspaceFolderPath ?? string.Empty;
        Reload();
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private void Reload()
    {
        _allEntries = string.IsNullOrEmpty(_workspaceFolderPath)
            ? new List<SummaryEntry>()
            : LlmSummaryService.ListSummaries(_workspaceFolderPath);

        var subtitle = this.FindControl<TextBlock>("DocsSubtitleText");
        if (subtitle != null)
        {
            subtitle.Text = string.IsNullOrEmpty(_workspaceFolderPath)
                ? "Open a workspace to browse its LLM-generated documentation."
                : $"LLM-generated documentation for this workspace.";
        }

        ApplyFilterAndRebuildList();
    }

    private void ApplyFilterAndRebuildList()
    {
        var listBox = this.FindControl<ListBox>("SummaryListBox");
        var countText = this.FindControl<TextBlock>("SummaryCountText");
        var noSummariesBanner = this.FindControl<Border>("NoSummariesBanner");

        if (listBox == null) return;

        var filter = _filterText.Trim();
        var filtered = string.IsNullOrEmpty(filter)
            ? _allEntries
            : _allEntries.Where(e =>
                e.SourceRelativePath.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();

        listBox.Items.Clear();

        foreach (var entry in filtered)
        {
            var item = new ListBoxItem
            {
                Tag = entry,
                Padding = new Avalonia.Thickness(8, 4),
                Content = new StackPanel
                {
                    Spacing = 2,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = entry.SourceRelativePath,
                            FontSize = 11.5,
                            Foreground = Avalonia.Media.Brushes.White,
                            TextWrapping = Avalonia.Media.TextWrapping.NoWrap
                        },
                        new TextBlock
                        {
                            Text = entry.GeneratedAt.ToString("yyyy-MM-dd HH:mm"),
                            FontSize = 10,
                            Foreground = new Avalonia.Media.SolidColorBrush(
                                Avalonia.Media.Color.Parse("#778899"))
                        }
                    }
                }
            };
            listBox.Items.Add(item);
        }

        if (countText != null)
            countText.Text = _allEntries.Count == 0
                ? "Summaries"
                : $"Summaries ({filtered.Count} / {_allEntries.Count})";

        bool noSummaries = _allEntries.Count == 0 && !string.IsNullOrEmpty(_workspaceFolderPath);
        if (noSummariesBanner != null)
            noSummariesBanner.IsVisible = noSummaries;

        // Clear the content viewer since selection is now gone.
        ClearContentView();
    }

    private void ClearContentView()
    {
        var title = this.FindControl<TextBlock>("SelectedSummaryTitle");
        var meta = this.FindControl<TextBlock>("SelectedSummaryMeta");
        var scroll = this.FindControl<ScrollViewer>("SummaryContentScroll");

        if (title != null) title.Text = "Select a file summary from the list.";
        if (meta != null) meta.Text = string.Empty;
        if (scroll != null) scroll.IsVisible = false;
    }

    private void OnFilterChanged(object? sender, Avalonia.Controls.TextChangedEventArgs e)
    {
        _filterText = (sender as TextBox)?.Text ?? string.Empty;
        ApplyFilterAndRebuildList();
    }

    private void OnSummarySelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListBox lb) return;
        if (lb.SelectedItem is not ListBoxItem item) return;
        if (item.Tag is not SummaryEntry entry) return;

        ShowSummary(entry);
    }

    private void ShowSummary(SummaryEntry entry)
    {
        var title = this.FindControl<TextBlock>("SelectedSummaryTitle");
        var meta = this.FindControl<TextBlock>("SelectedSummaryMeta");
        var block = this.FindControl<SelectableTextBlock>("SummaryContentBlock");
        var scroll = this.FindControl<ScrollViewer>("SummaryContentScroll");

        if (title != null) title.Text = entry.SourceRelativePath;
        if (meta != null) meta.Text = $"Generated {entry.GeneratedAt:yyyy-MM-dd HH:mm}  ·  {entry.SummaryFilePath}";

        if (block == null || scroll == null) return;

        if (!File.Exists(entry.SummaryFilePath))
        {
            block.Text = "(Summary file not found on disk.)";
            scroll.IsVisible = true;
            return;
        }

        try
        {
            var content = File.ReadAllText(entry.SummaryFilePath);
            if (content.Length > MaxPreviewCharacters)
                content = content[..MaxPreviewCharacters] + "\n\n[Truncated — file too large to display in full]";
            block.Text = content;
        }
        catch (Exception ex)
        {
            block.Text = $"(Error reading summary: {ex.Message})";
        }

        scroll.IsVisible = true;
        scroll.ScrollToHome();
    }
}
