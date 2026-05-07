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
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _ = StartWithSplashAsync(desktop);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static async Task StartWithSplashAsync(IClassicDesktopStyleApplicationLifetime desktop)
    {
        const int MinimumSplashMilliseconds = 5000;
        Window? splash = null;
        FancySplashWindow? fancy = null;
        var splashTimer = Stopwatch.StartNew();

        try
        {
            if (SplashModeSelector.ShouldUseFancySplash())
            {
                fancy = new FancySplashWindow();
                splash = fancy;
            }
            else
            {
                splash = new StaticSplashWindow();
            }

            splash.Show();

            await SimulateStartupAsync(fancy);
        }
        catch
        {
            splash?.Close();
            splash = new StaticSplashWindow();
            splash.Show();
            await Task.Delay(500);
        }

        var remaining = MinimumSplashMilliseconds - (int)splashTimer.ElapsedMilliseconds;
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

    private static async Task SimulateStartupAsync(FancySplashWindow? fancy)
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
            await Task.Delay(250);
        }
    }
}
