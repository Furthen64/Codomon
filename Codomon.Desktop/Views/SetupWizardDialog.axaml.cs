using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Codomon.Desktop.ViewModels;

namespace Codomon.Desktop.Views;

public partial class SetupWizardDialog : Window
{
    private readonly SetupWizardViewModel _vm;

    // Expose the finished ViewModel so the caller can read the results.
    public SetupWizardViewModel? Result { get; private set; }

    public SetupWizardDialog()
    {
        InitializeComponent();
        _vm = new SetupWizardViewModel();
        DataContext = _vm;

        SyncStepUi();
    }

    // ── Step navigation ──────────────────────────────────────────────────────

    private void OnNextClick(object? sender, RoutedEventArgs e)
    {
        if (!_vm.ValidateCurrentStep())
        {
            ShowError(_vm.ValidationError);
            return;
        }

        HideError();
        _vm.CurrentStep++;
        SyncStepUi();
    }

    private void OnBackClick(object? sender, RoutedEventArgs e)
    {
        HideError();
        _vm.CurrentStep--;
        SyncStepUi();
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(null);

    private void OnFinishClick(object? sender, RoutedEventArgs e)
    {
        // Step 4 has no mandatory fields, but still validate for consistency.
        if (!_vm.ValidateCurrentStep())
        {
            ShowError(_vm.ValidationError);
            return;
        }

        Result = _vm;
        Close(_vm);
    }

    // ── Synchronise UI to current step ──────────────────────────────────────

    private void SyncStepUi()
    {
        var step = _vm.CurrentStep;

        SetPanelVisible("Step1Panel", step == 1);
        SetPanelVisible("Step2Panel", step == 2);
        SetPanelVisible("Step3Panel", step == 3);
        SetPanelVisible("Step4Panel", step == 4);

        this.FindControl<Button>("BackButton")!.IsEnabled = step > 1;
        this.FindControl<Button>("NextButton")!.IsVisible = step < SetupWizardViewModel.TotalSteps;
        this.FindControl<Button>("FinishButton")!.IsVisible = step == SetupWizardViewModel.TotalSteps;

        this.FindControl<TextBlock>("StepTitleText")!.Text = _vm.StepTitle;

        UpdateStepDots(step);

        // When arriving at Step 3, auto-suggest the workspace name if the user
        // hasn't typed one yet.
        if (step == 3 && string.IsNullOrWhiteSpace(_vm.WorkspaceName))
        {
            var suggested = SuggestWorkspaceName(_vm.SourceProjectPath);
            if (!string.IsNullOrEmpty(suggested))
            {
                _vm.WorkspaceName = suggested;
                var nameBox = this.FindControl<TextBox>("WorkspaceNameBox");
                if (nameBox != null) nameBox.Text = suggested;
            }
        }
    }

    /// <summary>
    /// Derives a suggested workspace name from a source path.
    /// E.g. "C:\src\MyProj1\" → "myproj1_ws"
    ///      "C:\src\MyProj1\MyProj1.sln" → "myproj1_ws"
    /// </summary>
    private static string SuggestWorkspaceName(string sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath)) return string.Empty;

        string leafName;
        if (System.IO.Directory.Exists(sourcePath))
        {
            leafName = new System.IO.DirectoryInfo(sourcePath).Name;
        }
        else
        {
            // Strip extension for files like .sln / .csproj
            leafName = System.IO.Path.GetFileNameWithoutExtension(sourcePath);
        }

        // Lowercase and append _ws; keep only safe characters.
        leafName = leafName.ToLowerInvariant().Trim();
        if (string.IsNullOrEmpty(leafName)) return string.Empty;

        return leafName + "_ws";
    }

    private void SetPanelVisible(string name, bool visible)
    {
        var control = this.FindControl<Control>(name);
        if (control != null)
            control.IsVisible = visible;
    }

    private void UpdateStepDots(int step)
    {
        var activeBrush = new SolidColorBrush(Color.Parse("#3A8FBF"));
        var inactiveBrush = new SolidColorBrush(Color.Parse("#2A3F5A"));

        for (var i = 1; i <= SetupWizardViewModel.TotalSteps; i++)
        {
            var dot = this.FindControl<Ellipse>($"Dot{i}");
            if (dot != null)
                dot.Fill = i <= step ? activeBrush : inactiveBrush;
        }
    }

    // ── File / folder pickers ────────────────────────────────────────────────

    private async void OnBrowseSourceClick(object? sender, RoutedEventArgs e)
    {
        var storageProvider = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storageProvider == null) return;

        // Offer the user a choice: pick a file (.sln/.csproj) or a folder.
        // We start with a file picker; if they cancel we offer a folder picker.
        var files = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select Source Project or Solution",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Solution or Project") { Patterns = new[] { "*.sln", "*.csproj" } },
                new FilePickerFileType("All Files") { Patterns = new[] { "*.*" } }
            }
        });

        if (files.Count > 0)
        {
            _vm.SourceProjectPath = files[0].Path.LocalPath;
            var box = this.FindControl<TextBox>("SourcePathBox");
            if (box != null) box.Text = _vm.SourceProjectPath;
            HideError();
            return;
        }

        // No file selected — let them pick a folder instead.
        var folders = await storageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Or Select Source Project Folder"
        });

        if (folders.Count > 0)
        {
            _vm.SourceProjectPath = folders[0].Path.LocalPath;
            var box = this.FindControl<TextBox>("SourcePathBox");
            if (box != null) box.Text = _vm.SourceProjectPath;
            HideError();
        }
    }

    private async void OnBrowseWorkspaceFolderClick(object? sender, RoutedEventArgs e)
    {
        var storageProvider = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storageProvider == null) return;

        var folders = await storageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Choose or Create Workspace Folder"
        });

        if (folders.Count > 0)
        {
            _vm.WorkspaceFolderPath = folders[0].Path.LocalPath;
            var box = this.FindControl<TextBox>("WorkspaceFolderBox");
            if (box != null) box.Text = _vm.WorkspaceFolderPath;
            HideError();
        }
    }

    // ── TextBox change handlers (keep VM in sync with typed values) ──────────

    private void OnSourcePathChanged(object? sender, TextChangedEventArgs e)
    {
        if (sender is TextBox tb)
            _vm.SourceProjectPath = tb.Text ?? string.Empty;
    }

    private void OnWorkspaceFolderChanged(object? sender, TextChangedEventArgs e)
    {
        if (sender is TextBox tb)
            _vm.WorkspaceFolderPath = tb.Text ?? string.Empty;
    }

    private void OnWorkspaceNameChanged(object? sender, TextChangedEventArgs e)
    {
        if (sender is TextBox tb)
            _vm.WorkspaceName = tb.Text ?? string.Empty;
    }

    private void OnProfileNameSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox cb && cb.SelectedItem is ComboBoxItem item)
            _vm.ProfileName = item.Content?.ToString() ?? "Default";
    }

    private void OnNewSystemNameChanged(object? sender, TextChangedEventArgs e)
    {
        if (sender is TextBox tb)
            _vm.NewSystemName = tb.Text ?? string.Empty;
    }

    // ── System list (Step 4) ─────────────────────────────────────────────────

    private void OnSystemNameKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            AddSystem();
    }

    private void OnAddSystemClick(object? sender, RoutedEventArgs e) => AddSystem();

    private void AddSystem()
    {
        _vm.AddSystem();
        RefreshSystemsList();

        // Clear the input box.
        var box = this.FindControl<TextBox>("NewSystemNameBox");
        if (box != null) box.Text = string.Empty;
    }

    private void RefreshSystemsList()
    {
        var list = this.FindControl<ItemsControl>("SystemsList");
        if (list == null) return;

        list.Items.Clear();

        foreach (var name in _vm.SystemNames)
        {
            var systemName = name;
            var row = new DockPanel { LastChildFill = false, Margin = new Avalonia.Thickness(0, 2) };

            var removeBtn = new Button
            {
                Content = "✕",
                Padding = new Avalonia.Thickness(6, 2),
                FontSize = 11,
                Foreground = Brushes.Gray,
                [DockPanel.DockProperty] = Dock.Right
            };
            removeBtn.Click += (_, _) =>
            {
                _vm.RemoveSystem(systemName);
                RefreshSystemsList();
            };

            var label = new TextBlock
            {
                Text = systemName,
                Foreground = Brushes.LightGray,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                Margin = new Avalonia.Thickness(4, 0)
            };

            row.Children.Add(removeBtn);
            row.Children.Add(label);
            list.Items.Add(row);
        }
    }

    // ── Validation error banner ──────────────────────────────────────────────

    private void ShowError(string message)
    {
        var banner = this.FindControl<Border>("ErrorBanner");
        var text = this.FindControl<TextBlock>("ErrorText");
        if (banner != null) banner.IsVisible = true;
        if (text != null) text.Text = message;
    }

    private void HideError()
    {
        var banner = this.FindControl<Border>("ErrorBanner");
        if (banner != null) banner.IsVisible = false;
    }
}
