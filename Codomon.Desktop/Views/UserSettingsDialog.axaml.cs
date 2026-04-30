using Avalonia.Controls;
using Codomon.Desktop.Models;
using Codomon.Desktop.Persistence;

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

        if (endpointBox != null) endpointBox.Text = _config.DefaultLlmSettings.ApiEndpoint;
        if (modelBox    != null) modelBox.Text    = _config.DefaultLlmSettings.ModelName;
    }

    // ── Button handlers ───────────────────────────────────────────────────────

    private void OnSaveClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var endpointBox = this.FindControl<TextBox>("ApiEndpointBox");
        var modelBox    = this.FindControl<TextBox>("ModelNameBox");

        _config.DefaultLlmSettings.ApiEndpoint = endpointBox?.Text?.Trim() ?? string.Empty;
        _config.DefaultLlmSettings.ModelName   = modelBox?.Text?.Trim()    ?? string.Empty;

        UserConfigService.Save(_config);

        var statusText = this.FindControl<TextBlock>("SaveStatusText");
        if (statusText != null)
        {
            statusText.Text = "Saved ✔";
            statusText.Foreground = Avalonia.Media.Brushes.LightGreen;
        }
    }

    private void OnCancelClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        => Close();
}
