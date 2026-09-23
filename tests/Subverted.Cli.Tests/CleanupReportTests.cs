using Subverted.Protocol;

namespace Subverted.Cli.Tests;

/// <summary>
/// The lines <c>sv cleanup</c> prints. Every one of them is computed rather than passed through:
/// <c>svn cleanup</c> is silent on every outcome, so "unwedged a stuck working copy" and "found
/// nothing wrong" arrive as the same empty output and the same exit code.
/// </summary>
public sealed class CleanupReportTests
{
    [Test]
    public async Task A_healthy_working_copy_says_there_was_nothing_to_clean()
    {
        var lines = CleanupReport.Lines(new CleanupResponse([], 0, []));

        await Assert.That(lines).IsEquivalentTo(new[] { "nothing needed cleaning" });
    }

    [Test]
    public async Task Each_released_lock_is_named_on_its_own_line()
    {
        var lines = CleanupReport.Lines(new CleanupResponse(["art", "sub"], 0, []));

        await Assert.That(lines[0]).IsEqualTo("released  art");
        await Assert.That(lines[1]).IsEqualTo("released  sub");
    }

    /// <summary>
    /// The root's relative path is empty, and the row a crashed client leaves is the root's. Printed
    /// raw it would be a line reading <c>released</c> followed by nothing at all.
    /// </summary>
    [Test]
    public async Task The_working_copy_root_is_named_the_way_svn_status_names_it()
    {
        var lines = CleanupReport.Lines(new CleanupResponse([string.Empty], 0, []));

        await Assert.That(lines[0]).IsEqualTo("released  .");
    }

    [Test]
    public async Task Releasing_locks_says_that_writing_will_work_again()
    {
        var lines = CleanupReport.Lines(new CleanupResponse(["art", "sub"], 0, []));

        await Assert
            .That(lines)
            .Contains("2 path(s) unlocked — writes to this working copy will work again");
        await Assert.That(lines).DoesNotContain("nothing needed cleaning");
    }

    /// <summary>
    /// Its own line rather than folded into the lock count, because this is the state in which
    /// <c>svn status</c> itself was failing — the person has been unable to see anything at all.
    /// </summary>
    [Test]
    public async Task Finishing_interrupted_work_is_reported_separately_from_unlocking()
    {
        var lines = CleanupReport.Lines(new CleanupResponse([], 2, []));

        await Assert.That(lines).Contains("2 interrupted operation(s) finished");
        await Assert.That(lines).DoesNotContain("nothing needed cleaning");
    }

    [Test]
    public async Task A_cleanup_that_did_both_reports_both()
    {
        var lines = CleanupReport.Lines(new CleanupResponse([string.Empty], 3, []));

        await Assert.That(lines[0]).IsEqualTo("released  .");
        await Assert
            .That(lines)
            .Contains("1 path(s) unlocked — writes to this working copy will work again");
        await Assert.That(lines).Contains("3 interrupted operation(s) finished");
    }

    /// <summary>
    /// The one outcome a person has to be told about in capitals: cleanup ran, exited zero, and the
    /// working copy is still stuck.
    /// </summary>
    [Test]
    public async Task A_lock_that_survived_the_cleanup_is_reported_as_still_held()
    {
        var lines = CleanupReport.Lines(new CleanupResponse([], 0, ["art"]));

        await Assert
            .That(lines)
            .Contains(
                "1 path(s) are STILL locked — the working copy is not fixed. "
                    + "Check that no other SVN client is running."
            );
        await Assert.That(lines).DoesNotContain("nothing needed cleaning");
    }

    [Test]
    public async Task Releasing_one_lock_and_failing_another_reports_both()
    {
        var lines = CleanupReport.Lines(new CleanupResponse(["art"], 0, ["sub"]));

        await Assert.That(lines[0]).IsEqualTo("released  art");
        await Assert
            .That(lines)
            .Contains("1 path(s) unlocked — writes to this working copy will work again");
        await Assert
            .That(lines)
            .Contains(
                "1 path(s) are STILL locked — the working copy is not fixed. "
                    + "Check that no other SVN client is running."
            );
    }
}
