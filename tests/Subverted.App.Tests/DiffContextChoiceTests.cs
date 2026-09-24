using Microsoft.Extensions.Time.Testing;
using Subverted.App.Presentation;
using Subverted.App.ViewModels;
using Subverted.Core;
using Subverted.Protocol;
using TUnit.Assertions.Enums;
using static Subverted.App.Tests.Entries;

namespace Subverted.App.Tests;

/// <summary>
/// The context dropdown on both diff panes: what it asks the daemon, that picking again asks
/// again, that the pick outlives selections and resyncs, and when it says the file could not have it.
/// </summary>
public sealed class DiffContextChoiceTests
{
    private static readonly ChangeRow Child = ChangeRow.From(Entry("sub/child.txt"));
    private static readonly ChangeRow Other = ChangeRow.From(Entry("other.txt"));
    private static readonly ChangedPathRow Edited = ChangedPathRow.From(
        new ChangedPath("/a.txt", PathChange.Modified, null, null)
    );

    private static readonly DiffContextOption TenLines = DiffContextOption.All[1];
    private static readonly DiffContextOption WholeFile = DiffContextOption.All[3];

    private readonly FakeTimeProvider _clock = new();
    private readonly FakeWorkingCopyDiff _diffs = new();
    private readonly FakeRevisionDiff _revisionDiffs = new();

    [Test]
    public async Task The_dropdown_offers_svns_three_lines_then_ten_twenty_five_and_the_whole_file()
    {
        await Assert
            .That(DiffContextOption.All.Select(option => (option.Label, option.Asked)))
            .IsEquivalentTo(
                [
                    ("3 lines", (DiffContext?)null),
                    ("10 lines", new DiffContext(10)),
                    ("25 lines", new DiffContext(25)),
                    ("Whole file", DiffContext.WholeFile),
                ],
                CollectionOrdering.Matching
            );
        await Assert.That(DiffContextOption.Default).IsSameReferenceAs(DiffContextOption.All[0]);
        await Assert.That(TenLines.ToString()).IsEqualTo("10 lines");
    }

    [Test]
    public async Task Svns_own_three_lines_are_never_a_context_that_was_not_honoured()
    {
        await Assert
            .That(DiffContextOption.Default.WasNotHonouredBy(DiffContext.Default))
            .IsFalse();
        await Assert.That(DiffContextOption.Default.WasNotHonouredBy(null)).IsFalse();
    }

    [Test]
    public async Task More_context_answered_with_svns_three_or_by_an_older_daemon_was_not_honoured()
    {
        await Assert.That(TenLines.WasNotHonouredBy(new DiffContext(10))).IsFalse();
        await Assert.That(TenLines.WasNotHonouredBy(DiffContext.Default)).IsTrue();
        await Assert.That(TenLines.WasNotHonouredBy(null)).IsTrue();
    }

    [Test]
    public async Task A_changes_pane_starts_on_svns_own_diff_and_asks_for_no_context()
    {
        var pane = ChangesPane();

        await Select(pane, Child, "/wc/sub/child.txt");

        await Assert.That(pane.Context).IsSameReferenceAs(DiffContextOption.Default);
        await Assert.That(pane.ContextOptions).IsSameReferenceAs(DiffContextOption.All);
        await Assert.That(_diffs.Contexts).IsEquivalentTo([(DiffContext?)null]);
    }

    [Test]
    public async Task Picking_more_context_asks_again_for_the_row_on_screen_with_it()
    {
        _diffs.Answers(SvnDiffs.EditedText);
        _diffs.Answers(new DiffResponse(SvnDiffs.EditedText, new DiffContext(10)));
        var pane = ChangesPane();
        await Select(pane, Child, "/wc/sub/child.txt");

        pane.Context = TenLines;

        await Assert
            .That(_diffs.Paths)
            .IsEquivalentTo(
                ["/wc/sub/child.txt", "/wc/sub/child.txt"],
                CollectionOrdering.Matching
            );
        await Assert.That(_diffs.Contexts[1]).IsEqualTo(new DiffContext(10));
        await Assert.That(pane.ContextUnavailable).IsFalse();
    }

    [Test]
    public async Task Picking_a_context_with_nothing_selected_asks_nothing()
    {
        var pane = ChangesPane();

        pane.Context = TenLines;

        await Assert.That(_diffs.Paths).IsEmpty();
    }

    [Test]
    public async Task The_pick_is_kept_for_the_next_row_and_for_a_resync()
    {
        var pane = ChangesPane();
        pane.Context = WholeFile;

        await Select(pane, Child, "/wc/sub/child.txt");
        await Select(pane, Other, "/wc/other.txt");
        await pane.RefetchAsync(Other, "/wc/other.txt");

        await Assert
            .That(_diffs.Contexts)
            .IsEquivalentTo(
                new DiffContext?[]
                {
                    DiffContext.WholeFile,
                    DiffContext.WholeFile,
                    DiffContext.WholeFile,
                },
                CollectionOrdering.Matching
            );
    }

    [Test]
    public async Task A_file_that_could_not_have_the_context_says_so_and_clearing_forgets_it()
    {
        _diffs.Answers(new DiffResponse(SvnDiffs.EditedText, DiffContext.Default));
        var pane = ChangesPane();
        pane.Context = TenLines;

        await Select(pane, Child, "/wc/sub/child.txt");
        var afterFallback = pane.ContextUnavailable;
        pane.Clear();

        await Assert.That(afterFallback).IsTrue();
        await Assert.That(pane.ContextUnavailable).IsFalse();
    }

    [Test]
    public async Task A_row_with_nothing_to_show_does_not_say_its_context_was_unavailable()
    {
        _diffs.Answers(new DiffResponse(SvnDiffs.EditedText, DiffContext.Default));
        _diffs.Answers(new DiffResponse("", DiffContext.Default));
        var pane = ChangesPane();
        pane.Context = TenLines;
        await Select(pane, Child, "/wc/sub/child.txt");

        await pane.RefetchAsync(Child, "/wc/sub/child.txt");

        await Assert.That(pane.State).IsEqualTo(DiffPaneState.NothingToShow);
        await Assert.That(pane.ContextUnavailable).IsFalse();
    }

    [Test]
    public async Task A_history_pane_asks_for_the_picked_context_and_again_when_it_changes()
    {
        _revisionDiffs.Answers(SvnDiffs.EditedText);
        _revisionDiffs.Answers(new DiffResponse(SvnDiffs.EditedText, DiffContext.Default));
        var pane = HistoryPane();
        await Select(pane, Edited);

        pane.Context = TenLines;

        await Assert
            .That(_revisionDiffs.Questions)
            .IsEquivalentTo(
                [("/wc", "/a.txt", 2L), ("/wc", "/a.txt", 2L)],
                CollectionOrdering.Matching
            );
        await Assert
            .That(_revisionDiffs.Contexts)
            .IsEquivalentTo(
                new DiffContext?[] { null, new DiffContext(10) },
                CollectionOrdering.Matching
            );
        await Assert.That(pane.ContextUnavailable).IsTrue();
    }

    [Test]
    public async Task A_history_pane_keeps_the_pick_for_the_next_path_and_forgets_the_notice_on_clear()
    {
        _revisionDiffs.Answers(new DiffResponse(SvnDiffs.EditedText, DiffContext.WholeFile));
        var pane = HistoryPane();
        pane.Context = WholeFile;

        await Select(pane, Edited);
        var honoured = pane.ContextUnavailable;
        pane.Clear();
        pane.Context = TenLines;

        await Assert.That(_revisionDiffs.Contexts.Single()).IsEqualTo(DiffContext.WholeFile);
        await Assert.That(honoured).IsFalse();
        await Assert.That(pane.ContextOptions).IsSameReferenceAs(DiffContextOption.All);
    }

    [Test]
    public async Task A_history_path_with_nothing_to_show_does_not_say_its_context_was_unavailable()
    {
        _revisionDiffs.Answers(new DiffResponse(SvnDiffs.EditedText, DiffContext.Default));
        _revisionDiffs.Answers(new DiffResponse("", DiffContext.Default));
        var pane = HistoryPane();
        pane.Context = TenLines;
        await Select(pane, Edited);

        pane.Context = WholeFile;

        await Assert.That(pane.State).IsEqualTo(DiffPaneState.NothingToShow);
        await Assert.That(pane.ContextUnavailable).IsFalse();
    }

    private DiffPaneViewModel ChangesPane() => DiffPanes.Pane(_diffs, _clock);

    private RevisionDiffPaneViewModel HistoryPane() => new(_revisionDiffs, _clock);

    private async Task Select(DiffPaneViewModel pane, ChangeRow row, string path)
    {
        var selecting = pane.SelectAsync(row, path);
        _clock.Advance(DiffPaneViewModel.SelectionDebounce);
        await selecting;
    }

    private async Task Select(RevisionDiffPaneViewModel pane, ChangedPathRow path)
    {
        var selecting = pane.SelectAsync("/wc", 2, path);
        _clock.Advance(DiffPaneViewModel.SelectionDebounce);
        await selecting;
    }
}
