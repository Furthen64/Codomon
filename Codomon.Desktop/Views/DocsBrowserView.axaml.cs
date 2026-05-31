using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using Codomon.Desktop.Services;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace Codomon.Desktop.Views;

public partial class DocsBrowserView : UserControl
{
    private const int MaxPreviewCharacters = 200_000;
    private static readonly MarkdownPipeline MarkdownPipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .Build();
    private static readonly IBrush HeadingBrush = Brushes.White;
    private static readonly IBrush BodyBrush = new SolidColorBrush(Color.Parse("#D7DEE8"));
    private static readonly IBrush MutedBrush = new SolidColorBrush(Color.Parse("#98A7B8"));
    private static readonly IBrush LinkBrush = new SolidColorBrush(Color.Parse("#7DC4FF"));
    private static readonly IBrush CodeForegroundBrush = new SolidColorBrush(Color.Parse("#DCEBFF"));
    private static readonly IBrush CodeBackgroundBrush = new SolidColorBrush(Color.Parse("#142132"));
    private static readonly IBrush QuoteBorderBrush = new SolidColorBrush(Color.Parse("#34516F"));
    private static readonly IBrush QuoteBackgroundBrush = new SolidColorBrush(Color.Parse("#0E1825"));
    private static readonly IBrush RuleBrush = new SolidColorBrush(Color.Parse("#2D3C4C"));

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
        var host = this.FindControl<StackPanel>("SummaryContentHost");

        if (title != null) title.Text = "Select a file summary from the list.";
        if (meta != null) meta.Text = string.Empty;
        host?.Children.Clear();
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
        var host = this.FindControl<StackPanel>("SummaryContentHost");
        var scroll = this.FindControl<ScrollViewer>("SummaryContentScroll");

        if (title != null) title.Text = entry.SourceRelativePath;
        if (meta != null) meta.Text = $"Generated {entry.GeneratedAt:yyyy-MM-dd HH:mm}  ·  {entry.SummaryFilePath}";

        if (host == null || scroll == null) return;
        host.Children.Clear();

        if (!File.Exists(entry.SummaryFilePath))
        {
            host.Children.Add(CreateMessageBlock("(Summary file not found on disk.)"));
            scroll.IsVisible = true;
            return;
        }

        try
        {
            var content = File.ReadAllText(entry.SummaryFilePath);
            if (content.Length > MaxPreviewCharacters)
                content = content[..MaxPreviewCharacters] + "\n\n[Truncated — file too large to display in full]";
            RenderMarkdown(host, content);
        }
        catch (Exception ex)
        {
            host.Children.Add(CreateMessageBlock($"(Error reading summary: {ex.Message})"));
        }

        scroll.IsVisible = true;
        scroll.ScrollToHome();
    }

    private static void RenderMarkdown(Panel host, string content)
    {
        host.Children.Clear();

        var document = Markdown.Parse(content ?? string.Empty, MarkdownPipeline);
        foreach (var block in document)
            AddBlock(host, block, nestingLevel: 0);

        if (host.Children.Count == 0)
            host.Children.Add(CreateMessageBlock("(This summary is empty.)"));
    }

    private static void AddBlock(Panel host, Block block, int nestingLevel)
    {
        switch (block)
        {
            case HeadingBlock heading:
                host.Children.Add(CreateHeadingBlock(heading));
                break;
            case ParagraphBlock paragraph:
                host.Children.Add(CreateParagraphBlock(paragraph));
                break;
            case QuoteBlock quote:
                host.Children.Add(CreateQuoteBlock(quote, nestingLevel));
                break;
            case ListBlock list:
                host.Children.Add(CreateListBlock(list, nestingLevel));
                break;
            case FencedCodeBlock fencedCode:
                host.Children.Add(CreateCodeBlock(fencedCode));
                break;
            case CodeBlock codeBlock:
                host.Children.Add(CreateCodeBlock(codeBlock));
                break;
            case ThematicBreakBlock:
                host.Children.Add(new Border
                {
                    Height = 1,
                    Background = RuleBrush,
                    Margin = new Avalonia.Thickness(0, 4, 0, 4)
                });
                break;
            default:
                host.Children.Add(CreateFallbackBlock(block));
                break;
        }
    }

    private static Control CreateHeadingBlock(HeadingBlock heading)
    {
        var text = new SelectableTextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Foreground = HeadingBrush,
            FontWeight = FontWeight.Bold,
            Margin = new Avalonia.Thickness(0, 2, 0, 2)
        };

        text.FontSize = heading.Level switch
        {
            1 => 24,
            2 => 20,
            3 => 17,
            4 => 15,
            _ => 13
        };

        AddInlineChildren(text, heading.Inline);
        return text;
    }

    private static Control CreateParagraphBlock(ParagraphBlock paragraph)
    {
        var text = CreateSelectableTextBlock(fontSize: 12.5, foreground: BodyBrush);
        AddInlineChildren(text, paragraph.Inline);
        return text;
    }

    private static Control CreateQuoteBlock(QuoteBlock quote, int nestingLevel)
    {
        var inner = new StackPanel
        {
            Spacing = 10
        };

        foreach (var child in quote)
            AddBlock(inner, child, nestingLevel + 1);

        return new Border
        {
            BorderBrush = QuoteBorderBrush,
            BorderThickness = new Avalonia.Thickness(3, 0, 0, 0),
            Background = QuoteBackgroundBrush,
            Padding = new Avalonia.Thickness(12, 10),
            Margin = new Avalonia.Thickness(0, 2, 0, 2),
            Child = inner
        };
    }

    private static Control CreateListBlock(ListBlock list, int nestingLevel)
    {
        var stack = new StackPanel
        {
            Spacing = 6,
            Margin = new Avalonia.Thickness(nestingLevel * 18, 0, 0, 0)
        };

        int ordinal = int.TryParse(list.OrderedStart, out var parsedOrdinal)
            ? parsedOrdinal
            : 1;
        foreach (var item in list.OfType<ListItemBlock>())
        {
            stack.Children.Add(CreateListItemBlock(item, list.IsOrdered, ordinal, nestingLevel));
            ordinal++;
        }

        return stack;
    }

    private static Control CreateListItemBlock(ListItemBlock item, bool ordered, int ordinal, int nestingLevel)
    {
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*")
        };

        row.Children.Add(new TextBlock
        {
            Text = ordered ? $"{ordinal}." : "•",
            Foreground = BodyBrush,
            FontSize = 12.5,
            FontWeight = FontWeight.SemiBold,
            Margin = new Avalonia.Thickness(0, 1, 10, 0),
            VerticalAlignment = VerticalAlignment.Top
        });

        var content = new StackPanel
        {
            Spacing = 6
        };

        foreach (var child in item)
            AddBlock(content, child, nestingLevel + 1);

        Grid.SetColumn(content, 1);
        row.Children.Add(content);
        return row;
    }

    private static Control CreateCodeBlock(CodeBlock codeBlock)
    {
        var lines = codeBlock.Lines.ToString() ?? string.Empty;
        var text = CreateSelectableTextBlock(fontSize: 12, foreground: CodeForegroundBrush);
        text.FontFamily = FontFamily.Parse("Consolas, Menlo, Monaco, 'Courier New', monospace");
        text.Text = lines.TrimEnd('\r', '\n');

        return new Border
        {
            Background = CodeBackgroundBrush,
            CornerRadius = new Avalonia.CornerRadius(6),
            Padding = new Avalonia.Thickness(12, 10),
            Child = text
        };
    }

    private static Control CreateFallbackBlock(Block block)
    {
        var text = block switch
        {
            LeafBlock leaf when leaf.Inline != null => ExtractInlineText(leaf.Inline),
            LeafBlock leaf => leaf.Lines.ToString() ?? string.Empty,
            _ => block.ToString() ?? string.Empty
        };

        return CreateMessageBlock(text);
    }

    private static SelectableTextBlock CreateSelectableTextBlock(double fontSize, IBrush foreground)
        => new()
        {
            FontSize = fontSize,
            Foreground = foreground,
            TextWrapping = TextWrapping.Wrap
        };

    private static Control CreateMessageBlock(string text)
        => new SelectableTextBlock
        {
            Text = text,
            FontSize = 12.5,
            Foreground = MutedBrush,
            TextWrapping = TextWrapping.Wrap
        };

    private static void AddInlineChildren(SelectableTextBlock target, ContainerInline? container)
    {
        if (container == null)
            return;

        target.Inlines ??= new InlineCollection();
        foreach (var child in container)
            AddInline(target.Inlines, child);
    }

    private static void AddInline(InlineCollection inlines, Markdig.Syntax.Inlines.Inline inline)
    {
        switch (inline)
        {
            case LiteralInline literal:
                var text = literal.Content.ToString();
                if (!string.IsNullOrEmpty(text))
                    inlines.Add(new Run { Text = text, Foreground = BodyBrush });
                break;
            case LineBreakInline:
                inlines.Add(new LineBreak());
                break;
            case CodeInline code:
                inlines.Add(new Run
                {
                    Text = code.Content,
                    Foreground = CodeForegroundBrush,
                    Background = CodeBackgroundBrush,
                    FontFamily = FontFamily.Parse("Consolas, Menlo, Monaco, 'Courier New', monospace")
                });
                break;
            case EmphasisInline emphasis:
                AddEmphasisInline(inlines, emphasis);
                break;
            case LinkInline link when !link.IsImage:
                AddLinkInline(inlines, link);
                break;
            case ContainerInline childContainer:
                foreach (var child in childContainer)
                    AddInline(inlines, child);
                break;
        }
    }

    private static void AddEmphasisInline(InlineCollection inlines, EmphasisInline emphasis)
    {
        var span = new Span();
        if (emphasis.DelimiterCount >= 2)
            span.FontWeight = FontWeight.Bold;
        if (emphasis.DelimiterChar is '*' or '_')
            span.FontStyle = FontStyle.Italic;

        span.Inlines ??= new InlineCollection();
        foreach (var child in emphasis)
            AddInline(span.Inlines, child);

        inlines.Add(span);
    }

    private static void AddLinkInline(InlineCollection inlines, LinkInline link)
    {
        var text = string.IsNullOrWhiteSpace(ExtractInlineText(link))
            ? link.Url ?? string.Empty
            : ExtractInlineText(link);

        inlines.Add(new Run
        {
            Text = text,
            Foreground = LinkBrush,
            TextDecorations = TextDecorations.Underline
        });

        if (!string.IsNullOrWhiteSpace(link.Url))
        {
            inlines.Add(new Run
            {
                Text = $" ({link.Url})",
                Foreground = MutedBrush
            });
        }
    }

    private static string ExtractInlineText(ContainerInline container)
    {
        var parts = new List<string>();
        foreach (var child in container)
            AppendInlineText(parts, child);
        return string.Concat(parts);
    }

    private static void AppendInlineText(List<string> parts, Markdig.Syntax.Inlines.Inline inline)
    {
        switch (inline)
        {
            case LiteralInline literal:
                parts.Add(literal.Content.ToString());
                break;
            case CodeInline code:
                parts.Add(code.Content);
                break;
            case LineBreakInline:
                parts.Add(Environment.NewLine);
                break;
            case ContainerInline container:
                foreach (var child in container)
                    AppendInlineText(parts, child);
                break;
        }
    }

}
