using Subverted.Core;
using Subverted.Protocol;

namespace Subverted.Cli.Tests;

public sealed class MoveReportTests
{
    private const string Notifications = "A         art/protagonist.png\nD         art/hero.png\n";

    [Test]
    public async Task An_ordinary_rename_prints_svns_own_two_lines_and_nothing_else()
    {
        var lines = MoveReport.Lines(Response(MoveRoute.Ordinary), sourceHeldLock: false);

        await Assert
            .That(lines)
            .IsEquivalentTo(["A         art/protagonist.png", "D         art/hero.png"]);
    }

    /// <summary>
    /// The one the person did not ask for by name: they typed a rename and Subverted recorded one
    /// that had already happened. Saying so is how they learn the history was kept.
    /// </summary>
    [Test]
    public async Task A_rename_recorded_after_the_fact_says_that_is_what_happened()
    {
        var lines = MoveReport.Lines(Response(MoveRoute.AlreadyRenamed), sourceHeldLock: false);

        await Assert.That(lines[0]).Contains("already been made on disk");
        await Assert.That(lines[1]).IsEqualTo("A         art/protagonist.png");
    }

    /// <summary>
    /// Measured: SVN leaves the lock token on the old path, which now exists only to be deleted. So
    /// the file is not locked under its new name and nothing in SVN's output mentions it.
    /// </summary>
    [Test]
    public async Task A_lock_that_did_not_follow_the_file_is_warned_about()
    {
        var lines = MoveReport.Lines(Response(MoveRoute.Ordinary), sourceHeldLock: true);

        await Assert.That(lines[^1]).Contains("lock stayed on the old path");
    }

    [Test]
    public async Task A_file_that_was_not_locked_gets_no_warning()
    {
        var lines = MoveReport.Lines(Response(MoveRoute.Ordinary), sourceHeldLock: false);

        await Assert.That(lines.Any(line => line.Contains("lock"))).IsFalse();
    }

    [Test]
    public async Task A_rename_svn_said_nothing_about_says_so_rather_than_printing_nothing()
    {
        var lines = MoveReport.Lines(
            new MoveResponse(MoveRoute.Ordinary, string.Empty),
            sourceHeldLock: false
        );

        await Assert.That(lines).IsEquivalentTo(["nothing renamed"]);
    }

    private static MoveResponse Response(MoveRoute route) => new(route, Notifications);
}
