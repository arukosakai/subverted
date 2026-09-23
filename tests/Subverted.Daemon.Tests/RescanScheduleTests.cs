namespace Subverted.Daemon.Tests;

/// <summary>
/// The settle rule, without waiting for real seconds. Both halves matter: rescanning too eagerly
/// makes an asset import cost one full tree walk per file, and never rescanning makes "warm" the
/// lucky case instead of the common one.
/// </summary>
public sealed class RescanScheduleTests
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 19, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(500);

    /// <summary>The first sighting starts the clock; it cannot also be the end of it.</summary>
    [Test]
    public async Task A_root_seen_for_the_first_time_is_never_due_on_that_call()
    {
        var schedule = new RescanSchedule(Settle);

        await Assert.That(schedule.IsDue("/wc", 1, isStale: true, Noon)).IsFalse();
    }

    [Test]
    public async Task A_root_that_has_been_quiet_for_the_settle_period_is_due()
    {
        var schedule = new RescanSchedule(Settle);
        schedule.IsDue("/wc", 1, isStale: true, Noon);

        await Assert.That(schedule.IsDue("/wc", 1, isStale: true, Noon + Settle)).IsTrue();
    }

    /// <summary>
    /// The boundary. A tick landing exactly on the settle period is quiet enough — the alternative
    /// is a rescan that waits for the next tick for no reason anyone could state.
    /// </summary>
    [Test]
    [Arguments(499, false)]
    [Arguments(500, true)]
    public async Task The_settle_period_is_inclusive_at_its_edge(int elapsedMs, bool expected)
    {
        var schedule = new RescanSchedule(Settle);
        schedule.IsDue("/wc", 1, isStale: true, Noon);

        await Assert
            .That(schedule.IsDue("/wc", 1, isStale: true, Noon.AddMilliseconds(elapsedMs)))
            .IsEqualTo(expected);
    }

    /// <summary>
    /// The import case. Every write moves the generation, which restarts the clock, so a tree
    /// being written to continuously is never rescanned until the writing stops.
    /// </summary>
    [Test]
    public async Task A_root_that_keeps_changing_never_becomes_due()
    {
        var schedule = new RescanSchedule(Settle);
        var now = Noon;

        for (var generation = 1; generation <= 10; generation++)
        {
            now += TimeSpan.FromMilliseconds(400);
            await Assert.That(schedule.IsDue("/wc", generation, isStale: true, now)).IsFalse();
        }

        // And the moment it stops, one more quiet period is all it takes.
        await Assert.That(schedule.IsDue("/wc", 10, isStale: true, now + Settle)).IsTrue();
    }

    [Test]
    public async Task A_root_whose_scan_still_stands_is_not_due_however_long_it_has_been_quiet()
    {
        var schedule = new RescanSchedule(Settle);
        schedule.IsDue("/wc", 1, isStale: false, Noon);

        await Assert
            .That(schedule.IsDue("/wc", 1, isStale: false, Noon + TimeSpan.FromHours(1)))
            .IsFalse();
    }

    /// <summary>Being quiet is remembered per working copy, not across them.</summary>
    [Test]
    public async Task One_root_going_quiet_does_not_make_another_one_due()
    {
        var schedule = new RescanSchedule(Settle);
        schedule.IsDue("/wc", 1, isStale: true, Noon);

        await Assert.That(schedule.IsDue("/other", 1, isStale: true, Noon + Settle)).IsFalse();
        await Assert.That(schedule.IsDue("/wc", 1, isStale: true, Noon + Settle)).IsTrue();
    }

    /// <summary>
    /// Staying due is correct: the rescan it asks for can fail, and a schedule that forgot would
    /// never ask again.
    /// </summary>
    [Test]
    public async Task A_root_stays_due_until_something_changes_or_the_scan_catches_up()
    {
        var schedule = new RescanSchedule(Settle);
        schedule.IsDue("/wc", 1, isStale: true, Noon);

        await Assert.That(schedule.IsDue("/wc", 1, isStale: true, Noon + Settle)).IsTrue();
        await Assert.That(schedule.IsDue("/wc", 1, isStale: true, Noon + Settle)).IsTrue();
        await Assert.That(schedule.IsDue("/wc", 1, isStale: false, Noon + Settle)).IsFalse();
    }
}
