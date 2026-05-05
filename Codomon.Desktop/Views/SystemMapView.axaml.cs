using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Codomon.Desktop.Models.SystemMap;
using Codomon.Desktop.ViewModels;
using System;
using System.ComponentModel;
using System.Linq;

namespace Codomon.Desktop.Views;

/// <summary>
/// The System Map view, which hosts four sub-views: System Overview, Module View,
/// Code Detail View, and Startup View.  All rendering is driven by a
/// <see cref="SystemMapViewModel"/> passed in at construction time.
/// </summary>
public partial class SystemMapView : UserControl
{
    private readonly SystemMapViewModel _vm;
    private DragState? _dragState;
    private bool _showAppLibSeparator;
    /// <summary>
    /// Raised when the user requests a module-level relationship graph for the
    /// currently selected system in Module View.
    /// </summary>
    public event Action<SystemItemVm>? ShowDetailedRelationshipsRequested;

    /// <summary>
    /// Raised when the user requests clearing the current System Map canvas.
    /// </summary>
    public event Action? ClearCanvasRequested;

    /// <summary>
    /// Raised when a System Overview card has been repositioned.
    /// </summary>
    public event Action<string, bool, double, double>? LayoutPositionChanged;
    // ── Active-view button accent colour ─────────────────────────────────
    private static readonly IBrush ActiveButtonBg   = new SolidColorBrush(Color.Parse("#1A4A6A"));
    private static readonly IBrush InactiveButtonBg = new SolidColorBrush(Color.Parse("#1A2435"));

    // ── Arrow rendering constants ─────────────────────────────────────────
    /// <summary>Half of the fixed 220 px card width; used for card-edge intersection.</summary>
    private const double ArrowCardHalfWidth  = 110.0;
    /// <summary>Approximate half-height of a typical system card; used for card-edge intersection.</summary>
    private const double ArrowCardHalfHeight =  65.0;
    /// <summary>Horizontal offset applied when centering the arrow label over its midpoint.</summary>
    private const double ArrowLabelOffsetX   =  20.0;
    /// <summary>Vertical offset applied when centering the arrow label over its midpoint.</summary>
    private const double ArrowLabelOffsetY   =   8.0;
    /// <summary>Canvas background colour; reused as the label backing colour so labels look inset.</summary>
    private const string CanvasBgHex = "#0F141E";

    private sealed class DragState
    {
        public required Border Card { get; init; }
        public required Canvas Canvas { get; init; }
        public required string ItemId { get; init; }
        public required bool IsExternal { get; init; }
        public required Action ClickAction { get; init; }
        public Point PointerOffset { get; init; }
        public Point StartPosition { get; init; }
        public bool WasDragged { get; set; }
    }

    public SystemMapView()
        : this(new SystemMapViewModel())
    {
    }

    public SystemMapView(SystemMapViewModel vm)
    {
        InitializeComponent();
        _vm = vm;

        WireFilterCheckBoxes();
        WireViewButtons();
        SetupItemTemplates();

        _vm.PropertyChanged   += OnViewModelPropertyChanged;
        _vm.Systems.CollectionChanged           += (_, _) =>
        {
            if (_vm.Systems.Count == 0) _showAppLibSeparator = false;
            RefreshSystemOverview();
        };
        _vm.ExternalSystems.CollectionChanged   += (_, _) => RefreshSystemOverview();
        _vm.VisibleRelationships.CollectionChanged += (_, _) => RefreshSystemOverview();
        _vm.ModulesForSelectedSystem.CollectionChanged += (_, _) => RefreshModuleView();
        _vm.CodeNodesForSelectedScope.CollectionChanged += (_, _) => RefreshCodeDetailView();
        _vm.StartupItems.CollectionChanged      += (_, _) => RefreshStartupView();

        ShowView(_vm.ActiveView);
        RefreshSystemOverview();
        RefreshModuleView();
        RefreshCodeDetailView();
        RefreshStartupView();
        UpdateInspector();
    }

    // ── View switching ────────────────────────────────────────────────────

    private void WireViewButtons()
    {
        var btnOverview  = this.FindControl<Button>("BtnSystemOverview")!;
        var btnModule    = this.FindControl<Button>("BtnModuleView")!;
        var btnCode      = this.FindControl<Button>("BtnCodeDetailView")!;
        var btnStartup   = this.FindControl<Button>("BtnStartupView")!;

        btnOverview.Click += (_, _) => _vm.SetActiveView(SystemMapViewKind.SystemOverview);
        btnModule.Click   += (_, _) => _vm.SetActiveView(SystemMapViewKind.ModuleView);
        btnCode.Click     += (_, _) => _vm.SetActiveView(SystemMapViewKind.CodeDetailView);
        btnStartup.Click  += (_, _) => _vm.SetActiveView(SystemMapViewKind.StartupView);
    }

    private void ShowView(SystemMapViewKind view)
    {
        this.FindControl<ScrollViewer>("PanelSystemOverview")!.IsVisible = view == SystemMapViewKind.SystemOverview;
        this.FindControl<DockPanel>   ("PanelModuleView")!.IsVisible     = view == SystemMapViewKind.ModuleView;
        this.FindControl<DockPanel>   ("PanelCodeDetail")!.IsVisible     = view == SystemMapViewKind.CodeDetailView;
        this.FindControl<ScrollViewer>("PanelStartup")!.IsVisible        = view == SystemMapViewKind.StartupView;

        // Highlight the active view button.
        SetButtonActive("BtnSystemOverview",  view == SystemMapViewKind.SystemOverview);
        SetButtonActive("BtnModuleView",      view == SystemMapViewKind.ModuleView);
        SetButtonActive("BtnCodeDetailView",  view == SystemMapViewKind.CodeDetailView);
        SetButtonActive("BtnStartupView",     view == SystemMapViewKind.StartupView);
    }

    private void SetButtonActive(string name, bool active)
    {
        var btn = this.FindControl<Button>(name);
        if (btn != null)
            btn.Background = active ? ActiveButtonBg : InactiveButtonBg;
    }

    // ── Filter checkboxes ─────────────────────────────────────────────────

    private void WireFilterCheckBoxes()
    {
        var cbExternal  = this.FindControl<CheckBox>("CbShowExternal")!;
        var cbLowConf   = this.FindControl<CheckBox>("CbShowLowConf")!;
        var cbHighValue = this.FindControl<CheckBox>("CbHighValueOnly")!;
        var cbStartup   = this.FindControl<CheckBox>("CbShowStartupEdges")!;

        cbExternal.IsCheckedChanged  += (_, _) => _vm.ShowExternalSystems           = cbExternal.IsChecked  == true;
        cbLowConf.IsCheckedChanged   += (_, _) => _vm.ShowLowConfidenceItems        = cbLowConf.IsChecked   == true;
        cbHighValue.IsCheckedChanged += (_, _) => _vm.ShowOnlyHighValueCodeNodes    = cbHighValue.IsChecked == true;
        cbStartup.IsCheckedChanged   += (_, _) => _vm.ShowStartupRelationships      = cbStartup.IsChecked   == true;
    }

    // ── Item templates ────────────────────────────────────────────────────

    private void SetupItemTemplates()
    {
        // Module cards
        var modCtrl = this.FindControl<ItemsControl>("ModulesItemsControl")!;
        modCtrl.ItemTemplate = new FuncDataTemplate<ModuleItemVm>(BuildModuleCard, supportsRecycling: false);

        // Code node list rows
        var codeList = this.FindControl<ListBox>("CodeNodesListBox")!;
        codeList.ItemTemplate = new FuncDataTemplate<CodeNodeItemVm>(BuildCodeNodeRow, supportsRecycling: false);

        // Startup items
        var startupCtrl = this.FindControl<ItemsControl>("StartupItemsControl")!;
        startupCtrl.ItemTemplate = new FuncDataTemplate<StartupItemVm>(BuildStartupCard, supportsRecycling: false);

        // Inspector details list
        var detailCtrl = this.FindControl<ItemsControl>("InspDetailsItemsControl")!;
        detailCtrl.ItemTemplate = new FuncDataTemplate<string>((s, _) =>
        {
            if (s == null) return new TextBlock();
            return new TextBlock
            {
                Text = s,
                Foreground = new SolidColorBrush(Color.Parse("#88CCAA")),
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap
            };
        }, supportsRecycling: false);
    }

    // ── Card / row builders ───────────────────────────────────────────────

    private Control BuildSystemCard(SystemItemVm? item, INameScope? _scope)
    {
        if (item == null) return new Border();

        var confColor  = ConfidenceColor(item.Confidence);
        var kindBadge  = MakeBadge(item.KindLabel, "#1A3A5A", "#4A9FBF");
        var confBadge  = MakeBadge(item.Confidence.ToString(), "#2A1A2A", confColor);

        var header = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Children =
            {
                new TextBlock
                {
                    Text       = item.Name,
                    Foreground = Brushes.White,
                    FontWeight = FontWeight.SemiBold,
                    FontSize   = 14,
                    TextWrapping = TextWrapping.Wrap
                }
            }
        };

        var details = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 4,
            Children = { kindBadge, confBadge }
        };

        if (item.ModuleCount > 0)
        {
            var moduleInfo = new StackPanel
            {
                Spacing = 4,
                Children =
                {
                    new TextBlock
                    {
                        Text       = $"{item.ModuleCount} module(s)",
                        Foreground = new SolidColorBrush(Color.Parse("#778899")),
                        FontSize   = 11
                    },
                    BuildModuleSizeIndicator(item.ModuleCount)
                }
            };

            details.Children.Add(moduleInfo);
        }

        if (!string.IsNullOrEmpty(item.StartupMechanism))
        {
            details.Children.Add(new TextBlock
            {
                Text       = $"⚙ {item.StartupMechanism}",
                Foreground = new SolidColorBrush(Color.Parse("#88AABB")),
                FontSize   = 11
            });
        }

        var card = new Border
        {
            Background      = new SolidColorBrush(Color.Parse("#141C28")),
            BorderBrush     = new SolidColorBrush(Color.Parse("#2A3F5A")),
            BorderThickness = new Avalonia.Thickness(1),
            CornerRadius    = new Avalonia.CornerRadius(6),
            Padding         = new Avalonia.Thickness(14, 10),
            Margin          = new Avalonia.Thickness(0, 0, 10, 10),
            Width           = 220,
            Cursor          = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
            Child = new StackPanel { Spacing = 8, Children = { header, details } }
        };

        var systemsCanvas = this.FindControl<Canvas>("SystemsCanvas");
        if (systemsCanvas != null)
        {
            WireOverviewCardDrag(card, systemsCanvas, item.Id, isExternal: false, () =>
            {
                _vm.SelectSystem(item);
                _vm.SetActiveView(SystemMapViewKind.ModuleView);
            });
        }

        return card;
    }

    private Control BuildExternalSystemCard(ExternalSystemItemVm? item, INameScope? _scope)
    {
        if (item == null) return new Border();

        var confColor = ConfidenceColor(item.Confidence);
        var kindBadge = MakeBadge(!string.IsNullOrEmpty(item.Kind) ? item.Kind : "External", "#1A3A2A", "#4ABF7A");
        var confBadge = MakeBadge(item.Confidence.ToString(), "#2A1A2A", confColor);

        var card = new Border
        {
            Background      = new SolidColorBrush(Color.Parse("#141C28")),
            BorderBrush     = new SolidColorBrush(Color.Parse("#2A4A3A")),
            BorderThickness = new Avalonia.Thickness(1),
            CornerRadius    = new Avalonia.CornerRadius(6),
            Padding         = new Avalonia.Thickness(14, 10),
            Margin          = new Avalonia.Thickness(0, 0, 10, 10),
            Width           = 180,
            Cursor          = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
            Child = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new TextBlock
                    {
                        Text       = item.Name,
                        Foreground = Brushes.White,
                        FontWeight = FontWeight.SemiBold,
                        FontSize   = 13,
                        TextWrapping = TextWrapping.Wrap
                    },
                    new StackPanel { Orientation = Orientation.Vertical, Spacing = 4, Children = { kindBadge, confBadge } }
                }
            }
        };

        var externalCanvas = this.FindControl<Canvas>("ExternalSystemsCanvas");
        if (externalCanvas != null)
            WireOverviewCardDrag(card, externalCanvas, item.Id, isExternal: true, () => _vm.SelectExternalSystem(item));

        return card;
    }

    private Control BuildModuleCard(ModuleItemVm? item, INameScope _scope)
    {
        if (item == null) return new Border();

        var confColor = ConfidenceColor(item.Confidence);
        var kindBadge = MakeBadge(item.KindLabel, "#1A2A4A", "#5A8ABF");
        var confBadge = MakeBadge(item.Confidence.ToString(), "#2A1A2A", confColor);

        var card = new Border
        {
            Background      = new SolidColorBrush(Color.Parse("#141C28")),
            BorderBrush     = new SolidColorBrush(Color.Parse("#2A3F5A")),
            BorderThickness = new Avalonia.Thickness(1),
            CornerRadius    = new Avalonia.CornerRadius(6),
            Padding         = new Avalonia.Thickness(14, 10),
            Margin          = new Avalonia.Thickness(0, 0, 10, 10),
            Width           = 200,
            Cursor          = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
            Child = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new TextBlock
                    {
                        Text       = item.Name,
                        Foreground = Brushes.White,
                        FontWeight = FontWeight.SemiBold,
                        FontSize   = 13,
                        TextWrapping = TextWrapping.Wrap
                    },
                    new StackPanel
                    {
                        Orientation = Orientation.Vertical,
                        Spacing = 4,
                        Children =
                        {
                            kindBadge,
                            confBadge,
                            new TextBlock
                            {
                                Text       = $"{item.CodeNodeCount} code node(s)",
                                Foreground = new SolidColorBrush(Color.Parse("#778899")),
                                FontSize   = 11
                            }
                        }
                    }
                }
            }
        };

        card.PointerPressed += (_, _) =>
        {
            _vm.SelectModule(item);
            _vm.SetActiveView(SystemMapViewKind.CodeDetailView);
        };

        return card;
    }

    private Control BuildCodeNodeRow(CodeNodeItemVm? item, INameScope _scope)
    {
        if (item == null) return new Border();

        var confColor = ConfidenceColor(item.Confidence);
        var kindBadge = MakeBadge(item.KindLabel, "#1A2A4A", "#5A8ABF");

        string highValueMark = item.IsHighValue ? " ★" : string.Empty;

        var row = new Border
        {
            Padding         = new Avalonia.Thickness(10, 6),
            BorderBrush     = new SolidColorBrush(Color.Parse("#1A2A3A")),
            BorderThickness = new Avalonia.Thickness(0, 0, 0, 1),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing     = 8,
                Children =
                {
                    kindBadge,
                    new TextBlock
                    {
                        Text       = item.Name + highValueMark,
                        Foreground = item.IsHighValue
                            ? new SolidColorBrush(Color.Parse("#DDEEAA"))
                            : Brushes.LightGray,
                        FontWeight = item.IsHighValue ? FontWeight.SemiBold : FontWeight.Normal,
                        VerticalAlignment = VerticalAlignment.Center,
                        Width      = 180
                    },
                    new TextBlock
                    {
                        Text       = item.ModuleName,
                        Foreground = new SolidColorBrush(Color.Parse("#556677")),
                        FontSize   = 11,
                        VerticalAlignment = VerticalAlignment.Center,
                        Width      = 140
                    },
                    MakeBadge(item.Confidence.ToString(), "#2A1A2A", confColor)
                }
            }
        };

        return row;
    }

    private Control BuildStartupCard(StartupItemVm? item, INameScope _scope)
    {
        if (item == null) return new Border();

        var details = new StackPanel { Orientation = Orientation.Vertical, Spacing = 3 };

        if (!string.IsNullOrEmpty(item.StartupMechanism))
        {
            details.Children.Add(new TextBlock
            {
                Text       = $"⚙ {item.StartupMechanism}",
                Foreground = new SolidColorBrush(Color.Parse("#88AABB")),
                FontSize   = 11
            });
        }

        foreach (var ep in item.EntryPoints)
        {
            details.Children.Add(new TextBlock
            {
                Text       = $"  → {ep}",
                Foreground = new SolidColorBrush(Color.Parse("#66BBAA")),
                FontSize   = 10,
                FontFamily = new FontFamily("Monospace"),
                TextWrapping = TextWrapping.Wrap
            });
        }

        var orderBadge = MakeBadge($"#{item.StartOrder}", "#1A2A4A", "#5A8ABF");
        var kindBadge  = MakeBadge(item.KindLabel, "#1A3A5A", "#4A9FBF");

        var card = new Border
        {
            Background      = new SolidColorBrush(Color.Parse("#141C28")),
            BorderBrush     = new SolidColorBrush(Color.Parse("#2A3F5A")),
            BorderThickness = new Avalonia.Thickness(1),
            CornerRadius    = new Avalonia.CornerRadius(6),
            Padding         = new Avalonia.Thickness(14, 10),
            Margin          = new Avalonia.Thickness(0, 0, 0, 8),
            Child = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 8,
                        Children =
                        {
                            orderBadge,
                            kindBadge,
                            new TextBlock
                            {
                                Text       = item.Name,
                                Foreground = Brushes.White,
                                FontWeight = FontWeight.SemiBold,
                                FontSize   = 14,
                                VerticalAlignment = VerticalAlignment.Center,
                                TextWrapping = TextWrapping.Wrap
                            }
                        }
                    },
                    details
                }
            }
        };

        card.PointerPressed += (_, _) =>
        {
            var sys = _vm.Systems.FirstOrDefault(s => s.Id == item.Id);
            if (sys != null) _vm.SelectSystem(sys);
        };

        return card;
    }

    // ── View refresh methods ──────────────────────────────────────────────

    private void RefreshSystemOverview()
    {
        var systemsCanvas = this.FindControl<Canvas>("SystemsCanvas")!;
        var externalCanvas = this.FindControl<Canvas>("ExternalSystemsCanvas")!;
        var emptyText  = this.FindControl<TextBlock>("EmptySystemOverviewText")!;
        var sysSection = this.FindControl<StackPanel>("SystemsSection")!;
        var extSection = this.FindControl<StackPanel>("ExternalSystemsSection")!;
        var noExtText  = this.FindControl<TextBlock>("NoExternalSystemsText")!;

        bool hasSystems = _vm.Systems.Count > 0;
        bool hasExt     = _vm.ExternalSystems.Count > 0;

        emptyText.IsVisible  = !hasSystems && !hasExt;
        sysSection.IsVisible = hasSystems;
        extSection.IsVisible = _vm.ShowExternalSystems;

        systemsCanvas.Children.Clear();
        DrawRelationshipArrows(systemsCanvas);   // arrows first — cards render on top
        foreach (var system in _vm.Systems)
        {
            if (BuildSystemCard(system, null) is not Border card) continue;
            systemsCanvas.Children.Add(card);
            Canvas.SetLeft(card, system.X);
            Canvas.SetTop(card, system.Y);
        }

        // Draw the app/library separator when the sort-by-type layout is active.
        if (_showAppLibSeparator)
            AddAppLibrarySeparator(systemsCanvas);

        externalCanvas.Children.Clear();
        foreach (var external in _vm.ExternalSystems)
        {
            if (BuildExternalSystemCard(external, null) is not Border card) continue;
            externalCanvas.Children.Add(card);
            Canvas.SetLeft(card, external.X);
            Canvas.SetTop(card, external.Y);
        }

        UpdateCanvasExtent(systemsCanvas, minimumWidth: 980, minimumHeight: 200);
        UpdateCanvasExtent(externalCanvas, minimumWidth: 840, minimumHeight: 160);
        noExtText.IsVisible = _vm.ShowExternalSystems && !hasExt;
    }

    private void RefreshModuleView()
    {
        var header    = this.FindControl<TextBlock>("ModuleViewHeader")!;
        var modCtrl   = this.FindControl<ItemsControl>("ModulesItemsControl")!;
        var noModText = this.FindControl<TextBlock>("NoModulesText")!;
        var detailBtn = this.FindControl<Button>("ShowDetailedRelationshipsButton");

        header.Text = _vm.SelectedSystem != null
            ? $"Modules — {_vm.SelectedSystemName}"
            : "Select a System from System Overview to see its Modules.";

        modCtrl.ItemsSource = _vm.ModulesForSelectedSystem;
        noModText.IsVisible = _vm.SelectedSystem != null && _vm.ModulesForSelectedSystem.Count == 0;

        if (detailBtn != null)
            detailBtn.IsEnabled = _vm.SelectedSystem != null && _vm.ModulesForSelectedSystem.Count > 0;
    }

    private void RefreshCodeDetailView()
    {
        var header     = this.FindControl<TextBlock>("CodeDetailHeader")!;
        var codeList   = this.FindControl<ListBox>("CodeNodesListBox")!;
        var noCodeText = this.FindControl<TextBlock>("NoCodeNodesText")!;

        if (_vm.SelectedModule != null)
            header.Text = $"Code Nodes — {_vm.SelectedModuleName}";
        else if (_vm.SelectedSystem != null)
            header.Text = $"Code Nodes — {_vm.SelectedSystemName}";
        else
            header.Text = "Select a System or Module to scope Code Nodes.";

        codeList.ItemsSource = _vm.CodeNodesForSelectedScope;
        noCodeText.IsVisible = _vm.CodeNodesForSelectedScope.Count == 0;
    }

    private void RefreshStartupView()
    {
        var emptyText    = this.FindControl<TextBlock>("EmptyStartupText")!;
        var itemsSection = this.FindControl<StackPanel>("StartupItemsSection")!;
        var startupCtrl  = this.FindControl<ItemsControl>("StartupItemsControl")!;

        bool hasItems = _vm.StartupItems.Count > 0;
        emptyText.IsVisible    = !hasItems;
        itemsSection.IsVisible = hasItems;
        startupCtrl.ItemsSource = _vm.StartupItems;
    }

    // ── Inspector update ──────────────────────────────────────────────────

    private void UpdateInspector()
    {
        var nameText   = this.FindControl<TextBlock>("InspNameText")!;
        var typePanel  = this.FindControl<StackPanel>("InspTypePanel")!;
        var typeText   = this.FindControl<TextBlock>("InspTypeText")!;
        var kindPanel  = this.FindControl<StackPanel>("InspKindPanel")!;
        var kindText   = this.FindControl<TextBlock>("InspKindText")!;
        var confPanel  = this.FindControl<StackPanel>("InspConfPanel")!;
        var confText   = this.FindControl<TextBlock>("InspConfText")!;
        var notesPanel = this.FindControl<StackPanel>("InspNotesPanel")!;
        var notesText  = this.FindControl<TextBlock>("InspNotesText")!;
        var detPanel   = this.FindControl<StackPanel>("InspDetailsPanel")!;
        var detCtrl    = this.FindControl<ItemsControl>("InspDetailsItemsControl")!;

        nameText.Text = _vm.InspectorName;

        bool hasType   = !string.IsNullOrEmpty(_vm.InspectorType);
        bool hasKind   = !string.IsNullOrEmpty(_vm.InspectorKind);
        bool hasConf   = !string.IsNullOrEmpty(_vm.InspectorConfidence);
        bool hasNotes  = !string.IsNullOrEmpty(_vm.InspectorNotes);
        bool hasDetail = _vm.InspectorDetails.Count > 0;

        typePanel.IsVisible  = hasType;
        kindPanel.IsVisible  = hasKind;
        confPanel.IsVisible  = hasConf;
        notesPanel.IsVisible = hasNotes;
        detPanel.IsVisible   = hasDetail;

        if (hasType)  typeText.Text  = _vm.InspectorType;
        if (hasKind)  kindText.Text  = _vm.InspectorKind;
        if (hasConf)  confText.Text  = _vm.InspectorConfidence;
        if (hasNotes) notesText.Text = _vm.InspectorNotes;
        if (hasDetail) detCtrl.ItemsSource = _vm.InspectorDetails;
    }

    // ── VM property change handler ────────────────────────────────────────

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(SystemMapViewModel.ActiveView):
                ShowView(_vm.ActiveView);
                break;

            case nameof(SystemMapViewModel.InspectorName):
            case nameof(SystemMapViewModel.InspectorType):
            case nameof(SystemMapViewModel.InspectorKind):
            case nameof(SystemMapViewModel.InspectorNotes):
            case nameof(SystemMapViewModel.InspectorConfidence):
            case nameof(SystemMapViewModel.InspectorDetails):
                UpdateInspector();
                break;

            case nameof(SystemMapViewModel.SelectedSystemName):
                RefreshModuleView();
                RefreshCodeDetailView();
                break;

            case nameof(SystemMapViewModel.SelectedModuleName):
                RefreshCodeDetailView();
                break;

            case nameof(SystemMapViewModel.ShowExternalSystems):
                RefreshSystemOverview();
                break;
        }
    }

    // ── Code node selection handler ────────────────────────────────────────

    public void OnCodeNodeSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count == 0) return;
        if (e.AddedItems[0] is CodeNodeItemVm node)
            _vm.SelectCodeNode(node);
    }

    public void OnShowDetailedRelationshipsClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_vm.SelectedSystem == null) return;
        ShowDetailedRelationshipsRequested?.Invoke(_vm.SelectedSystem);
    }

    public void OnClearCanvasClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _showAppLibSeparator = false;
        ClearCanvasRequested?.Invoke();
    }

    public void OnCleanupNamesClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _vm.CleanupNames();
        // Rebuild the canvases so the renamed cards are re-drawn.
        RefreshSystemOverview();
        RefreshModuleView();
    }

    public void OnSortAppsFromLibrariesClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var updates = _vm.SortAppsFromLibraries();
        _showAppLibSeparator = updates.Count > 0;
        foreach (var (id, x, y) in updates)
            LayoutPositionChanged?.Invoke(id, false, x, y);
        RefreshSystemOverview();
    }

    public void OnArrangeClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _showAppLibSeparator = false;
        var updates = _vm.ArrangeAlphabetically();
        foreach (var (id, x, y) in updates)
            LayoutPositionChanged?.Invoke(id, false, x, y);
        RefreshSystemOverview();
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static Border MakeBadge(string text, string bgHex, string fgHex)
    {
        return new Border
        {
            Background      = new SolidColorBrush(Color.Parse(bgHex)),
            CornerRadius    = new Avalonia.CornerRadius(3),
            Padding         = new Avalonia.Thickness(5, 2),
            Child = new TextBlock
            {
                Text         = text,
                FontSize     = 9,
                FontWeight   = FontWeight.Bold,
                Foreground   = new SolidColorBrush(Color.Parse(fgHex)),
                LetterSpacing = 0.5
            }
        };
    }

    /// <summary>
    /// Adds a horizontal separator line and a "CLASS LIBRARIES" section header to the
    /// canvas when systems from both groups are present.
    /// </summary>
    private void AddAppLibrarySeparator(Canvas canvas)
    {
        var apps = _vm.Systems.Where(s => !s.IsLibrary).ToList();
        var libs = _vm.Systems.Where(s =>  s.IsLibrary).ToList();

        if (apps.Count == 0 || libs.Count == 0)
            return;

        double libMinY = libs.Min(s => s.Y);
        double appMaxY = apps.Max(s => s.Y);

        // Place the separator line halfway between the last app row and first library row.
        double sepY = appMaxY + (libMinY - appMaxY) / 2.0;

        var separatorLine = new Border
        {
            Height     = 1,
            Width      = 2000,
            Background = new SolidColorBrush(Color.Parse("#2A3F5A"))
        };
        Canvas.SetLeft(separatorLine, 0);
        Canvas.SetTop(separatorLine, sepY);
        canvas.Children.Add(separatorLine);

        var sectionLabel = new TextBlock
        {
            Text          = "CLASS LIBRARIES",
            FontSize      = 9,
            FontWeight    = FontWeight.Bold,
            Foreground    = new SolidColorBrush(Color.Parse("#4A6A8A")),
            LetterSpacing = 1
        };
        Canvas.SetLeft(sectionLabel, 24);
        Canvas.SetTop(sectionLabel, sepY + 6);
        canvas.Children.Add(sectionLabel);
    }

    private static Control BuildModuleSizeIndicator(int moduleCount)
    {
        const int cellCount = 10;
        int filled = MapModuleCountToCells(moduleCount, cellCount);

        var grid = new Grid();
        for (int col = 0; col < 5; col++)
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        for (int row = 0; row < 2; row++)
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        for (int i = 0; i < cellCount; i++)
        {
            bool isFilled = i < filled;
            var cell = new Border
            {
                Width = 12,
                Height = 8,
                Margin = new Avalonia.Thickness(0, 0, 3, 3),
                CornerRadius = new Avalonia.CornerRadius(2),
                Background = isFilled
                    ? new SolidColorBrush(Color.Parse("#4A9FBF"))
                    : new SolidColorBrush(Color.Parse("#26384E")),
                BorderBrush = new SolidColorBrush(Color.Parse("#2A3F5A")),
                BorderThickness = new Avalonia.Thickness(1)
            };

            Grid.SetColumn(cell, i % 5);
            Grid.SetRow(cell, i / 5);
            grid.Children.Add(cell);
        }

        var container = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 2,
            Children = { grid }
        };

        container.Children.Add(new TextBlock
        {
            Text = "log scale",
            FontSize = 10,
            Foreground = new SolidColorBrush(Color.Parse("#5B6F88"))
        });

        return container;
    }

    private static int MapModuleCountToCells(int moduleCount, int cellCount)
    {
        if (moduleCount <= 0) return 0;

        // Log-scale fill keeps large systems visually distinguishable instead of
        // saturating the meter too early.
        int mapped = (int)Math.Ceiling(Math.Log2(moduleCount + 1));
        return Math.Clamp(mapped, 1, cellCount);
    }

    private void WireOverviewCardDrag(Border card, Canvas canvas, string itemId, bool isExternal, Action clickAction)
    {
        card.PointerPressed += (_, e) => OnOverviewCardPointerPressed(e, card, canvas, itemId, isExternal, clickAction);
        card.PointerMoved += (_, e) => OnOverviewCardPointerMoved(e, card);
        card.PointerReleased += (_, e) => OnOverviewCardPointerReleased(e, card);
        card.PointerCaptureLost += (_, _) =>
        {
            if (_dragState?.Card == card)
                _dragState = null;
        };
    }

    private void OnOverviewCardPointerPressed(
        PointerPressedEventArgs e,
        Border card,
        Canvas canvas,
        string itemId,
        bool isExternal,
        Action clickAction)
    {
        if (!e.GetCurrentPoint(card).Properties.IsLeftButtonPressed)
            return;

        double currentX = GetCanvasLeft(card);
        double currentY = GetCanvasTop(card);
        var pointerPosition = e.GetPosition(canvas);

        _dragState = new DragState
        {
            Card = card,
            Canvas = canvas,
            ItemId = itemId,
            IsExternal = isExternal,
            ClickAction = clickAction,
            PointerOffset = new Point(pointerPosition.X - currentX, pointerPosition.Y - currentY),
            StartPosition = new Point(currentX, currentY)
        };

        e.Pointer.Capture(card);
        e.Handled = true;
    }

    private void OnOverviewCardPointerMoved(PointerEventArgs e, Border card)
    {
        if (_dragState?.Card != card)
            return;

        var pointerPosition = e.GetPosition(_dragState.Canvas);
        double x = Math.Max(0, pointerPosition.X - _dragState.PointerOffset.X);
        double y = Math.Max(0, pointerPosition.Y - _dragState.PointerOffset.Y);

        if (!_dragState.WasDragged)
        {
            var delta = new Point(x - _dragState.StartPosition.X, y - _dragState.StartPosition.Y);
            _dragState.WasDragged = Math.Abs(delta.X) >= 3 || Math.Abs(delta.Y) >= 3;
        }

        Canvas.SetLeft(card, x);
        Canvas.SetTop(card, y);
        UpdateCanvasExtent(_dragState.Canvas, minimumWidth: 720, minimumHeight: 140);
        e.Handled = true;
    }

    private void OnOverviewCardPointerReleased(PointerReleasedEventArgs e, Border card)
    {
        if (_dragState?.Card != card)
            return;

        var dragState = _dragState;
        _dragState = null;
        e.Pointer.Capture(null);

        double x = GetCanvasLeft(card);
        double y = GetCanvasTop(card);

        if (dragState.WasDragged)
        {
            _vm.SetOverviewPosition(dragState.ItemId, x, y, dragState.IsExternal);
            LayoutPositionChanged?.Invoke(dragState.ItemId, dragState.IsExternal, x, y);
            UpdateCanvasExtent(dragState.Canvas, minimumWidth: 720, minimumHeight: 140);
            // Redraw arrows from the updated card positions.
            RefreshSystemOverview();
        }
        else
        {
            dragState.ClickAction();
        }

        e.Handled = true;
    }

    private static double GetCanvasLeft(Control control)
        => double.IsNaN(Canvas.GetLeft(control)) ? 0 : Canvas.GetLeft(control);

    private static double GetCanvasTop(Control control)
        => double.IsNaN(Canvas.GetTop(control)) ? 0 : Canvas.GetTop(control);

    private static void UpdateCanvasExtent(Canvas canvas, double minimumWidth, double minimumHeight)
    {
        double maxRight = minimumWidth;
        double maxBottom = minimumHeight;

        foreach (var child in canvas.Children.OfType<Control>())
        {
            double left = GetCanvasLeft(child);
            double top = GetCanvasTop(child);
            double width = !double.IsNaN(child.Width) && child.Width > 0 ? child.Width : child.Bounds.Width;
            double height = child.Bounds.Height > 0 ? child.Bounds.Height : child.DesiredSize.Height;

            maxRight = Math.Max(maxRight, left + width + 32);
            maxBottom = Math.Max(maxBottom, top + Math.Max(height, 100) + 24);
        }

        canvas.Width = maxRight;
        canvas.Height = maxBottom;
    }

    // ── Relationship arrow drawing ─────────────────────────────────────────

    /// <summary>
    /// Draws typed directional arrows for all <see cref="SystemMapViewModel.VisibleRelationships"/>
    /// whose both endpoints are Systems on this canvas.  Arrows are added before cards so that
    /// cards always render on top in canvas z-order.
    /// </summary>
    private void DrawRelationshipArrows(Canvas canvas)
    {
        if (_vm.VisibleRelationships.Count == 0)
            return;

        // Index the centre-point of every visible System card.
        var systemCenters = _vm.Systems.ToDictionary(
            s => s.Id,
            s => new Point(s.X + ArrowCardHalfWidth, s.Y + ArrowCardHalfHeight),
            StringComparer.Ordinal);

        foreach (var rel in _vm.VisibleRelationships)
        {
            if (rel.FromId == rel.ToId) continue;   // skip self-loops

            // Only draw arrows between Systems on this canvas; skip External↔External
            // and System↔External (which are on a separate canvas).
            if (!systemCenters.TryGetValue(rel.FromId, out var fromCenter)) continue;
            if (!systemCenters.TryGetValue(rel.ToId,   out var toCenter))   continue;

            double dx  = toCenter.X - fromCenter.X;
            double dy  = toCenter.Y - fromCenter.Y;
            double len = Math.Sqrt(dx * dx + dy * dy);
            if (len < 20) continue;

            double ux = dx / len;
            double uy = dy / len;

            // Exit point on the source card's border, entry point on the target card's border.
            var fromPt = ComputeCardEdge(fromCenter,  ux,  uy, ArrowCardHalfWidth, ArrowCardHalfHeight);
            var toPt   = ComputeCardEdge(toCenter,   -ux, -uy, ArrowCardHalfWidth, ArrowCardHalfHeight);

            var brush = new SolidColorBrush(Color.Parse(RelationshipColor(rel.Kind)));
            AddArrowToCanvas(canvas, fromPt, toPt, brush, rel.Label, rel);
        }
    }

    /// <summary>
    /// Returns the point on a card's bounding-box border in direction (ux, uy) from the card centre.
    /// Uses the axis-aligned bounding-box intersection formula with the given half-dimensions.
    /// </summary>
    private static Point ComputeCardEdge(Point center, double ux, double uy, double hw, double hh)
    {
        double tx = Math.Abs(ux) > 1e-9 ? hw / Math.Abs(ux) : double.MaxValue;
        double ty = Math.Abs(uy) > 1e-9 ? hh / Math.Abs(uy) : double.MaxValue;
        double t  = Math.Min(tx, ty);
        return new Point(center.X + ux * t, center.Y + uy * t);
    }

    /// <summary>Draws a directed arrow with an optional kind label between two canvas points.</summary>
    private void AddArrowToCanvas(Canvas canvas, Point from, Point to, IBrush brush, string label, RelationshipItemVm rel)
    {
        double dx  = to.X - from.X;
        double dy  = to.Y - from.Y;
        double len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 10) return;

        double ux = dx / len;
        double uy = dy / len;
        double px = -uy;   // perpendicular unit vector
        double py =  ux;

        const double headLen  = 10.0;
        const double headHalf =  5.0;

        // Arrowhead: tip is `to`; wings branch from a point `headLen` back along the shaft.
        var wingBase = new Point(to.X - ux * headLen, to.Y - uy * headLen);
        var wing1End = new Point(wingBase.X + px * headHalf, wingBase.Y + py * headHalf);
        var wing2End = new Point(wingBase.X - px * headHalf, wingBase.Y - py * headHalf);

        // Shaft line stops at the wing base so it doesn't overdraw the arrowhead.
        var shaft = new Line
        {
            StartPoint      = from,
            EndPoint        = wingBase,
            Stroke          = brush,
            StrokeThickness = 1.5,
            Opacity         = 0.7
        };
        canvas.Children.Add(shaft);

        var wing1 = new Line { StartPoint = to, EndPoint = wing1End, Stroke = brush, StrokeThickness = 1.5, Opacity = 0.7 };
        var wing2 = new Line { StartPoint = to, EndPoint = wing2End, Stroke = brush, StrokeThickness = 1.5, Opacity = 0.7 };
        canvas.Children.Add(wing1);
        canvas.Children.Add(wing2);

        // Small label at the midpoint of the shaft.
        if (!string.IsNullOrEmpty(label))
        {
            double midX = (from.X + wingBase.X) / 2.0;
            double midY = (from.Y + wingBase.Y) / 2.0;

            var lbl = new Border
            {
                Background   = new SolidColorBrush(Color.Parse(CanvasBgHex)),
                CornerRadius = new Avalonia.CornerRadius(3),
                Padding      = new Avalonia.Thickness(3, 1),
                Cursor       = new Cursor(StandardCursorType.Hand),
                Child = new TextBlock
                {
                    Text          = label,
                    FontSize      = 9,
                    Foreground    = brush,
                    FontWeight    = FontWeight.Bold,
                    LetterSpacing = 0.3
                }
            };
            lbl.PointerPressed += (_, e) =>
            {
                if (e.GetCurrentPoint(lbl).Properties.IsLeftButtonPressed)
                {
                    _vm.SelectRelationship(rel);
                    e.Handled = true;
                }
            };

            Canvas.SetLeft(lbl, midX - ArrowLabelOffsetX);
            Canvas.SetTop(lbl, midY - ArrowLabelOffsetY);
            canvas.Children.Add(lbl);
        }

        // Hit-transparent overlay on the shaft so clicking the line also opens the inspector.
        var hitArea = new Line
        {
            StartPoint      = from,
            EndPoint        = to,
            Stroke          = Brushes.Transparent,
            StrokeThickness = 8,
            Cursor          = new Cursor(StandardCursorType.Hand)
        };
        hitArea.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(hitArea).Properties.IsLeftButtonPressed)
            {
                _vm.SelectRelationship(rel);
                e.Handled = true;
            }
        };
        canvas.Children.Add(hitArea);
    }

    private static string RelationshipColor(RelationshipKind kind) => kind switch
    {
        RelationshipKind.Calls      => "#4ABF8A",
        RelationshipKind.Imports    => "#66AABB",
        RelationshipKind.Depends    => "#4A7FBF",
        RelationshipKind.Configures => "#AA88CC",
        RelationshipKind.Logs       => "#778899",
        RelationshipKind.Publishes  => "#DFAA44",
        RelationshipKind.Subscribes => "#DF8844",
        RelationshipKind.Reads      => "#88AACC",
        RelationshipKind.Writes     => "#CC8888",
        RelationshipKind.Hosts      => "#88CC88",
        _                           => "#778899"
    };

    private static string ConfidenceColor(Models.SystemMap.ConfidenceLevel c) => c switch
    {
        Models.SystemMap.ConfidenceLevel.Manual    => "#88EE88",
        Models.SystemMap.ConfidenceLevel.Confirmed => "#66CCAA",
        Models.SystemMap.ConfidenceLevel.Likely    => "#AABB66",
        Models.SystemMap.ConfidenceLevel.Possible  => "#CCAA44",
        _                                          => "#778899"
    };
}
