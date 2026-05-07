using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Platform;

namespace Codomon.Desktop.Services;

public static class SplashModeSelector
{
    public static bool ShouldUseFancySplash()
    {
        var forceStatic = Environment.GetEnvironmentVariable("CODOMON_SPLASH_FORCE_STATIC");
        if (string.Equals(forceStatic, "1", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(forceStatic, "true", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var forceFancy = Environment.GetEnvironmentVariable("CODOMON_SPLASH_FORCE_FANCY");
        if (string.Equals(forceFancy, "1", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(forceFancy, "true", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var renderingMode = Environment.GetEnvironmentVariable("AVALONIA_RENDERING_MODE");
        if (!string.IsNullOrWhiteSpace(renderingMode) &&
            renderingMode.Contains("software", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var gpuRequested = Environment.GetEnvironmentVariable("AVALONIA_SKIA_GPU");
        if (!string.IsNullOrWhiteSpace(gpuRequested) &&
            (gpuRequested == "0" || gpuRequested.Equals("false", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        // Avalonia no longer exposes renderer details on Window in current versions.
        // If not explicitly forced to software/static modes above, prefer the fancy splash.
        return true;
    }
}
