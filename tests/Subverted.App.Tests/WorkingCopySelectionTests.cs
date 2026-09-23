using System.Collections.Specialized;
using Microsoft.Extensions.Time.Testing;
using Subverted.App.ViewModels;
using Subverted.Core;
using static Subverted.App.Tests.Entries;

namespace Subverted.App.Tests;

/// <summary>The selected row drives the diff pane, and survives the once-a-second resync.</summary>
public sealed class WorkingCopySelectionTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    private readonly FakeTimeProvider _clock = new();
    private readonly FakeWorkingCopyDiff _diffs = new();

    [Test]
    public async Task Selecting_a_row_asks_for_its_diff_at_its_absolute_path()
    {
        var view = await ListedAsync(Listing(Entry("art/hero.png")));

        await SelectAsync(view, 0);

        await Assert
            .That(_diffs.Paths)
            .IsEquivalentTo(new[] { DiffTarget.PathOf(Info.RootPath, "art/hero.png") });
        await Assert.That(view.Diff.Row).IsSameReferenceAs(view.Changes[0]);
    }

    /// <summary>The root row is listed when the root's own properties changed; its path is the root.</summary>
    [Test]
    public async Task Selecting_the_root_row_asks_about_the_root_itself()
    {
        var view = await ListedAsync(
            Listing(Entry("", NodeStatus.Unmodified, PropertyStatus.Modified))
        );

        await SelectAsync(view, 0);

        await Assert.That(_diffs.Paths).IsEquivalentTo(new[] { Info.RootPath });
    }

    [Test]
    public async Task Deselecting_clears_the_pane()
    {
        var view = await ListedAsync(Listing(Entry("a.png")));
        await SelectAsync(view, 0);

        view.SelectedChange = null;

        await Assert.That(view.Diff.State).IsEqualTo(DiffPaneState.NothingSelected);
        await Assert.That(view.Diff.Row).IsNull();
    }

    /// <summary>
    /// The list control drops a replaced item from its selection and writes that back through the
    /// binding, mid-merge. That is not the person deselecting: the row stays selected, at its new
    /// record, and its diff is asked for again at once — no debounce, no loading flash.
    /// </summary>
    [Test]
    public async Task A_row_the_resync_replaced_stays_selected_and_is_asked_about_again_at_once()
    {
        var status = new FakeWorkingCopyStatus()
            .Answers(Listing(Entry("a.png")))
            .Answers(Listing(Entry("a.png", NodeStatus.Modified, PropertyStatus.Modified)));
        var view = new WorkingCopyViewModel("/studio/game", status, Pane());
        await view.RefreshAsync(None);
        await SelectAsync(view, 0);
        DropSelectionOnReplace(view);
        var states = new List<DiffPaneState>();
        view.Diff.PropertyChanged += (_, _) => states.Add(view.Diff.State);

        await view.RefreshAsync(None);
        await Settle();

        await Assert.That(view.SelectedChange).IsSameReferenceAs(view.Changes[0]);
        await Assert.That(view.Changes[0].HasPropertyChange).IsTrue();
        await Assert.That(view.Diff.Row).IsSameReferenceAs(view.Changes[0]);
        await Assert.That(_diffs.Paths.Count).IsEqualTo(2);
        await Assert.That(states).DoesNotContain(DiffPaneState.Loading);
        await Assert.That(states).DoesNotContain(DiffPaneState.NothingSelected);
    }

    /// <summary>
    /// A file already <c>M</c> saved again lists at the same status; only its fingerprint moves.
    /// That has to reach the pane the same way a status change does, selection and all.
    /// </summary>
    [Test]
    public async Task A_selected_file_saved_again_is_asked_about_again_and_stays_selected()
    {
        var saved = new DateTime(2030, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var status = new FakeWorkingCopyStatus()
            .Answers(Listing(Entry("a.png", onDisk: new FileFingerprint(10, saved))))
            .Answers(Listing(Entry("a.png", onDisk: new FileFingerprint(10, saved.AddTicks(1)))));
        var view = new WorkingCopyViewModel("/studio/game", status, Pane());
        await view.RefreshAsync(None);
        await SelectAsync(view, 0);
        DropSelectionOnReplace(view);

        await view.RefreshAsync(None);
        await Settle();

        await Assert.That(view.SelectedChange).IsSameReferenceAs(view.Changes[0]);
        await Assert.That(view.Diff.Row).IsSameReferenceAs(view.Changes[0]);
        await Assert.That(view.Changes[0].OnDisk!.LastWriteTimeUtc).IsEqualTo(saved.AddTicks(1));
        await Assert.That(_diffs.Paths.Count).IsEqualTo(2);
    }

    [Test]
    public async Task A_row_the_resync_left_alone_is_not_asked_about_again()
    {
        var status = new FakeWorkingCopyStatus().Answers(Listing(Entry("a.png")));
        var view = new WorkingCopyViewModel("/studio/game", status, Pane());
        await view.RefreshAsync(None);
        await SelectAsync(view, 0);
        var selected = view.SelectedChange;

        await view.RefreshAsync(None);
        _clock.Advance(DiffPaneViewModel.SelectionDebounce);
        await Settle();

        await Assert.That(view.SelectedChange).IsSameReferenceAs(selected);
        await Assert.That(_diffs.Paths.Count).IsEqualTo(1);
    }

    /// <summary>Committed or reverted from somewhere else: there is nothing left to show a diff of.</summary>
    [Test]
    public async Task A_row_that_left_the_listing_takes_the_selection_with_it()
    {
        var status = new FakeWorkingCopyStatus()
            .Answers(Listing(Entry("a.png"), Entry("b.png")))
            .Answers(Listing(Entry("b.png")));
        var view = new WorkingCopyViewModel("/studio/game", status, Pane());
        await view.RefreshAsync(None);
        await SelectAsync(view, 0);
        DropSelectionOnReplace(view);

        await view.RefreshAsync(None);

        await Assert.That(view.SelectedChange).IsNull();
        await Assert.That(view.Diff.State).IsEqualTo(DiffPaneState.NothingSelected);
        await Assert.That(view.Diff.Row).IsNull();
    }

    /// <summary>Once the selection is gone, a later resync must not bring it back.</summary>
    [Test]
    public async Task A_row_that_returns_after_leaving_is_not_reselected()
    {
        var status = new FakeWorkingCopyStatus()
            .Answers(Listing(Entry("a.png")))
            .Answers(Listing())
            .Answers(Listing(Entry("a.png")));
        var view = new WorkingCopyViewModel("/studio/game", status, Pane());
        await view.RefreshAsync(None);
        await SelectAsync(view, 0);
        await view.RefreshAsync(None);

        await view.RefreshAsync(None);

        await Assert.That(view.SelectedChange).IsNull();
        await Assert.That(view.Diff.State).IsEqualTo(DiffPaneState.NothingSelected);
    }

    [Test]
    public async Task A_resync_with_nothing_selected_leaves_the_pane_alone()
    {
        var status = new FakeWorkingCopyStatus()
            .Answers(Listing(Entry("a.png")))
            .Answers(Listing(Entry("a.png", NodeStatus.Conflicted)));
        var view = new WorkingCopyViewModel("/studio/game", status, Pane());

        await view.RefreshAsync(None);
        await view.RefreshAsync(None);
        _clock.Advance(DiffPaneViewModel.SelectionDebounce);
        await Settle();

        await Assert.That(view.SelectedChange).IsNull();
        await Assert.That(view.Diff.State).IsEqualTo(DiffPaneState.NothingSelected);
        await Assert.That(_diffs.Paths).IsEmpty();
    }

    /// <summary>A failed refresh says nothing about the row, so the diff is neither dropped nor re-asked.</summary>
    [Test]
    public async Task A_failed_refresh_leaves_the_selection_and_its_diff_alone()
    {
        var status = new FakeWorkingCopyStatus().Answers(Listing(Entry("a.png"))).IsUnreachable();
        var view = new WorkingCopyViewModel("/studio/game", status, Pane());
        await view.RefreshAsync(None);
        await SelectAsync(view, 0);
        var selected = view.SelectedChange;

        await view.RefreshAsync(None);

        await Assert.That(view.SelectedChange).IsSameReferenceAs(selected);
        await Assert.That(view.Diff.State).IsEqualTo(DiffPaneState.NothingToShow);
        await Assert.That(_diffs.Paths.Count).IsEqualTo(1);
    }

    private DiffPaneViewModel Pane() => DiffPanes.Pane(_diffs, _clock);

    private async Task<WorkingCopyViewModel> ListedAsync(Protocol.StatusResponse listing)
    {
        var view = new WorkingCopyViewModel(
            "/studio/game",
            new FakeWorkingCopyStatus().Answers(listing),
            Pane()
        );
        await view.RefreshAsync(None);
        return view;
    }

    private async Task SelectAsync(WorkingCopyViewModel view, int index)
    {
        view.SelectedChange = view.Changes[index];
        _clock.Advance(DiffPaneViewModel.SelectionDebounce);
        await Settle();
    }

    /// <summary>What a bound ListBox does when the item it has selected is replaced or removed.</summary>
    private static void DropSelectionOnReplace(WorkingCopyViewModel view) =>
        view.Changes.CollectionChanged += (_, change) =>
        {
            if (
                change.Action
                    is NotifyCollectionChangedAction.Replace
                        or NotifyCollectionChangedAction.Remove
                && change.OldItems!.Contains(view.SelectedChange)
            )
            {
                view.SelectedChange = null;
            }
        };

    private static async Task Settle()
    {
        for (var turn = 0; turn < 5; turn++)
        {
            await Task.Yield();
            await Task.Delay(1);
        }
    }
}
