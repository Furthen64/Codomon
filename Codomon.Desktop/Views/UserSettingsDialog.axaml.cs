using Avalonia.Controls;
using Codomon.Desktop.Models;
using Codomon.Desktop.Persistence;
using Codomon.Desktop.Services;
using Codomon.Desktop.ViewModels;

namespace Codomon.Desktop.Views;

/// <summary>
/// Dialog for editing application-wide user settings.
/// Changes are committed to disk only when the user clicks Save Settings.
/// </summary>
public partial class UserSettingsDialog : Window
{
    private readonly UserConfigModel _config;

    public UserSettingsDialog()
    {
        InitializeComponent();
        _config = UserConfigService.Load();
        Opened += (_, _) => PopulateFields();
    }

    // ── Initialisation ────────────────────────────────────────────────────────

    private void PopulateFields()
    {
        var endpointBox = this.FindControl<TextBox>("ApiEndpointBox");
        var modelBox    = this.FindControl<TextBox>("ModelNameBox");
        var pathText    = this.FindControl<TextBlock>("ConfigPathText");

        if (endpointBox != null) endpointBox.Text = _config.DefaultLlmSettings.ApiEndpoint;
        if (modelBox    != null) modelBox.Text    = _config.DefaultLlmSettings.ModelName;
        if (pathText    != null) pathText.Text    = $"Path: {UserConfigService.GetConfigFilePath()}";

        // Autosave
        var autosaveInterval = this.FindControl<NumericUpDown>("AutosaveIntervalBox");
        var maxAutosaves     = this.FindControl<NumericUpDown>("MaxAutosavesBox");
        if (autosaveInterval != null) autosaveInterval.Value = _config.AutosaveIntervalMinutes;
        if (maxAutosaves     != null) maxAutosaves.Value     = _config.MaxAutosaves;

        // Recent workspaces
        var maxRecent = this.FindControl<NumericUpDown>("MaxRecentWorkspacesBox");
        if (maxRecent != null) maxRecent.Value = _config.MaxRecentWorkspaces;

        // Replay speed
        PopulateReplaySpeedComboBox();

        // Import defaults
        PopulateImportDefaultComboBox("DefaultDelimiterComboBox",
            ImportWizardViewModel.DelimiterOptions.Select(o => (o.Label, o.Key)),
            _config.DefaultImportDelimiterKey);
        PopulateImportDefaultComboBox("DefaultKnownFormatComboBox",
            ImportWizardViewModel.KnownAppLogFormats.Select(o => (o.Label, o.Key)),
            _config.DefaultImportKnownFormatKey);
        PopulateImportDefaultComboBox("DefaultTimestampFormatComboBox",
            ImportWizardViewModel.TimestampFormatOptions.Select(o => (o.Label, o.Key)),
            _config.DefaultImportTimestampFormatKey);
        PopulateImportDefaultComboBox("DefaultTimeZoneComboBox",
            ImportWizardViewModel.TimeZoneOptions.Select(o => (o.Label, o.Id)),
            _config.DefaultImportTimeZoneId);
    }

    private void PopulateReplaySpeedComboBox()
    {
        var combo = this.FindControl<ComboBox>("DefaultReplaySpeedComboBox");
        if (combo == null) return;

        var speeds = new[] { ("0.5×", 0.5), ("1×", 1.0), ("2×", 2.0), ("4×", 4.0), ("8×", 8.0) };
        combo.Items.Clear();
        foreach (var (label, value) in speeds)
            combo.Items.Add(new ComboBoxItem
            {
                Content = label,
                Tag = value.ToString(System.Globalization.CultureInfo.InvariantCulture)
            });

        int bestIndex = 1;
        double bestDiff = double.MaxValue;
        for (int i = 0; i < speeds.Length; i++)
        {
            var diff = Math.Abs(speeds[i].Item2 - _config.DefaultReplaySpeed);
            if (diff < bestDiff) { bestDiff = diff; bestIndex = i; }
        }
        combo.SelectedIndex = bestIndex;
    }

    private void PopulateImportDefaultComboBox(string controlName,
        IEnumerable<(string Label, string Key)> options, string selectedKey)
    {
        var combo = this.FindControl<ComboBox>(controlName);
        if (combo == null) return;

        combo.Items.Clear();
        int selectIndex = 0, i = 0;
        foreach (var (label, key) in options)
        {
            combo.Items.Add(new ComboBoxItem { Content = label, Tag = key });
            if (key == selectedKey) selectIndex = i;
            i++;
        }
        combo.SelectedIndex = selectIndex;
    }

    private static string? GetSelectedTag(ComboBox? combo)
        => combo?.SelectedItem is ComboBoxItem item ? item.Tag?.ToString() : null;

    // ── Button handlers ───────────────────────────────────────────────────────

    private void OnSaveClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var endpointBox = this.FindControl<TextBox>("ApiEndpointBox");
        var modelBox    = this.FindControl<TextBox>("ModelNameBox");
        var okButton    = this.FindControl<Button>("OkButton");

        _config.DefaultLlmSettings.ApiEndpoint = endpointBox?.Text?.Trim() ?? string.Empty;
        _config.DefaultLlmSettings.ModelName   = modelBox?.Text?.Trim()    ?? string.Empty;

        // Autosave
        var autosaveInterval = this.FindControl<NumericUpDown>("AutosaveIntervalBox");
        var maxAutosaves     = this.FindControl<NumericUpDown>("MaxAutosavesBox");
        if (autosaveInterval?.Value is decimal iv) _config.AutosaveIntervalMinutes = Math.Max(1, (int)iv);
        if (maxAutosaves?.Value     is decimal mv) _config.MaxAutosaves            = Math.Max(1, (int)mv);

        // Recent workspaces
        var maxRecent = this.FindControl<NumericUpDown>("MaxRecentWorkspacesBox");
        if (maxRecent?.Value is decimal rv) _config.MaxRecentWorkspaces = Math.Max(1, (int)rv);

        // Replay speed
        var replayCombo = this.FindControl<ComboBox>("DefaultReplaySpeedComboBox");
        if (replayCombo?.SelectedItem is ComboBoxItem replayItem &&
            double.TryParse(replayItem.Tag?.ToString(),
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture,
                out var speed))
            _config.DefaultReplaySpeed = speed;

        // Import defaults
        _config.DefaultImportDelimiterKey       = GetSelectedTag(this.FindControl<ComboBox>("DefaultDelimiterComboBox"))       ?? _config.DefaultImportDelimiterKey;
        _config.DefaultImportKnownFormatKey     = GetSelectedTag(this.FindControl<ComboBox>("DefaultKnownFormatComboBox"))     ?? _config.DefaultImportKnownFormatKey;
        _config.DefaultImportTimestampFormatKey = GetSelectedTag(this.FindControl<ComboBox>("DefaultTimestampFormatComboBox")) ?? _config.DefaultImportTimestampFormatKey;
        _config.DefaultImportTimeZoneId         = GetSelectedTag(this.FindControl<ComboBox>("DefaultTimeZoneComboBox"))        ?? _config.DefaultImportTimeZoneId;

        UserConfigService.Save(_config);

        var statusText = this.FindControl<TextBlock>("SaveStatusText");
        if (statusText != null)
        {
            statusText.Text = "Saved ✔";
            statusText.Foreground = Avalonia.Media.Brushes.LightGreen;
        }

        if (okButton != null)
            okButton.IsEnabled = true;
    }

    private async void OnTestConnectionClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var endpointBox = this.FindControl<TextBox>("ApiEndpointBox");
        var modelBox = this.FindControl<TextBox>("ModelNameBox");
        var btn = this.FindControl<Button>("TestConnectionButton");
        var status = this.FindControl<TextBlock>("ConnectionStatusText");

        var endpoint = endpointBox?.Text?.Trim() ?? string.Empty;
        var model = modelBox?.Text?.Trim() ?? string.Empty;

        if (btn != null) btn.IsEnabled = false;
        if (status != null)
        {
            status.Text = "Testing...";
            status.Foreground = Avalonia.Media.Brushes.Gray;
        }

        try
        {
            var (ok, message) = await LlmSummaryService.TestConnectionAsync(endpoint, model, CancellationToken.None);
            if (status != null)
            {
                status.Text = ok ? "OK  " + message : "Fail  " + message;
                status.Foreground = ok
                    ? Avalonia.Media.Brushes.LightGreen
                    : new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#FF8888"));
            }
        }
        catch (Exception ex)
        {
            if (status != null)
            {
                status.Text = "Fail  " + ex.Message;
                status.Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#FF8888"));
            }
        }
        finally
        {
            if (btn != null) btn.IsEnabled = true;
        }
    }

    private async void OnProbeModelsClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var endpointBox = this.FindControl<TextBox>("ApiEndpointBox");
        var btn = this.FindControl<Button>("ProbeModelsButton");
        var probeText = this.FindControl<TextBlock>("ProbeStatusText");
        var picker = this.FindControl<ComboBox>("ModelPickerComboBox");

        var endpoint = endpointBox?.Text?.Trim() ?? string.Empty;

        if (btn != null) btn.IsEnabled = false;
        if (probeText != null)
        {
            probeText.Text = "Probing...";
            probeText.Foreground = Avalonia.Media.Brushes.Gray;
        }

        try
        {
            var models = await LlmSummaryService.FetchModelsAsync(endpoint, CancellationToken.None);
            var count = models.Count;

            if (picker != null)
            {
                picker.ItemsSource = models;
                picker.IsEnabled = count > 0;
            }

            if (probeText != null)
            {
                if (count > 0)
                {
                    probeText.Text = $"Found {count} model(s).";
                    probeText.Foreground = Avalonia.Media.Brushes.LightGreen;
                }
                else
                {
                    probeText.Text = "No models returned - check endpoint and try Test Connection first.";
                    probeText.Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#FF8888"));
                }
            }
        }
        catch (Exception ex)
        {
            if (probeText != null)
            {
                probeText.Text = $"Probe failed: {ex.Message}";
                probeText.Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#FF8888"));
            }
        }
        finally
        {
            if (btn != null) btn.IsEnabled = true;
        }
    }

    private void OnModelPickerSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox cb && cb.SelectedItem is string modelId)
        {
            var modelBox = this.FindControl<TextBox>("ModelNameBox");
            if (modelBox != null) modelBox.Text = modelId;
        }
    }

    private void OnOkClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => Close();

    private void OnCancelClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => Close();
}
