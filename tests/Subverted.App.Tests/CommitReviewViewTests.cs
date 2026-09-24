using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Extensions.Time.Testing;
using Subverted.App.ViewModels;
using Subverted.App.Views;
using Subverted.Core;
using Subverted.Protocol;
using static Subverted.App.Tests.Entries;

namespace Subverted.App.Tests;

/// <summary>
/// The strip's Review button and the review window in the real views, keys pressed on the headless
/// platform, and the window rendered for a person to look at under <c>screens/</c>.
/// </summary>
public sealed class CommitReviewViewTests
{
    private static readonly string Screens = Path.Combine(AppContext.BaseDirectory, "screens");

    private const string ForestDiff = """
        Index: levels/forest.map
        ===================================================================
        --- levels/forest.map	(revision 7)
        +++ levels/forest.map	(working copy)
        @@ -1,4 +1,5 @@
         spawn 12 40
        -tree 18 22
        +tree 18 24
        +tree 20 26
         river 0 30 64 30
         exit 63 12

        """;

    [Test]
    public async Task The_review_button_waits_for_a_tick_but_not_for_a_message()
    {
        var (before, after) = await OnStripAsync(
            Listing(Entry("new.png", NodeStatus.Unversioned)),
            (_, strip, view, _) =>
            {
                var button = Named<Button>(strip, "ReviewButton");
                var enabledBefore = button.IsEffectivelyEnabled;
                view.ToggleTickCommand.Execute(view.Entries[0]);
                Dispatcher.UIThread.RunJobs();
                return (enabledBefore, button.IsEffectivelyEnabled);
            }
        );

        await Assert.That(before).IsFalse();
        await Assert.That(after).IsTrue();
    }

    [Test]
    public async Task Clicking_review_opens_it_on_the_ticked_changes()
    {
        var keys = await OnStripAsync(
            Listing(Entry("a.png"), Entry("b.png")),
            (_, strip, _, reviews) =>
            {
                Named<Button>(strip, "ReviewButton").Command!.Execute(null);
                Dispatcher.UIThread.RunJobs();
                return reviews.Review.Changes.Select(entry => entry.Key).ToList();
            }
        );

        await Assert.That(keys).IsEquivalentTo(new[] { "a.png", "b.png" });
    }

    [Test]
    public async Task Escape_closes_the_window_without_committing()
    {
        var commits = new FakeWorkingCopyCommit();
        var (closed, message) = await OnWindowAsync(
            commits,
            Listing(Entry("a.png")),
            (window, review) =>
            {
                review.Composer.Message = "Kept";
                var closed = false;
                window.Closed += (_, _) => closed = true;
                Named<Button>(window, "CancelButton").Focus();
                window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
                Dispatcher.UIThread.RunJobs();
                return (closed, review.Composer.Message);
            }
        );

        await Assert.That(closed).IsTrue();
        await Assert.That(message).IsEqualTo("Kept");
        await Assert.That(commits.Commits).IsEmpty();
    }

    [Test]
    public async Task Ctrl_enter_in_the_message_box_commits_and_closes_the_window()
    {
        var commits = new FakeWorkingCopyCommit();
        var closed = await OnWindowAsync(
            commits,
            Listing(Entry("a.png")),
            (window, _) =>
            {
                var closed = false;
                window.Closed += (_, _) => closed = true;
                Named<TextBox>(window, "MessageBox").Focus();
                window.KeyTextInput("From the review");
                window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.Control);
                Dispatcher.UIThread.RunJobs();
                return closed;
            }
        );

        await Assert.That(closed).IsTrue();
        await Assert
            .That(commits.Commits.Select(commit => commit.Message))
            .IsEquivalentTo(new[] { "From the review" });
    }

    [Test]
    public async Task The_message_box_has_the_focus_when_the_window_opens()
    {
        var focused = await OnWindowAsync(
            new FakeWorkingCopyCommit(),
            Listing(Entry("a.png")),
            (window, _) => Named<TextBox>(window, "MessageBox").IsFocused
        );

        await Assert.That(focused).IsTrue();
    }

    [Test]
    public async Task A_refused_commit_leaves_the_window_open_with_its_notice()
    {
        var commits = new FakeWorkingCopyCommit().Answers(
            new ErrorResponse(DaemonErrorKind.RequestRefused, "'a.png' is out of date.")
        );
        var (closed, headline, detail) = await OnWindowAsync(
            commits,
            Listing(Entry("a.png")),
            (window, review) =>
            {
                var closed = false;
                window.Closed += (_, _) => closed = true;
                review.Composer.Message = "Try";
                Named<Button>(window, "CommitButton").Command!.Execute(null);
                Dispatcher.UIThread.RunJobs();
                var notice = Named<NoticeView>(window, "CommitNotice");
                return (
                    closed,
                    Named<TextBlock>(notice, "Headline").Text,
                    Named<SelectableTextBlock>(notice, "Detail").Text
                );
            }
        );

        await Assert.That(closed).IsFalse();
        await Assert.That(headline).IsEqualTo("Nothing was committed");
        await Assert.That(detail).IsEqualTo("'a.png' is out of date.");
    }

    [Test]
    public async Task Space_on_a_line_drops_it_from_the_commit()
    {
        var (ticked, button) = await OnWindowAsync(
            new FakeWorkingCopyCommit(),
            Listing(Entry("a.png"), Entry("b.png")),
            (window, review) =>
            {
                var list = Named<ListBox>(window, "List");
                list.ContainerFromIndex(0)!.Focus();
                window.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None);
                Dispatcher.UIThread.RunJobs();
                return (
                    (review.Changes[0].IsTicked, review.Changes[1].IsTicked),
                    (string?)Named<Button>(window, "CommitButton").Content
                );
            }
        );

        await Assert.That(ticked).IsEqualTo((false, true));
        await Assert.That(button).IsEqualTo("Commit 1 file");
    }

    [Test]
    public async Task Clicking_a_lines_box_unticks_it()
    {
        var ticked = await OnWindowAsync(
            new FakeWorkingCopyCommit(),
            Listing(Entry("a.png"), Entry("b.png")),
            (window, review) =>
            {
                var box = Named<ListBox>(window, "List")
                    .GetVisualDescendants()
                    .OfType<CheckBox>()
                    .First();
                box.Command!.Execute(box.CommandParameter);
                Dispatcher.UIThread.RunJobs();
                return (review.Changes[0].IsTicked, review.Changes[1].IsTicked);
            }
        );

        await Assert.That(ticked).IsEqualTo((false, true));
    }

    [Test]
    [Arguments("Dark")]
    [Arguments("Light")]
    public async Task The_review_renders_its_list_the_first_diff_and_the_message(string variant)
    {
        var clock = new FakeTimeProvider();
        var diffs = new FakeWorkingCopyDiff().Answers(ForestDiff);
        var (rows, shown) = await HeadlessApp.Session.Dispatch(
            async () =>
            {
                Application.Current!.RequestedThemeVariant =
                    variant == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
                var reviews = new FakeCommitReviewOpener();
                var view = WorkingCopies.View(
                    new FakeWorkingCopyStatus().Answers(
                        Listing(
                            Entry("levels/forest.map"),
                            Entry(
                                "scripts/player.lua",
                                NodeStatus.Modified,
                                PropertyStatus.Modified
                            ),
                            Entry("sound/theme.ogg", NodeStatus.Missing),
                            Entry("textures/crate.png", NodeStatus.Added),
                            Entry("notes.txt", NodeStatus.Unversioned)
                        )
                    ),
                    reviews: reviews,
                    reviewPanes: () => DiffPanes.Pane(diffs, clock)
                );
                await view.RefreshAsync(CancellationToken.None);
                view.Composer.Message = "Widen the forest path";
                _ = view.Composer.ReviewCommand.ExecuteAsync(null);
                var review = reviews.Review;
                review.ToggleTickCommand.Execute(
                    review.Changes.Single(entry => entry.Key == "sound/theme.ogg")
                );

                var window = new CommitReviewWindow(review);
                window.Show();
                clock.Advance(DiffPaneViewModel.SelectionDebounce);
                for (var turn = 0; turn < 50 && review.Diff.State != DiffPaneState.Ready; turn++)
                {
                    Dispatcher.UIThread.RunJobs();
                    await Task.Delay(10);
                }

                Dispatcher.UIThread.RunJobs();
                Directory.CreateDirectory(Screens);
                window
                    .CaptureRenderedFrame()
                    ?.Save(
                        Path.Combine(Screens, $"commit-review-{variant.ToLowerInvariant()}.png"),
                        PngBitmapEncoderOptions.Default
                    );
                var result = (
                    Named<ListBox>(window, "List")
                        .GetVisualDescendants()
                        .OfType<ListBoxItem>()
                        .Count(),
                    window
                        .GetVisualDescendants()
                        .OfType<DiffLinesView>()
                        .Single()
                        .IsEffectivelyVisible
                );
                window.Close();
                return result;
            },
            CancellationToken.None
        );

        await Assert.That(rows).IsEqualTo(4);
        await Assert.That(shown).IsTrue();
    }

    private static T Named<T>(Control root, string name)
        where T : Control =>
        root.GetVisualDescendants().OfType<T>().Single(control => control.Name == name);

    private static Task<T> OnStripAsync<T>(
        StatusResponse listing,
        Func<Window, Control, WorkingCopyViewModel, FakeCommitReviewOpener, T> act
    ) =>
        HeadlessApp.Session.Dispatch(
            async () =>
            {
                var reviews = new FakeCommitReviewOpener();
                var view = WorkingCopies.View(
                    new FakeWorkingCopyStatus().Answers(listing),
                    reviews: reviews
                );
                await view.RefreshAsync(CancellationToken.None);
                var strip = new CommitComposerView { DataContext = view.Composer };
                var window = new Window
                {
                    Width = 900,
                    Height = 200,
                    Content = strip,
                };
                window.Show();
                Dispatcher.UIThread.RunJobs();
                var result = act(window, strip, view, reviews);
                window.Close();
                return result;
            },
            CancellationToken.None
        );

    private static Task<T> OnWindowAsync<T>(
        FakeWorkingCopyCommit commits,
        StatusResponse listing,
        Func<CommitReviewWindow, CommitReviewViewModel, T> act
    ) =>
        HeadlessApp.Session.Dispatch(
            async () =>
            {
                var reviews = new FakeCommitReviewOpener();
                var view = WorkingCopies.View(
                    new FakeWorkingCopyStatus().Answers(listing),
                    commits: commits,
                    reviews: reviews
                );
                await view.RefreshAsync(CancellationToken.None);
                _ = view.Composer.ReviewCommand.ExecuteAsync(null);
                var window = new CommitReviewWindow(reviews.Review);
                window.Show();
                Dispatcher.UIThread.RunJobs();
                var result = act(window, reviews.Review);
                if (window.IsVisible)
                {
                    window.Close();
                }

                return result;
            },
            CancellationToken.None
        );
}
