using System;
using System.Text;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Avalonia.Threading;

namespace Codomon.Desktop.Views;

public partial class FancySplashWindow : Window
{
    private readonly DispatcherTimer _codeTimer;
    private readonly TimeSpan _fadeInDuration;
    private int _lineOffset;

    public FancySplashWindow() : this(1300, 4000)
    {
    }

    public FancySplashWindow(int codeRefreshMilliseconds, int fadeInMilliseconds)
    {
        InitializeComponent();

        _codeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(codeRefreshMilliseconds) };
        _fadeInDuration = TimeSpan.FromMilliseconds(fadeInMilliseconds);
        _codeTimer.Tick += (_, _) => UpdateCodeBackdrop();

        Opened += async (_, _) =>
        {
            _codeTimer.Start();
            await RunFadeInAsync();
        };

        Closed += (_, _) => _codeTimer.Stop();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void UpdateCodeBackdrop()
    {
        if (this.FindControl<TextBlock>("CodeBackdrop") is not { } code)
            return;

        var sb = new StringBuilder();
        for (var i = 0; i < 22; i++)
        {
            var n = _lineOffset + i;
            sb.AppendLine($"[trace] module_{n % 9}::AnalyzeNode(id:{n * 13 % 1037}) => OK");
        }

        code.Text = sb.ToString();
        _lineOffset++;
    }

    private async System.Threading.Tasks.Task RunFadeInAsync()
    {
        Opacity = 0;
        var animation = new Animation
        {
            Duration = _fadeInDuration,
            Easing = new CubicEaseOut(),
            FillMode = FillMode.Forward,
            Children =
            {
                new KeyFrame { Cue = new Cue(0d), Setters = { new Setter(OpacityProperty, 0d) } },
                new KeyFrame { Cue = new Cue(1d), Setters = { new Setter(OpacityProperty, 1d) } }
            }
        };

        await animation.RunAsync(this);
    }

    public void SetProgress(double value, string status)
    {
        if (this.FindControl<ProgressBar>("LoadProgress") is { } bar)
            bar.Value = Math.Clamp(value, 0, 100);

        if (this.FindControl<TextBlock>("StatusText") is { } text)
            text.Text = status;
    }
}
