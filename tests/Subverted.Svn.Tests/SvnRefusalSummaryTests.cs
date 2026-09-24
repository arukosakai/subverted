namespace Subverted.Svn.Tests;

/// <summary>
/// svn 1.14 exits one after <c>lock</c> or <c>unlock</c> refuses any path, where 1.8.15 exits zero,
/// and closes the same warnings with one summary error. Every blob is what 1.14.5 wrote to stderr.
/// </summary>
public sealed class SvnRefusalSummaryTests
{
    private const string AlreadyLocked =
        "svn: warning: W160035: Path '/art/hero.png' is already locked by user 'rena' "
        + "in filesystem 'fd30000e-d860-f740-9f86-e40d00f5e82a'\r\n";

    private const string NoLockThere =
        "svn: warning: W160040: No lock on path '/art/hero.png' "
        + "in filesystem 'fd30000e-d860-f740-9f86-e40d00f5e82a'\r\n";

    private const string LocksNotObtained =
        "svn: E200009: One or more locks could not be obtained\r\n";

    private const string LocksNotReleased =
        "svn: E200009: One or more locks could not be released\r\n";

    [Test]
    public async Task A_refused_lock_closed_by_its_summary_is_only_refusals()
    {
        await Assert
            .That(SvnRefusalSummary.IsAllThatFailed(AlreadyLocked + LocksNotObtained))
            .IsTrue();
    }

    [Test]
    public async Task A_refused_unlock_closed_by_its_summary_is_only_refusals()
    {
        await Assert
            .That(SvnRefusalSummary.IsAllThatFailed(NoLockThere + LocksNotReleased))
            .IsTrue();
    }

    [Test]
    public async Task Several_refusals_under_one_summary_are_only_refusals()
    {
        var standardError =
            AlreadyLocked + AlreadyLocked.Replace("hero", "villain") + LocksNotObtained;

        await Assert.That(SvnRefusalSummary.IsAllThatFailed(standardError)).IsTrue();
    }

    /// <summary>unlock's local check fails the whole command before the server is asked.</summary>
    [Test]
    public async Task An_error_with_no_warning_before_it_is_a_failure()
    {
        var standardError =
            "svn: E195013: 'C:\\wc\\art\\hero.png' is not locked in this working copy\r\n";

        await Assert.That(SvnRefusalSummary.IsAllThatFailed(standardError)).IsFalse();
    }

    /// <summary>A client killed or crashed says nothing, and that must not read as "only refused".</summary>
    [Test]
    public async Task Nothing_on_stderr_is_a_failure()
    {
        await Assert.That(SvnRefusalSummary.IsAllThatFailed(string.Empty)).IsFalse();
    }

    /// <summary>The summary alone names no path, so there would be nothing to report as refused.</summary>
    [Test]
    public async Task A_summary_with_no_warning_is_a_failure()
    {
        await Assert.That(SvnRefusalSummary.IsAllThatFailed(LocksNotObtained)).IsFalse();
    }

    [Test]
    public async Task A_second_error_beside_the_summary_is_a_failure()
    {
        var standardError =
            AlreadyLocked
            + "svn: E170013: Unable to connect to a repository at URL 'svn://example/repo'\r\n"
            + LocksNotObtained;

        await Assert.That(SvnRefusalSummary.IsAllThatFailed(standardError)).IsFalse();
    }

    [Test]
    public async Task Warnings_with_no_summary_are_a_failure_when_svn_exited_non_zero()
    {
        await Assert.That(SvnRefusalSummary.IsAllThatFailed(AlreadyLocked)).IsFalse();
    }
}
