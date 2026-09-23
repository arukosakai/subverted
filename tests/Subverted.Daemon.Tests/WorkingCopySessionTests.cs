using Subverted.Core;
using Subverted.Protocol;

namespace Subverted.Daemon.Tests;

/// <summary>
/// The whole daemon premise is this class: an answer that is already known when you ask, and that
/// is never the answer from before your last save. Both halves are asserted with a scan counter,
/// because a cache that never serves warm and a cache that serves stale both look fine otherwise.
/// </summary>
public sealed class WorkingCopySessionTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    [Test]
    public async Task The_first_caller_pays_for_the_scan()
    {
        var (session, scan, _) = Session();

        var current = await session.CurrentAsync(None);

        await Assert.That(current.ServedFromWarmIndex).IsFalse();
        await Assert.That(scan.Scans).IsEqualTo(1);
    }

    [Test]
    public async Task The_second_caller_does_not_pay_again()
    {
        var (session, scan, _) = Session();
        await session.CurrentAsync(None);

        var current = await session.CurrentAsync(None);

        await Assert.That(current.ServedFromWarmIndex).IsTrue();
        await Assert.That(scan.Scans).IsEqualTo(1);
    }

    [Test]
    public async Task A_change_makes_the_next_caller_pay()
    {
        var (session, scan, notifier) = Session();
        await session.CurrentAsync(None);

        notifier.RaiseChanged();
        var current = await session.CurrentAsync(None);

        await Assert.That(current.ServedFromWarmIndex).IsFalse();
        await Assert.That(scan.Scans).IsEqualTo(2);
    }

    /// <summary>
    /// The generation is read before the scan runs, so a save that lands while the scan is walking
    /// the tree leaves the result stale. One wasted scan; never an answer from before the save.
    /// </summary>
    [Test]
    public async Task A_change_that_lands_during_a_scan_does_not_get_swallowed_by_it()
    {
        var (session, scan, notifier) = Session();
        scan.DuringScan = () => notifier.RaiseChanged();

        await session.CurrentAsync(None);
        scan.DuringScan = null;

        await Assert.That(session.IsStale).IsTrue();
        await Assert.That((await session.CurrentAsync(None)).ServedFromWarmIndex).IsFalse();
        await Assert.That(scan.Scans).IsEqualTo(2);
    }

    /// <summary>
    /// Nothing is watching, so nothing can be trusted. Slow and right beats fast and wrong: this is
    /// the inotify-exhausted case from D5.
    /// </summary>
    [Test]
    public async Task A_session_nobody_is_watching_rescans_every_single_time()
    {
        var (session, scan, notifier) = Session();
        notifier.IsWatching = false;

        await session.CurrentAsync(None);
        var current = await session.CurrentAsync(None);

        await Assert.That(current.ServedFromWarmIndex).IsFalse();
        await Assert.That(scan.Scans).IsEqualTo(2);
    }

    [Test]
    public async Task A_watcher_that_comes_back_lets_the_index_be_trusted_again()
    {
        var (session, scan, notifier) = Session();
        notifier.IsWatching = false;
        await session.CurrentAsync(None);

        notifier.IsWatching = true;
        var current = await session.CurrentAsync(None);

        await Assert.That(current.ServedFromWarmIndex).IsTrue();
        await Assert.That(scan.Scans).IsEqualTo(1);
    }

    [Test]
    public async Task An_unwatched_session_reports_unavailable_however_fresh_its_scan_is()
    {
        var (session, _, notifier) = Session();
        await session.CurrentAsync(None);
        notifier.IsWatching = false;

        await Assert.That(session.WatcherState).IsEqualTo(WatcherState.Unavailable);
    }

    [Test]
    public async Task A_watched_session_with_a_current_scan_is_healthy()
    {
        var (session, _, _) = Session();

        await session.CurrentAsync(None);

        await Assert.That(session.WatcherState).IsEqualTo(WatcherState.Healthy);
    }

    [Test]
    public async Task A_session_with_a_change_it_has_not_scanned_yet_is_recovering()
    {
        var (session, _, notifier) = Session();
        await session.CurrentAsync(None);

        notifier.RaiseChanged();

        await Assert.That(session.WatcherState).IsEqualTo(WatcherState.Recovering);
    }

    [Test]
    public async Task A_session_that_has_never_scanned_is_recovering_rather_than_healthy()
    {
        var (session, _, _) = Session();

        await Assert.That(session.WatcherState).IsEqualTo(WatcherState.Recovering);
        await Assert.That(session.EntryCount).IsEqualTo(0);
    }

    [Test]
    public async Task The_entry_count_is_the_last_scans_count()
    {
        var (session, scan, _) = Session();
        scan.Entries = [Entry("a"), Entry("b")];

        await session.CurrentAsync(None);

        await Assert.That(session.EntryCount).IsEqualTo(2);
    }

    [Test]
    public async Task The_generation_moves_once_per_change_and_not_otherwise()
    {
        var (session, _, notifier) = Session();
        var before = session.Generation;

        notifier.RaiseChanged();
        notifier.RaiseChanged();

        await Assert.That(session.Generation - before).IsEqualTo(2);
    }

    /// <summary>
    /// Ten front-ends asking at once during a rescan must cost one tree walk, not ten. The blocked
    /// caller rechecks after the semaphore rather than scanning again.
    /// </summary>
    [Test]
    public async Task Callers_that_arrive_together_share_one_scan()
    {
        var (session, scan, _) = Session();
        using var inScan = new SemaphoreSlim(0);
        using var release = new SemaphoreSlim(0);
        scan.DuringScan = () =>
        {
            inScan.Release();
            release.Wait();
        };

        var first = Task.Run(() => session.CurrentAsync(None));
        await inScan.WaitAsync();
        var second = Task.Run(() => session.CurrentAsync(None));
        release.Release();
        var results = await Task.WhenAll(first, second);

        await Assert.That(scan.Scans).IsEqualTo(1);
        await Assert.That(results.Count(r => r.ServedFromWarmIndex)).IsEqualTo(1);
    }

    [Test]
    public async Task Disposing_lets_go_of_the_watcher_and_of_wc_db()
    {
        var (session, scan, notifier) = Session();

        session.Dispose();

        await Assert.That(notifier.IsDisposed).IsTrue();
        await Assert.That(scan.IsDisposed).IsTrue();
        await Assert.That(notifier.HasSubscriber).IsFalse();
    }

    /// <summary>
    /// A change at a known path is re-resolved rather than rescanned. The scan counter staying at
    /// one is the assertion — an incremental reader that is consulted and then ignored looks
    /// identical from the outside.
    /// </summary>
    [Test]
    public async Task A_change_at_a_named_path_is_applied_without_rescanning()
    {
        var (session, scan, notifier, incremental) = IncrementalSession();
        await session.CurrentAsync(None);

        notifier.RaiseChangedAt("art/hero.png");
        await session.CurrentAsync(None);

        await Assert.That(incremental.Applications).IsEqualTo(1);
        await Assert.That(scan.Scans).IsEqualTo(1);
        await Assert.That(incremental.Requested[0]).IsEquivalentTo(new[] { "art/hero.png" });
    }

    /// <summary>
    /// D5's overflow rule, intact. Events were dropped, so no set of paths describes the change
    /// and re-resolving the ones that did arrive would miss whatever was in the lost buffer.
    /// </summary>
    [Test]
    public async Task An_unattributable_change_rescans_even_when_named_paths_are_also_pending()
    {
        var (session, scan, notifier, incremental) = IncrementalSession();
        await session.CurrentAsync(None);

        notifier.RaiseChangedAt("art/hero.png");
        notifier.RaiseChanged();
        await session.CurrentAsync(None);

        await Assert.That(scan.Scans).IsEqualTo(2);
        await Assert.That(incremental.Applications).IsEqualTo(0);
    }

    [Test]
    public async Task A_reader_that_cannot_apply_the_change_falls_back_to_a_full_scan()
    {
        var (session, scan, notifier, incremental) = IncrementalSession();
        incremental.Apply = (_, _) => null;
        await session.CurrentAsync(None);

        notifier.RaiseChangedAt("art/hero.png");
        await session.CurrentAsync(None);

        await Assert.That(incremental.Applications).IsEqualTo(1);
        await Assert.That(scan.Scans).IsEqualTo(2);
    }

    /// <summary>
    /// The CLI fallback has no incremental reader, and must keep behaving exactly as it did.
    /// </summary>
    [Test]
    public async Task A_session_with_no_incremental_reader_rescans_on_every_change()
    {
        var (session, scan, notifier) = Session();
        await session.CurrentAsync(None);

        notifier.RaiseChangedAt("art/hero.png");
        await session.CurrentAsync(None);

        await Assert.That(scan.Scans).IsEqualTo(2);
    }

    [Test]
    public async Task Every_path_touched_since_the_last_answer_is_applied_at_once()
    {
        var (session, _, notifier, incremental) = IncrementalSession();
        await session.CurrentAsync(None);

        notifier.RaiseChangedAt("a.txt");
        notifier.RaiseChangedAt("b.txt");
        notifier.RaiseChangedAt("a.txt");
        await session.CurrentAsync(None);

        await Assert.That(incremental.Applications).IsEqualTo(1);
        await Assert.That(incremental.Requested[0]).IsEquivalentTo(new[] { "a.txt", "b.txt" });
    }

    /// <summary>
    /// The paths are cleared when they are taken, not when the answer is stored — so a second
    /// request with nothing new in between is warm rather than re-applying the same paths.
    /// </summary>
    [Test]
    public async Task Applied_paths_are_not_applied_a_second_time()
    {
        var (session, _, notifier, incremental) = IncrementalSession();
        await session.CurrentAsync(None);
        notifier.RaiseChangedAt("a.txt");
        await session.CurrentAsync(None);

        var current = await session.CurrentAsync(None);

        await Assert.That(current.ServedFromWarmIndex).IsTrue();
        await Assert.That(incremental.Applications).IsEqualTo(1);
    }

    /// <summary>
    /// The same rule as the full-scan case, and the reason the generation moves under the same
    /// lock as the path: a save landing mid-apply must not be counted as already accounted for.
    /// </summary>
    [Test]
    public async Task A_change_landing_during_an_incremental_apply_is_not_swallowed()
    {
        var (session, _, notifier, incremental) = IncrementalSession();
        await session.CurrentAsync(None);

        var raised = false;
        incremental.Apply = (held, _) =>
        {
            if (!raised)
            {
                raised = true;
                notifier.RaiseChangedAt("landed-mid-apply.txt");
            }

            return held;
        };

        notifier.RaiseChangedAt("first.txt");
        await session.CurrentAsync(None);

        await Assert.That(session.IsStale).IsTrue();
        await session.CurrentAsync(None);
        await Assert
            .That(incremental.Requested[^1])
            .IsEquivalentTo(new[] { "landed-mid-apply.txt" });
    }

    /// <summary>
    /// Past the cap the paths stop being tracked, because a long import would otherwise grow the
    /// set one entry per file for no benefit — the tree walk amortises long before that.
    /// </summary>
    [Test]
    public async Task Past_the_path_cap_the_change_is_rescanned_instead()
    {
        var (session, scan, notifier, incremental) = IncrementalSession();
        await session.CurrentAsync(None);

        for (var i = 0; i <= 1000; i++)
        {
            notifier.RaiseChangedAt($"pack/file{i}.dat");
        }

        await session.CurrentAsync(None);

        await Assert.That(scan.Scans).IsEqualTo(2);
        await Assert.That(incremental.Applications).IsEqualTo(0);
    }

    /// <summary>
    /// Exactly at the cap it is still applied — the boundary is tested on both sides because
    /// <c>&lt;</c> against <c>&lt;=</c> here is the difference between using this feature and not.
    /// </summary>
    [Test]
    public async Task Exactly_at_the_path_cap_the_change_is_still_applied()
    {
        var (session, scan, notifier, incremental) = IncrementalSession();
        await session.CurrentAsync(None);

        for (var i = 0; i < 1000; i++)
        {
            notifier.RaiseChangedAt($"pack/file{i}.dat");
        }

        await session.CurrentAsync(None);

        await Assert.That(scan.Scans).IsEqualTo(1);
        await Assert.That(incremental.Applications).IsEqualTo(1);
        await Assert.That(incremental.Requested[0].Count).IsEqualTo(1000);
    }

    /// <summary>
    /// The first answer has nothing to update, so it is a scan however the change arrived.
    /// </summary>
    [Test]
    public async Task The_first_answer_is_always_a_full_scan()
    {
        var (session, scan, notifier, incremental) = IncrementalSession();

        notifier.RaiseChangedAt("a.txt");
        await session.CurrentAsync(None);

        await Assert.That(scan.Scans).IsEqualTo(1);
        await Assert.That(incremental.Applications).IsEqualTo(0);
    }

    [Test]
    public async Task An_interrupted_operation_is_reported_alongside_the_scan_that_found_it()
    {
        var (session, _, _, work) = WedgedSession();
        work.Operations = 3;

        var current = await session.CurrentAsync(None);

        await Assert.That(current.UnfinishedOperations).IsEqualTo(3);
    }

    /// <summary>
    /// The CLI fallback cannot see the work queue at all. Reporting zero is the only thing it can
    /// say, and it must not be mistaken for a reader that looked and found nothing.
    /// </summary>
    [Test]
    public async Task A_reader_that_cannot_see_the_work_queue_reports_none()
    {
        var (session, _, _) = Session();

        var current = await session.CurrentAsync(None);

        await Assert.That(current.UnfinishedOperations).IsEqualTo(0);
    }

    [Test]
    public async Task A_healthy_working_copy_reports_none_from_a_reader_that_did_look()
    {
        var (session, _, _, work) = WedgedSession();
        work.Operations = 0;

        var current = await session.CurrentAsync(None);

        await Assert.That(current.UnfinishedOperations).IsEqualTo(0);
        await Assert.That(work.Reads).IsEqualTo(1);
    }

    [Test]
    public async Task A_caller_served_warm_gets_the_count_without_the_queue_being_read_again()
    {
        var (session, _, _, work) = WedgedSession();
        work.Operations = 2;
        await session.CurrentAsync(None);

        var current = await session.CurrentAsync(None);

        await Assert.That(current.ServedFromWarmIndex).IsTrue();
        await Assert.That(current.UnfinishedOperations).IsEqualTo(2);
        await Assert.That(work.Reads).IsEqualTo(1);
    }

    /// <summary>
    /// The state is transient: a client can wedge a working copy the daemon is already holding,
    /// and <c>sv cleanup</c> clears it while the daemon still holds it. A count read once at open
    /// would be wrong in both directions.
    /// </summary>
    [Test]
    public async Task A_cleanup_between_scans_changes_the_answer()
    {
        var (session, _, notifier, work) = WedgedSession();
        work.Operations = 4;
        await session.CurrentAsync(None);

        work.Operations = 0;
        notifier.RaiseChanged();
        var current = await session.CurrentAsync(None);

        await Assert.That(current.UnfinishedOperations).IsEqualTo(0);
        await Assert.That(work.Reads).IsEqualTo(2);
    }

    /// <summary>
    /// The cheap path re-resolves a handful of paths instead of walking the tree, and the queue is
    /// no part of what it re-resolves — so a count folded into the scan would go stale exactly
    /// where the daemon is fastest.
    /// </summary>
    [Test]
    public async Task An_incremental_update_re_reads_the_queue_even_though_it_skipped_the_scan()
    {
        var (session, scan, notifier, incremental, work) = WedgedIncrementalSession();
        await session.CurrentAsync(None);

        work.Operations = 5;
        notifier.RaiseChangedAt("art/hero.png");
        var current = await session.CurrentAsync(None);

        await Assert.That(incremental.Applications).IsEqualTo(1);
        await Assert.That(scan.Scans).IsEqualTo(1);
        await Assert.That(current.UnfinishedOperations).IsEqualTo(5);
    }

    /// <summary>
    /// A reader that cannot pair a rename reports none, and that is not a claim that none was made
    /// — the CLI fallback has no recorded checksum to pair against at all.
    /// </summary>
    [Test]
    public async Task A_session_whose_reader_cannot_pair_renames_reports_none()
    {
        var (session, _, _) = Session();

        await Assert.That((await session.CurrentAsync(None)).UnrecordedMoves).IsEmpty();
    }

    [Test]
    public async Task A_paired_rename_rides_the_scan_that_found_it()
    {
        var (session, _, _, moves) = RenamingSession();
        moves.Moves = [new UnrecordedMove("art/hero.png", "art/protagonist.png")];

        var current = await session.CurrentAsync(None);

        await Assert.That(current.UnrecordedMoves.Count).IsEqualTo(1);
        await Assert.That(current.UnrecordedMoves[0].ToRelPath).IsEqualTo("art/protagonist.png");
    }

    /// <summary>
    /// A warm answer costs nothing, pairs included: re-reading them per request would put a hash of
    /// every size-matched unversioned file on the path the daemon exists to keep at a millisecond.
    /// </summary>
    [Test]
    public async Task A_warm_answer_does_not_pair_renames_again()
    {
        var (session, _, _, moves) = RenamingSession();
        await session.CurrentAsync(None);
        await session.CurrentAsync(None);

        await Assert.That(moves.Reads).IsEqualTo(1);
    }

    [Test]
    public async Task A_rename_made_between_scans_is_found_by_the_next_one()
    {
        var (session, _, notifier, moves) = RenamingSession();
        await session.CurrentAsync(None);

        moves.Moves = [new UnrecordedMove("art/hero.png", "art/protagonist.png")];
        notifier.RaiseChanged();
        var current = await session.CurrentAsync(None);

        await Assert.That(current.UnrecordedMoves.Count).IsEqualTo(1);
        await Assert.That(moves.Reads).IsEqualTo(2);
    }

    /// <summary>
    /// The pairing is against the listing it was given, so it must be given the current one. Handing
    /// it the held entries after an incremental update would pair against nodes that have moved on.
    /// </summary>
    [Test]
    public async Task Pairing_is_handed_the_listing_the_scan_just_produced()
    {
        var (session, scan, _, moves) = RenamingSession();
        scan.Entries = [Entry("art/hero.png"), Entry("art/villain.png")];

        await session.CurrentAsync(None);

        await Assert.That(moves.LastEntries).IsNotNull();
        await Assert.That(moves.LastEntries!.Count).IsEqualTo(2);
    }

    /// <summary>
    /// The incremental path can turn a node Missing, which is one half of a rename made outside SVN
    /// — so it re-pairs even though it skipped the tree walk.
    /// </summary>
    [Test]
    public async Task An_incremental_update_pairs_again_even_though_it_skipped_the_scan()
    {
        var scan = new FakeWorkingCopyScan { Entries = [Entry("art/hero.png")] };
        var notifier = new FakeChangeNotifier();
        var incremental = new FakeIncrementalScan();
        var moves = new FakeUnrecordedMoveScan();
        using var session = new WorkingCopySession(scan, notifier, incremental, null, moves);
        await session.CurrentAsync(None);

        moves.Moves = [new UnrecordedMove("art/hero.png", "art/protagonist.png")];
        notifier.RaiseChangedAt("art/hero.png");
        var current = await session.CurrentAsync(None);

        await Assert.That(incremental.Applications).IsEqualTo(1);
        await Assert.That(scan.Scans).IsEqualTo(1);
        await Assert.That(current.UnrecordedMoves.Count).IsEqualTo(1);
    }

    private static (
        WorkingCopySession Session,
        FakeWorkingCopyScan Scan,
        FakeChangeNotifier Notifier,
        FakeUnrecordedMoveScan Moves
    ) RenamingSession()
    {
        var scan = new FakeWorkingCopyScan();
        var notifier = new FakeChangeNotifier();
        var moves = new FakeUnrecordedMoveScan();
        return (new WorkingCopySession(scan, notifier, null, null, moves), scan, notifier, moves);
    }

    private static (
        WorkingCopySession Session,
        FakeWorkingCopyScan Scan,
        FakeChangeNotifier Notifier
    ) Session()
    {
        var scan = new FakeWorkingCopyScan();
        var notifier = new FakeChangeNotifier();
        return (new WorkingCopySession(scan, notifier), scan, notifier);
    }

    private static (
        WorkingCopySession Session,
        FakeWorkingCopyScan Scan,
        FakeChangeNotifier Notifier,
        FakeIncrementalScan Incremental
    ) IncrementalSession()
    {
        var scan = new FakeWorkingCopyScan { Entries = [Entry("art/hero.png")] };
        var notifier = new FakeChangeNotifier();
        var incremental = new FakeIncrementalScan();
        return (new WorkingCopySession(scan, notifier, incremental), scan, notifier, incremental);
    }

    private static (
        WorkingCopySession Session,
        FakeWorkingCopyScan Scan,
        FakeChangeNotifier Notifier,
        FakeUnfinishedWorkScan Work
    ) WedgedSession()
    {
        var scan = new FakeWorkingCopyScan();
        var notifier = new FakeChangeNotifier();
        var work = new FakeUnfinishedWorkScan();
        return (new WorkingCopySession(scan, notifier, null, work), scan, notifier, work);
    }

    private static (
        WorkingCopySession Session,
        FakeWorkingCopyScan Scan,
        FakeChangeNotifier Notifier,
        FakeIncrementalScan Incremental,
        FakeUnfinishedWorkScan Work
    ) WedgedIncrementalSession()
    {
        var scan = new FakeWorkingCopyScan { Entries = [Entry("art/hero.png")] };
        var notifier = new FakeChangeNotifier();
        var incremental = new FakeIncrementalScan();
        var work = new FakeUnfinishedWorkScan();
        return (
            new WorkingCopySession(scan, notifier, incremental, work),
            scan,
            notifier,
            incremental,
            work
        );
    }

    private static WorkingCopyEntry Entry(string relPath) =>
        new(
            relPath,
            NodeKind.File,
            NodeStatus.Modified,
            PropertyStatus.Unmodified,
            Revision: 1,
            Changelist: null,
            IsConflicted: false,
            HasLockToken: false,
            IsWriteLocked: false,
            IsCopied: false
        );
}
