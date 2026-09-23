using Microsoft.Extensions.Time.Testing;
using Subverted.App.Presentation;
using Subverted.App.ViewModels;
using Subverted.Core;
using Subverted.Frontend.Diff;
using Subverted.Protocol;

namespace Subverted.App.Tests;

/// <summary>Driven by a fake clock, so the debounce is asserted to the millisecond without waiting.</summary>
public sealed class RevisionDiffPaneViewModelTests
{
    /// <summary><c>svn diff -c 2 URL/a.txt@2</c> on <c>subverted-history</c>, as captured.</summary>
    private const string EditInRevision = """
        Index: a.txt
        ===================================================================
        --- a.txt	(revision 1)
        +++ a.txt	(revision 2)
        @@ -1,3 +1,3 @@
         one
        -two
        +TWO
         three

        """;

    private static readonly TimeSpan Debounce = DiffPaneViewModel.SelectionDebounce;
    private static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(1);

    private static readonly ChangedPathRow Edited = ChangedPathRow.From(
        new ChangedPath("/a.txt", PathChange.Modified, null, null)
    );
    private static readonly ChangedPathRow Copied = ChangedPathRow.From(
        new ChangedPath("/b.txt", PathChange.Added, "/a.txt", 2)
    );

    private readonly FakeTimeProvider _clock = new();
    private readonly FakeRevisionDiff _diffs = new();

    [Test]
    public async Task Before_anything_is_selected_the_pane_says_so()
    {
        var pane = Pane();

        await Assert.That(pane.State).IsEqualTo(DiffPaneState.NothingSelected);
        await Assert.That(pane.Path).IsNull();
        await Assert.That(pane.Revision).IsNull();
    }

    [Test]
    public async Task A_selection_shows_as_loading_at_once_and_is_not_asked_about_before_the_debounce()
    {
        var pane = Pane();

        var selecting = pane.SelectAsync("/wc", 2, Edited);
        _clock.Advance(Debounce - Tick);

        await Assert.That(pane.State).IsEqualTo(DiffPaneState.Loading);
        await Assert.That(pane.Path).IsSameReferenceAs(Edited);
        await Assert.That(pane.Revision).IsEqualTo(2L);
        await Assert.That(_diffs.Questions).IsEmpty();

        _clock.Advance(Tick);
        await selecting;

        await Assert.That(_diffs.Questions).IsEquivalentTo([("/wc", "/a.txt", 2L)]);
    }

    [Test]
    public async Task A_selection_that_holds_shows_the_parsed_diff_of_that_revision()
    {
        _diffs.Answers(EditInRevision);
        var pane = Pane();

        await SelectAndWait(pane, 2, Edited);

        await Assert.That(pane.State).IsEqualTo(DiffPaneState.Ready);
        await Assert.That(pane.Message).IsNull();
        var file = pane.Document!.Files.Single();
        await Assert.That(file.Path).IsEqualTo("a.txt");
        await Assert
            .That(((TextChange)file.Content!).Hunks.Single().Lines.Select(line => line.Kind))
            .IsEquivalentTo(
                [
                    DiffLineKind.Context,
                    DiffLineKind.Removed,
                    DiffLineKind.Added,
                    DiffLineKind.Context,
                ],
                TUnit.Assertions.Enums.CollectionOrdering.Matching
            );
    }

    [Test]
    public async Task A_second_pick_within_the_debounce_is_the_only_one_asked_about()
    {
        var pane = Pane();

        _ = pane.SelectAsync("/wc", 2, Edited);
        _clock.Advance(Debounce - Tick);
        var second = pane.SelectAsync("/wc", 3, Copied);
        _clock.Advance(Debounce);
        await second;

        await Assert.That(_diffs.Questions).IsEquivalentTo([("/wc", "/b.txt", 3L)]);
    }

    /// <summary>A slow answer to the last pick must never overwrite the current one.</summary>
    [Test]
    public async Task A_late_answer_to_a_superseded_pick_is_dropped()
    {
        var release = new TaskCompletionSource();
        _diffs
            .Answers(EditInRevision, release.Task)
            .Answers(new ErrorResponse(DaemonErrorKind.SvnCommandFailed, "second"));
        var pane = Pane();

        var first = pane.SelectAsync("/wc", 2, Edited);
        _clock.Advance(Debounce);
        await SelectAndWait(pane, 3, Copied);
        release.SetResult();
        await first;

        await Assert.That(pane.State).IsEqualTo(DiffPaneState.Failed);
        await Assert.That(pane.Message).IsEqualTo("second");
        await Assert.That(pane.Path).IsSameReferenceAs(Copied);
        await Assert.That(pane.Document).IsNull();
    }

    [Test]
    public async Task A_superseded_pick_whose_daemon_went_away_reports_nothing()
    {
        var release = new TaskCompletionSource();
        _diffs.IsUnreachable("gone", release.Task).Answers(EditInRevision);
        var pane = Pane();

        var first = pane.SelectAsync("/wc", 2, Edited);
        _clock.Advance(Debounce);
        await SelectAndWait(pane, 3, Edited);
        release.SetResult();
        await first;

        await Assert.That(pane.State).IsEqualTo(DiffPaneState.Ready);
        await Assert.That(pane.Message).IsNull();
    }

    [Test]
    public async Task A_question_still_on_the_wire_is_cancelled_when_another_is_asked()
    {
        _diffs.HoldsUntilCancelled().Answers(EditInRevision);
        var pane = Pane();

        var first = pane.SelectAsync("/wc", 2, Edited);
        _clock.Advance(Debounce);
        await SelectAndWait(pane, 3, Edited);
        await first;

        await Assert.That(_diffs.Tokens[0].IsCancellationRequested).IsTrue();
        await Assert.That(pane.State).IsEqualTo(DiffPaneState.Ready);
    }

    [Test]
    public async Task An_unedited_copy_shows_nothing_to_compare_and_says_where_it_came_from()
    {
        _diffs.Answers("");
        var pane = Pane();

        await SelectAndWait(pane, 3, Copied);

        await Assert.That(pane.State).IsEqualTo(DiffPaneState.NothingToShow);
        await Assert.That(pane.Message).IsEqualTo(RevisionDiffEmptyMessage.For(Copied));
        await Assert.That(pane.Document).IsNull();
    }

    [Test]
    public async Task A_daemon_that_is_not_answering_says_so()
    {
        _diffs.IsUnreachable("connection refused");
        var pane = Pane();

        await SelectAndWait(pane, 2, Edited);

        await Assert.That(pane.State).IsEqualTo(DiffPaneState.Unreachable);
        await Assert.That(pane.Message).IsEqualTo("connection refused");
    }

    [Test]
    public async Task A_failure_from_svn_shows_what_svn_said()
    {
        _diffs.Answers(new ErrorResponse(DaemonErrorKind.SvnCommandFailed, "svn: E160013: gone"));
        var pane = Pane();

        await SelectAndWait(pane, 2, Edited);

        await Assert.That(pane.State).IsEqualTo(DiffPaneState.Failed);
        await Assert.That(pane.Message).IsEqualTo("svn: E160013: gone");
    }

    [Test]
    public async Task An_answer_that_is_not_a_diff_is_a_failure_that_names_it()
    {
        _diffs.Answers(new AcknowledgedResponse());
        var pane = Pane();

        await SelectAndWait(pane, 2, Edited);

        await Assert.That(pane.State).IsEqualTo(DiffPaneState.Failed);
        await Assert.That(pane.Message).Contains("AcknowledgedResponse");
    }

    [Test]
    public async Task Clearing_drops_the_question_in_flight_and_shows_nothing_selected()
    {
        _diffs.Answers(EditInRevision);
        var pane = Pane();

        var selecting = pane.SelectAsync("/wc", 2, Edited);
        pane.Clear();
        _clock.Advance(Debounce);
        await selecting;

        await Assert.That(pane.State).IsEqualTo(DiffPaneState.NothingSelected);
        await Assert.That(pane.Path).IsNull();
        await Assert.That(pane.Revision).IsNull();
        await Assert.That(_diffs.Questions).IsEmpty();
    }

    [Test]
    public async Task A_new_pick_after_a_diff_shows_loading_rather_than_the_old_diff()
    {
        _diffs.Answers(EditInRevision);
        var pane = Pane();
        await SelectAndWait(pane, 2, Edited);

        _ = pane.SelectAsync("/wc", 3, Copied);

        await Assert.That(pane.State).IsEqualTo(DiffPaneState.Loading);
        await Assert.That(pane.Document).IsNull();
    }

    private RevisionDiffPaneViewModel Pane() => new(_diffs, _clock);

    private async Task SelectAndWait(
        RevisionDiffPaneViewModel pane,
        long revision,
        ChangedPathRow path
    )
    {
        var selecting = pane.SelectAsync("/wc", revision, path);
        _clock.Advance(Debounce);
        await selecting;
    }
}
