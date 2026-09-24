using Microsoft.Extensions.Time.Testing;
using Subverted.App.ViewModels;
using Subverted.Core;
using Subverted.Protocol;
using static Subverted.App.Tests.Entries;

namespace Subverted.App.Tests;

/// <summary>
/// The review window through its view model: it lists what would be sent, shares the table's
/// ticks and the strip's message and commit, and closes only on a commit the daemon took.
/// </summary>
public sealed class CommitReviewTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    private readonly FakeWorkingCopyCommit _commits = new();
    private readonly FakeCommitReviewOpener _reviews = new();
    private readonly FakeTimeProvider _reviewClock = new();
    private readonly FakeWorkingCopyDiff _reviewDiffs = new();

    [Test]
    public async Task Review_needs_something_ticked_but_not_yet_a_message()
    {
        var view = await ListedAsync(Listing(Entry("new.cs", NodeStatus.Unversioned)));
        var before = view.Composer.ReviewCommand.CanExecute(null);

        view.ToggleTickCommand.Execute(view.Entries[0]);

        await Assert.That(before).IsFalse();
        await Assert.That(view.Composer.Message).IsEqualTo("");
        await Assert.That(view.Composer.ReviewCommand.CanExecute(null)).IsTrue();
    }

    [Test]
    public async Task Review_waits_while_a_commit_is_in_flight()
    {
        var release = new TaskCompletionSource();
        _commits.Answers(FakeWorkingCopyCommit.Committed(9), release.Task);
        var view = await ListedAsync(Listing(Entry("a.cs")));
        view.Composer.Message = "Fix";

        var committing = view.Composer.CommitCommand.ExecuteAsync(null);
        var whileCommitting = view.Composer.ReviewCommand.CanExecute(null);
        release.SetResult();
        await committing;
        view.ToggleTickCommand.Execute(view.Entries[0]);

        await Assert.That(whileCommitting).IsFalse();
        await Assert.That(view.Composer.ReviewCommand.CanExecute(null)).IsTrue();
    }

    [Test]
    public async Task It_lists_only_what_the_commit_would_send()
    {
        var view = await ListedAsync(
            Listing(
                Entry("art/a.png"),
                Entry("build.log", NodeStatus.Unversioned),
                Entry("src/b.cs", NodeStatus.Added),
                Entry("c.txt", NodeStatus.Conflicted, isConflicted: true)
            )
        );

        var review = await OpenAsync(view);

        await Assert
            .That(review.Changes.Select(entry => entry.Key))
            .IsEquivalentTo(new[] { "art/a.png", "src/b.cs" });
        await Assert.That(review.Changes.All(entry => entry.IsTicked)).IsTrue();
    }

    [Test]
    public async Task The_first_change_is_selected_and_its_diff_asked_for()
    {
        var view = await ListedAsync(Listing(Entry("art/a.png"), Entry("src/b.cs")));

        var review = await OpenAsync(view);
        _reviewClock.Advance(DiffPaneViewModel.SelectionDebounce);
        // The debounce's continuation runs off this thread, so the question lands a moment later.
        for (var wait = 0; wait < 200 && _reviewDiffs.Paths.Count == 0; wait++)
        {
            await Task.Delay(10);
        }

        await Assert.That(review.SelectedChange).IsSameReferenceAs(review.Changes[0]);
        await Assert.That(review.Diff.Row!.RelPath).IsEqualTo("art/a.png");
        await Assert
            .That(_reviewDiffs.Paths)
            .IsEquivalentTo(new[] { DiffTarget.PathOf(Info.RootPath, "art/a.png") });
    }

    [Test]
    public async Task Picking_another_change_shows_its_diff_and_picking_none_clears_it()
    {
        var view = await ListedAsync(Listing(Entry("art/a.png"), Entry("src/b.cs")));
        var review = await OpenAsync(view);

        review.SelectedChange = review.Changes[1];
        var picked = review.Diff.Row?.RelPath;
        review.SelectedChange = null;

        await Assert.That(picked).IsEqualTo("src/b.cs");
        await Assert.That(review.Diff.State).IsEqualTo(DiffPaneState.NothingSelected);
    }

    [Test]
    public async Task Its_diff_is_its_own_and_leaves_the_tables_alone()
    {
        var view = await ListedAsync(Listing(Entry("art/a.png")));

        var review = await OpenAsync(view);

        await Assert.That(review.Diff).IsNotSameReferenceAs(view.Diff);
        await Assert.That(review.Diff.State).IsEqualTo(DiffPaneState.Loading);
        await Assert.That(view.Diff.State).IsEqualTo(DiffPaneState.NothingSelected);
    }

    [Test]
    public async Task Unticking_in_the_review_unticks_in_the_table_and_keeps_the_line_listed()
    {
        var view = await ListedAsync(Listing(Entry("art/a.png"), Entry("src/b.cs")));
        var review = await OpenAsync(view);

        review.ToggleTickCommand.Execute(review.Changes[0]);

        await Assert.That(view.Ticked).IsEquivalentTo(new[] { "src/b.cs" });
        await Assert.That(Line(view, "art/a.png").IsTicked).IsFalse();
        await Assert.That(review.Changes.Count).IsEqualTo(2);
        await Assert.That(review.Changes[0].IsTicked).IsFalse();
        await Assert.That(review.Changes[1].IsTicked).IsTrue();
        await Assert.That(view.Composer.ButtonText).IsEqualTo("Commit 1 file");
    }

    [Test]
    public async Task Ticking_a_line_again_in_the_review_ticks_it_in_the_table()
    {
        var view = await ListedAsync(Listing(Entry("art/a.png"), Entry("src/b.cs")));
        var review = await OpenAsync(view);

        review.ToggleTickCommand.Execute(review.Changes[0]);
        review.ToggleTickCommand.Execute(review.Changes[0]);

        await Assert.That(view.Ticked).IsEquivalentTo(new[] { "art/a.png", "src/b.cs" });
        await Assert.That(Line(view, "art/a.png").IsTicked).IsTrue();
        await Assert.That(review.Changes[0].IsTicked).IsTrue();
    }

    [Test]
    public async Task A_tick_changed_in_the_table_shows_in_the_review()
    {
        var view = await ListedAsync(Listing(Entry("art/a.png"), Entry("src/b.cs")));
        var review = await OpenAsync(view);

        view.ToggleTickCommand.Execute(Line(view, "src/b.cs"));

        await Assert.That(review.Changes[1].IsTicked).IsFalse();
        await Assert.That(review.Changes[0].IsTicked).IsTrue();
    }

    /// <summary>D20 holds here as in the table: unticking an added folder settles what lies in it.</summary>
    [Test]
    public async Task Unticking_an_added_folder_makes_its_contents_no_longer_a_choice()
    {
        var view = await ListedAsync(
            Listing(
                Entry("new", NodeStatus.Added, kind: NodeKind.Directory),
                Entry("new/a.png", NodeStatus.Added)
            )
        );
        var review = await OpenAsync(view);
        var child = review.Changes.Single(entry => entry.Key == "new/a.png");
        var tickableBefore = review.ToggleTickCommand.CanExecute(child);

        review.ToggleTickCommand.Execute(review.Changes.Single(entry => entry.Key == "new"));

        await Assert.That(tickableBefore).IsTrue();
        await Assert.That(child.TickMark).IsNull();
        await Assert.That(review.ToggleTickCommand.CanExecute(child)).IsFalse();
        await Assert.That(view.Composer.Selection.Sent.Count).IsEqualTo(0);
    }

    [Test]
    public async Task A_line_that_is_not_there_cannot_be_ticked()
    {
        var view = await ListedAsync(Listing(Entry("art/a.png")));
        var review = await OpenAsync(view);

        await Assert.That(review.ToggleTickCommand.CanExecute(null)).IsFalse();
    }

    [Test]
    public async Task The_message_is_the_strips_own()
    {
        var view = await ListedAsync(Listing(Entry("art/a.png")));
        view.Composer.Message = "Started in the strip";

        var review = await OpenAsync(view);
        review.Composer.Message = "Finished in the review";

        await Assert.That(review.Composer).IsSameReferenceAs(view.Composer);
        await Assert.That(view.Composer.Message).IsEqualTo("Finished in the review");
    }

    [Test]
    public async Task A_commit_from_the_review_sends_the_ticks_and_closes_it()
    {
        _commits.Answers(FakeWorkingCopyCommit.Committed(12));
        var view = await ListedAsync(Listing(Entry("art/a.png"), Entry("src/b.cs")));
        var review = await OpenAsync(view);
        review.ToggleTickCommand.Execute(review.Changes[1]);
        review.Composer.Message = "Art only";

        await review.Composer.CommitCommand.ExecuteAsync(null);

        var (paths, message) = _commits.Commits.Single();
        await Assert
            .That(paths)
            .IsEquivalentTo(new[] { DiffTarget.PathOf(Info.RootPath, "art/a.png") });
        await Assert.That(message).IsEqualTo("Art only");
        await Assert.That(_reviews.IsOpen).IsFalse();
        await Assert.That(view.Composer.Notice!.Headline).IsEqualTo("Committed r12");
        await Assert.That(view.Composer.Message).IsEqualTo("");
        await Assert.That(view.Ticked).IsEmpty();
    }

    [Test]
    public async Task A_commit_that_needed_nothing_sending_closes_it_too()
    {
        _commits.Answers(FakeWorkingCopyCommit.Committed(null));
        var view = await ListedAsync(Listing(Entry("art/a.png")));
        await OpenAsync(view);
        view.Composer.Message = "Again";

        await view.Composer.CommitCommand.ExecuteAsync(null);

        await Assert.That(_reviews.IsOpen).IsFalse();
        await Assert.That(view.Composer.Notice!.Kind).IsEqualTo(NoticeKind.Succeeded);
    }

    [Test]
    public async Task A_refused_commit_keeps_it_open_with_the_strips_notice()
    {
        _commits.Answers(new ErrorResponse(DaemonErrorKind.RequestRefused, "out of date"));
        var view = await ListedAsync(Listing(Entry("art/a.png")));
        var review = await OpenAsync(view);
        review.Composer.Message = "Try";

        await review.Composer.CommitCommand.ExecuteAsync(null);

        await Assert.That(_reviews.IsOpen).IsTrue();
        await Assert.That(review.Composer.Notice!.Kind).IsEqualTo(NoticeKind.NothingWritten);
        await Assert.That(review.Composer.Notice.Detail).IsEqualTo("out of date");
        await Assert.That(view.Composer.Message).IsEqualTo("Try");
        await Assert.That(review.Changes[0].IsTicked).IsTrue();
    }

    [Test]
    public async Task A_commit_left_marked_keeps_it_open()
    {
        _commits.Answers(
            new SelectionNotCommittedResponse(
                new SelectionSchedule([], [], []),
                SelectionStep.Commit,
                "",
                "svn: E165001: refused by the hook"
            )
        );
        var view = await ListedAsync(Listing(Entry("art/a.png")));
        var review = await OpenAsync(view);
        review.Composer.Message = "Try";

        await review.Composer.CommitCommand.ExecuteAsync(null);

        await Assert.That(_reviews.IsOpen).IsTrue();
        await Assert.That(review.Composer.Notice!.Kind).IsEqualTo(NoticeKind.LeftMarked);
    }

    [Test]
    public async Task A_commit_no_daemon_answered_keeps_it_open()
    {
        _commits.IsUnreachable();
        var view = await ListedAsync(Listing(Entry("art/a.png")));
        var review = await OpenAsync(view);
        review.Composer.Message = "Try";

        await review.Composer.CommitCommand.ExecuteAsync(null);

        await Assert.That(_reviews.IsOpen).IsTrue();
        await Assert.That(review.Composer.Notice!.Kind).IsEqualTo(NoticeKind.Uncertain);
    }

    [Test]
    public async Task Cancel_closes_it_without_committing_and_keeps_the_message_and_ticks()
    {
        var view = await ListedAsync(Listing(Entry("art/a.png"), Entry("src/b.cs")));
        var review = await OpenAsync(view);
        review.ToggleTickCommand.Execute(review.Changes[0]);
        review.Composer.Message = "Half written";

        review.CancelCommand.Execute(null);

        await Assert.That(_reviews.IsOpen).IsFalse();
        await Assert.That(_commits.Commits).IsEmpty();
        await Assert.That(view.Composer.Message).IsEqualTo("Half written");
        await Assert.That(view.Ticked).IsEquivalentTo(new[] { "src/b.cs" });
    }

    [Test]
    public async Task Once_closed_it_stops_following_the_strip_and_drops_its_diff()
    {
        var view = await ListedAsync(Listing(Entry("art/a.png"), Entry("src/b.cs")));
        var review = await OpenAsync(view);
        _reviews.CloseFromOutside();
        await Task.Yield();

        view.ToggleTickCommand.Execute(Line(view, "art/a.png"));
        view.Composer.Message = "From the strip";
        await view.Composer.CommitCommand.ExecuteAsync(null);

        await Assert.That(review.Diff.State).IsEqualTo(DiffPaneState.NothingSelected);
        await Assert.That(review.Changes[0].IsTicked).IsTrue();
        await Assert.That(_reviews.CloseRequests).IsEqualTo(0);
    }

    [Test]
    public async Task Each_review_starts_from_what_is_ticked_then()
    {
        var view = await ListedAsync(Listing(Entry("art/a.png"), Entry("src/b.cs")));
        var first = await OpenAsync(view);
        first.ToggleTickCommand.Execute(first.Changes[0]);
        first.CancelCommand.Execute(null);

        var second = await OpenAsync(view);

        await Assert.That(_reviews.Opened.Count).IsEqualTo(2);
        await Assert.That(second.Changes.Select(entry => entry.Key)).IsEquivalentTo(["src/b.cs"]);
    }

    private async Task<CommitReviewViewModel> OpenAsync(WorkingCopyViewModel view)
    {
        _ = view.Composer.ReviewCommand.ExecuteAsync(null);
        await Task.Yield();
        return _reviews.Review;
    }

    private async Task<WorkingCopyViewModel> ListedAsync(StatusResponse listing)
    {
        var view = WorkingCopies.View(
            new FakeWorkingCopyStatus().Answers(listing),
            commits: _commits,
            reviews: _reviews,
            reviewPanes: () => DiffPanes.Pane(_reviewDiffs, _reviewClock)
        );
        await view.RefreshAsync(None);
        return view;
    }

    private static ChangeListEntry Line(WorkingCopyViewModel view, string key) =>
        view.Entries.Single(entry => entry.Key == key);
}
