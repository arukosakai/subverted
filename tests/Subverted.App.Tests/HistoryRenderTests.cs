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
using Subverted.Protocol;

namespace Subverted.App.Tests;

/// <summary>
/// The window switched to History, rendered headless around fake answers shaped like the
/// <c>subverted-history</c> fixture's. The frames are saved for a person to look at.
/// </summary>
public sealed class HistoryRenderTests
{
    private static readonly string Screens = Path.Combine(AppContext.BaseDirectory, "screens");

    [Test]
    [Arguments("Dark")]
    [Arguments("Light")]
    public async Task History_renders_the_revisions_the_marker_and_the_picked_diff(string variant)
    {
        var seen = await HeadlessApp.Session.Dispatch(
            async () =>
            {
                var clock = new FakeTimeProvider();
                var history = new FakeRevisionHistory()
                    .Page(
                        new LogResponse([
                            Entry(
                                9,
                                "Edit the file with an odd name",
                                ("/100% done file.txt", PathChange.Modified, null)
                            ),
                            Entry(
                                8,
                                "Add a file with an odd name",
                                ("/100% done file.txt", PathChange.Added, null)
                            ),
                            Entry(
                                7,
                                "b.txt loses its trailing newline",
                                ("/b.txt", PathChange.Modified, null)
                            ),
                            Entry(
                                6,
                                "Drop the readme",
                                ("/docs/readme.txt", PathChange.Deleted, null)
                            ),
                            Entry(5, "New hero", ("/art/hero.png", PathChange.Modified, null)),
                            Entry(
                                3,
                                "Rename a.txt to b.txt",
                                ("/a.txt", PathChange.Deleted, null),
                                ("/b.txt", PathChange.Added, "/a.txt")
                            ),
                            Entry(2, "Edit a.txt", ("/a.txt", PathChange.Modified, null)),
                        ])
                    )
                    .Range(3, 5);
                var diffs = new FakeRevisionDiff().Answers(
                    "Index: a.txt\n===================================================================\n--- a.txt\t(revision 2)\n+++ a.txt\t(revision 3)\n@@ -1,3 +0,0 @@\n-one\n-TWO\n-three\n"
                );
                var window = await ShowAsync(variant, Revisions.View(history, diffs, clock));
                var viewModel = (MainWindowViewModel)window.DataContext!;

                await viewModel.ShowHistoryCommand.ExecuteAsync(null);
                viewModel.History.SelectedItem = viewModel.History.Items[5];
                clock.Advance(DiffPaneViewModel.SelectionDebounce);
                await Settle();

                Save(window, $"history-{variant.ToLowerInvariant()}.png");
                var texts = window
                    .GetVisualDescendants()
                    .OfType<TextBlock>()
                    .Where(text => text.IsEffectivelyVisible)
                    .Select(text => text.Text)
                    .ToList();
                var historyShown = window
                    .GetVisualDescendants()
                    .OfType<HistoryView>()
                    .Any(view => view.IsEffectivelyVisible);
                var changesShown = window
                    .GetVisualDescendants()
                    .OfType<WorkingCopyView>()
                    .Any(view => view.IsEffectivelyVisible);
                window.Close();
                return (texts, historyShown, changesShown);
            },
            CancellationToken.None
        );

        await Assert.That(seen.historyShown).IsTrue();
        await Assert.That(seen.changesShown).IsFalse();
        await Assert.That(seen.texts).Contains("Local changes");
        await Assert.That(seen.texts).Contains("Your copy is at r3–r5 — mixed, updated in parts");
        await Assert.That(seen.texts.Count(text => text == "not in your copy")).IsEqualTo(4);
        await Assert.That(seen.texts.Count(text => text == "in part of your copy")).IsEqualTo(1);
        await Assert.That(seen.texts).Contains("Rename a.txt to b.txt");
        await Assert.That(seen.texts).Contains("from /a.txt@2");
        await Assert.That(seen.texts).Contains("TWO");
        await Assert.That(seen.texts).Contains("7 loaded");
    }

    /// <summary>
    /// The real list, scrolled: nothing is asked while the top of a full page is showing, and the
    /// next page is asked for once the list is scrolled to its end.
    /// </summary>
    [Test]
    public async Task Scrolling_the_list_to_its_end_reads_the_next_page()
    {
        var history = new FakeRevisionHistory()
            .Page(
                new LogResponse([
                    .. Enumerable
                        .Range(0, HistoryViewModel.PageSize)
                        .Select(offset => Revisions.Entry(200 - offset)),
                ])
            )
            .Page(9, 8);

        var pagesBeforeScrolling = await HeadlessApp.Session.Dispatch(
            async () =>
            {
                var window = await ShowAsync("Dark", Revisions.View(history));
                var viewModel = (MainWindowViewModel)window.DataContext!;
                await viewModel.ShowHistoryCommand.ExecuteAsync(null);
                await Settle();
                var before = history.Pages.Count;

                var scroller = window
                    .GetVisualDescendants()
                    .OfType<ListBox>()
                    .Single(list => list.Name == "Revisions")
                    .GetVisualDescendants()
                    .OfType<ScrollViewer>()
                    .First();
                scroller.Offset = new Vector(0, scroller.Extent.Height);
                await Settle();
                window.Close();
                return before;
            },
            CancellationToken.None
        );

        await Assert.That(pagesBeforeScrolling).IsEqualTo(1);
        await Assert.That(history.Pages.Count).IsEqualTo(2);
        await Assert.That(history.Pages[1].Start).IsEqualTo(new HistoryFromRevision(150));
    }

    [Test]
    public async Task A_history_view_with_nothing_bound_to_it_ignores_its_own_scrolling()
    {
        var rendered = await HeadlessApp.Session.Dispatch(
            async () =>
            {
                var window = new Window
                {
                    Content = new HistoryView(),
                    Width = 800,
                    Height = 600,
                };
                window.Show();
                await Settle();
                var shown = window.GetVisualDescendants().OfType<HistoryView>().Single();
                window.Close();
                return shown.DataContext is null;
            },
            CancellationToken.None
        );

        await Assert.That(rendered).IsTrue();
    }

    private static RevisionEntry Entry(
        long revision,
        string message,
        params (string Path, PathChange Change, string? From)[] paths
    ) =>
        new(
            revision,
            "aruko",
            Revisions.Committed.AddMinutes(revision),
            message,
            [
                .. paths.Select(path => new ChangedPath(
                    path.Path,
                    path.Change,
                    path.From,
                    path.From is null ? null : revision - 1
                )),
            ]
        );

    private static async Task<MainWindow> ShowAsync(string variant, HistoryViewModel history)
    {
        Application.Current!.RequestedThemeVariant =
            variant == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
        var viewModel = new MainWindowViewModel(
            new FakeRecentStore("/studio/game", "/studio/tools"),
            new FakeFolderPicker(null),
            path => new WorkingCopyViewModel(
                path,
                new FakeWorkingCopyStatus().Answers(Entries.Listing()),
                DiffPanes.Pane()
            ),
            new FakeTimeProvider(),
            StringComparison.Ordinal,
            history
        );

        // Tall enough that every revision is realised, so the counts below are the whole list's.
        var window = new MainWindow(viewModel) { Height = 1100 };
        window.Show();
        await Settle();
        return window;
    }

    private static async Task Settle()
    {
        for (var turn = 0; turn < 8; turn++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Yield();
        }
    }

    private static void Save(MainWindow window, string file)
    {
        Directory.CreateDirectory(Screens);
        window
            .CaptureRenderedFrame()
            ?.Save(Path.Combine(Screens, file), PngBitmapEncoderOptions.Default);
    }
}
