using Avalonia.Controls;
using Avalonia.Interactivity;
using Codomon.Desktop.Models;
using System.Linq;

namespace Codomon.Desktop.Views;

/// <summary>
/// Small modal dialog used to create or edit a single <see cref="MappingRuleModel"/>.
/// Returns the edited model on OK, or <c>null</c> on Cancel.
/// </summary>
public class RuleEditorDialog : Window
{
    private readonly MappingRuleModel _rule;
    private MappingRuleModel? _result;

    private ComboBox? _ruleTypeCombo;
    private TextBox? _patternBox;
    private CheckBox? _enabledCheck;
    private TextBox? _notesBox;

    private static readonly (string Label, RuleType Value)[] RuleTypes =
    {
        ("Log Source Pattern",   RuleType.LogSourcePattern),
        ("Namespace Pattern",    RuleType.NamespacePattern),
        ("Class Name Pattern",   RuleType.ClassNamePattern),
        ("Folder Path Pattern",  RuleType.FolderPathPattern),
        ("Message Keyword",      RuleType.MessageKeywordPattern),
    };

    public RuleEditorDialog(MappingRuleModel rule)
    {
        _rule = rule;

        Title = rule.Id == rule.Id && string.IsNullOrEmpty(rule.Pattern)
            ? "Add Mapping Rule"
            : "Edit Mapping Rule";
        Width = 480;
        Height = 310;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#111820"));

        BuildContent();
    }

    private void BuildContent()
    {
        _ruleTypeCombo = new ComboBox { Width = double.NaN };
        foreach (var (label, _) in RuleTypes)
            _ruleTypeCombo.Items.Add(new ComboBoxItem { Content = label });

        // Select the current rule type.
        var selectedIndex = System.Array.FindIndex(RuleTypes, t => t.Value == _rule.RuleType);
        _ruleTypeCombo.SelectedIndex = selectedIndex >= 0 ? selectedIndex : 0;

        _patternBox = new TextBox
        {
            Text = _rule.Pattern,
            Watermark = "e.g. MyApp.Services.OrderService",
            Foreground = Avalonia.Media.Brushes.White,
            Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#1A2435")),
            Padding = new Avalonia.Thickness(6, 4)
        };
        _patternBox.KeyDown += (_, ke) =>
        {
            if (ke.Key == Avalonia.Input.Key.Enter) AcceptAndClose();
        };

        _enabledCheck = new CheckBox
        {
            Content = "Enabled",
            IsChecked = _rule.IsEnabled,
            Foreground = Avalonia.Media.Brushes.White
        };

        _notesBox = new TextBox
        {
            Text = _rule.Notes,
            Watermark = "Optional notes",
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
        okBtn.Click += (_, _) => AcceptAndClose();
        cancelBtn.Click += (_, _) => Close(null);

        var labelStyle = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#AABBCC"));

        Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(16),
            Spacing = 10,
            Children =
            {
                new TextBlock { Text = "Rule Type:", Foreground = labelStyle },
                _ruleTypeCombo,
                new TextBlock { Text = "Pattern (substring, case-insensitive):", Foreground = labelStyle },
                _patternBox,
                _enabledCheck,
                new TextBlock { Text = "Notes:", Foreground = labelStyle },
                _notesBox,
                new StackPanel
                {
                    Orientation = Avalonia.Layout.Orientation.Horizontal,
                    Spacing = 8,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                    Children = { okBtn, cancelBtn }
                }
            }
        };
    }

    private void AcceptAndClose()
    {
        var pattern = _patternBox?.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(pattern))
        {
            // Show minimal inline validation — highlight the field.
            if (_patternBox != null)
                _patternBox.BorderBrush = Avalonia.Media.Brushes.Red;
            return;
        }

        var selectedIndex = _ruleTypeCombo?.SelectedIndex ?? 0;
        var ruleType = selectedIndex >= 0 && selectedIndex < RuleTypes.Length
            ? RuleTypes[selectedIndex].Value
            : RuleType.LogSourcePattern;

        _result = new MappingRuleModel
        {
            Id = _rule.Id,
            TargetType = _rule.TargetType,
            TargetId = _rule.TargetId,
            RuleType = ruleType,
            Pattern = pattern,
            IsEnabled = _enabledCheck?.IsChecked ?? true,
            Notes = _notesBox?.Text?.Trim() ?? string.Empty
        };

        Close(_result);
    }
}
