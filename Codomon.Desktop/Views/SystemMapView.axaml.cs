using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Codomon.Desktop.ViewModels;
using System.ComponentModel;

namespace Codomon.Desktop.Views;

/// <summary>
/// The System Map view, which hosts four sub-views: System Overview, Module View,
/// Code Detail View, and Startup View.  All rendering is driven by a
/// <see cref="SystemMapViewModel"/> passed in at construction time.
/// </summary>
public partial class SystemMapView : UserControl
{
    private readonly SystemMapViewModel _vm;

    // ── Active-view button accent colour ─────────────────────────────────
    private static readonly IBrush ActiveButtonBg   = new SolidColorBrush(Color.Parse("#1A4A6A"));
    private static readonly IBrush InactiveButtonBg = new SolidColorBrush(Color.Parse("#1A2435"));

    public SystemMapView(SystemMapViewModel vm)
    {
        InitializeComponent();
        _vm = vm;

        WireFilterCheckBoxes();
        WireViewButtons();
        SetupItemTemplates();

        _vm.PropertyChanged   += OnViewModelPropertyChanged;
        _vm.Systems.CollectionChanged           += (_, _) => RefreshSystemOverview();
        _vm.ExternalSystems.CollectionChanged   += (_, _) => RefreshSystemOverview();
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
        // System cards
        var sysCtrl = this.FindControl<ItemsControl>("SystemsItemsControl")!;
        sysCtrl.ItemTemplate = new FuncDataTemplate<SystemItemVm>(BuildSystemCard, supportsRecycling: false);

        // External system cards
        var extCtrl = this.FindControl<ItemsControl>("ExternalSystemsItemsControl")!;
        extCtrl.ItemTemplate = new FuncDataTemplate<ExternalSystemItemVm>(BuildExternalSystemCard, supportsRecycling: false);

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

    private Control BuildSystemCard(SystemItemVm? item, INameScope _scope)
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
            details.Children.Add(new TextBlock
            {
                Text       = $"{item.ModuleCount} module(s)",
                Foreground = new SolidColorBrush(Color.Parse("#778899")),
                FontSize   = 11
            });
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

        card.PointerPressed += (_, _) =>
        {
            _vm.SelectSystem(item);
            _vm.SetActiveView(SystemMapViewKind.ModuleView);
        };

        return card;
    }

    private Control BuildExternalSystemCard(ExternalSystemItemVm? item, INameScope _scope)
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

        card.PointerPressed += (_, _) => _vm.SelectExternalSystem(item);
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
        var sysCtrl    = this.FindControl<ItemsControl>("SystemsItemsControl")!;
        var extCtrl    = this.FindControl<ItemsControl>("ExternalSystemsItemsControl")!;
        var emptyText  = this.FindControl<TextBlock>("EmptySystemOverviewText")!;
        var sysSection = this.FindControl<StackPanel>("SystemsSection")!;
        var extSection = this.FindControl<StackPanel>("ExternalSystemsSection")!;
        var noExtText  = this.FindControl<TextBlock>("NoExternalSystemsText")!;

        bool hasSystems = _vm.Systems.Count > 0;
        bool hasExt     = _vm.ExternalSystems.Count > 0;

        emptyText.IsVisible  = !hasSystems && !hasExt;
        sysSection.IsVisible = hasSystems;
        extSection.IsVisible = _vm.ShowExternalSystems;

        sysCtrl.ItemsSource = _vm.Systems;
        extCtrl.ItemsSource = _vm.ExternalSystems;
        noExtText.IsVisible = _vm.ShowExternalSystems && !hasExt;
    }

    private void RefreshModuleView()
    {
        var header    = this.FindControl<TextBlock>("ModuleViewHeader")!;
        var modCtrl   = this.FindControl<ItemsControl>("ModulesItemsControl")!;
        var noModText = this.FindControl<TextBlock>("NoModulesText")!;

        header.Text = _vm.SelectedSystem != null
            ? $"Modules — {_vm.SelectedSystemName}"
            : "Select a System from System Overview to see its Modules.";

        modCtrl.ItemsSource = _vm.ModulesForSelectedSystem;
        noModText.IsVisible = _vm.SelectedSystem != null && _vm.ModulesForSelectedSystem.Count == 0;
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

    private static string ConfidenceColor(Models.SystemMap.ConfidenceLevel c) => c switch
    {
        Models.SystemMap.ConfidenceLevel.Manual    => "#88EE88",
        Models.SystemMap.ConfidenceLevel.Confirmed => "#66CCAA",
        Models.SystemMap.ConfidenceLevel.Likely    => "#AABB66",
        Models.SystemMap.ConfidenceLevel.Possible  => "#CCAA44",
        _                                          => "#778899"
    };
}
