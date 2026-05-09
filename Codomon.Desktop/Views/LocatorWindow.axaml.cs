using Avalonia.Controls;
using Avalonia.Threading;
using Codomon.Desktop.Models;
using Codomon.Desktop.Persistence;
using Codomon.Desktop.Services;

namespace Codomon.Desktop.Views;

/// <summary>
/// Locator window — lets the user type a plain-English question and uses the LLM together with
/// the workspace architecture notes and file summaries to provide a targeted answer.
/// </summary>
public partial class LocatorWindow : Window
{
    private readonly WorkspaceModel _workspace;
    private readonly string _workspaceFolderPath;

    private CancellationTokenSource? _cts;

    public LocatorWindow()
        : this(new WorkspaceModel(), string.Empty)
    {
    }

    public LocatorWindow(WorkspaceModel workspace, string workspaceFolderPath)
    {
        InitializeComponent();
        _workspace          = workspace;
        _workspaceFolderPath = workspaceFolderPath;

        Opened += (_, _) => CheckRequirements();
    }

    // ── Requirements check ────────────────────────────────────────────────────

    private void CheckRequirements()
    {
        var (apiEndpoint, modelName) = ResolveEffectiveLlmSettings();
        bool llmOk      = !string.IsNullOrWhiteSpace(apiEndpoint) && !string.IsNullOrWhiteSpace(modelName);
        bool summaryOk  = !string.IsNullOrWhiteSpace(_workspaceFolderPath)
                          && LocatorService.HasSummaries(_workspaceFolderPath);
        bool archOk     = !string.IsNullOrWhiteSpace(_workspaceFolderPath)
                          && LocatorService.HasArchitectureNotes(_workspaceFolderPath);

        SetCheckMark("LlmCheckMark",      "LlmCheckLabel",      llmOk,
                     llmOk ? $"LLM configured — {modelName}" : "LLM not configured (open Preferences)");
        SetCheckMark("SummaryCheckMark",  "SummaryCheckLabel",  summaryOk,
                     summaryOk ? "Summaries found" : "No summaries yet (run LLM Summaries first)");
        SetCheckMark("ArchCheckMark",     "ArchCheckLabel",     archOk,
                     archOk ? "Architecture notes found" : "No architecture notes yet (run Architecture Hypothesis first)");

        // The Ask button requires at minimum LLM + architecture notes.
        var askBtn = this.FindControl<Button>("AskButton");
        if (askBtn != null)
            askBtn.IsEnabled = llmOk && archOk;

        if (!llmOk || !archOk)
        {
            var answer = this.FindControl<TextBlock>("AnswerText");
            if (answer != null)
            {
                var missing = new List<string>();
                if (!llmOk)     missing.Add("LLM configuration (Preferences)");
                if (!archOk)    missing.Add("Architecture notes (Architecture Hypothesis dialog)");
                if (!summaryOk) missing.Add("Summaries (LLM Summaries dialog)");
                answer.Text = $"Requirements not met. Please set up the following: {string.Join(", ", missing)}.";
            }
        }
    }

    private void SetCheckMark(string markName, string labelName, bool ok, string labelText)
    {
        var mark  = this.FindControl<TextBlock>(markName);
        var label = this.FindControl<TextBlock>(labelName);

        if (mark != null)
        {
            mark.Text       = ok ? "✔" : "○";
            mark.Foreground = ok
                ? new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#44CC44"))
                : new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#CC4444"));
        }

        if (label != null)
        {
            label.Text       = labelText;
            label.Foreground = ok
                ? new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#88CCAA"))
                : new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#CC7777"));
        }
    }

    // ── Button handlers ───────────────────────────────────────────────────────

    private void OnCloseClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _cts?.Cancel();
        Close();
    }

    private void OnCancelClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _cts?.Cancel();
        SetStatus("Cancelled.");
        SetBusy(false);
    }

    private async void OnAskClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var questionBox = this.FindControl<TextBox>("QuestionBox");
        var question    = questionBox?.Text?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(question))
        {
            SetStatus("Please type a question first.");
            return;
        }

        var includeSummaryCheckBox = this.FindControl<CheckBox>("IncludeSummariesCheckBox");
        bool includeSummaries = includeSummaryCheckBox?.IsChecked == true;

        var (apiEndpoint, modelName) = ResolveEffectiveLlmSettings();

        _cts = new CancellationTokenSource();
        SetBusy(true);

        var answerText = this.FindControl<TextBlock>("AnswerText");
        if (answerText != null)
            answerText.Text = "Thinking…";

        var progress = new Progress<string>(msg => Dispatcher.UIThread.Post(() => SetStatus(msg)));

        try
        {
            var answer = await LocatorService.AskAsync(
                apiEndpoint,
                modelName,
                _workspaceFolderPath,
                question,
                includeSummaries,
                progress,
                _cts.Token);

            if (answerText != null)
                answerText.Text = answer;

            SetStatus("Done.");
        }
        catch (OperationCanceledException)
        {
            if (answerText != null)
                answerText.Text = "(Cancelled)";
            SetStatus("Cancelled.");
        }
        catch (Exception ex)
        {
            AppLogger.Error($"[Locator] Ask failed: {ex.Message}");
            if (answerText != null)
                answerText.Text = $"Error: {ex.Message}";
            SetStatus("Error — see answer area.");
        }
        finally
        {
            SetBusy(false);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private (string ApiEndpoint, string ModelName) ResolveEffectiveLlmSettings()
    {
        // Prefer workspace-level settings; fall back to user defaults.
        const string DefaultEndpoint = "http://localhost:8080/v1";
        var userConfig = UserConfigService.Load();

        var wsEndpoint = _workspace.LlmSettings.ApiEndpoint;
        var wsModel    = _workspace.LlmSettings.ModelName;

        bool hasExplicitEndpoint = !string.IsNullOrWhiteSpace(wsEndpoint)
            && (!string.Equals(wsEndpoint, DefaultEndpoint, StringComparison.OrdinalIgnoreCase)
                || !string.IsNullOrWhiteSpace(wsModel));

        var endpoint = hasExplicitEndpoint ? wsEndpoint : userConfig.DefaultLlmSettings.ApiEndpoint;
        var model    = !string.IsNullOrEmpty(wsModel)    ? wsModel  : userConfig.DefaultLlmSettings.ModelName;

        return (endpoint, model);
    }

    private void SetStatus(string message)
    {
        var statusText = this.FindControl<TextBlock>("StatusText");
        if (statusText != null)
            statusText.Text = message;
    }

    private void SetBusy(bool busy)
    {
        var askBtn    = this.FindControl<Button>("AskButton");
        var cancelBtn = this.FindControl<Button>("CancelButton");

        if (askBtn    != null) askBtn.IsEnabled    = !busy;
        if (cancelBtn != null) cancelBtn.IsEnabled = busy;
    }
}
