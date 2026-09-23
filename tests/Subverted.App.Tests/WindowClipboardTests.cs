using Avalonia.Controls;
using Avalonia.Input.Platform;
using Subverted.App.Infrastructure;

namespace Subverted.App.Tests;

/// <summary>The clipboard adapter against the headless platform's clipboard, which is a real one.</summary>
public sealed class WindowClipboardTests
{
    [Test]
    public async Task Copied_text_is_what_the_next_paste_finds()
    {
        var pasted = await HeadlessApp.Session.Dispatch(
            async () =>
            {
                var window = new Window();
                window.Show();
                await new WindowClipboard(() => window).CopyAsync("/studio/game/art/hero.png");
                var text = await window.Clipboard!.TryGetTextAsync();
                window.Close();
                return text;
            },
            CancellationToken.None
        );

        await Assert.That(pasted).IsEqualTo("/studio/game/art/hero.png");
    }

    /// <summary>Before the window exists there is no clipboard to reach, and that is not an error.</summary>
    [Test]
    public async Task With_no_window_yet_copying_does_nothing()
    {
        await new WindowClipboard(() => null).CopyAsync("anything");
    }
}
