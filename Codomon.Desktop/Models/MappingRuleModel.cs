using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Codomon.Desktop.Models;

public enum RuleTargetType { System, Module }

/// <summary>
/// The kind of data field a mapping rule matches against.
/// Priority order (highest → lowest): LogSourcePattern, NamespacePattern,
/// ClassNamePattern, FolderPathPattern, MessageKeywordPattern.
/// </summary>
public enum RuleType
{
    LogSourcePattern,
    NamespacePattern,
    ClassNamePattern,
    FolderPathPattern,
    MessageKeywordPattern
}

/// <summary>
/// A single workspace-level rule that maps log data to a System or Module.
/// Rules are evaluated by the <see cref="Codomon.Desktop.Services.LogMatcher"/> in
/// priority order defined by <see cref="RuleType"/>.
/// </summary>
public class MappingRuleModel : INotifyPropertyChanged
{
    private string _id = Guid.NewGuid().ToString();
    private RuleTargetType _targetType;
    private string _targetId = string.Empty;
    private RuleType _ruleType;
    private string _pattern = string.Empty;
    private bool _isEnabled = true;
    private string _notes = string.Empty;

    public string Id
    {
        get => _id;
        set { _id = value; OnPropertyChanged(); }
    }

    public RuleTargetType TargetType
    {
        get => _targetType;
        set { _targetType = value; OnPropertyChanged(); }
    }

    public string TargetId
    {
        get => _targetId;
        set { _targetId = value; OnPropertyChanged(); }
    }

    public RuleType RuleType
    {
        get => _ruleType;
        set { _ruleType = value; OnPropertyChanged(); }
    }

    public string Pattern
    {
        get => _pattern;
        set { _pattern = value; OnPropertyChanged(); }
    }

    public bool IsEnabled
    {
        get => _isEnabled;
        set { _isEnabled = value; OnPropertyChanged(); }
    }

    public string Notes
    {
        get => _notes;
        set { _notes = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
