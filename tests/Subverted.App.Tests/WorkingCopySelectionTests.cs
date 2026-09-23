using System.Collections.Specialized;
using Microsoft.Extensions.Time.Testing;
using Subverted.App.ViewModels;
using Subverted.Core;
using static Subverted.App.Tests.Entries;

namespace Subverted.App.Tests;

/// <summary>
/// The selected line drives the diff pane, and survives the once-a-second resync, a change of
/// layout and a filter that still shows it.
/// </summary>
public sealed class WorkingCopySelectionTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    private readonly FakeTimeProvider _clock = new();
    private readonly FakeWorkingCopyDiff _diffs = new();

    [Test]
    public async Task Selecting_a_line_asks_for_its_diff_at_its_absolute_path()
    {
        var view = await ListedAsync(Listing(Entry("art/hero.png")));

        await SelectAsync(view, "art/hero.png");

        await Assert
            .That(_diffs.Paths)
            .IsEquivalentTo(new[] { DiffTarget.PathOf(Info.RootPath, "art/hero.png") });
        await Assert.That(view.Diff.Row).IsSameReferenceAs(view.Changes[0]);
    }

    /// <summary>The root row is listed when the root's own properties changed; its path is the root.</summary>
    [Test]
    public async Task Selecting_the_root_line_asks_about_the_root_itself()
    {
        var view = await ListedAsync(
            Listing(Entry("", NodeStatus.Unmodified, PropertyStatus.Modified))
        );

        await SelectAsync(view, "");

        await Assert.That(_diffs.Paths).IsEquivalentTo(new[] { Info.RootPath });
    }

    [Test]
    public async Task Deselecting_clears_the_pane()
    {
        var view = await ListedAsync(Listing(Entry("a.png")));
        await SelectAsync(view, "a.png");

        view.SelectedEntry = null;

        await Assert.That(view.Diff.State).IsEqualTo(DiffPaneState.NothingSelected);
        await Assert.That(view.Diff.Row).IsNull();
    }

    /// <summary>A folder in the tree only holds changes; there is no diff of it to show.</summary>
    [Test]
    public async Task Selecting_a_folder_line_clears_the_pane_and_asks_nothing()
    {
        var view = await ListedAsync(Listing(Entry("art/a.png"), Entry("b.png")));
        await SelectAsync(view, "b.png");
        view.ShowTreeCommand.Execute(null);

        await SelectAsync(view, "art/");

        await Assert.That(view.Diff.State).IsEqualTo(DiffPaneState.NothingSelected);
        await Assert.That(_diffs.Paths.Count).IsEqualTo(1);
    }

    /// <summary>
    /// A resync updates a changed line in place rather than replacing it, so the list keeps its
    /// selection without having to be told — and the diff is asked for again at once, with no
    /// debounce and no loading flash.
    /// </summary>
    [Test]
    public async Task A_line_whose_change_changed_stays_selected_and_is_asked_about_again_at_once()
    {
        var status = new FakeWorkingCopyStatus()
            .Answers(Listing(Entry("a.png")))
            .Answers(Listing(Entry("a.png", NodeStatus.Modified, PropertyStatus.Modified)));
        var view = WorkingCopies.View(status, Pane());
        await view.RefreshAsync(None);
        await SelectAsync(view, "a.png");
        var selected = view.SelectedEntry;
        var states = new List<DiffPaneState>();
        view.Diff.PropertyChanged += (_, _) => states.Add(view.Diff.State);

        await view.RefreshAsync(None);
        await Settle();

        await Assert.That(view.SelectedEntry).IsSameReferenceAs(selected);
        await Assert.That(view.SelectedEntry!.Row!.HasPropertyChange).IsTrue();
        await Assert.That(view.Diff.Row).IsSameReferenceAs(view.SelectedEntry.Row);
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
        var view = WorkingCopies.View(status, Pane());
        await view.RefreshAsync(None);
        await SelectAsync(view, "a.png");
        var selected = view.SelectedEntry;
        DropSelectionOnRemove(view);

        await view.RefreshAsync(None);
        await Settle();

        await Assert.That(view.SelectedEntry).IsSameReferenceAs(selected);
        await Assert.That(view.Diff.Row).IsSameReferenceAs(view.Entries[0].Row);
        await Assert
            .That(view.Entries[0].Row!.OnDisk!.LastWriteTimeUtc)
            .IsEqualTo(saved.AddTicks(1));
        await Assert.That(_diffs.Paths.Count).IsEqualTo(2);
    }

    /// <summary>
    /// Turning into a conflict pins the line to the top, which moves it — and the list control
    /// drops a moved item from its selection. The selection comes back to it all the same.
    /// </summary>
    [Test]
    public async Task A_line_the_resync_moved_to_the_top_stays_selected()
    {
        var status = new FakeWorkingCopyStatus()
            .Answers(Listing(Entry("a.png"), Entry("b.png")))
            .Answers(Listing(Entry("a.png"), Entry("b.png", NodeStatus.Conflicted)));
        var view = WorkingCopies.View(status, Pane());
        await view.RefreshAsync(None);
        await SelectAsync(view, "b.png");
        var selected = view.SelectedEntry;
        DropSelectionOnRemove(view);

        await view.RefreshAsync(None);
        await Settle();

        await Assert.That(view.Entries[0]).IsSameReferenceAs(selected);
        await Assert.That(view.SelectedEntry).IsSameReferenceAs(selected);
        await Assert.That(_diffs.Paths.Count).IsEqualTo(2);
    }

    [Test]
    public async Task A_line_the_resync_left_alone_is_not_asked_about_again()
    {
        var status = new FakeWorkingCopyStatus().Answers(Listing(Entry("a.png")));
        var view = WorkingCopies.View(status, Pane());
        await view.RefreshAsync(None);
        await SelectAsync(view, "a.png");
        var selected = view.SelectedEntry;

        await view.RefreshAsync(None);
        _clock.Advance(DiffPaneViewModel.SelectionDebounce);
        await Settle();

        await Assert.That(view.SelectedEntry).IsSameReferenceAs(selected);
        await Assert.That(_diffs.Paths.Count).IsEqualTo(1);
    }

    /// <summary>Committed or reverted from somewhere else: there is nothing left to show a diff of.</summary>
    [Test]
    public async Task A_line_that_left_the_listing_takes_the_selection_with_it()
    {
        var status = new FakeWorkingCopyStatus()
            .Answers(Listing(Entry("a.png"), Entry("b.png")))
            .Answers(Listing(Entry("b.png")));
        var view = WorkingCopies.View(status, Pane());
        await view.RefreshAsync(None);
        await SelectAsync(view, "a.png");
        DropSelectionOnRemove(view);

        await view.RefreshAsync(None);

        await Assert.That(view.SelectedEntry).IsNull();
        await Assert.That(view.Diff.State).IsEqualTo(DiffPaneState.NothingSelected);
        await Assert.That(view.Diff.Row).IsNull();
    }

    /// <summary>Once the selection is gone, a later resync must not bring it back.</summary>
    [Test]
    public async Task A_line_that_returns_after_leaving_is_not_reselected()
    {
        var status = new FakeWorkingCopyStatus()
            .Answers(Listing(Entry("a.png")))
            .Answers(Listing())
            .Answers(Listing(Entry("a.png")));
        var view = WorkingCopies.View(status, Pane());
        await view.RefreshAsync(None);
        await SelectAsync(view, "a.png");
        await view.RefreshAsync(None);

        await view.RefreshAsync(None);

        await Assert.That(view.SelectedEntry).IsNull();
        await Assert.That(view.Diff.State).IsEqualTo(DiffPaneState.NothingSelected);
    }

    [Test]
    public async Task A_resync_with_nothing_selected_leaves_the_pane_alone()
    {
        var status = new FakeWorkingCopyStatus()
            .Answers(Listing(Entry("a.png")))
            .Answers(Listing(Entry("a.png", NodeStatus.Conflicted)));
        var view = WorkingCopies.View(status, Pane());

        await view.RefreshAsync(None);
        await view.RefreshAsync(None);
        _clock.Advance(DiffPaneViewModel.SelectionDebounce);
        await Settle();

        await Assert.That(view.SelectedEntry).IsNull();
        await Assert.That(view.Diff.State).IsEqualTo(DiffPaneState.NothingSelected);
        await Assert.That(_diffs.Paths).IsEmpty();
    }

    /// <summary>A failed refresh says nothing about the row, so the diff is neither dropped nor re-asked.</summary>
    [Test]
    public async Task A_failed_refresh_leaves_the_selection_and_its_diff_alone()
    {
        var status = new FakeWorkingCopyStatus().Answers(Listing(Entry("a.png"))).IsUnreachable();
        var view = WorkingCopies.View(status, Pane());
        await view.RefreshAsync(None);
        await SelectAsync(view, "a.png");
        var selected = view.SelectedEntry;

        await view.RefreshAsync(None);

        await Assert.That(view.SelectedEntry).IsSameReferenceAs(selected);
        await Assert.That(view.Diff.State).IsEqualTo(DiffPaneState.NothingToShow);
        await Assert.That(_diffs.Paths.Count).IsEqualTo(1);
    }

    [Test]
    public async Task A_filter_that_still_shows_the_selected_line_keeps_it_and_asks_nothing()
    {
        var view = await ListedAsync(Listing(Entry("art/a.png"), Entry("src/b.cs")));
        await SelectAsync(view, "art/a.png");
        var selected = view.SelectedEntry;

        view.Filter = "art";
        await Settle();

        await Assert.That(view.SelectedEntry).IsSameReferenceAs(selected);
        await Assert.That(view.Diff.Row).IsSameReferenceAs(selected!.Row);
        await Assert.That(_diffs.Paths.Count).IsEqualTo(1);
    }

    /// <summary>A diff of a line the list no longer shows would be a diff of nothing on screen.</summary>
    [Test]
    public async Task A_filter_that_hides_the_selected_line_takes_the_selection_and_the_diff_away()
    {
        var view = await ListedAsync(Listing(Entry("art/a.png"), Entry("src/b.cs")));
        await SelectAsync(view, "art/a.png");
        DropSelectionOnRemove(view);

        view.Filter = "src";

        await Assert.That(view.SelectedEntry).IsNull();
        await Assert.That(view.Diff.State).IsEqualTo(DiffPaneState.NothingSelected);
    }

    [Test]
    public async Task Switching_to_the_tree_keeps_the_selected_file_selected_and_asks_nothing()
    {
        var view = await ListedAsync(Listing(Entry("art/a.png"), Entry("src/b.cs")));
        await SelectAsync(view, "src/b.cs");
        var selected = view.SelectedEntry;
        DropSelectionOnRemove(view);

        view.ShowTreeCommand.Execute(null);
        await Settle();

        await Assert.That(view.SelectedEntry).IsSameReferenceAs(selected);
        await Assert.That(view.SelectedEntry!.Content.Depth).IsEqualTo(1);
        await Assert.That(_diffs.Paths.Count).IsEqualTo(1);
    }

    [Test]
    public async Task A_selected_folder_line_is_dropped_when_the_flat_list_has_no_such_line()
    {
        var view = await ListedAsync(Listing(Entry("art/a.png")));
        view.ShowTreeCommand.Execute(null);
        await SelectAsync(view, "art/");

        view.ShowFlatCommand.Execute(null);

        await Assert.That(view.SelectedEntry).IsNull();
        await Assert.That(view.Diff.State).IsEqualTo(DiffPaneState.NothingSelected);
    }

    private DiffPaneViewModel Pane() => DiffPanes.Pane(_diffs, _clock);

    private async Task<WorkingCopyViewModel> ListedAsync(Protocol.StatusResponse listing)
    {
        var view = WorkingCopies.View(new FakeWorkingCopyStatus().Answers(listing), Pane());
        await view.RefreshAsync(None);
        return view;
    }

    private async Task SelectAsync(WorkingCopyViewModel view, string key)
    {
        view.Select(key);
        _clock.Advance(DiffPaneViewModel.SelectionDebounce);
        await Settle();
    }

    /// <summary>What a bound ListBox does when the item it has selected leaves it, even to move.</summary>
    private static void DropSelectionOnRemove(WorkingCopyViewModel view) =>
        view.Entries.CollectionChanged += (_, change) =>
        {
            if (
                change.Action is NotifyCollectionChangedAction.Remove
                && change.OldItems!.Contains(view.SelectedEntry)
            )
            {
                view.SelectedEntry = null;
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
