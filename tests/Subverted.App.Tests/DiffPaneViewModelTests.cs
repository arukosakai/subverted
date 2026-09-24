using Microsoft.Extensions.Time.Testing;
using Subverted.App.Presentation;
using Subverted.App.ViewModels;
using Subverted.Core;
using Subverted.Frontend.Diff;
using Subverted.Protocol;
using static Subverted.App.Tests.Entries;

namespace Subverted.App.Tests;

/// <summary>Driven by a fake clock, so the debounce is asserted to the millisecond without waiting.</summary>
public sealed class DiffPaneViewModelTests
{
    private static readonly TimeSpan Debounce = DiffPaneViewModel.SelectionDebounce;
    private static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(1);

    private static readonly ChangeRow Child = ChangeRow.From(Entry("sub/child.txt"));
    private static readonly ChangeRow Hero = ChangeRow.From(Entry("gfx/hero.png"));
    private static readonly ChangeRow Stray = ChangeRow.From(
        Entry("tmp.log", NodeStatus.Unversioned)
    );

    private readonly FakeTimeProvider _clock = new();
    private readonly FakeWorkingCopyDiff _diffs = new();
    private readonly FakeFileSizeReader _sizes = new(("/wc/gfx/hero.png", 2048));
    private readonly FakeFileLauncher _launcher = new();

    [Test]
    public async Task Before_anything_is_selected_the_pane_says_so_and_offers_nothing_to_open()
    {
        var pane = Pane();

        await Assert.That(pane.State).IsEqualTo(DiffPaneState.NothingSelected);
        await Assert.That(pane.Row).IsNull();
        await Assert.That(pane.OpenInAppCommand.CanExecute(null)).IsFalse();
    }

    /// <summary>The pane takes room only while a row is picked — a failed diff is still a picked row.</summary>
    [Test]
    public async Task The_pane_has_a_selection_from_the_pick_until_it_is_cleared()
    {
        _diffs.Answers(new ErrorResponse(DaemonErrorKind.SvnCommandFailed, "refused"));
        var pane = Pane();
        var announced = new List<string>();
        pane.PropertyChanged += (_, change) => announced.Add(change.PropertyName!);
        var beforeAnyPick = pane.HasSelection;

        var selecting = pane.SelectAsync(Child, "/wc/sub/child.txt");
        var whileLoading = pane.HasSelection;
        _clock.Advance(Debounce);
        await selecting;
        var afterAFailure = (pane.State, pane.HasSelection);
        pane.Clear();

        await Assert.That(beforeAnyPick).IsFalse();
        await Assert.That(whileLoading).IsTrue();
        await Assert.That(afterAFailure).IsEqualTo((DiffPaneState.Failed, true));
        await Assert.That(pane.HasSelection).IsFalse();
        await Assert.That(announced).Contains(nameof(DiffPaneViewModel.HasSelection));
    }

    [Test]
    public async Task A_selection_shows_as_loading_at_once_and_is_not_asked_about_before_the_debounce()
    {
        var pane = Pane();

        var selecting = pane.SelectAsync(Child, "/wc/sub/child.txt");
        _clock.Advance(Debounce - Tick);

        await Assert.That(pane.State).IsEqualTo(DiffPaneState.Loading);
        await Assert.That(pane.Row).IsSameReferenceAs(Child);
        await Assert.That(_diffs.Paths).IsEmpty();

        _clock.Advance(Tick);
        await selecting;

        await Assert.That(_diffs.Paths).IsEquivalentTo(new[] { "/wc/sub/child.txt" });
    }

    /// <summary>Waits on the parser: the document is what it made of the text.</summary>
    [Test]
    public async Task A_selection_that_holds_shows_the_parsed_diff_and_reads_no_size_for_text()
    {
        _diffs.Answers(SvnDiffs.EditedText);
        var pane = Pane();

        await SelectAndWait(pane, Child, "/wc/sub/child.txt");

        await Assert.That(pane.State).IsEqualTo(DiffPaneState.Ready);
        await Assert.That(pane.Message).IsNull();
        await Assert.That(pane.Document!.Files.Count).IsEqualTo(1);
        var file = pane.Document.Files[0];
        await Assert.That(file.Path).IsEqualTo("sub/child.txt");
        var hunk = ((TextChange)file.Content!).Hunks.Single();
        await Assert
            .That(hunk.Lines.Select(line => line.Kind))
            .IsEquivalentTo(new[] { DiffLineKind.Context, DiffLineKind.Added });
        await Assert.That(pane.SizeInBytes).IsNull();
        await Assert.That(_sizes.Asked).IsEmpty();
    }

    /// <summary>Waits on the parser.</summary>
    [Test]
    public async Task A_binary_diff_reads_the_file_size_for_the_card()
    {
        _diffs.Answers(SvnDiffs.EditedBinary);
        var pane = Pane();

        await SelectAndWait(pane, Hero, "/wc/gfx/hero.png");

        await Assert.That(pane.State).IsEqualTo(DiffPaneState.Ready);
        await Assert
            .That(pane.Document!.Files.Single().Content)
            .IsEqualTo(new BinaryChange("application/octet-stream"));
        await Assert.That(pane.SizeInBytes).IsEqualTo(2048);
        await Assert.That(_sizes.Asked).IsEquivalentTo(new[] { "/wc/gfx/hero.png" });
    }

    /// <summary>Waits on the parser: a property section is still a file with something to show.</summary>
    [Test]
    public async Task A_diff_with_a_property_section_is_ready_rather_than_empty()
    {
        _diffs.Answers(SvnDiffs.EditedTextAndProperty);
        var pane = Pane();

        await SelectAndWait(pane, Child, "/wc/both.txt");

        await Assert.That(pane.State).IsEqualTo(DiffPaneState.Ready);
        await Assert
            .That(pane.Document!.Files.Single().PropertyChanges.Single().Name)
            .IsEqualTo("svn:mime-type");
    }

    [Test]
    public async Task Moving_on_before_the_debounce_asks_only_about_the_row_it_stopped_on()
    {
        var pane = Pane();

        var first = pane.SelectAsync(Child, "/wc/sub/child.txt");
        _clock.Advance(Debounce - Tick);
        var second = pane.SelectAsync(Hero, "/wc/gfx/hero.png");
        _clock.Advance(Tick);
        await first;

        await Assert.That(_diffs.Paths).IsEmpty();

        _clock.Advance(Debounce);
        await second;

        await Assert.That(_diffs.Paths).IsEquivalentTo(new[] { "/wc/gfx/hero.png" });
        await Assert.That(pane.Row).IsSameReferenceAs(Hero);
    }

    /// <summary>
    /// The race the cancellation exists for: the first row's answer is already on the wire when the
    /// selection moves, and lands after the second row's. It must be dropped, not shown.
    /// </summary>
    [Test]
    public async Task A_late_answer_for_the_previous_row_never_replaces_the_current_one()
    {
        var late = new TaskCompletionSource();
        _diffs.Answers(new ErrorResponse(DaemonErrorKind.SvnCommandFailed, "late"), late.Task);
        _diffs.Answers("");
        var pane = Pane();

        var first = pane.SelectAsync(Child, "/wc/sub/child.txt");
        _clock.Advance(Debounce);
        await SelectAndWait(pane, Hero, "/wc/gfx/hero.png");
        late.SetResult();
        await first;

        await Assert.That(_diffs.Tokens[0].IsCancellationRequested).IsTrue();
        await Assert.That(_diffs.Tokens[1].IsCancellationRequested).IsFalse();
        await Assert.That(pane.Row).IsSameReferenceAs(Hero);
        await Assert.That(pane.State).IsEqualTo(DiffPaneState.NothingToShow);
        await Assert.That(pane.Message).IsEqualTo(EmptyDiffMessage.For(Hero));
    }

    [Test]
    public async Task A_late_unreachable_for_the_previous_row_is_dropped_too()
    {
        var late = new TaskCompletionSource();
        _diffs.IsUnreachable("gone", late.Task);
        _diffs.Answers("");
        var pane = Pane();

        var first = pane.SelectAsync(Child, "/wc/sub/child.txt");
        _clock.Advance(Debounce);
        await SelectAndWait(pane, Hero, "/wc/gfx/hero.png");
        late.SetResult();
        await first;

        await Assert.That(pane.State).IsEqualTo(DiffPaneState.NothingToShow);
        await Assert.That(pane.Message).IsEqualTo(EmptyDiffMessage.For(Hero));
    }

    /// <summary>The real channel throws when its question is cancelled; that is not a failure.</summary>
    [Test]
    public async Task A_question_that_ends_by_being_cancelled_changes_nothing()
    {
        _diffs.HoldsUntilCancelled();
        var pane = Pane();

        var first = pane.SelectAsync(Child, "/wc/sub/child.txt");
        _clock.Advance(Debounce);
        pane.Clear();
        await first;

        await Assert.That(pane.State).IsEqualTo(DiffPaneState.NothingSelected);
        await Assert.That(pane.Message).IsNull();
    }

    /// <summary>A cancellation nobody here asked for is somebody else's failure, and is not hidden.</summary>
    [Test]
    public async Task A_cancellation_the_pane_did_not_ask_for_is_not_swallowed()
    {
        var pane = Pane(new ThrowingDiff(new OperationCanceledException("not ours")));

        var selecting = pane.SelectAsync(Child, "/wc/sub/child.txt");
        _clock.Advance(Debounce);

        await Assert.That(async () => await selecting).Throws<OperationCanceledException>();
    }

    [Test]
    public async Task Deselecting_shows_nothing_selected_and_drops_the_question_in_flight()
    {
        var late = new TaskCompletionSource();
        _diffs.Answers(new ErrorResponse(DaemonErrorKind.SvnCommandFailed, "late"), late.Task);
        var pane = Pane();

        var selecting = pane.SelectAsync(Child, "/wc/sub/child.txt");
        _clock.Advance(Debounce);
        pane.Clear();
        late.SetResult();
        await selecting;

        await Assert.That(_diffs.Tokens.Single().IsCancellationRequested).IsTrue();
        await Assert.That(pane.State).IsEqualTo(DiffPaneState.NothingSelected);
        await Assert.That(pane.Row).IsNull();
        await Assert.That(pane.Message).IsNull();
        await Assert.That(pane.OpenInAppCommand.CanExecute(null)).IsFalse();
    }

    [Test]
    public async Task Deselecting_during_the_debounce_asks_nothing_at_all()
    {
        var pane = Pane();

        var selecting = pane.SelectAsync(Child, "/wc/sub/child.txt");
        pane.Clear();
        _clock.Advance(Debounce);
        await selecting;

        await Assert.That(_diffs.Paths).IsEmpty();
        await Assert.That(pane.State).IsEqualTo(DiffPaneState.NothingSelected);
    }

    /// <summary>Measured: <c>svn diff</c> of an untouched copy or move, or a missing file, prints nothing.</summary>
    [Test]
    public async Task An_empty_diff_has_nothing_to_show_and_says_why_for_that_row()
    {
        var moved = ChangeRow.From(Entry("moved.txt", NodeStatus.Added, isCopied: true));
        _diffs.Answers("");
        var pane = Pane();

        await SelectAndWait(pane, moved, "/wc/moved.txt");

        await Assert.That(pane.State).IsEqualTo(DiffPaneState.NothingToShow);
        await Assert
            .That(pane.Message)
            .IsEqualTo("Copied or moved without edits since, so it matches where it came from.");
        await Assert.That(pane.Document).IsNull();
        await Assert.That(pane.SizeInBytes).IsNull();
    }

    /// <summary><c>svn diff</c> on an unversioned path exits 1 with E150000, so it is not asked.</summary>
    [Test]
    public async Task An_unversioned_row_is_never_asked_about()
    {
        var pane = Pane();

        await pane.SelectAsync(Stray, "/wc/tmp.log");
        _clock.Advance(Debounce);

        await Assert.That(_diffs.Paths).IsEmpty();
        await Assert.That(pane.State).IsEqualTo(DiffPaneState.NothingToShow);
        await Assert.That(pane.Row).IsSameReferenceAs(Stray);
        await Assert.That(pane.Message).IsEqualTo(EmptyDiffMessage.For(Stray));
    }

    [Test]
    public async Task A_refusal_from_the_daemon_with_nothing_shown_is_a_failure_with_its_reason()
    {
        _diffs.Answers(new ErrorResponse(DaemonErrorKind.SvnCommandFailed, "E720002: gone"));
        var pane = Pane();

        await SelectAndWait(pane, Child, "/wc/sub/child.txt");

        await Assert.That(pane.State).IsEqualTo(DiffPaneState.Failed);
        await Assert.That(pane.Message).IsEqualTo("E720002: gone");
        await Assert.That(pane.Document).IsNull();
    }

    [Test]
    public async Task An_answer_that_is_not_a_diff_is_a_failure_not_a_crash()
    {
        _diffs.Answers(new AcknowledgedResponse());
        var pane = Pane();

        await SelectAndWait(pane, Child, "/wc/sub/child.txt");

        await Assert.That(pane.State).IsEqualTo(DiffPaneState.Failed);
        await Assert
            .That(pane.Message)
            .IsEqualTo("The daemon answered with AcknowledgedResponse, which is not a diff.");
    }

    [Test]
    public async Task No_daemon_with_nothing_shown_is_unreachable()
    {
        _diffs.IsUnreachable("connection refused");
        var pane = Pane();

        await SelectAndWait(pane, Child, "/wc/sub/child.txt");

        await Assert.That(pane.State).IsEqualTo(DiffPaneState.Unreachable);
        await Assert.That(pane.Message).IsEqualTo("connection refused");
    }

    /// <summary>A new row must not show under the last row's diff or its complaint.</summary>
    [Test]
    public async Task Selecting_another_row_forgets_what_the_last_one_said()
    {
        _diffs.Answers(new ErrorResponse(DaemonErrorKind.SvnCommandFailed, "no"));
        var pane = Pane();
        await SelectAndWait(pane, Child, "/wc/sub/child.txt");

        _ = pane.SelectAsync(Hero, "/wc/gfx/hero.png");

        await Assert.That(pane.State).IsEqualTo(DiffPaneState.Loading);
        await Assert.That(pane.Message).IsNull();
        await Assert.That(pane.Document).IsNull();
    }

    /// <summary>Waits on the parser.</summary>
    [Test]
    public async Task Selecting_another_row_drops_the_last_rows_document_and_size()
    {
        _diffs.Answers(SvnDiffs.EditedBinary);
        var pane = Pane();
        await SelectAndWait(pane, Hero, "/wc/gfx/hero.png");

        _ = pane.SelectAsync(Child, "/wc/sub/child.txt");

        await Assert.That(pane.State).IsEqualTo(DiffPaneState.Loading);
        await Assert.That(pane.Document).IsNull();
        await Assert.That(pane.SizeInBytes).IsNull();
    }

    /// <summary>Waits on the parser. D30's rule: asking again is never a blank or a spinner.</summary>
    [Test]
    public async Task A_refetch_keeps_the_diff_on_screen_until_the_new_one_arrives()
    {
        var held = new TaskCompletionSource();
        _diffs.Answers(SvnDiffs.EditedText);
        _diffs.Answers(SvnDiffs.EditedTextAndProperty, held.Task);
        var pane = Pane();
        await SelectAndWait(pane, Child, "/wc/sub/child.txt");
        var before = pane.Document;
        var changed = Child with { HasPropertyChange = true };

        var refetching = pane.RefetchAsync(changed, "/wc/sub/child.txt");

        await Assert.That(_diffs.Paths.Count).IsEqualTo(2);
        await Assert.That(pane.State).IsEqualTo(DiffPaneState.Ready);
        await Assert.That(pane.Document).IsSameReferenceAs(before);
        await Assert.That(pane.Row).IsSameReferenceAs(changed);

        held.SetResult();
        await refetching;

        await Assert.That(pane.Document).IsNotSameReferenceAs(before);
        await Assert.That(pane.Document!.Files.Single().PropertyChanges.Count).IsEqualTo(1);
    }

    /// <summary>Waits on the parser. The stale diff stays, and the message says why it may be old.</summary>
    [Test]
    public async Task A_refetch_the_daemon_refuses_keeps_the_last_diff_and_says_why()
    {
        _diffs.Answers(SvnDiffs.EditedText);
        _diffs.Answers(new ErrorResponse(DaemonErrorKind.SvnCommandFailed, "E155037"));
        var pane = Pane();
        await SelectAndWait(pane, Child, "/wc/sub/child.txt");
        var before = pane.Document;

        await pane.RefetchAsync(Child with { IsLocked = true }, "/wc/sub/child.txt");

        await Assert.That(pane.State).IsEqualTo(DiffPaneState.Ready);
        await Assert.That(pane.Document).IsSameReferenceAs(before);
        await Assert.That(pane.Message).IsEqualTo("E155037");
    }

    /// <summary>Waits on the parser.</summary>
    [Test]
    public async Task A_refetch_with_no_daemon_keeps_the_last_diff_and_says_why()
    {
        _diffs.Answers(SvnDiffs.EditedText);
        _diffs.IsUnreachable("connection refused");
        var pane = Pane();
        await SelectAndWait(pane, Child, "/wc/sub/child.txt");
        var before = pane.Document;

        await pane.RefetchAsync(Child with { IsLocked = true }, "/wc/sub/child.txt");

        await Assert.That(pane.State).IsEqualTo(DiffPaneState.Ready);
        await Assert.That(pane.Document).IsSameReferenceAs(before);
        await Assert.That(pane.Message).IsEqualTo("connection refused");
    }

    /// <summary>Waits on the parser: the next good answer clears the stale mark.</summary>
    [Test]
    public async Task A_good_answer_after_a_stale_one_clears_the_message()
    {
        _diffs.Answers(SvnDiffs.EditedText);
        _diffs.IsUnreachable();
        _diffs.Answers(SvnDiffs.EditedText);
        var pane = Pane();
        await SelectAndWait(pane, Child, "/wc/sub/child.txt");
        await pane.RefetchAsync(Child, "/wc/sub/child.txt");

        await pane.RefetchAsync(Child, "/wc/sub/child.txt");

        await Assert.That(pane.State).IsEqualTo(DiffPaneState.Ready);
        await Assert.That(pane.Message).IsNull();
    }

    /// <summary>What was said stays said while asking again: a refetch is not a new selection.</summary>
    [Test]
    public async Task A_refetch_after_a_failure_does_not_flash_loading_while_it_asks()
    {
        var held = new TaskCompletionSource();
        _diffs.Answers(new ErrorResponse(DaemonErrorKind.SvnCommandFailed, "no"));
        _diffs.Answers("", held.Task);
        var pane = Pane();
        await SelectAndWait(pane, Child, "/wc/sub/child.txt");

        var refetching = pane.RefetchAsync(Child, "/wc/sub/child.txt");

        await Assert.That(pane.State).IsEqualTo(DiffPaneState.Failed);
        await Assert.That(pane.Message).IsEqualTo("no");

        held.SetResult();
        await refetching;

        await Assert.That(pane.State).IsEqualTo(DiffPaneState.NothingToShow);
    }

    [Test]
    public async Task A_refetch_during_the_debounce_asks_at_once_and_only_once()
    {
        var pane = Pane();

        var selecting = pane.SelectAsync(Child, "/wc/sub/child.txt");
        await pane.RefetchAsync(Child, "/wc/sub/child.txt");
        _clock.Advance(Debounce);
        await selecting;

        await Assert.That(_diffs.Paths.Count).IsEqualTo(1);
    }

    /// <summary>A file SVN forgot — reverted from under an add, say — is not asked about either.</summary>
    [Test]
    public async Task A_refetch_of_a_row_that_became_unversioned_asks_nothing()
    {
        _diffs.Answers(new ErrorResponse(DaemonErrorKind.SvnCommandFailed, "no"));
        var pane = Pane();
        await SelectAndWait(pane, Child, "/wc/sub/child.txt");
        var unversioned = Child with { Badge = Stray.Badge };

        await pane.RefetchAsync(unversioned, "/wc/sub/child.txt");

        await Assert.That(_diffs.Paths.Count).IsEqualTo(1);
        await Assert.That(pane.State).IsEqualTo(DiffPaneState.NothingToShow);
        await Assert.That(pane.Message).IsEqualTo(EmptyDiffMessage.For(unversioned));
    }

    [Test]
    public async Task Open_in_app_opens_the_shown_rows_file()
    {
        var pane = Pane();
        await SelectAndWait(pane, Hero, "/wc/gfx/hero.png");

        await Assert.That(pane.OpenInAppCommand.CanExecute(null)).IsTrue();
        await pane.OpenInAppCommand.ExecuteAsync(null);

        await Assert.That(_launcher.Opened).IsEquivalentTo(new[] { "/wc/gfx/hero.png" });
    }

    /// <summary>Bound to a button that is hidden when nothing is selected, and harmless if pressed anyway.</summary>
    [Test]
    public async Task Open_in_app_with_nothing_selected_opens_nothing()
    {
        var pane = Pane();
        await SelectAndWait(pane, Hero, "/wc/gfx/hero.png");
        pane.Clear();

        await pane.OpenInAppCommand.ExecuteAsync(null);

        await Assert.That(_launcher.Opened).IsEmpty();
    }

    private DiffPaneViewModel Pane(IWorkingCopyDiff? diffs = null) =>
        new(diffs ?? _diffs, _sizes, _launcher, _clock);

    private async Task SelectAndWait(DiffPaneViewModel pane, ChangeRow row, string path)
    {
        var selecting = pane.SelectAsync(row, path);
        _clock.Advance(Debounce);
        await selecting;
    }

    private sealed class ThrowingDiff(Exception exception) : IWorkingCopyDiff
    {
        public Task<DaemonResponse> ReadAsync(string path, CancellationToken cancellationToken) =>
            Task.FromException<DaemonResponse>(exception);
    }
}
