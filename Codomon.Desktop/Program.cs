using Avalonia;
using System;

namespace Codomon.Desktop;

class Program
{
    // Entry point for the Codomon desktop application.   
    // The Avalonia UI bootstrap and app lifecycle are handled by `App.axaml` and
    // `App.axaml.cs`.
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

    
    // Builds and configures the Avalonia application builder.
    
    // - AppBuilder.Configure(): registers the application type and its XAML root with 
    //   the Avalonia AppBuilder
    // - UsePlatformDetect(): detects and initializes the appropriate platform/windowing
    //   and rendering backend for the current OS (Windows, Linux Wayland/X11, macOS).
    // - WithInterFont(): adds the Inter font to the app's font collection so UI text
    //   uses a consistent, readable typeface (provided by Avalonia extensions).
    // - LogToTrace(): routes Avalonia logging to System.Diagnostics.Trace
    //   to help with debugging and diagnostics.    
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
