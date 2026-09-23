namespace Subverted.Svn.Tests;

/// <summary>
/// What a cleanup did, worked out by comparing wc.db on both sides of the run. This is the entire
/// report: <c>svn cleanup</c> writes nothing to either stream and exits zero whether it unwedged a
/// working copy or found nothing wrong, so there is no output to parse and nothing else to go on.
/// </summary>
public sealed class CleanupEffectTests
{
    private const int EveryLevel = -1;

    [Test]
    public async Task A_healthy_working_copy_reports_that_there_was_nothing_to_do()
    {
        var effect = CleanupEffect.Of(PendingCleanup.Nothing, PendingCleanup.Nothing);

        await Assert.That(effect.ReleasedWriteLocks).IsEmpty();
        await Assert.That(effect.FinishedOperations).IsEqualTo(0);
        await Assert.That(effect.RemainingWriteLocks).IsEmpty();
        await Assert.That(effect.FoundNothingToDo).IsTrue();
    }

    [Test]
    public async Task A_lock_that_is_gone_afterwards_was_released()
    {
        var effect = CleanupEffect.Of(
            new PendingCleanup([new WorkingCopyWriteLock(string.Empty, EveryLevel)], 0),
            PendingCleanup.Nothing
        );

        await Assert.That(effect.ReleasedWriteLocks).IsEquivalentTo(new[] { string.Empty });
        await Assert.That(effect.RemainingWriteLocks).IsEmpty();
        await Assert.That(effect.FoundNothingToDo).IsFalse();
    }

    /// <summary>
    /// The case that stops a failed cleanup reading as a successful one. A lock in both readings did
    /// not go anywhere, and counting it as released would say the copy was fixed when it is not.
    /// </summary>
    [Test]
    public async Task A_lock_still_held_afterwards_is_remaining_and_never_released()
    {
        var stuck = new PendingCleanup([new WorkingCopyWriteLock("art", 0)], 0);

        var effect = CleanupEffect.Of(stuck, stuck);

        await Assert.That(effect.ReleasedWriteLocks).IsEmpty();
        await Assert.That(effect.RemainingWriteLocks).IsEquivalentTo(new[] { "art" });
        await Assert.That(effect.FoundNothingToDo).IsFalse();
    }

    [Test]
    public async Task Releasing_one_of_two_locks_reports_both_halves()
    {
        var effect = CleanupEffect.Of(
            new PendingCleanup(
                [new WorkingCopyWriteLock("art", 0), new WorkingCopyWriteLock("sub", 0)],
                0
            ),
            new PendingCleanup([new WorkingCopyWriteLock("sub", 0)], 0)
        );

        await Assert.That(effect.ReleasedWriteLocks).IsEquivalentTo(new[] { "art" });
        await Assert.That(effect.RemainingWriteLocks).IsEquivalentTo(new[] { "sub" });
    }

    [Test]
    public async Task The_queued_steps_that_are_gone_afterwards_are_the_ones_that_ran()
    {
        var effect = CleanupEffect.Of(new PendingCleanup([], 3), new PendingCleanup([], 0));

        await Assert.That(effect.FinishedOperations).IsEqualTo(3);
        await Assert.That(effect.FoundNothingToDo).IsFalse();
    }

    [Test]
    public async Task Queued_steps_left_behind_do_not_count_as_finished()
    {
        var effect = CleanupEffect.Of(new PendingCleanup([], 5), new PendingCleanup([], 2));

        await Assert.That(effect.FinishedOperations).IsEqualTo(3);
    }

    /// <summary>
    /// Another client can queue work while this one cleans. That is a race, not a negative number of
    /// finished operations, and reporting "-2 interrupted operations finished" would be nonsense.
    /// </summary>
    [Test]
    public async Task More_queued_work_afterwards_than_before_reports_none_finished()
    {
        var effect = CleanupEffect.Of(new PendingCleanup([], 1), new PendingCleanup([], 3));

        await Assert.That(effect.FinishedOperations).IsEqualTo(0);
    }

    [Test]
    public async Task Released_and_remaining_are_both_ordered_so_the_report_is_stable()
    {
        var effect = CleanupEffect.Of(
            new PendingCleanup(
                [
                    new WorkingCopyWriteLock("sub", 0),
                    new WorkingCopyWriteLock("art", 0),
                    new WorkingCopyWriteLock("zed", 0),
                    new WorkingCopyWriteLock("mid", 0),
                ],
                0
            ),
            new PendingCleanup(
                [new WorkingCopyWriteLock("zed", 0), new WorkingCopyWriteLock("mid", 0)],
                0
            )
        );

        await Assert.That(effect.ReleasedWriteLocks).IsEquivalentTo(new[] { "art", "sub" });
        await Assert.That(effect.RemainingWriteLocks).IsEquivalentTo(new[] { "mid", "zed" });
    }

    /// <summary>
    /// A lock released and queued work run in the same cleanup — the state a killed client during a
    /// commit leaves, and the one this command exists for.
    /// </summary>
    [Test]
    public async Task Both_halves_of_a_wedged_working_copy_are_reported_together()
    {
        var effect = CleanupEffect.Of(
            new PendingCleanup([new WorkingCopyWriteLock(string.Empty, EveryLevel)], 2),
            PendingCleanup.Nothing
        );

        await Assert.That(effect.ReleasedWriteLocks).IsEquivalentTo(new[] { string.Empty });
        await Assert.That(effect.FinishedOperations).IsEqualTo(2);
        await Assert.That(effect.FoundNothingToDo).IsFalse();
    }
}
