using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Codomon.Desktop.Models.ArchitectureHypothesis;
using Codomon.Desktop.Services;
using Codomon.Desktop.ViewModels;

namespace Codomon.Desktop.Views;

/// <summary>
/// Architecture Hypothesis dialog.
/// Five tabs: Setup (prompt editor), Run (synthesis + history), Systems, High-Value Nodes, Accept.
/// </summary>
public partial class ArchitectureHypothesisDialog : Window
{
    private readonly ArchitectureHypothesisViewModel _vm;

    public ArchitectureHypothesisDialog(ArchitectureHypothesisViewModel vm)
    {
        InitializeComponent();
        _vm = vm;

        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(ArchitectureHypothesisViewModel.StatusMessage))
                SyncStatusText();
            else if (e.PropertyName is nameof(ArchitectureHypothesisViewModel.IsRunning))
                SyncRunButtons();
            else if (e.PropertyName is nameof(ArchitectureHypothesisViewModel.CurrentHypothesis))
                Dispatcher.UIThread.Post(RebuildResultTabs);
        };

        _vm.ProgressMessages.CollectionChanged += (_, _) =>
            Dispatcher.UIThread.Post(ScrollProgressToBottom);

        _vm.SavedHypotheses.CollectionChanged += (_, _) =>
            Dispatcher.UIThread.Post(RebuildHistoryList);

        Opened += async (_, _) => await OnDialogOpenedAsync();
    }

    // ── Initialisation ────────────────────────────────────────────────────────

    private async Task OnDialogOpenedAsync()
    {
        // Show current LLM settings (read-only info).
        var infoText = this.FindControl<TextBlock>("LlmSettingsInfoText");
        if (infoText != null)
            infoText.Text = string.Empty;

        await _vm.LoadPromptAsync();
        var promptBox = this.FindControl<TextBox>("PromptBox");
        if (promptBox != null) promptBox.Text = _vm.PromptTemplate;

        _vm.RefreshSavedHypotheses();
        RebuildHistoryList();
        SyncStatusText();
    }

    // ── Setup tab ─────────────────────────────────────────────────────────────

    private async void OnSavePromptClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var promptBox = this.FindControl<TextBox>("PromptBox");
        if (promptBox != null)
            _vm.PromptTemplate = promptBox.Text ?? string.Empty;

        await _vm.SavePromptAsync();
        _vm.StatusMessage = "Prompt saved.";
    }

    // ── Run tab ───────────────────────────────────────────────────────────────

    private async void OnRunSynthesisClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        await _vm.RunSynthesisAsync();
        SyncStatusText();
    }

    private void OnCancelClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => _vm.CancelSynthesis();

    private void OnRefreshHistoryClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _vm.RefreshSavedHypotheses();
        RebuildHistoryList();
    }

    private async void OnHistoryDoubleTapped(object? sender, TappedEventArgs e)
    {
        var listBox = this.FindControl<ListBox>("HistoryListBox");
        if (listBox?.SelectedItem is not ListBoxItem item) return;
        if (item.Tag is not HypothesisEntry entry) return;

        await _vm.LoadHypothesisAsync(entry);
        SyncStatusText();
    }

    private void SyncRunButtons()
    {
        var runBtn    = this.FindControl<Button>("RunButton");
        var cancelBtn = this.FindControl<Button>("CancelButton");
        if (runBtn    != null) runBtn.IsEnabled    = !_vm.IsRunning;
        if (cancelBtn != null) cancelBtn.IsEnabled =  _vm.IsRunning;
    }

    private void ScrollProgressToBottom()
    {
        var listBox = this.FindControl<ListBox>("ProgressListBox");
        if (listBox == null) return;
        listBox.ItemsSource = null;
        listBox.ItemsSource = _vm.ProgressMessages;
        if (listBox.ItemCount == 0) return;
        var last = listBox.Items[listBox.ItemCount - 1];
        if (last != null) listBox.ScrollIntoView(last);
    }

    private void RebuildHistoryList()
    {
        var listBox = this.FindControl<ListBox>("HistoryListBox");
        if (listBox == null) return;
        listBox.Items.Clear();

        if (_vm.SavedHypotheses.Count == 0)
        {
            listBox.Items.Add(new ListBoxItem
            {
                IsEnabled = false,
                Content = new TextBlock
                {
                    Text = "No hypotheses yet.",
                    Foreground = new Avalonia.Media.SolidColorBrush(
                        Avalonia.Media.Color.Parse("#556677")),
                    FontSize = 11,
                    Margin = new Avalonia.Thickness(6, 4)
                }
            });
            return;
        }

        foreach (var entry in _vm.SavedHypotheses)
        {
            listBox.Items.Add(new ListBoxItem
            {
                Tag = entry,
                Padding = new Avalonia.Thickness(8, 5),
                Content = new TextBlock
                {
                    Text = entry.DisplayName,
                    FontSize = 12,
                    Foreground = Avalonia.Media.Brushes.LightGray
                }
            });
        }
    }

    // ── Systems / HVN tabs ────────────────────────────────────────────────────

    private void RebuildResultTabs()
    {
        RebuildSystemsList();
        RebuildHvnList();
        RebuildAcceptTab();
    }

    private void RebuildSystemsList()
    {
        var listBox = this.FindControl<ListBox>("SystemsListBox");
        if (listBox == null) return;
        listBox.Items.Clear();

        var headerText = this.FindControl<TextBlock>("SystemsHeaderText");

        if (_vm.Systems.Count == 0)
        {
            if (headerText != null)
                headerText.Text = "No systems suggested — run or load a hypothesis first.";
            listBox.Items.Add(MakePlaceholderItem("No system suggestions in this hypothesis."));
            return;
        }

        if (headerText != null)
            headerText.Text = $"{_vm.Systems.Count} system(s) suggested";

        foreach (var sys in _vm.Systems)
        {
            var panel = new StackPanel { Spacing = 4, Margin = new Avalonia.Thickness(2, 4) };

            panel.Children.Add(new TextBlock
            {
                Text = $"{sys.Name}  [{sys.Kind}]  — {sys.Confidence}",
                FontSize = 13,
                FontWeight = Avalonia.Media.FontWeight.SemiBold,
                Foreground = sys.IsAccepted
                    ? new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#88BB88"))
                    : Avalonia.Media.Brushes.White
            });

            foreach (var ev in sys.Evidence)
                panel.Children.Add(new TextBlock
                {
                    Text = $"  • {ev}",
                    FontSize = 11,
                    Foreground = new Avalonia.Media.SolidColorBrush(
                        Avalonia.Media.Color.Parse("#778899")),
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap
                });

            foreach (var mod in sys.Modules)
                panel.Children.Add(new TextBlock
                {
                    Text = $"  ◦ Module: {mod.Name}  ({mod.Confidence})" +
                           (mod.HighValueNodes.Count > 0
                               ? $"  — HVN: {string.Join(", ", mod.HighValueNodes)}"
                               : string.Empty),
                    FontSize = 11,
                    Foreground = new Avalonia.Media.SolidColorBrush(
                        Avalonia.Media.Color.Parse("#AABBCC")),
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap
                });

            listBox.Items.Add(new ListBoxItem
            {
                Tag = sys,
                Padding = new Avalonia.Thickness(10, 6),
                Content = panel
            });
        }
    }

    private void RebuildHvnList()
    {
        var listBox = this.FindControl<ListBox>("HvnListBox");
        if (listBox == null) return;
        listBox.Items.Clear();

        var headerText = this.FindControl<TextBlock>("HvnHeaderText");

        if (_vm.HighValueNodes.Count == 0)
        {
            if (headerText != null)
                headerText.Text = "No high-value nodes suggested — run or load a hypothesis first.";
            listBox.Items.Add(MakePlaceholderItem("No high-value node suggestions."));
            return;
        }

        if (headerText != null)
            headerText.Text = $"{_vm.HighValueNodes.Count} high-value node(s) suggested";

        foreach (var node in _vm.HighValueNodes)
        {
            var panel = new StackPanel { Spacing = 2, Margin = new Avalonia.Thickness(2, 2) };

            panel.Children.Add(new TextBlock
            {
                Text = $"{node.Name}  [{node.Signal}]  — {node.Confidence}",
                FontSize = 13,
                FontWeight = Avalonia.Media.FontWeight.SemiBold,
                Foreground = node.IsAccepted
                    ? new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#88BB88"))
                    : Avalonia.Media.Brushes.White
            });

            panel.Children.Add(new TextBlock
            {
                Text = $"  {node.Reason}",
                FontSize = 11,
                Foreground = new Avalonia.Media.SolidColorBrush(
                    Avalonia.Media.Color.Parse("#778899")),
                TextWrapping = Avalonia.Media.TextWrapping.Wrap
            });

            listBox.Items.Add(new ListBoxItem
            {
                Tag = node,
                Padding = new Avalonia.Thickness(10, 5),
                Content = panel
            });
        }
    }

    // ── Accept tab ────────────────────────────────────────────────────────────

    private void RebuildAcceptTab()
    {
        RebuildAcceptSystemsList();
        RebuildAcceptHvnList();
        RebuildUncertainList();
    }

    private void RebuildAcceptSystemsList()
    {
        var listBox = this.FindControl<ListBox>("AcceptSystemsListBox");
        if (listBox == null) return;
        listBox.Items.Clear();

        foreach (var sys in _vm.Systems)
        {
            listBox.Items.Add(new ListBoxItem
            {
                Tag = sys,
                Padding = new Avalonia.Thickness(8, 4),
                Content = new TextBlock
                {
                    Text = $"{(sys.IsAccepted ? "✔ " : "")}{sys.Name}  [{sys.Kind}]  — {sys.Confidence}",
                    FontSize = 12,
                    Foreground = sys.IsAccepted
                        ? new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#88BB88"))
                        : Avalonia.Media.Brushes.LightGray
                }
            });
        }

        if (listBox.Items.Count == 0)
            listBox.Items.Add(MakePlaceholderItem("No systems — run or load a hypothesis first."));
    }

    private void RebuildAcceptHvnList()
    {
        var listBox = this.FindControl<ListBox>("AcceptHvnListBox");
        if (listBox == null) return;
        listBox.Items.Clear();

        foreach (var node in _vm.HighValueNodes)
        {
            listBox.Items.Add(new ListBoxItem
            {
                Tag = node,
                Padding = new Avalonia.Thickness(8, 4),
                Content = new TextBlock
                {
                    Text = $"{(node.IsAccepted ? "✔ " : "")}{node.Name}  [{node.Signal}]  — {node.Confidence}",
                    FontSize = 12,
                    Foreground = node.IsAccepted
                        ? new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#88BB88"))
                        : Avalonia.Media.Brushes.LightGray
                }
            });
        }

        if (listBox.Items.Count == 0)
            listBox.Items.Add(MakePlaceholderItem("No high-value nodes — run or load a hypothesis first."));
    }

    private void RebuildUncertainList()
    {
        var listBox = this.FindControl<ListBox>("UncertainListBox");
        if (listBox == null) return;
        listBox.Items.Clear();

        foreach (var area in _vm.UncertainAreas)
        {
            listBox.Items.Add(new ListBoxItem
            {
                Padding = new Avalonia.Thickness(8, 3),
                Content = new TextBlock
                {
                    Text = area,
                    FontSize = 11,
                    Foreground = new Avalonia.Media.SolidColorBrush(
                        Avalonia.Media.Color.Parse("#AABBCC")),
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap
                }
            });
        }

        if (listBox.Items.Count == 0)
            listBox.Items.Add(MakePlaceholderItem("No uncertain areas noted."));
    }

    private void OnAcceptSystemClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var listBox = this.FindControl<ListBox>("AcceptSystemsListBox");
        if (listBox?.SelectedItem is not ListBoxItem item) return;
        if (item.Tag is not HypothesisSystemModel sys) return;
        if (sys.IsAccepted) return;

        _vm.AcceptSystem(sys);
        _vm.StatusMessage = $"Accepted system: {sys.Name}";
        RebuildAcceptSystemsList();
        RebuildSystemsList();
    }

    private void OnAcceptNodeClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var listBox = this.FindControl<ListBox>("AcceptHvnListBox");
        if (listBox?.SelectedItem is not ListBoxItem item) return;
        if (item.Tag is not HypothesisHighValueNodeModel node) return;
        if (node.IsAccepted) return;

        _vm.AcceptHighValueNode(node);
        _vm.StatusMessage = $"Accepted high-value node: {node.Name}";
        RebuildAcceptHvnList();
        RebuildHvnList();
    }

    // ── Shared ────────────────────────────────────────────────────────────────

    private void SyncStatusText()
    {
        var text = this.FindControl<TextBlock>("StatusText");
        if (text != null)
            text.Text = _vm.StatusMessage;
    }

    private static ListBoxItem MakePlaceholderItem(string message) =>
        new()
        {
            IsEnabled = false,
            Content = new TextBlock
            {
                Text = message,
                Foreground = new Avalonia.Media.SolidColorBrush(
                    Avalonia.Media.Color.Parse("#556677")),
                FontSize = 12,
                Margin = new Avalonia.Thickness(8, 6)
            }
        };

    private void OnCloseClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => Close();
}
