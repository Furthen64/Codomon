using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using Codomon.Desktop.ViewModels;
using Nodify;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace Codomon.Desktop.Views;

public partial class GraphView : UserControl
{
    private int _lastHandledAutoAlignToken = -1;
    private GraphViewModel? _subscribedViewModel;
    public event Action? NavigateToSystemMapRequested;
    public event Action<string>? NavigateToModuleRequested;
    public event Action<string>? NavigateToCodeNodesRequested;
    public event Action<string>? NavigateToCallerModuleRequested;

    public GraphView()
    {
        InitializeComponent();

        // ── VIEW ─────────────────────────────────────────────────────────────
        FitToViewButton.Click += (_, _) => Editor.FitToScreen();
        ZoomInButton.Click    += (_, _) => Editor.ZoomIn();
        ZoomOutButton.Click   += (_, _) => Editor.ZoomOut();

        // ── ALIGN SELECTED ───────────────────────────────────────────────────
        // Nodify's EditorCommands.Align routes through the NodifyEditor and
        // operates on Editor.SelectedItems using the specified Alignment value.
        AlignLeftButton.Click   += (_, _) => EditorCommands.Align.Execute(EditorCommands.Alignment.Left,   Editor);
        AlignRightButton.Click  += (_, _) => EditorCommands.Align.Execute(EditorCommands.Alignment.Right,  Editor);
        AlignTopButton.Click    += (_, _) => EditorCommands.Align.Execute(EditorCommands.Alignment.Top,    Editor);
        AlignBottomButton.Click += (_, _) => EditorCommands.Align.Execute(EditorCommands.Alignment.Bottom, Editor);
        CenterHButton.Click     += (_, _) => EditorCommands.Align.Execute(EditorCommands.Alignment.Middle, Editor);
        CenterVButton.Click     += (_, _) => EditorCommands.Align.Execute(EditorCommands.Alignment.Center, Editor);

        // ── ARRANGE ──────────────────────────────────────────────────────────
        AutoAlignButton.Click   += OnAutoAlignClick;
        DistributeHButton.Click += OnDistributeHorizontallyClick;

        // ── FILTER ───────────────────────────────────────────────────────────
        FilterLowConfidenceCheck.IsCheckedChanged += (_, _) => ApplyFilterChange();
        FilterCallsCheck.IsCheckedChanged         += (_, _) => ApplyFilterChange();
        FilterDependsCheck.IsCheckedChanged       += (_, _) => ApplyFilterChange();
        FilterImportsCheck.IsCheckedChanged       += (_, _) => ApplyFilterChange();
        FilterOtherKindsCheck.IsCheckedChanged    += (_, _) => ApplyFilterChange();

        Editor.PointerReleased += (_, _) =>
        {
            if (DataContext is GraphViewModel vm)
                vm.SavePositions();
        };

        BreadcrumbSystemMapButton.Click += (_, _) => NavigateToSystemMapRequested?.Invoke();
        BreadcrumbModuleButton.Click += (_, _) =>
        {
            if (DataContext is not GraphViewModel vm) return;
            if (string.IsNullOrWhiteSpace(vm.BreadcrumbSystemId)) return;
            NavigateToModuleRequested?.Invoke(vm.BreadcrumbSystemId);
        };
        BreadcrumbCodeNodesButton.Click += (_, _) =>
        {
            if (DataContext is not GraphViewModel vm) return;
            if (string.IsNullOrWhiteSpace(vm.BreadcrumbModuleId)) return;
            NavigateToCodeNodesRequested?.Invoke(vm.BreadcrumbModuleId);
        };
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        if (_subscribedViewModel != null)
            _subscribedViewModel.PropertyChanged -= OnViewModelPropertyChanged;

        if (DataContext is GraphViewModel vm)
        {
            vm.PropertyChanged += OnViewModelPropertyChanged;
            _subscribedViewModel = vm;
        }
        else
            _subscribedViewModel = null;

        base.OnDataContextChanged(e);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not GraphViewModel vm) return;
        if (!string.Equals(e.PropertyName, nameof(GraphViewModel.AutoAlignRequestToken), StringComparison.Ordinal)) return;
        if (vm.AutoAlignRequestToken == _lastHandledAutoAlignToken) return;

        _lastHandledAutoAlignToken = vm.AutoAlignRequestToken;
        vm.AutoAlign();
        Editor.FitToScreen();
    }

    private void ApplyFilterChange()
    {
        if (DataContext is not GraphViewModel vm) return;
        vm.ShowLowConfidenceItems    = FilterLowConfidenceCheck.IsChecked == true;
        vm.ShowCallsRelationships    = FilterCallsCheck.IsChecked         == true;
        vm.ShowDependsRelationships  = FilterDependsCheck.IsChecked       == true;
        vm.ShowImportsRelationships  = FilterImportsCheck.IsChecked       == true;
        vm.ShowOtherRelationships    = FilterOtherKindsCheck.IsChecked    == true;
    }

    private void OnAutoAlignClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not GraphViewModel vm) return;
        vm.AutoAlign();
        Editor.FitToScreen();
    }

    private void OnDistributeHorizontallyClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not GraphViewModel vm) return;

        var selected = Editor.SelectedItems?
            .OfType<NodeViewModel>()
            .ToList();

        if (selected is { Count: >= 3 })
            vm.DistributeHorizontally(selected);
    }

    private void OnNodePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not GraphViewModel vm) return;
        if (sender is not Control ctrl) return;
        vm.SelectNode(ctrl.DataContext as NodeViewModel);
    }

    private void OnFileClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button) return;
        if (button.Tag is not string filePath || string.IsNullOrWhiteSpace(filePath)) return;

        var candidate = DataContext is GraphViewModel vm
            ? vm.ResolveFilePath(filePath)
            : filePath;
        if (string.IsNullOrWhiteSpace(candidate) || !File.Exists(candidate)) return;

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = candidate,
                UseShellExecute = true
            });
        }
        catch (Exception ex) when (ex is Win32Exception or UnauthorizedAccessException or FileNotFoundException)
        {
            Models.AppLogger.Error($"[Graph] Failed to open source file '{candidate}': {ex.Message}");
        }
    }

    private void OnCallerCardClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button) return;
        if (button.Tag is not string moduleId || string.IsNullOrWhiteSpace(moduleId)) return;
        NavigateToCallerModuleRequested?.Invoke(moduleId);
    }
}
