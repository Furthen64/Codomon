// Phase 01 – Graph Canvas Extraction
// The custom canvas rendering logic has been removed in preparation for Nodify integration.
// This file retains the public API surface (HighlightModule, HighlightSystem, OnLayoutChanged)
// so that callers in MainWindow compile unchanged.  The placeholder UI is provided by
// GraphCanvasPlaceholder (an Avalonia Panel) which is set as the CanvasHost content.

using Avalonia.Controls;
using Codomon.Desktop.Models;

namespace Codomon.Desktop.Controls;

/// <summary>
/// Stub replacement for the old custom-drawn canvas.
/// All rendering, hit-testing, drag and highlight logic has been removed.
/// A visual placeholder is displayed until the Nodify-based graph is integrated.
/// </summary>
public class MainCanvasControl
{
    // TODO (Phase 02): wire up a Nodify graph data provider here.
    // TODO (Phase 02): define Node / Edge view-model types for Nodify.
    // TODO (Phase 02): bind ViewModel hooks to the NodifyEditor control.

    /// <summary>Invoked when the user finishes dragging a node, marking the workspace as dirty.</summary>
    public Action? OnLayoutChanged;

    /// <summary>
    /// Initialises the stub.  The <paramref name="workspace"/> and
    /// <paramref name="selectionState"/> parameters are kept so call-sites in
    /// <c>MainWindow</c> do not need to change.
    /// </summary>
    public MainCanvasControl(WorkspaceModel workspace, SelectionStateModel selectionState)
    {
        // Intentionally empty — graph rendering is pending Nodify integration.
    }

    /// <summary>
    /// Stub: will highlight the given module once the Nodify graph is integrated.
    /// </summary>
    public void HighlightModule(string moduleId, string systemId)
    {
        // TODO (Phase 02): forward highlight to the Nodify node view-model.
    }

    /// <summary>
    /// Stub: will highlight the given system once the Nodify graph is integrated.
    /// </summary>
    public void HighlightSystem(string systemId)
    {
        // TODO (Phase 02): forward highlight to the Nodify node view-model.
    }

    /// <summary>
    /// Returns the placeholder <see cref="Control"/> to be placed in the CanvasHost.
    /// </summary>
    public Control CreatePlaceholderView()
    {
        return new Border
        {
            Background = Avalonia.Media.Brushes.Transparent,
            Child = new StackPanel
            {
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                VerticalAlignment   = Avalonia.Layout.VerticalAlignment.Center,
                Spacing = 8,
                Children =
                {
                    new TextBlock
                    {
                        Text              = "[ Graph Canvas Placeholder ]",
                        Foreground        = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromRgb(100, 160, 220)),
                        FontSize          = 18,
                        FontWeight        = Avalonia.Media.FontWeight.SemiBold,
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                    },
                    new TextBlock
                    {
                        Text              = "Nodify integration pending",
                        Foreground        = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromRgb(120, 140, 160)),
                        FontSize          = 13,
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                    },
                }
            }
        };
    }
}
