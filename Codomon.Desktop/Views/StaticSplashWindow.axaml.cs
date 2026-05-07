using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Codomon.Desktop.Views;

public partial class StaticSplashWindow : Window
{
    public StaticSplashWindow()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
