using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Codomon.Desktop.Models;
using Codomon.Desktop.Models.SystemMap;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Codomon.Desktop.Views;

public partial class CodeBrowserView : UserControl
{
    private static readonly HashSet<string> ExcludedDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git",
        ".vs",
        ".idea",
        "bin",
        "obj",
        "node_modules"
    };

    private static readonly string[] ByteSizeUnits = ["B", "KB", "MB", "GB"];
    private const int MaxPreviewCharacters = 120_000;
    private const int MaxPreviewLines = 2_000;

    // Lines beyond this threshold are rendered without syntax highlighting for performance
    private const int SyntaxHighlightLineThreshold = 1_500;

    // ── Syntax-highlighting colours (VS Code dark+ palette) ──────────────────
    private static readonly IBrush ClrKeyword     = new SolidColorBrush(Color.Parse("#569CD6"));
    private static readonly IBrush ClrString      = new SolidColorBrush(Color.Parse("#CE9178"));
    private static readonly IBrush ClrComment     = new SolidColorBrush(Color.Parse("#6A9955"));
    private static readonly IBrush ClrNumber      = new SolidColorBrush(Color.Parse("#B5CEA8"));
    private static readonly IBrush ClrPreprocessor = new SolidColorBrush(Color.Parse("#C586C0"));
    private static readonly IBrush ClrPlain       = new SolidColorBrush(Color.Parse("#D4D4D4"));

    // Matches C# tokens in priority order.
    // BlockComment uses an "unrolled loop" pattern to avoid catastrophic backtracking
    // on files with unclosed /* comments. A 2-second timeout is also applied.
    private static readonly Regex CSharpTokenRegex = new(
        @"(?<BlockComment>/\*[^*]*(?:\*+(?!/)[^*]*)*\*/)" +
        @"|(?<LineComment>//[^\n\r]*)" +
        @"|(?<VerbatimString>@""(?:[^""]|"""")*"")" +
        @"|(?<String>""(?:[^""\\]|\\.)*"")" +
        @"|(?<Char>'(?:[^'\\]|\\.)*')" +
        @"|(?<Number>\b\d+(?:\.\d+)?(?:[eE][+-]?\d+)?[fFdDmMlLuUbB]{0,2}\b)" +
        @"|(?<Preprocessor>^\s*#(?:if|else|elif|endif|define|undef|warning|error|pragma|region|endregion|line|nullable)\b)" +
        @"|(?<Keyword>\b(?:abstract|as|base|bool|break|byte|case|catch|char|checked|class|const|continue|decimal|default|delegate|do|double|else|enum|event|explicit|extern|false|finally|fixed|float|for|foreach|goto|if|implicit|in|int|interface|internal|is|lock|long|namespace|new|null|object|operator|out|override|params|private|protected|public|readonly|ref|return|sbyte|sealed|short|sizeof|stackalloc|static|string|struct|switch|this|throw|true|try|typeof|uint|ulong|unchecked|unsafe|ushort|using|var|virtual|void|volatile|while|async|await|yield|get|set|add|remove|value|partial|record|init|with|required|from|where|select|group|join|into|orderby|ascending|descending|let|on|equals|by|file|scoped|global)\b)",
        RegexOptions.Compiled | RegexOptions.Multiline,
        matchTimeout: TimeSpan.FromSeconds(2));

    private WorkspaceModel? _workspace;
    private CodeBrowserItem? _selectedItem;
    private string _sourceRoot = string.Empty;

    private sealed class CodeBrowserItem
    {
        public string Name { get; init; } = string.Empty;
        public string FullPath { get; init; } = string.Empty;
        public string RelativePath { get; init; } = string.Empty;
        public bool IsDirectory { get; init; }
        public List<CodeBrowserItem> Children { get; } = new();
        public HashSet<SystemKind> Tags { get; } = new();
    }

    public CodeBrowserView()
    {
        InitializeComponent();

        this.FindControl<Button>("RefreshBrowserButton")!.Click += (_, _) => LoadWorkspace(_workspace);
        this.FindControl<Button>("OpenSelectedItemButton")!.Click += (_, _) => OpenSelectedItem();
    }

    public void LoadWorkspace(WorkspaceModel? workspace)
    {
        _workspace = workspace;
        _selectedItem = null;
        _sourceRoot = ResolveSourceRoot(workspace);

        var tree = this.FindControl<TreeView>("CodeTreeView")!;
        tree.ItemsSource = null;
        this.FindControl<Button>("OpenSelectedItemButton")!.IsEnabled = false;

        if (workspace == null || string.IsNullOrWhiteSpace(_sourceRoot) || !Directory.Exists(_sourceRoot))
        {
            this.FindControl<TextBlock>("CodeRootText")!.Text = "Open a workspace to browse its code.";
            this.FindControl<Border>("NoScanBanner")!.IsVisible = false;
            ShowPlaceholder("Open a workspace to browse its codebase.");
            return;
        }

        this.FindControl<TextBlock>("CodeRootText")!.Text = _sourceRoot;

        // Show banner when a workspace is open but no Roslyn scan data has been applied
        var hasScanData = workspace.SystemMap.AllCodeNodes.Any();
        this.FindControl<Border>("NoScanBanner")!.IsVisible = !hasScanData;

        var rootItem = BuildDirectoryItem(_sourceRoot, _sourceRoot, BuildFileTagMap(workspace));
        if (rootItem == null)
        {
            ShowPlaceholder("The selected source path could not be read.");
            return;
        }

        tree.ItemsSource = new List<TreeViewItem> { CreateTreeViewItem(rootItem, isRoot: true) };
        ShowDirectory(rootItem);
    }

    private static string ResolveSourceRoot(WorkspaceModel? workspace)
    {
        if (workspace == null || string.IsNullOrWhiteSpace(workspace.SourceProjectPath))
            return string.Empty;

        var sourcePath = workspace.SourceProjectPath.Trim();
        if (Directory.Exists(sourcePath))
            return sourcePath;

        if (File.Exists(sourcePath))
            return Path.GetDirectoryName(sourcePath) ?? string.Empty;

        return Path.HasExtension(sourcePath)
            ? Path.GetDirectoryName(sourcePath) ?? string.Empty
            : sourcePath;
    }

    private static Dictionary<string, HashSet<SystemKind>> BuildFileTagMap(WorkspaceModel workspace)
    {
        var map = new Dictionary<string, HashSet<SystemKind>>(StringComparer.OrdinalIgnoreCase);
        var systemsById = workspace.SystemMap.Systems.ToDictionary(s => s.Id, s => s, StringComparer.Ordinal);

        foreach (var module in workspace.SystemMap.AllModules)
        {
            var kinds = ResolveSystemKindsForModule(module, workspace.SystemMap.Systems, systemsById);
            if (kinds.Count == 0)
                continue;

            foreach (var codeNode in module.CodeNodes)
            {
                var key = NormalizeRelativePath(codeNode.FilePath);
                if (string.IsNullOrWhiteSpace(key))
                    continue;

                if (!map.TryGetValue(key, out var bucket))
                {
                    bucket = new HashSet<SystemKind>();
                    map[key] = bucket;
                }

                bucket.UnionWith(kinds);
            }
        }

        return map;
    }

    private static HashSet<SystemKind> ResolveSystemKindsForModule(
        ModuleModel module,
        IEnumerable<SystemModel> systems,
        IReadOnlyDictionary<string, SystemModel> systemsById)
    {
        var ownerIds = new HashSet<string>(module.SystemIds.Where(id => !string.IsNullOrWhiteSpace(id)), StringComparer.Ordinal);

        foreach (var system in systems)
        {
            if (system.Modules.Any(m => string.Equals(m.Id, module.Id, StringComparison.Ordinal)))
                ownerIds.Add(system.Id);
        }

        var result = new HashSet<SystemKind>();
        foreach (var ownerId in ownerIds)
        {
            if (systemsById.TryGetValue(ownerId, out var system))
                result.Add(system.Kind);
        }

        return result;
    }

    private CodeBrowserItem? BuildDirectoryItem(
        string directoryPath,
        string rootPath,
        IReadOnlyDictionary<string, HashSet<SystemKind>> fileTagMap)
    {
        try
        {
            var item = new CodeBrowserItem
            {
                Name = Path.GetFileName(directoryPath),
                FullPath = directoryPath,
                RelativePath = NormalizeRelativePath(Path.GetRelativePath(rootPath, directoryPath)),
                IsDirectory = true
            };

            var childDirectories = Directory.EnumerateDirectories(directoryPath)
                .Where(ShouldIncludeDirectory)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase);

            foreach (var childDirectory in childDirectories)
            {
                var child = BuildDirectoryItem(childDirectory, rootPath, fileTagMap);
                if (child == null) continue;
                item.Children.Add(child);
                item.Tags.UnionWith(child.Tags);
            }

            var childFiles = Directory.EnumerateFiles(directoryPath)
                .Where(ShouldIncludeFile)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase);

            foreach (var childFile in childFiles)
            {
                var fileItem = new CodeBrowserItem
                {
                    Name = Path.GetFileName(childFile),
                    FullPath = childFile,
                    RelativePath = NormalizeRelativePath(Path.GetRelativePath(rootPath, childFile)),
                    IsDirectory = false
                };

                if (fileTagMap.TryGetValue(fileItem.RelativePath, out var tags))
                    fileItem.Tags.UnionWith(tags);

                item.Children.Add(fileItem);
                item.Tags.UnionWith(fileItem.Tags);
            }
            return item;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static bool ShouldIncludeDirectory(string directoryPath)
    {
        var name = Path.GetFileName(directoryPath);
        return !string.IsNullOrWhiteSpace(name) &&
               !name.StartsWith(".", StringComparison.Ordinal) &&
               !ExcludedDirectoryNames.Contains(name);
    }

    private static bool ShouldIncludeFile(string filePath)
    {
        var name = Path.GetFileName(filePath);
        return !string.IsNullOrWhiteSpace(name) && !name.StartsWith(".", StringComparison.Ordinal);
    }

    private static string NormalizeRelativePath(string path)
        => (path ?? string.Empty).Replace('\\', '/');

    private TreeViewItem CreateTreeViewItem(CodeBrowserItem item, bool isRoot = false)
    {
        var treeItem = new TreeViewItem
        {
            Header = BuildTreeHeader(item),
            Tag = item,
            IsExpanded = isRoot
        };

        foreach (var child in item.Children.Where(c => c.IsDirectory))
            treeItem.Items.Add(CreateTreeViewItem(child));

        foreach (var child in item.Children.Where(c => !c.IsDirectory))
            treeItem.Items.Add(CreateTreeViewItem(child));

        return treeItem;
    }

    private Control BuildTreeHeader(CodeBrowserItem item)
    {
        var row = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            Spacing = 6
        };

        row.Children.Add(new TextBlock
        {
            Text = item.IsDirectory ? "📁" : "📄",
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        });

        row.Children.Add(new TextBlock
        {
            Text = item.Name,
            Foreground = Brushes.White,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        });

        foreach (var kind in item.Tags.OrderBy(k => k.ToString(), StringComparer.Ordinal))
            row.Children.Add(CreateTagBadge(kind));

        return row;
    }

    private Border CreateTagBadge(SystemKind kind)
    {
        var (background, foreground) = GetTagColors(kind);
        return new Border
        {
            Background = new SolidColorBrush(Color.Parse(background)),
            CornerRadius = new Avalonia.CornerRadius(10),
            Padding = new Avalonia.Thickness(8, 2),
            Child = new TextBlock
            {
                Text = kind.ToString(),
                FontSize = 10,
                FontWeight = FontWeight.SemiBold,
                Foreground = new SolidColorBrush(Color.Parse(foreground))
            }
        };
    }

    private static (string Background, string Foreground) GetTagColors(SystemKind kind) => kind switch
    {
        SystemKind.DesktopApp => ("#1A3A5A", "#8BD4FF"),
        SystemKind.WebApp => ("#2A1A4A", "#D5AEFF"),
        SystemKind.BackendService => ("#163A29", "#86E5A6"),
        SystemKind.WorkerService => ("#3A2B16", "#FFCF92"),
        SystemKind.ScheduledJob => ("#3A2516", "#FFBA80"),
        SystemKind.CliTool => ("#1E324A", "#9FCFFF"),
        SystemKind.DatabaseProcess => ("#3A1A1A", "#FF9F9F"),
        SystemKind.LibraryOnly => ("#2D323A", "#D1D7DF"),
        _ => ("#25303B", "#AABBCC")
    };

    private void OnCodeTreeSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count == 0 || e.AddedItems[0] is not TreeViewItem treeItem || treeItem.Tag is not CodeBrowserItem item)
            return;

        _selectedItem = item;
        this.FindControl<Button>("OpenSelectedItemButton")!.IsEnabled = true;

        if (item.IsDirectory)
            ShowDirectory(item);
        else
            ShowFile(item);
    }

    private void ShowDirectory(CodeBrowserItem item)
    {
        UpdateHeader(item, $"{item.Children.Count} item(s)");

        var itemsControl = this.FindControl<ItemsControl>("DirectoryChildrenItemsControl")!;
        itemsControl.ItemsSource = item.Children.Select(child =>
            new TextBlock
            {
                Text = $"{(child.IsDirectory ? "📁" : "📄")}  {child.Name}",
                Foreground = new SolidColorBrush(Color.Parse(child.IsDirectory ? "#AABBCC" : "#88AABB")),
                FontFamily = new FontFamily("Monospace"),
                Margin = new Avalonia.Thickness(0, 0, 0, 2)
            }).ToList();

        this.FindControl<TextBlock>("DirectorySummaryText")!.Text =
            item.Children.Count == 0
                ? "This folder is empty."
                : $"Showing {item.Children.Count} direct child item(s).";

        this.FindControl<ScrollViewer>("DirectoryPreviewScrollViewer")!.IsVisible = true;
        this.FindControl<ScrollViewer>("CodePreviewScrollViewer")!.IsVisible = false;
        this.FindControl<TextBlock>("PreviewPlaceholderText")!.IsVisible = false;
    }

    private void ShowFile(CodeBrowserItem item)
    {
        UpdateHeader(item, BuildFileMetadata(item.FullPath));

        var previewText = TryReadPreview(item.FullPath);
        var block = this.FindControl<SelectableTextBlock>("CodePreviewBlock")!;
        PopulateCodePreview(block, previewText, item.FullPath);

        this.FindControl<ScrollViewer>("CodePreviewScrollViewer")!.IsVisible = true;
        this.FindControl<ScrollViewer>("DirectoryPreviewScrollViewer")!.IsVisible = false;
        this.FindControl<TextBlock>("PreviewPlaceholderText")!.IsVisible = false;
    }

    private void ShowPlaceholder(string message)
    {
        this.FindControl<TextBlock>("SelectedItemPathText")!.Text = "Code Browser";
        this.FindControl<TextBlock>("SelectedItemMetaText")!.Text = string.Empty;
        this.FindControl<WrapPanel>("SelectedItemTagsPanel")!.Children.Clear();
        this.FindControl<TextBlock>("PreviewPlaceholderText")!.Text = message;
        this.FindControl<TextBlock>("PreviewPlaceholderText")!.IsVisible = true;
        this.FindControl<ScrollViewer>("CodePreviewScrollViewer")!.IsVisible = false;
        this.FindControl<ScrollViewer>("DirectoryPreviewScrollViewer")!.IsVisible = false;
    }

    private void UpdateHeader(CodeBrowserItem item, string metadata)
    {
        var displayPath = item.RelativePath == "." ? item.FullPath : item.RelativePath;
        this.FindControl<TextBlock>("SelectedItemPathText")!.Text = displayPath;
        this.FindControl<TextBlock>("SelectedItemMetaText")!.Text = metadata;

        var tagsPanel = this.FindControl<WrapPanel>("SelectedItemTagsPanel")!;
        tagsPanel.Children.Clear();
        foreach (var kind in item.Tags.OrderBy(k => k.ToString(), StringComparer.Ordinal))
            tagsPanel.Children.Add(CreateTagBadge(kind));
    }

    private static string BuildFileMetadata(string fullPath)
    {
        try
        {
            var info = new FileInfo(fullPath);
            return $"{FormatByteSize(info.Length)} · {info.Extension} · Last modified {info.LastWriteTime:yyyy-MM-dd HH:mm}";
        }
        catch (IOException)
        {
            return "File metadata unavailable.";
        }
        catch (UnauthorizedAccessException)
        {
            return "File metadata unavailable.";
        }
    }

    private static string TryReadPreview(string fullPath)
    {
        try
        {
            using var stream = File.OpenRead(fullPath);
            var buffer = new byte[Math.Min(8192, (int)stream.Length)];
            var read = stream.Read(buffer, 0, buffer.Length);
            if (buffer.Take(read).Any(b => b == 0))
                return "Binary or unsupported file preview.";

            stream.Position = 0;
            using var reader = new StreamReader(stream);
            var lines = new List<string>();
            int totalCharacters = 0;

            while (!reader.EndOfStream && lines.Count < MaxPreviewLines && totalCharacters < MaxPreviewCharacters)
            {
                var line = reader.ReadLine() ?? string.Empty;
                lines.Add(line);
                totalCharacters += line.Length + Environment.NewLine.Length;
            }

            var preview = string.Join(Environment.NewLine, lines);
            if (!reader.EndOfStream)
                preview += $"{Environment.NewLine}{Environment.NewLine}… preview truncated …";

            return preview;
        }
        catch (IOException ex)
        {
            return $"Unable to read file preview: {ex.Message}";
        }
        catch (UnauthorizedAccessException ex)
        {
            return $"Unable to read file preview: {ex.Message}";
        }
        catch (DecoderFallbackException)
        {
            return "Binary or unsupported file preview.";
        }
    }

    private static string FormatByteSize(long bytes)
    {
        double size = bytes;
        int unitIndex = 0;

        while (size >= 1024 && unitIndex < ByteSizeUnits.Length - 1)
        {
            size /= 1024;
            unitIndex++;
        }

        return $"{size:0.#} {ByteSizeUnits[unitIndex]}";
    }

    // ── Code preview / syntax highlighting ───────────────────────────────────

    private static void PopulateCodePreview(SelectableTextBlock block, string text, string filePath)
    {
        block.Inlines?.Clear();

        if (string.IsNullOrEmpty(text))
            return;

        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        var lines = text.Split('\n');

        if (ext == ".cs" && lines.Length <= SyntaxHighlightLineThreshold)
            BuildCSharpHighlightedInlines(block, text, lines);
        else
            BuildPlainInlines(block, lines);
    }

    private static void BuildPlainInlines(SelectableTextBlock block, string[] lines)
    {
        block.Inlines ??= new InlineCollection();
        for (int i = 0; i < lines.Length; i++)
        {
            block.Inlines.Add(new Run { Text = lines[i].TrimEnd('\r'), Foreground = ClrPlain });
            if (i < lines.Length - 1)
                block.Inlines.Add(new LineBreak());
        }
    }

    private static void BuildCSharpHighlightedInlines(SelectableTextBlock block, string text, string[] lines)
    {
        block.Inlines ??= new InlineCollection();

        // We tokenize the full text at once so multi-line block comments work correctly,
        // then track line breaks ourselves.
        try
        {
            int pos = 0;
            foreach (Match m in CSharpTokenRegex.Matches(text))
            {
                // Emit plain text (including any newlines) between the last match and this one
                if (m.Index > pos)
                    EmitSegment(block, text.AsSpan(pos, m.Index - pos));

                var brush = m.Groups["BlockComment"].Success || m.Groups["LineComment"].Success
                    ? ClrComment
                    : m.Groups["VerbatimString"].Success || m.Groups["String"].Success || m.Groups["Char"].Success
                        ? ClrString
                        : m.Groups["Number"].Success
                            ? ClrNumber
                            : m.Groups["Preprocessor"].Success
                                ? ClrPreprocessor
                                : ClrKeyword; // Keyword group

                EmitSegment(block, m.Value.AsSpan(), brush);
                pos = m.Index + m.Length;
            }

            // Remainder after last match
            if (pos < text.Length)
                EmitSegment(block, text.AsSpan(pos));
        }
        catch (RegexMatchTimeoutException)
        {
            // Timeout: fall back to plain rendering
            block.Inlines?.Clear();
            BuildPlainInlines(block, lines);
        }
    }

    /// <summary>
    /// Emits a (possibly multi-line) text segment as a series of <see cref="Run"/> and
    /// <see cref="LineBreak"/> inlines, honouring both \n and \r\n line endings.
    /// </summary>
    private static void EmitSegment(SelectableTextBlock block, ReadOnlySpan<char> span, IBrush? brush = null)
    {
        brush ??= ClrPlain;
        int start = 0;
        for (int i = 0; i < span.Length; i++)
        {
            if (span[i] != '\n') continue;

            // Slice up to (but not including) \r if present
            int end = i > 0 && span[i - 1] == '\r' ? i - 1 : i;
            var lineText = span[start..end].ToString();
            if (lineText.Length > 0)
                block.Inlines!.Add(new Run { Text = lineText, Foreground = brush });
            block.Inlines!.Add(new LineBreak());
            start = i + 1;
        }

        // Trailing text after last newline
        if (start < span.Length)
        {
            var tail = span[start..].ToString();
            if (tail.Length > 0)
                block.Inlines!.Add(new Run { Text = tail, Foreground = brush });
        }
    }

    private void OpenSelectedItem()
    {
        if (_selectedItem == null || string.IsNullOrWhiteSpace(_selectedItem.FullPath))
            return;

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = _selectedItem.FullPath,
                UseShellExecute = true
            });
        }
        catch (Exception ex) when (ex is Win32Exception or UnauthorizedAccessException or FileNotFoundException)
        {
            AppLogger.Error($"[CodeBrowser] Failed to open '{_selectedItem.FullPath}': {ex.Message}");
        }
    }
}
