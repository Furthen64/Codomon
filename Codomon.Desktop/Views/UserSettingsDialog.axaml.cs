using Avalonia.Controls;
using Codomon.Desktop.Models;
using Codomon.Desktop.Persistence;
using Codomon.Desktop.Services;

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
    }

    // ── Button handlers ───────────────────────────────────────────────────────

    private void OnSaveClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var endpointBox = this.FindControl<TextBox>("ApiEndpointBox");
        var modelBox    = this.FindControl<TextBox>("ModelNameBox");
        var okButton    = this.FindControl<Button>("OkButton");

        _config.DefaultLlmSettings.ApiEndpoint = endpointBox?.Text?.Trim() ?? string.Empty;
        _config.DefaultLlmSettings.ModelName   = modelBox?.Text?.Trim()    ?? string.Empty;

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
