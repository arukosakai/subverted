using Subverted.App.Presentation;
using Subverted.App.ViewModels;
using Subverted.Core;
using Subverted.Protocol;
using static Subverted.App.Tests.Entries;

namespace Subverted.App.Tests;

/// <summary>
/// The Changed / All toggle. All exists so a file nobody has touched has a line to lock from; what
/// a commit sends, and every count on screen, must not notice which side it is on.
/// </summary>
public sealed class ListingToggleTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    private static readonly WorkingCopyEntry Edited = Entry("art/hero.png");
    private static readonly WorkingCopyEntry Stray = Entry("build.log", NodeStatus.Unversioned);
    private static readonly WorkingCopyEntry Untouched = Entry("art/tree.png", NodeStatus.Unmodified);
    private static readonly WorkingCopyEntry UntouchedElsewhere = Entry(
        "src/main.cs",
        NodeStatus.Unmodified
    );
    private static readonly WorkingCopyEntry UntouchedFolder = Entry(
        "src",
        NodeStatus.Unmodified,
        kind: NodeKind.Directory
    );

    private static StatusResponse ChangesOnly() => Listing(Edited, Stray);

    private static StatusResponse Everything() =>
        Listing(UntouchedFolder, Edited, Stray, Untouched, UntouchedElsewhere);

    [Test]
    public async Task Changes_is_what_a_view_lists_first_and_what_it_asks_the_daemon_for()
    {
        var status = new FakeWorkingCopyStatus().Answers(ChangesOnly());
        var view = WorkingCopies.View(status);

        await view.RefreshAsync(None);

        await Assert.That(view.Listing).IsEqualTo(ListedNodes.Changes);
        await Assert.That(view.IsListingAll).IsFalse();
        await Assert.That(status.Listings).IsEquivalentTo([ListedNodes.Changes]);
    }

    [Test]
    public async Task Listing_all_asks_for_all_and_shows_the_unmodified_files_among_the_changes()
    {
        var status = new FakeWorkingCopyStatus().Answers(ChangesOnly()).Answers(Everything());
        var view = WorkingCopies.View(status);
        await view.RefreshAsync(None);

        await view.ListAllCommand.ExecuteAsync(null);

        await Assert.That(view.IsListingAll).IsTrue();
        await Assert.That(status.Listings[^1]).IsEqualTo(ListedNodes.All);
        await Assert
            .That(Keys(view))
            .IsEqualTo("art/hero.png,art/tree.png,build.log,src/main.cs");
    }

    /// <summary>Going back needs nothing from the daemon to be right: the unmodified lines go at once.</summary>
    [Test]
    public async Task Listing_changes_again_drops_the_unmodified_lines_before_the_daemon_answers()
    {
        var status = new FakeWorkingCopyStatus()
            .Answers(ChangesOnly())
            .Answers(Everything())
            .IsUnreachable();
        var view = WorkingCopies.View(status);
        await view.RefreshAsync(None);
        await view.ListAllCommand.ExecuteAsync(null);

        await view.ListChangesCommand.ExecuteAsync(null);

        await Assert.That(view.Listing).IsEqualTo(ListedNodes.Changes);
        await Assert.That(status.Listings[^1]).IsEqualTo(ListedNodes.Changes);
        await Assert.That(Keys(view)).IsEqualTo("art/hero.png,build.log");
        await Assert.That(view.IsStale).IsTrue();
    }

    [Test]
    public async Task Choosing_the_side_already_listed_asks_nothing()
    {
        var status = new FakeWorkingCopyStatus().Answers(ChangesOnly());
        var view = WorkingCopies.View(status);
        await view.RefreshAsync(None);

        await view.ListChangesCommand.ExecuteAsync(null);

        await Assert.That(status.Reads).IsEqualTo(1);
    }

    /// <summary>The operator's rule for this toggle: the commit set is the same whichever side is listed.</summary>
    [Test]
    public async Task The_commit_would_send_exactly_the_same_paths_listing_changes_or_all()
    {
        var byChanges = WorkingCopies.View(new FakeWorkingCopyStatus().Answers(ChangesOnly()));
        await byChanges.RefreshAsync(None);
        var byAll = WorkingCopies.View(
            new FakeWorkingCopyStatus().Answers(ChangesOnly()).Answers(Everything())
        );
        await byAll.RefreshAsync(None);
        await byAll.ListAllCommand.ExecuteAsync(null);

        await Assert
            .That(string.Join(",", byAll.Composer.Selection.RelPaths))
            .IsEqualTo(string.Join(",", byChanges.Composer.Selection.RelPaths));
        await Assert.That(string.Join(",", byAll.Composer.Selection.RelPaths)).IsEqualTo("art/hero.png");
        await Assert.That(byAll.Ticked).IsEquivalentTo(byChanges.Ticked);
    }

    /// <summary>
    /// Seen first as unmodified, a file that is then edited is a new change and takes its default
    /// tick — as it would have if it had first appeared listing changes.
    /// </summary>
    [Test]
    public async Task A_file_first_listed_unmodified_takes_its_default_tick_once_it_changes()
    {
        var status = new FakeWorkingCopyStatus()
            .Answers(ChangesOnly())
            .Answers(Everything())
            .Answers(Listing(UntouchedFolder, Edited, Stray, Entry("art/tree.png"), UntouchedElsewhere));
        var view = WorkingCopies.View(status);
        await view.RefreshAsync(None);
        await view.ListAllCommand.ExecuteAsync(null);
        var tickedWhileUnmodified = view.Ticked.Contains("art/tree.png");

        await view.RefreshAsync(None);

        await Assert.That(tickedWhileUnmodified).IsFalse();
        await Assert.That(view.Ticked).IsEquivalentTo(["art/hero.png", "art/tree.png"]);
    }

    [Test]
    public async Task Counts_the_summary_and_the_hidden_line_are_about_changes_only()
    {
        var status = new FakeWorkingCopyStatus().Answers(ChangesOnly()).Answers(Everything());
        var view = WorkingCopies.View(status);
        await view.RefreshAsync(None);
        await view.ListAllCommand.ExecuteAsync(null);

        view.Filter = "art";

        await Assert.That(view.Changes.Count).IsEqualTo(2);
        await Assert
            .That(string.Join(",", view.Summary.Select(count => count.Text)))
            .IsEqualTo("1 modified,1 not versioned");
        await Assert.That(Keys(view)).IsEqualTo("art/hero.png,art/tree.png");
        await Assert.That(view.HiddenText).IsEqualTo("1 change hidden");
    }

    /// <summary>With only unmodified files out of sight, no change is hidden, and the line says nothing.</summary>
    [Test]
    public async Task A_filter_hiding_only_unmodified_files_hides_no_change()
    {
        var status = new FakeWorkingCopyStatus()
            .Answers(Listing(Edited))
            .Answers(Listing(Edited, Untouched, UntouchedElsewhere));
        var view = WorkingCopies.View(status);
        await view.RefreshAsync(None);
        await view.ListAllCommand.ExecuteAsync(null);

        view.Filter = "hero";

        await Assert.That(Keys(view)).IsEqualTo("art/hero.png");
        await Assert.That(view.HiddenText).IsNull();
    }

    [Test]
    public async Task The_tree_listing_all_holds_the_folders_of_unmodified_files_counting_no_change_there()
    {
        var status = new FakeWorkingCopyStatus().Answers(ChangesOnly()).Answers(Everything());
        var view = WorkingCopies.View(status);
        await view.RefreshAsync(None);
        var before = string.Join(",", view.Folders.Select(folder => folder.Content.RelPath));

        await view.ListAllCommand.ExecuteAsync(null);

        await Assert.That(before).IsEqualTo(",art");
        await Assert
            .That(view.Folders.Select(folder => (folder.Content.RelPath, folder.Content.Count)))
            .IsEquivalentTo([("", 2), ("art", 1), ("src", 0)]);
    }

    /// <summary>Nothing to commit, revert or resolve, but it is a file the repository has, so it can be locked.</summary>
    [Test]
    public async Task An_unmodified_line_can_be_locked_and_looked_up_but_not_ticked_reverted_or_resolved()
    {
        var status = new FakeWorkingCopyStatus().Answers(ChangesOnly()).Answers(Everything());
        var view = WorkingCopies.View(status);
        view.HistoryRequested += _ => { };
        await view.RefreshAsync(None);
        await view.ListAllCommand.ExecuteAsync(null);

        var line = view.Entries.Single(entry => entry.Key == "art/tree.png");

        await Assert.That(line.CanTick).IsFalse();
        await Assert.That(line.IsFolder).IsFalse();
        await Assert.That(view.ToggleTickCommand.CanExecute(line)).IsFalse();
        await Assert.That(view.RevertCommand.CanExecute(line)).IsFalse();
        await Assert.That(view.KeepMineCommand.CanExecute(line)).IsFalse();
        await Assert.That(view.LockCommand.CanExecute(line)).IsTrue();
        await Assert.That(view.ShowHistoryCommand.CanExecute(line)).IsTrue();
        await Assert.That(line.Row!.Badge).IsEqualTo(new ChangeBadge("Unchanged", ChangeTone.Quiet));
    }

    [Test]
    public async Task A_clean_copy_listing_changes_shows_only_its_message()
    {
        var view = WorkingCopies.View(new FakeWorkingCopyStatus().Answers(Listing()));

        await view.RefreshAsync(None);

        await Assert.That(view.IsClean).IsTrue();
        await Assert.That(view.ShowsCleanMessage).IsTrue();
        await Assert.That(view.HasLines).IsFalse();
    }

    /// <summary>Clean is where an artist locks before starting, so All lists the files rather than the message.</summary>
    [Test]
    public async Task A_clean_copy_listing_all_shows_its_files_and_still_nothing_to_commit()
    {
        var status = new FakeWorkingCopyStatus()
            .Answers(Listing())
            .Answers(Listing(Untouched, UntouchedElsewhere));
        var view = WorkingCopies.View(status);
        await view.RefreshAsync(None);

        await view.ListAllCommand.ExecuteAsync(null);

        await Assert.That(view.IsClean).IsTrue();
        await Assert.That(view.ShowsCleanMessage).IsFalse();
        await Assert.That(view.HasLines).IsTrue();
        await Assert.That(view.HasChanges).IsFalse();
        await Assert.That(view.Summary).IsEmpty();
        await Assert.That(Keys(view)).IsEqualTo("art/tree.png,src/main.cs");
    }

    /// <summary>A copy with no files at all has nothing to list either way, so it says it is clean.</summary>
    [Test]
    public async Task A_clean_copy_with_no_files_listing_all_still_says_it_is_clean()
    {
        var status = new FakeWorkingCopyStatus().Answers(Listing()).Answers(Listing(UntouchedFolder));
        var view = WorkingCopies.View(status);
        await view.RefreshAsync(None);

        await view.ListAllCommand.ExecuteAsync(null);

        await Assert.That(view.ShowsCleanMessage).IsTrue();
        await Assert.That(view.HasLines).IsFalse();
    }

    [Test]
    public async Task A_listing_all_that_goes_unreachable_stays_on_screen_as_stale()
    {
        var status = new FakeWorkingCopyStatus()
            .Answers(Listing())
            .Answers(Listing(Untouched))
            .IsUnreachable();
        var view = WorkingCopies.View(status);
        await view.RefreshAsync(None);
        await view.ListAllCommand.ExecuteAsync(null);

        await view.RefreshAsync(None);

        await Assert.That(view.IsStale).IsTrue();
        await Assert.That(view.IsBlocked).IsFalse();
        await Assert.That(Keys(view)).IsEqualTo("art/tree.png");
    }

    [Test]
    public async Task The_next_poll_says_which_scan_is_on_screen()
    {
        var scan = Guid.NewGuid();
        var status = new FakeWorkingCopyStatus().Answers(ChangesOnly() with { ScanId = scan });
        var view = WorkingCopies.View(status);

        await view.RefreshAsync(None);
        await view.RefreshAsync(None);

        await Assert.That(status.HeldScans).IsEquivalentTo(new Guid?[] { null, scan });
    }

    /// <summary>"Unchanged" means the listing on screen is the answer: every line stays the same instance.</summary>
    [Test]
    public async Task An_unchanged_answer_leaves_every_line_and_tick_as_it_was()
    {
        var scan = Guid.NewGuid();
        var status = new FakeWorkingCopyStatus()
            .Answers(ChangesOnly() with { ScanId = scan })
            .Answers(new StatusUnchangedResponse(scan, 0.1));
        var view = WorkingCopies.View(status);
        await view.RefreshAsync(None);
        var lines = view.Entries.ToList();
        view.ToggleTickCommand.Execute(view.Entries.Single(entry => entry.Key == "build.log"));

        await view.RefreshAsync(None);

        await Assert.That(view.Entries.SequenceEqual(lines)).IsTrue();
        await Assert.That(view.Ticked).IsEquivalentTo(["art/hero.png", "build.log"]);
        await Assert.That(view.State).IsEqualTo(WorkingCopyState.Ready);
    }

    /// <summary>A scan held for one side of the toggle says nothing about the other.</summary>
    [Test]
    public async Task Toggling_asks_for_the_whole_listing_whatever_scan_was_held()
    {
        var status = new FakeWorkingCopyStatus()
            .Answers(ChangesOnly() with { ScanId = Guid.NewGuid() })
            .Answers(Everything() with { ScanId = Guid.NewGuid() });
        var view = WorkingCopies.View(status);
        await view.RefreshAsync(None);

        await view.ListAllCommand.ExecuteAsync(null);

        await Assert.That(status.HeldScans[^1]).IsNull();
    }

    /// <summary>Only a listing puts the state back, so after a failure the next answer has to be one.</summary>
    [Test]
    public async Task After_a_failure_the_next_poll_holds_no_scan()
    {
        var status = new FakeWorkingCopyStatus()
            .Answers(ChangesOnly() with { ScanId = Guid.NewGuid() })
            .Answers(new ErrorResponse(DaemonErrorKind.Internal, "boom"))
            .Answers(ChangesOnly());
        var view = WorkingCopies.View(status);
        await view.RefreshAsync(None);
        await view.RefreshAsync(None);

        await view.RefreshAsync(None);

        await Assert.That(status.HeldScans[^1]).IsNull();
        await Assert.That(view.State).IsEqualTo(WorkingCopyState.Ready);
    }

    [Test]
    public async Task After_the_daemon_went_unreachable_the_next_poll_holds_no_scan()
    {
        var status = new FakeWorkingCopyStatus()
            .Answers(ChangesOnly() with { ScanId = Guid.NewGuid() })
            .IsUnreachable()
            .Answers(ChangesOnly());
        var view = WorkingCopies.View(status);
        await view.RefreshAsync(None);
        await view.RefreshAsync(None);

        await view.RefreshAsync(None);

        await Assert.That(status.HeldScans[^1]).IsNull();
    }

    /// <summary>
    /// A poll asked for changes and answered after the toggle moved to All must not draw a
    /// changes-only table under All — nor hold its scan as if it were All's.
    /// </summary>
    [Test]
    public async Task An_answer_for_the_side_the_toggle_has_left_is_dropped()
    {
        var status = new HeldFirstStatus(ChangesOnly() with { ScanId = Guid.NewGuid() });
        var view = WorkingCopies.View(status);
        var poll = view.RefreshAsync(None);

        status.Next = Everything();
        await view.ListAllCommand.ExecuteAsync(null);
        status.Release();
        await poll;

        await Assert.That(Keys(view)).IsEqualTo("art/hero.png,art/tree.png,build.log,src/main.cs");
        await Assert.That(status.HeldScans[^1]).IsNull();
        await view.RefreshAsync(None);
        await Assert.That(status.HeldScans[^1]).IsNull();
    }

    private static string Keys(WorkingCopyViewModel view) =>
        string.Join(",", view.Entries.Select(entry => entry.Key));

    /// <summary>Holds its first answer until released; every later question gets <see cref="Next"/> at once.</summary>
    private sealed class HeldFirstStatus(StatusResponse first) : IWorkingCopyStatus
    {
        private readonly TaskCompletionSource _gate = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        private readonly StatusResponse _first = first;
        private bool _answeredFirst;

        public StatusResponse Next { get; set; } = first;

        public List<Guid?> HeldScans { get; } = [];

        public void Release() => _gate.TrySetResult();

        public async Task<DaemonResponse> ReadAsync(
            string workingCopyPath,
            ListedNodes listed,
            Guid? heldScan,
            CancellationToken cancellationToken
        )
        {
            HeldScans.Add(heldScan);
            if (_answeredFirst)
            {
                return Next;
            }

            _answeredFirst = true;
            await _gate.Task;
            return _first;
        }
    }
}
