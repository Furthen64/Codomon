using Avalonia;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Codomon.Desktop.ViewModels;

public class NodeViewModel : INotifyPropertyChanged
{
    public const double MinSizeMultiplier = 0.8;
    public const double MaxSizeMultiplier = 2.2;
    public const double SizeStep = 0.2;

    private string _key = string.Empty;
    private string _title = string.Empty;
    private string _subtitle = string.Empty;
    private string _kindLabel = string.Empty;
    private string _kindBadgeBackground = "#1A2435";
    private string _kindBadgeForeground = "#AABBCC";
    private string _entityType = string.Empty;
    private string _confidence = string.Empty;
    private string _fullName = string.Empty;
    private string _moduleName = string.Empty;
    private string _systemName = string.Empty;
    private string _summary = string.Empty;
    private Point _location;
    private int _childCount;
    private bool _isCodeLeaf;
    private IReadOnlyList<string> _relatedFiles = Array.Empty<string>();
    private double _sizeMultiplier = 1d;

    public string Key
    {
        get => _key;
        set { _key = value; OnPropertyChanged(); }
    }

    public string Title
    {
        get => _title;
        set { _title = value; OnPropertyChanged(); }
    }

    public string Subtitle
    {
        get => _subtitle;
        set { _subtitle = value; OnPropertyChanged(); }
    }

    public string KindLabel
    {
        get => _kindLabel;
        set { _kindLabel = value; OnPropertyChanged(); }
    }

    public string KindBadgeBackground
    {
        get => _kindBadgeBackground;
        set { _kindBadgeBackground = value; OnPropertyChanged(); }
    }

    public string KindBadgeForeground
    {
        get => _kindBadgeForeground;
        set { _kindBadgeForeground = value; OnPropertyChanged(); }
    }

    public string EntityType
    {
        get => _entityType;
        set { _entityType = value; OnPropertyChanged(); }
    }

    public string Confidence
    {
        get => _confidence;
        set { _confidence = value; OnPropertyChanged(); }
    }

    public string FullName
    {
        get => _fullName;
        set { _fullName = value; OnPropertyChanged(); }
    }

    public string ModuleName
    {
        get => _moduleName;
        set { _moduleName = value; OnPropertyChanged(); }
    }

    public string SystemName
    {
        get => _systemName;
        set { _systemName = value; OnPropertyChanged(); }
    }

    public string Summary
    {
        get => _summary;
        set { _summary = value; OnPropertyChanged(); }
    }

    public IReadOnlyList<string> RelatedFiles
    {
        get => _relatedFiles;
        set { _relatedFiles = value; OnPropertyChanged(); }
    }

    public Point Location
    {
        get => _location;
        set { _location = value; OnPropertyChanged(); }
    }

    /// <summary>Number of outgoing edges (children) this node has.</summary>
    public int ChildCount
    {
        get => _childCount;
        set
        {
            _childCount = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(BorderThickness));
        }
    }

    /// <summary>
    /// Visual border thickness that scales with the number of children:
    /// 1 px for 0 children, up to 6 px for 5 or more children.
    /// </summary>
    public Thickness BorderThickness => new(Math.Clamp(_childCount + 1, 1, 6));

    /// <summary>Input connectors displayed on the left side of the node.</summary>
    public ObservableCollection<ConnectorViewModel> Inputs { get; } = new() { new ConnectorViewModel() };

    /// <summary>Output connectors displayed on the right side of the node.</summary>
    public ObservableCollection<ConnectorViewModel> Outputs { get; } = new() { new ConnectorViewModel() };

    /// <summary>Convenience accessor for the single input connector.</summary>
    public ConnectorViewModel InputConnector => Inputs[0];

    /// <summary>Convenience accessor for the single output connector.</summary>
    public ConnectorViewModel OutputConnector => Outputs[0];

    public bool IsCodeLeaf
    {
        get => _isCodeLeaf;
        set { _isCodeLeaf = value; OnPropertyChanged(); }
    }

    public double SizeMultiplier
    {
        get => _sizeMultiplier;
        set
        {
            var clamped = Math.Clamp(value, MinSizeMultiplier, MaxSizeMultiplier);
            if (Math.Abs(_sizeMultiplier - clamped) < 0.001) return;

            _sizeMultiplier = clamped;
            OnPropertyChanged();
            OnPropertyChanged(nameof(NodeMinWidth));
            OnPropertyChanged(nameof(TitleFontSize));
            OnPropertyChanged(nameof(BadgeFontSize));
            OnPropertyChanged(nameof(SubtitleFontSize));
            OnPropertyChanged(nameof(SubtitleMaxWidth));
            OnPropertyChanged(nameof(LeafIndicatorFontSize));
        }
    }

    public double NodeMinWidth => 190 * _sizeMultiplier;
    public double TitleFontSize => 14 * _sizeMultiplier;
    public double BadgeFontSize => 10 * _sizeMultiplier;
    public double SubtitleFontSize => 10 * _sizeMultiplier;
    public double SubtitleMaxWidth => 170 * _sizeMultiplier;
    public double LeafIndicatorFontSize => 18 * _sizeMultiplier;

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
