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

namespace Codomon.Desktop;

public partial class App : Application
{
    // step 1: The XAML root is loaded by the App instance itself, 
    // Initialize(),
    // AvaloniaXamlLoader.Load() 

    // step 2: After that:
    // OnFrameworkInitializationCompleted() performs lifetime setup and creates the MainWindow.

    public override void Initialize()
    {
        // (step 1)
        AvaloniaXamlLoader.Load(this);
    }

    
    public override void OnFrameworkInitializationCompleted()
    {
        // (step 2)
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _ = StartWithSplashAsync(desktop);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static async Task StartWithSplashAsync(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var timings = new SplashTimings(
            MinimumSplashMilliseconds: 20000,
            FallbackSplashMilliseconds: 500,
            ProgressStepMilliseconds: 250,
            FancyCodeRefreshMilliseconds: 1300,
            FancyFadeInMilliseconds: 4000);
        Window? splash = null;
        FancySplashWindow? fancy = null;
        var splashTimer = Stopwatch.StartNew();

        try
        {
            if (SplashModeSelector.ShouldUseFancySplash())
            {
                fancy = new FancySplashWindow(
                    timings.FancyCodeRefreshMilliseconds,
                    timings.FancyFadeInMilliseconds);
                splash = fancy;
            }
            else
            {
                splash = new StaticSplashWindow();
            }

            splash.Show();

            await SimulateStartupAsync(fancy, timings.ProgressStepMilliseconds);
        }
        catch
        {
            splash?.Close();
            splash = new StaticSplashWindow();
            splash.Show();
            await Task.Delay(timings.FallbackSplashMilliseconds);
        }

        var remaining = timings.MinimumSplashMilliseconds - (int)splashTimer.ElapsedMilliseconds;
        if (remaining > 0)
        {
            await Task.Delay(remaining);
        }

        var main = new MainWindow();
        desktop.MainWindow = main;
        main.Show();

        if (splash is not null)
        {
            await Dispatcher.UIThread.InvokeAsync(() => splash.Close());
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
