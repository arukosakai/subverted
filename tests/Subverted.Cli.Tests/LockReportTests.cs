using Subverted.Protocol;

namespace Subverted.Cli.Tests;

public sealed class LockReportTests
{
    private const string Granted = "'hero.png' locked by user 'ada'.\n";

    private const string AlreadyLocked =
        "svn: warning: W160035: Path '/art/hero.png' is already locked by user 'bob' "
        + "in filesystem 'fd30000e-d860-f740-9f86-e40d00f5e82a'";

    [Test]
    public async Task A_lock_that_was_granted_is_svns_own_line_and_nothing_else()
    {
        var lines = LockReport.Locked(new LockResponse(Granted, []));

        await Assert.That(lines.Count).IsEqualTo(1);
        await Assert.That(lines[0]).IsEqualTo("'hero.png' locked by user 'ada'.");
    }

    /// <summary>
    /// The case the report exists for. SVN prints nothing on stdout for a refused path, so without
    /// the last two lines this is indistinguishable from a lock command with nothing to do.
    /// </summary>
    [Test]
    public async Task A_refused_lock_says_so_rather_than_reading_as_nothing_to_do()
    {
        var lines = LockReport.Locked(new LockResponse(string.Empty, [AlreadyLocked]));

        await Assert.That(lines.Count).IsEqualTo(3);
        await Assert.That(lines[0]).IsEqualTo("nothing locked");
        await Assert.That(lines[1]).IsEqualTo(AlreadyLocked);
        await Assert
            .That(lines[2])
            .IsEqualTo(
                "1 path(s) were NOT locked — see the warning(s) above. "
                    + "Do not start work on them."
            );
    }

    [Test]
    public async Task A_partly_granted_lock_shows_what_went_through_and_what_did_not()
    {
        var lines = LockReport.Locked(new LockResponse(Granted, [AlreadyLocked]));

        await Assert.That(lines.Count).IsEqualTo(3);
        await Assert.That(lines[0]).IsEqualTo("'hero.png' locked by user 'ada'.");
        await Assert.That(lines[2]).StartsWith("1 path(s)");
    }

    [Test]
    public async Task The_count_is_how_many_paths_were_refused()
    {
        var lines = LockReport.Locked(
            new LockResponse(string.Empty, [AlreadyLocked, "svn: warning: W160042: and another"])
        );

        await Assert.That(lines.Count).IsEqualTo(4);
        await Assert.That(lines[3]).StartsWith("2 path(s)");
    }

    [Test]
    public async Task An_unlock_that_went_through_is_svns_own_line_and_nothing_else()
    {
        var lines = LockReport.Unlocked(new UnlockResponse("'hero.png' unlocked.\n", []));

        await Assert.That(lines.Count).IsEqualTo(1);
        await Assert.That(lines[0]).IsEqualTo("'hero.png' unlocked.");
    }

    /// <summary>
    /// An unlock refusal reads differently from a lock one on purpose: nothing was left undone, the
    /// lock had simply already gone — which is how somebody learns theirs was stolen.
    /// </summary>
    [Test]
    public async Task An_unlock_refusal_reports_the_lock_as_already_gone()
    {
        var lines = LockReport.Unlocked(
            new UnlockResponse(string.Empty, ["svn: warning: W160040: No lock on path '/x'"])
        );

        await Assert.That(lines.Count).IsEqualTo(3);
        await Assert.That(lines[0]).IsEqualTo("nothing unlocked");
        await Assert
            .That(lines[2])
            .IsEqualTo("1 path(s) had no lock on the server — see the warning(s) above.");
    }

    [Test]
    public async Task Nothing_at_all_is_reported_as_nothing_rather_than_as_silence()
    {
        await Assert
            .That(LockReport.Locked(new LockResponse(string.Empty, [])))
            .IsEquivalentTo(new[] { "nothing locked" });
        await Assert
            .That(LockReport.Unlocked(new UnlockResponse(string.Empty, [])))
            .IsEquivalentTo(new[] { "nothing unlocked" });
    }
}
