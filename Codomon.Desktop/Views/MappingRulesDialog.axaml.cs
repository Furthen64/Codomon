using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Interactivity;
using Codomon.Desktop.Models;
using System.Collections.Generic;
using System.Linq;

namespace Codomon.Desktop.Views;

/// <summary>
/// Dialog for viewing and editing mapping rules that belong to a specific System or Module.
/// </summary>
public class MappingRulesDialog : Window
{
    private readonly List<MappingRuleModel> _rules;
    private readonly string _targetId;
    private readonly RuleTargetType _targetType;

    private ListBox _listBox = null!;
    private Button  _editBtn   = null!;
    private Button  _deleteBtn = null!;
    private Button  _toggleBtn = null!;

    /// <summary>True when any rule was added, edited, or deleted during this session.</summary>
    public bool HasChanges { get; private set; }

    public MappingRulesDialog(
        List<MappingRuleModel> workspaceRules,
        RuleTargetType targetType,
        string targetId,
        string targetName)
    {
        _rules = workspaceRules;
        _targetType = targetType;
        _targetId = targetId;

        Title = "Mapping Rules";
        Width = 720;
        Height = 520;
        MinWidth = 600;
        MinHeight = 380;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#111820"));

        BuildContent(targetType, targetName);
        RefreshList();
    }

    private void BuildContent(RuleTargetType targetType, string targetName)
    {
        var labelColor  = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#AABBCC"));
        var subtleColor = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#556677"));

        var addBtn = new Button { Content = "✚ Add Rule", Padding = new Avalonia.Thickness(10, 4) };
        _editBtn   = new Button { Content = "✏ Edit",     Padding = new Avalonia.Thickness(10, 4), IsEnabled = false };
        _deleteBtn = new Button
        {
            Content = "🗑 Delete",
            Padding = new Avalonia.Thickness(10, 4),
            IsEnabled = false,
            Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#3A1A1A")),
            Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#FF7070"))
        };
        _toggleBtn = new Button { Content = "⏸ Disable", Padding = new Avalonia.Thickness(10, 4), IsEnabled = false };
        var closeBtn = new Button
        {
            Content = "Close",
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            Padding = new Avalonia.Thickness(20, 5),
            Margin = new Avalonia.Thickness(0, 10, 0, 0)
        };

        addBtn.Click     += OnAddRuleClick;
        _editBtn.Click   += OnEditRuleClick;
        _deleteBtn.Click += OnDeleteRuleClick;
        _toggleBtn.Click += OnToggleRuleClick;
        closeBtn.Click   += OnCloseClick;

        _listBox = new ListBox { Background = Avalonia.Media.Brushes.Transparent };
        _listBox.SelectionChanged += OnRulesListSelectionChanged;

        var listBorder = new Border
        {
            Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#1A2435")),
            CornerRadius = new Avalonia.CornerRadius(4),
            BorderBrush = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#2A3F5A")),
            BorderThickness = new Avalonia.Thickness(1),
            Child = _listBox
        };

        var headerPanel = new StackPanel
        {
            Spacing = 4,
            Margin = new Avalonia.Thickness(0, 0, 0, 10),
            Children =
            {
                new TextBlock
                {
                    Text = $"{targetType}: {targetName}",
                    FontSize = 16,
                    FontWeight = Avalonia.Media.FontWeight.SemiBold,
                    Foreground = labelColor
                },
                new TextBlock
                {
                    Text = "Mapping rules control how log entries are linked to this item during replay.",
                    FontSize = 11,
                    Foreground = subtleColor,
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap
                }
            }
        };

        var buttonsPanel = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            Spacing = 6,
            Margin = new Avalonia.Thickness(0, 0, 0, 8),
            Children = { addBtn, _editBtn, _deleteBtn, _toggleBtn }
        };

        var dock = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(headerPanel,  Dock.Top);
        DockPanel.SetDock(buttonsPanel, Dock.Top);
        DockPanel.SetDock(closeBtn,     Dock.Bottom);
        dock.Children.Add(headerPanel);
        dock.Children.Add(buttonsPanel);
        dock.Children.Add(closeBtn);
        dock.Children.Add(listBorder);

        Content = new Border { Margin = new Avalonia.Thickness(14), Child = dock };
    }

    // ── List management ────────────────────────────────────────────────────────

    private List<MappingRuleModel> TargetRules =>
        _rules.Where(r => r.TargetType == _targetType && r.TargetId == _targetId).ToList();

    private void RefreshList()
    {
        _listBox.ItemTemplate = BuildRuleTemplate();
        _listBox.ItemsSource = null;
        _listBox.ItemsSource = TargetRules;
        UpdateButtonStates(null);
    }

    private static FuncDataTemplate<MappingRuleModel> BuildRuleTemplate()
    {
        return new FuncDataTemplate<MappingRuleModel>((rule, _) =>
        {
            if (rule == null) return new TextBlock();

            var grid = new Grid
            {
                ColumnDefinitions = new Avalonia.Controls.ColumnDefinitions("24,150,*,80")
            };

            var enabledDot = new TextBlock
            {
                Text = "●",
                FontSize = 10,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                Foreground = rule.IsEnabled
                    ? new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#44BB44"))
                    : new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#555555"))
            };

            var typeText = new TextBlock
            {
                Text = FormatRuleType(rule.RuleType),
                FontSize = 12,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#AABBCC")),
                Margin = new Avalonia.Thickness(4, 0)
            };

            var patternText = new TextBlock
            {
                Text = rule.Pattern,
                FontSize = 12,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                Foreground = Avalonia.Media.Brushes.White,
                FontFamily = new Avalonia.Media.FontFamily("Monospace"),
                TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis,
                Margin = new Avalonia.Thickness(4, 0)
            };

            var notesText = new TextBlock
            {
                Text = rule.Notes,
                FontSize = 11,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#778899")),
                TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis,
                Margin = new Avalonia.Thickness(4, 0)
            };

            Grid.SetColumn(enabledDot,  0);
            Grid.SetColumn(typeText,    1);
            Grid.SetColumn(patternText, 2);
            Grid.SetColumn(notesText,   3);
            grid.Children.Add(enabledDot);
            grid.Children.Add(typeText);
            grid.Children.Add(patternText);
            grid.Children.Add(notesText);

            return new Border { Padding = new Avalonia.Thickness(2, 3), Child = grid };
        });
    }

    private static string FormatRuleType(RuleType rt) => rt switch
    {
        RuleType.LogSourcePattern       => "Log Source",
        RuleType.NamespacePattern       => "Namespace",
        RuleType.ClassNamePattern       => "Class Name",
        RuleType.FolderPathPattern      => "Folder Path",
        RuleType.MessageKeywordPattern  => "Msg Keyword",
        _                               => rt.ToString()
    };

    private void UpdateButtonStates(MappingRuleModel? selected)
    {
        bool has = selected != null;
        _editBtn.IsEnabled   = has;
        _deleteBtn.IsEnabled = has;
        _toggleBtn.IsEnabled = has;
        if (has)
            _toggleBtn.Content = selected!.IsEnabled ? "⏸ Disable" : "▶ Enable";
    }

    private void OnRulesListSelectionChanged(object? sender, SelectionChangedEventArgs e)
        => UpdateButtonStates(_listBox.SelectedItem as MappingRuleModel);

    // ── Button handlers ────────────────────────────────────────────────────────

    private async void OnAddRuleClick(object? sender, RoutedEventArgs e)
    {
        var editor = new RuleEditorDialog(new MappingRuleModel
        {
            TargetType = _targetType,
            TargetId = _targetId
        });
        var result = await editor.ShowDialog<MappingRuleModel?>(this);
        if (result == null) return;

        _rules.Add(result);
        HasChanges = true;
        RefreshList();
    }

    private async void OnEditRuleClick(object? sender, RoutedEventArgs e)
    {
        if (_listBox.SelectedItem is not MappingRuleModel original) return;

        var copy = CloneRule(original);
        var editor = new RuleEditorDialog(copy);
        var result = await editor.ShowDialog<MappingRuleModel?>(this);
        if (result == null) return;

        var idx = _rules.IndexOf(original);
        if (idx >= 0)
        {
            _rules[idx] = result;
            HasChanges = true;
        }
        RefreshList();
    }

    private async void OnDeleteRuleClick(object? sender, RoutedEventArgs e)
    {
        if (_listBox.SelectedItem is not MappingRuleModel rule) return;

        bool confirmed = await ShowConfirmDeleteAsync(rule.Pattern);
        if (!confirmed) return;

        _rules.Remove(rule);
        HasChanges = true;
        RefreshList();
    }

    private void OnToggleRuleClick(object? sender, RoutedEventArgs e)
    {
        if (_listBox.SelectedItem is not MappingRuleModel rule) return;

        rule.IsEnabled = !rule.IsEnabled;
        HasChanges = true;
        RefreshList();
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static MappingRuleModel CloneRule(MappingRuleModel src) => new()
    {
        Id = src.Id,
        TargetType = src.TargetType,
        TargetId = src.TargetId,
        RuleType = src.RuleType,
        Pattern = src.Pattern,
        IsEnabled = src.IsEnabled,
        Notes = src.Notes
    };

    private async System.Threading.Tasks.Task<bool> ShowConfirmDeleteAsync(string pattern)
    {
        bool confirmed = false;
        var dialog = new Window
        {
            Title = "Delete Rule",
            Width = 400,
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
                    Text = $"Delete rule with pattern \"{pattern}\"? This cannot be undone.",
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
}
