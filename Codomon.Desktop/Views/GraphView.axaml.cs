using Avalonia.Controls;
using Avalonia.Interactivity;
using Codomon.Desktop.ViewModels;
using Nodify;
using System.Linq;

namespace Codomon.Desktop.Views;

public partial class GraphView : UserControl
{
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
}
