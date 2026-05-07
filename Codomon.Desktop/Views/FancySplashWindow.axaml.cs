using System;
using System.Text;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

namespace Codomon.Desktop.Views;

public partial class FancySplashWindow : Window
{
    private readonly DispatcherTimer _codeTimer;
    private int _lineOffset;

    public FancySplashWindow()
    {
        InitializeComponent();

        _codeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(130) };
        _codeTimer.Tick += (_, _) => UpdateCodeBackdrop();

        Opened += async (_, _) =>
        {
            _codeTimer.Start();
            await RunIntroAnimationAsync();
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

    private async System.Threading.Tasks.Task RunIntroAnimationAsync()
    {
        Opacity = 0;
        var animation = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(400),
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
