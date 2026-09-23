using Avalonia;

namespace Subverted.App;

public static class Program
{
    [STAThread]
    public static int Main(string[] args) =>
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    /// <summary>Also what the XAML previewer and the headless tests start from.</summary>
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder
            .Configure<App>()
            .UsePlatformDetect()
            .With(
                new Win32PlatformOptions
                {
                    // WinUI composition is what lets Mica show through with rounded corners, the
                    // way a WinUI 3 window looks; the rest are the fallbacks for older Windows.
                    CompositionMode =
                    [
                        Win32CompositionMode.WinUIComposition,
                        Win32CompositionMode.DirectComposition,
                        Win32CompositionMode.RedirectionSurface,
                    ],
                    WinUICompositionBackdropCornerRadius = 8,
                }
            )
            .LogToTrace();
}
