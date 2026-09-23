using Avalonia;
using Avalonia.Headless;

namespace Subverted.App.Tests;

/// <summary>
/// The real <see cref="App"/> — its theme, tokens and styles — on the headless platform, drawn with
/// Skia so a rendered frame is real pixels rather than a stub.
/// </summary>
public static class HeadlessApp
{
    private static readonly Lazy<HeadlessUnitTestSession> Shared = new(() =>
        HeadlessUnitTestSession.StartNew(typeof(HeadlessApp))
    );

    public static HeadlessUnitTestSession Session => Shared.Value;

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder
            .Configure<App>()
            .UseSkia()
            .WithInterFont()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
