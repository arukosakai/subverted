using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.Time.Testing;
using Subverted.App.ViewModels;
using Subverted.App.Views;
using Subverted.Core;
using static Subverted.App.Tests.Entries;

namespace Subverted.App.Tests;

/// <summary>
/// The window as it really renders, headless. The assertions are about the visual tree the XAML
/// produced; the saved frames are for a person to look at, since nothing here has a display.
/// </summary>
public sealed class MainWindowRenderTests
{
    private static readonly string Screens = Path.Combine(AppContext.BaseDirectory, "screens");

    [Test]
    [Arguments("Dark")]
    [Arguments("Light")]
    public async Task A_working_copy_renders_one_row_per_change(string variant)
    {
        var rows = await RenderAsync(
            variant,
            new FakeWorkingCopyStatus().Answers(
                Listing(
                    Entry("art/characters/hero.png"),
                    Entry("art/characters/villain.png", NodeStatus.Conflicted),
                    Entry("art/props/crate.png", NodeStatus.Added, isCopied: true),
                    Entry("levels/forest.map", NodeStatus.Modified, PropertyStatus.Modified),
                    Entry("levels/old-cave.map", NodeStatus.Deleted),
                    Entry("sound/theme.ogg", NodeStatus.Missing),
                    Entry("art/ui/button.psd", NodeStatus.Unmodified, hasLockToken: true),
                    Entry("notes.txt", NodeStatus.Unversioned)
                )
            ),
            $"working-copy-{variant.ToLowerInvariant()}.png"
        );

        await Assert.That(rows).IsEqualTo(8);
    }

    [Test]
    public async Task A_clean_working_copy_renders_no_rows()
    {
        var rows = await RenderAsync(
            "Dark",
            new FakeWorkingCopyStatus().Answers(Listing()),
            "clean.png"
        );

        await Assert.That(rows).IsEqualTo(0);
    }

    [Test]
    public async Task No_daemon_renders_no_rows()
    {
        var rows = await RenderAsync(
            "Dark",
            new FakeWorkingCopyStatus().IsUnreachable("Nothing is listening on the daemon socket."),
            "unreachable.png"
        );

        await Assert.That(rows).IsEqualTo(0);
    }

    [Test]
    public async Task First_run_renders_without_a_working_copy()
    {
        var hasView = await HeadlessApp.Session.Dispatch(
            async () =>
            {
                var window = await ShowAsync(
                    "Dark",
                    new FakeRecentStore(),
                    new FakeWorkingCopyStatus()
                );
                Save(window, "first-run.png");
                var shown = window
                    .GetVisualDescendants()
                    .OfType<WorkingCopyView>()
                    .Any(view => view.IsEffectivelyVisible);
                window.Close();
                return shown;
            },
            CancellationToken.None
        );

        await Assert.That(hasView).IsFalse();
    }

    private static Task<int> RenderAsync(
        string variant,
        FakeWorkingCopyStatus status,
        string file
    ) =>
        HeadlessApp.Session.Dispatch(
            async () =>
            {
                var window = await ShowAsync(
                    variant,
                    new FakeRecentStore("/studio/game", "/studio/tools", "/studio/website"),
                    status
                );
                Save(window, file);
                var rows = window.GetVisualDescendants().OfType<ListBoxItem>().Count();
                window.Close();
                return rows;
            },
            CancellationToken.None
        );

    private static async Task<MainWindow> ShowAsync(
        string variant,
        FakeRecentStore store,
        FakeWorkingCopyStatus status
    )
    {
        Application.Current!.RequestedThemeVariant =
            variant == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
        var viewModel = new MainWindowViewModel(
            store,
            new FakeFolderPicker(null),
            path => new WorkingCopyViewModel(path, status, DiffPanes.Pane()),
            new FakeTimeProvider(),
            StringComparison.Ordinal
        );

        var window = new MainWindow(viewModel);
        window.Show();

        // Opening starts the view model; the fake answers at once, so a few turns settle it.
        for (var turn = 0; turn < 5; turn++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Yield();
        }

        return window;
    }

    private static void Save(MainWindow window, string file)
    {
        Directory.CreateDirectory(Screens);
        window
            .CaptureRenderedFrame()
            ?.Save(Path.Combine(Screens, file), PngBitmapEncoderOptions.Default);
    }
}
