using Microsoft.Extensions.Time.Testing;
using Subverted.App.Presentation;
using Subverted.App.ViewModels;
using Subverted.Core;
using Subverted.Protocol;
using TUnit.Assertions.Enums;

namespace Subverted.App.Tests;

public sealed class HistoryViewModelTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    private readonly FakeTimeProvider _clock = new();
    private readonly FakeRevisionHistory _history = new();
    private readonly FakeRevisionDiff _diffs = new();

    [Test]
    public async Task Before_any_path_is_shown_nothing_is_listed_and_nothing_claims_to_be_empty()
    {
        var view = View();

        await Assert.That(view.State).IsEqualTo(HistoryState.NothingShown);
        await Assert.That(view.IsEmpty).IsFalse();
        await Assert.That(view.Items).IsEmpty();
    }

    [Test]
    public async Task Showing_a_path_reads_its_first_page_from_head_and_its_base_at_once()
    {
        _history.Page(FullPage(from: 60));
        var view = View();

        await view.ShowAsync("/wc", None);

        await Assert
            .That(_history.Pages)
            .IsEquivalentTo([
                ("/wc", (HistoryStart)new HistoryFromHead(), HistoryViewModel.PageSize),
            ]);
        await Assert.That(_history.Ranges).IsEquivalentTo(["/wc"]);
        await Assert.That(view.State).IsEqualTo(HistoryState.Ready);
        await Assert.That(view.Path).IsEqualTo("/wc");
        await Assert.That(view.Items.Count).IsEqualTo(HistoryViewModel.PageSize);
        await Assert.That(view.Items[0].Row.Revision).IsEqualTo(60L);
        await Assert.That(view.HasMore).IsTrue();
        await Assert.That(view.LoadedText).IsEqualTo("50 loaded");
    }

    [Test]
    public async Task While_the_first_page_is_on_its_way_the_list_is_loading_and_not_empty()
    {
        var release = new TaskCompletionSource();
        _history.Page(new LogResponse([]), release.Task);
        var view = View();

        var showing = view.ShowAsync("/wc", None);

        await Assert.That(view.State).IsEqualTo(HistoryState.Loading);
        await Assert.That(view.IsEmpty).IsFalse();
        await Assert.That(view.IsLoadingMore).IsFalse();

        release.SetResult();
        await showing;

        await Assert.That(view.State).IsEqualTo(HistoryState.Ready);
        await Assert.That(view.IsEmpty).IsTrue();
    }

    [Test]
    public async Task Loading_more_starts_one_below_the_oldest_revision_shown_and_keeps_the_rows_already_there()
    {
        _history.Page(FullPage(from: 60)).Page(9, 8, 7);
        var view = View();
        await view.ShowAsync("/wc", None);
        var firstRow = view.Items[0];

        await view.LoadMoreCommand.ExecuteAsync(null);

        await Assert.That(_history.Pages[1].Start).IsEqualTo(new HistoryFromRevision(10));
        await Assert.That(view.Items.Count).IsEqualTo(HistoryViewModel.PageSize + 3);
        await Assert.That(view.Items[0]).IsSameReferenceAs(firstRow);
        await Assert.That(view.Items[^1].Row.Revision).IsEqualTo(7L);
        await Assert.That(view.HasMore).IsFalse();
    }

    [Test]
    public async Task A_short_first_page_is_the_whole_history_so_loading_more_asks_nothing()
    {
        _history.Page(3, 2, 1);
        var view = View();
        await view.ShowAsync("/wc", None);

        await view.LoadMoreCommand.ExecuteAsync(null);

        await Assert.That(view.HasMore).IsFalse();
        await Assert.That(_history.Pages.Count).IsEqualTo(1);
    }

    [Test]
    public async Task Loading_more_before_any_path_was_shown_asks_nothing()
    {
        var view = View();

        await view.LoadMoreCommand.ExecuteAsync(null);

        await Assert.That(_history.Pages).IsEmpty();
    }

    /// <summary>Scrolling fires many times a second; one page must be asked for once.</summary>
    [Test]
    public async Task Scrolling_again_while_a_page_is_on_its_way_does_not_ask_for_it_twice()
    {
        var release = new TaskCompletionSource();
        _history.Page(FullPage(from: 60)).Page(new LogResponse([Revisions.Entry(9)]), release.Task);
        var view = View();
        await view.ShowAsync("/wc", None);

        var loading = view.LoadMoreCommand.ExecuteAsync(null);
        await Assert.That(view.IsLoadingMore).IsTrue();
        await view.LoadMoreCommand.ExecuteAsync(null);
        release.SetResult();
        await loading;

        await Assert.That(_history.Pages.Count).IsEqualTo(2);
        await Assert.That(view.IsLoadingMore).IsFalse();
    }

    [Test]
    public async Task A_daemon_that_does_not_answer_the_first_page_says_so()
    {
        _history.PageIsUnreachable("connection refused");
        var view = View();

        await view.ShowAsync("/wc", None);

        await Assert.That(view.State).IsEqualTo(HistoryState.Unreachable);
        await Assert.That(view.Message).IsEqualTo("connection refused");
        await Assert.That(view.IsEmpty).IsFalse();
    }

    [Test]
    public async Task A_first_page_svn_refused_shows_what_svn_said()
    {
        _history.Page(
            new ErrorResponse(DaemonErrorKind.SvnCommandFailed, "svn: E170013: no server")
        );
        var view = View();

        await view.ShowAsync("/wc", None);

        await Assert.That(view.State).IsEqualTo(HistoryState.Failed);
        await Assert.That(view.Message).IsEqualTo("svn: E170013: no server");
    }

    [Test]
    public async Task An_answer_that_is_not_a_history_is_a_failure_that_names_it()
    {
        _history.Page(new AcknowledgedResponse());
        var view = View();

        await view.ShowAsync("/wc", None);

        await Assert.That(view.State).IsEqualTo(HistoryState.Failed);
        await Assert.That(view.Message).Contains("AcknowledgedResponse");
    }

    [Test]
    public async Task A_later_page_failing_keeps_what_was_read_and_says_why_no_more_came()
    {
        _history.Page(FullPage(from: 60)).PageIsUnreachable("gone").Page(9);
        var view = View();
        await view.ShowAsync("/wc", None);

        await view.LoadMoreCommand.ExecuteAsync(null);

        await Assert.That(view.State).IsEqualTo(HistoryState.Ready);
        await Assert.That(view.Message).IsEqualTo("gone");
        await Assert.That(view.Items.Count).IsEqualTo(HistoryViewModel.PageSize);
        await Assert.That(view.HasMore).IsTrue();

        await view.LoadMoreCommand.ExecuteAsync(null);

        await Assert.That(view.Message).IsNull();
        await Assert.That(view.Items[^1].Row.Revision).IsEqualTo(9L);
    }

    /// <summary>A history of the whole copy that answers after a file's was asked for must not replace it.</summary>
    [Test]
    public async Task A_late_page_for_a_path_no_longer_shown_is_dropped()
    {
        var release = new TaskCompletionSource();
        _history.Page(new LogResponse([Revisions.Entry(99)]), release.Task).Page(5);
        var view = View();

        var first = view.ShowAsync("/wc", None);
        await view.ShowAsync("/wc/art/hero.png", None);
        release.SetResult();
        await first;

        await Assert.That(view.Path).IsEqualTo("/wc/art/hero.png");
        await Assert.That(view.Items.Select(item => item.Row.Revision)).IsEquivalentTo([5L]);
    }

    /// <summary>A question still on the wire for the old path is cancelled, not just ignored.</summary>
    [Test]
    public async Task Showing_another_path_cancels_what_is_still_being_read_for_the_last()
    {
        _history.PageHoldsUntilCancelled().RangeHoldsUntilCancelled().Page(5).Range(5, 5);
        var view = View();

        var first = view.ShowAsync("/wc", None);
        await view.ShowAsync("/wc/sub", None);
        await first;

        await Assert.That(view.State).IsEqualTo(HistoryState.Ready);
        await Assert.That(view.Items.Select(item => item.Row.Revision)).IsEquivalentTo([5L]);
        await Assert.That(view.BaseRange).IsEqualTo(new BaseRevisionRange(5, 5));
        await Assert.That(view.IsLoadingMore).IsFalse();
    }

    [Test]
    public async Task Deselecting_the_revision_clears_its_paths_and_its_diff()
    {
        _history.Page(new LogResponse([Entry(7, "m", "/trunk/a.txt")]));
        var view = View();
        await view.ShowAsync("/wc", None);
        view.SelectedItem = view.Items[0];

        view.SelectedItem = null;

        await Assert.That(view.SelectedRevision).IsNull();
        await Assert.That(view.SelectedPath).IsNull();
        await Assert.That(view.Diff.State).IsEqualTo(DiffPaneState.NothingSelected);
    }

    [Test]
    public async Task A_late_base_range_for_a_path_no_longer_shown_is_dropped()
    {
        var release = new TaskCompletionSource();
        _history
            .Page(5)
            .Page(5)
            .Range(new WorkingCopyRevisionResponse(new BaseRevisionRange(1, 1)), release.Task)
            .Range(5, 5);
        var view = View();

        var first = view.ShowAsync("/wc", None);
        await view.ShowAsync("/wc/sub", None);
        release.SetResult();
        await first;

        await Assert.That(view.BaseRange).IsEqualTo(new BaseRevisionRange(5, 5));
    }

    [Test]
    public async Task A_late_failure_for_a_path_no_longer_shown_is_dropped()
    {
        var release = new TaskCompletionSource();
        _history
            .Page(new ErrorResponse(DaemonErrorKind.SvnCommandFailed, "old"), release.Task)
            .Page(5);
        var view = View();

        var first = view.ShowAsync("/wc", None);
        await view.ShowAsync("/wc/sub", None);
        release.SetResult();
        await first;

        await Assert.That(view.State).IsEqualTo(HistoryState.Ready);
        await Assert.That(view.Message).IsNull();
    }

    [Test]
    public async Task The_base_range_marks_where_the_copy_is_and_what_it_does_not_have()
    {
        _history.Page(12, 10, 9, 7).Range(9, 9);
        var view = View();

        await view.ShowAsync("/wc", None);

        await Assert.That(view.BaseRange).IsEqualTo(new BaseRevisionRange(9, 9));
        await Assert.That(view.BaseText).IsEqualTo("Your copy is at r9");
        await Assert
            .That(view.Items.Select(item => item.BaseMarkerAbove is not null))
            .IsEquivalentTo([false, false, true, false], CollectionOrdering.Matching);
        await Assert
            .That(view.Items.Select(item => item.IsNotInCopy))
            .IsEquivalentTo([true, true, false, false], CollectionOrdering.Matching);
    }

    /// <summary>The marker is a nicety: a copy whose BASE cannot be read still shows its history.</summary>
    [Test]
    public async Task A_base_range_that_cannot_be_read_leaves_the_history_unmarked()
    {
        _history.Page(12, 10).RangeIsUnreachable();
        var view = View();

        await view.ShowAsync("/wc", None);

        await Assert.That(view.State).IsEqualTo(HistoryState.Ready);
        await Assert.That(view.BaseRange).IsNull();
        await Assert.That(view.BaseText).IsNull();
        await Assert.That(view.Items.All(item => item.Presence is null)).IsTrue();
    }

    [Test]
    public async Task A_base_range_answered_with_a_failure_leaves_the_history_unmarked()
    {
        _history.Page(12).Range(new ErrorResponse(DaemonErrorKind.WorkingCopyUnreadable, "no"));
        var view = View();

        await view.ShowAsync("/wc", None);

        await Assert.That(view.BaseRange).IsNull();
    }

    [Test]
    public async Task Search_narrows_what_is_listed_and_says_how_much_it_hides()
    {
        _history.Page(
            new LogResponse([
                Entry(3, "hero walk cycle"),
                Entry(2, "villain"),
                Entry(1, "hero idle"),
            ])
        );
        var view = View();
        await view.ShowAsync("/wc", None);

        view.SearchText = "hero";

        await Assert.That(view.Items.Select(item => item.Row.Revision)).IsEquivalentTo([3L, 1L]);
        await Assert.That(view.LoadedText).IsEqualTo("2 of 3 loaded match");

        view.SearchText = "";

        await Assert.That(view.Items.Count).IsEqualTo(3);
        await Assert.That(view.LoadedText).IsEqualTo("3 loaded");
    }

    [Test]
    public async Task Picking_a_revision_picks_its_first_path_and_asks_for_that_diff()
    {
        _history.Page(new LogResponse([Entry(7, "m", "/trunk/a.txt", "/trunk/b.txt")]));
        var view = View();
        await view.ShowAsync("/wc", None);

        view.SelectedItem = view.Items[0];
        _clock.Advance(DiffPaneViewModel.SelectionDebounce);
        await Settle();

        await Assert.That(view.SelectedRevision).IsSameReferenceAs(view.Items[0].Row);
        await Assert.That(view.SelectedPath!.Path).IsEqualTo("/trunk/a.txt");
        await Assert.That(_diffs.Questions).IsEquivalentTo([("/wc", "/trunk/a.txt", 7L)]);
    }

    [Test]
    public async Task Picking_another_path_asks_for_its_diff_in_the_same_revision()
    {
        _history.Page(new LogResponse([Entry(7, "m", "/trunk/a.txt", "/trunk/b.txt")]));
        var view = View();
        await view.ShowAsync("/wc", None);
        view.SelectedItem = view.Items[0];

        view.SelectedPath = view.SelectedRevision!.ChangedPaths[1];
        _clock.Advance(DiffPaneViewModel.SelectionDebounce);
        await Settle();

        await Assert.That(_diffs.Questions).IsEquivalentTo([("/wc", "/trunk/b.txt", 7L)]);
    }

    /// <summary>The same file modified in two revisions lists equal first paths.</summary>
    [Test]
    public async Task Picking_another_revision_whose_first_path_is_equal_still_shows_that_revisions_diff()
    {
        _history.Page(
            new LogResponse([Entry(8, "m", "/trunk/a.txt"), Entry(7, "m", "/trunk/a.txt")])
        );
        var view = View();
        await view.ShowAsync("/wc", None);
        view.SelectedItem = view.Items[0];
        _clock.Advance(DiffPaneViewModel.SelectionDebounce);
        await Settle();

        view.SelectedItem = view.Items[1];
        _clock.Advance(DiffPaneViewModel.SelectionDebounce);
        await Settle();

        await Assert
            .That(_diffs.Questions)
            .IsEquivalentTo(
                [("/wc", "/trunk/a.txt", 8L), ("/wc", "/trunk/a.txt", 7L)],
                CollectionOrdering.Matching
            );
    }

    [Test]
    public async Task A_revision_with_no_paths_clears_the_diff()
    {
        _history.Page(new LogResponse([Entry(7, "m")]));
        var view = View();
        await view.ShowAsync("/wc", None);

        view.SelectedItem = view.Items[0];

        await Assert.That(view.SelectedPath).IsNull();
        await Assert.That(view.Diff.State).IsEqualTo(DiffPaneState.NothingSelected);
    }

    [Test]
    public async Task Deselecting_the_path_clears_the_diff()
    {
        _history.Page(new LogResponse([Entry(7, "m", "/trunk/a.txt")]));
        var view = View();
        await view.ShowAsync("/wc", None);
        view.SelectedItem = view.Items[0];

        view.SelectedPath = null;

        await Assert.That(view.Diff.State).IsEqualTo(DiffPaneState.NothingSelected);
    }

    [Test]
    public async Task A_search_that_hides_the_picked_revision_drops_it_and_its_diff()
    {
        _history.Page(new LogResponse([Entry(3, "hero", "/a.txt"), Entry(2, "villain", "/b.txt")]));
        var view = View();
        await view.ShowAsync("/wc", None);
        view.SelectedItem = view.Items[1];

        view.SearchText = "hero";

        await Assert.That(view.SelectedItem).IsNull();
        await Assert.That(view.SelectedRevision).IsNull();
        await Assert.That(view.SelectedPath).IsNull();
        await Assert.That(view.Diff.State).IsEqualTo(DiffPaneState.NothingSelected);
    }

    [Test]
    public async Task A_search_that_keeps_the_picked_revision_keeps_it_picked_without_asking_again()
    {
        _history.Page(new LogResponse([Entry(3, "hero", "/a.txt"), Entry(2, "villain", "/b.txt")]));
        var view = View();
        await view.ShowAsync("/wc", None);
        view.SelectedItem = view.Items[0];
        var picked = view.SelectedItem;

        view.SearchText = "hero";
        _clock.Advance(DiffPaneViewModel.SelectionDebounce);
        await Settle();

        await Assert.That(view.SelectedItem).IsSameReferenceAs(picked);
        await Assert.That(_diffs.Questions.Count).IsEqualTo(1);
    }

    /// <summary>
    /// The BASE range arriving after a pick replaces that row's item — it gains the marker — and
    /// the pick follows it rather than falling off or asking for the diff again.
    /// </summary>
    [Test]
    public async Task A_marker_arriving_on_the_picked_row_keeps_it_picked_without_asking_again()
    {
        var release = new TaskCompletionSource();
        _history
            .Page(new LogResponse([Entry(9, "m", "/a.txt")]))
            .Range(new WorkingCopyRevisionResponse(new BaseRevisionRange(9, 9)), release.Task);
        var view = View();
        var showing = view.ShowAsync("/wc", None);
        await Settle();
        view.SelectedItem = view.Items[0];
        _clock.Advance(DiffPaneViewModel.SelectionDebounce);
        await Settle();

        release.SetResult();
        await showing;

        await Assert.That(view.SelectedItem!.BaseMarkerAbove).IsEqualTo("Your copy is at r9");
        await Assert.That(view.SelectedRevision!.Revision).IsEqualTo(9L);
        await Assert.That(_diffs.Questions.Count).IsEqualTo(1);
    }

    [Test]
    public async Task Showing_another_path_starts_again_with_no_search_no_pick_and_no_marker()
    {
        _history.Page(new LogResponse([Entry(3, "hero", "/a.txt")])).Range(3, 3).Page(1);
        var view = View();
        await view.ShowAsync("/wc", None);
        view.SearchText = "hero";
        view.SelectedItem = view.Items[0];

        await view.ShowAsync("/wc/sub", None);

        await Assert.That(view.SearchText).IsEmpty();
        await Assert.That(view.SelectedItem).IsNull();
        await Assert.That(view.BaseRange).IsNull();
        await Assert.That(view.Items.Select(item => item.Row.Revision)).IsEquivalentTo([1L]);
    }

    [Test]
    public async Task Reloading_reads_the_same_path_again_from_head()
    {
        _history.Page(3).Page(4, 3);
        var view = View();
        await view.ShowAsync("/wc", None);

        await view.ReloadCommand.ExecuteAsync(null);

        await Assert.That(_history.Pages.Select(page => page.Path)).IsEquivalentTo(["/wc", "/wc"]);
        await Assert.That(_history.Pages[1].Start).IsEqualTo(new HistoryFromHead());
        await Assert.That(view.Items.Select(item => item.Row.Revision)).IsEquivalentTo([4L, 3L]);
    }

    [Test]
    public async Task Reloading_before_any_path_was_shown_asks_nothing()
    {
        var view = View();

        await view.ReloadCommand.ExecuteAsync(null);

        await Assert.That(_history.Pages).IsEmpty();
    }

    [Test]
    public async Task The_pinned_local_changes_row_asks_to_return_to_changes()
    {
        var view = View();
        var asked = 0;
        view.ReturnToChangesRequested += (_, _) => asked++;

        view.ReturnToChangesCommand.Execute(null);

        await Assert.That(asked).IsEqualTo(1);
    }

    [Test]
    public async Task The_local_changes_row_with_no_window_listening_does_nothing()
    {
        var view = View();

        await Assert.That(() => view.ReturnToChangesCommand.Execute(null)).ThrowsNothing();
    }

    [Test]
    public async Task Commit_times_are_shown_on_the_viewers_clock()
    {
        _clock.SetLocalTimeZone(
            TimeZoneInfo.CreateCustomTimeZone("test+2", TimeSpan.FromHours(2), "t", "t")
        );
        _history.Page(1);
        var view = View();

        await view.ShowAsync("/wc", None);

        await Assert.That(view.Items[0].Row.When).IsEqualTo("2026-09-23 15:16");
    }

    private HistoryViewModel View() => Revisions.View(_history, _diffs, _clock);

    private static LogResponse FullPage(long from) =>
        new([
            .. Enumerable
                .Range(0, HistoryViewModel.PageSize)
                .Select(offset => Revisions.Entry(from - offset)),
        ]);

    private static RevisionEntry Entry(long revision, string message, params string[] paths) =>
        new(
            revision,
            "keiichi",
            Revisions.Committed,
            message,
            [.. paths.Select(path => new ChangedPath(path, PathChange.Modified, null, null))]
        );

    private static async Task Settle()
    {
        for (var turn = 0; turn < 10; turn++)
        {
            await Task.Yield();
        }
    }
}
