using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Codomon.Desktop.Services;
using Codomon.Desktop.Views;
using Codomon.Desktop.Persistence;

namespace Codomon.Desktop;

public partial class App : Application
{
    // step 1: The XAML root is loaded by the App instance itself, 
    // Initialize(),
    // AvaloniaXamlLoader.Load() 

    // step 2: After that:
    // OnFrameworkInitializationCompleted() performs lifetime setup and creates the MainWindow.

    // step 3: Views/MainWindow

    
    public override void Initialize()
    {
        // (step 1)
        DebugLaunchTrace.Write("Loading App.axaml.");
        AvaloniaXamlLoader.Load(this);
        DebugLaunchTrace.Write("App.axaml loaded.");
    }

    
    public override void OnFrameworkInitializationCompleted()
    {
        // (step 2)
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            DebugLaunchTrace.Write("Starting splash-to-main window flow.");
            _ = StartWithSplashAsync(desktop);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static async Task StartWithSplashAsync(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var timings = new SplashTimings(
            MinimumSplashMilliseconds: 7000,
            FallbackSplashMilliseconds: 500,
            ProgressStepMilliseconds: 250,
            FancyCodeRefreshMilliseconds: 700,
            FancyFadeInMilliseconds: 3000);
        Window? splash = null;
        FancySplashWindow? fancy = null;
        var splashTimer = Stopwatch.StartNew();

        try
        {
            DebugLaunchTrace.Write("Selecting splash window implementation.");
            if (SplashModeSelector.ShouldUseFancySplash())
            {
                DebugLaunchTrace.Write("Constructing FancySplashWindow from Views/FancySplashWindow.axaml.cs.");
                fancy = new FancySplashWindow(
                    timings.FancyCodeRefreshMilliseconds,
                    timings.FancyFadeInMilliseconds);
                splash = fancy;
            }
            else
            {
                DebugLaunchTrace.Write("Constructing StaticSplashWindow from Views/StaticSplashWindow.axaml.cs.");
                splash = new StaticSplashWindow();
            }

            DebugLaunchTrace.Write($"Opening {splash.GetType().Name}.");
            splash.Show();

            await SimulateStartupAsync(fancy, timings.ProgressStepMilliseconds);
        }
        catch (Exception ex)
        {
            DebugLaunchTrace.Exception(ex);
            splash?.Close();
            DebugLaunchTrace.Write("Constructing fallback StaticSplashWindow from Views/StaticSplashWindow.axaml.cs.");
            splash = new StaticSplashWindow();
            DebugLaunchTrace.Write("Opening fallback StaticSplashWindow.");
            splash.Show();
            await Task.Delay(timings.FallbackSplashMilliseconds);
        }

        var remaining = timings.MinimumSplashMilliseconds - (int)splashTimer.ElapsedMilliseconds;
        if (remaining > 0)
        {
            await Task.Delay(remaining);
        }

        try
        {
            DebugLaunchTrace.Write("Transition splash => main: constructing Views/MainWindow.axaml.cs.");
            var main = new MainWindow();
            DebugLaunchTrace.Write("MainWindow construction completed.");
            desktop.MainWindow = main;
            main.Opened += OnMainWindowOpened;
            DebugLaunchTrace.Write("Opening MainWindow.");
            main.Show();

            if (splash is not null)
            {
                DebugLaunchTrace.Write($"Closing {splash.GetType().Name} after MainWindow.Show().");
                await Dispatcher.UIThread.InvokeAsync(() => splash.Close());
            }
        }
        catch (Exception ex)
        {
            DebugLaunchTrace.Exception(ex);
            throw;
        }
    }

    private static void OnMainWindowOpened(object? sender, EventArgs e)
    {
        if (sender is not Window main)
            return;

        main.Opened -= OnMainWindowOpened;

        try
        {
            var userCfg = UserConfigService.Load();
            main.WindowState = userCfg.StartMaximized ? WindowState.Maximized : WindowState.Normal;
        }
        catch
        {
            // ignore and proceed with default state
        }
    }

    private readonly record struct SplashTimings(
        int MinimumSplashMilliseconds,
        int FallbackSplashMilliseconds,
        int ProgressStepMilliseconds,
        int FancyCodeRefreshMilliseconds,
        int FancyFadeInMilliseconds);

    private static async Task SimulateStartupAsync(FancySplashWindow? fancy, int progressStepMilliseconds)
    {
        (int progress, string status)[] steps =
        [
            (20, "Loading workspace metadata..."),
            (45, "Warming analysis services..."),
            (70, "Preparing graph rendering..."),
            (95, "Finalizing startup...")
        ];

        foreach (var (progress, status) in steps)
        {
            fancy?.SetProgress(progress, status);
            await Task.Delay(progressStepMilliseconds);
        }
    }
}
