using System.Diagnostics;

namespace Subverted.Svn.Tests;

/// <summary>
/// <c>svn cleanup</c> against a real repository. These pin the fact the whole command is built
/// around — <b>cleanup prints nothing whatever it did</b>, on either stream, exiting zero for an
/// unwedged working copy and for a healthy one alike — and the two different states it clears,
/// which behave nothing like each other: a write lock leaves <c>svn status</c> working and printing
/// <c>L</c>, while queued work stops it reading the working copy at all.
/// </summary>
public sealed class SvnCleanupIntegrationTests
{
    private static readonly CancellationToken None = CancellationToken.None;
    private static readonly SvnCommand Svn = new("svn");
    private const int EveryLevel = -1;

    [Test]
    public async Task A_working_copy_with_nothing_wrong_reports_that_there_was_nothing_to_do()
    {
        using var copy = Committed();

        var outcome = await CleanUp(copy);

        await Assert.That(outcome.FoundNothingToDo).IsTrue();
        await Assert.That(outcome.ReleasedWriteLocks).IsEmpty();
        await Assert.That(outcome.FinishedOperations).IsEqualTo(0);
    }

    /// <summary>
    /// The state this command exists for, produced the way a studio produces it: a client that died
    /// mid-commit. The hook is what makes it reproducible rather than a race — the write lock is
    /// taken before the server is contacted, so blocking the hook parks the client holding it.
    /// </summary>
    [Test]
    public async Task A_client_killed_mid_commit_leaves_the_root_locked_and_cleanup_releases_it()
    {
        using var copy = Committed();
        KillAClientMidCommit(copy);

        await Assert
            .That(HeldLocks(copy))
            .IsEquivalentTo(new[] { new WorkingCopyWriteLock(string.Empty, EveryLevel) });

        var outcome = await CleanUp(copy);

        await Assert.That(outcome.ReleasedWriteLocks).IsEquivalentTo(new[] { string.Empty });
        await Assert.That(outcome.RemainingWriteLocks).IsEmpty();
        await Assert.That(HeldLocks(copy)).IsEmpty();
    }

    /// <summary>
    /// Why the lock matters at all. Everything that writes to the working copy fails with
    /// <c>E155004</c> while it stands, and works again the moment it is released.
    /// </summary>
    [Test]
    public async Task Writing_fails_while_the_lock_stands_and_works_once_cleanup_has_run()
    {
        using var copy = Committed();
        WriteLock(copy, string.Empty, EveryLevel);

        var refused = await Svn.RunAsync(copy.Root, ["revert", "src/a.txt"], None);
        await Assert.That(refused.ExitCode).IsNotEqualTo(0);
        await Assert.That(refused.StandardError).Contains("E155004");

        await CleanUp(copy);

        var allowed = await Svn.RunAsync(copy.Root, ["revert", "src/a.txt"], None);
        await Assert.That(allowed.ExitCode).IsEqualTo(0);
    }

    /// <summary>
    /// A write lock does not stop <c>svn status</c>; it shows up in its third column. This is what
    /// <c>sv st</c> has to match, and what it reported as a clean working copy before D25.
    /// </summary>
    [Test]
    public async Task A_write_lock_is_what_svn_status_prints_L_for()
    {
        using var copy = Committed();
        WriteLock(copy, string.Empty, EveryLevel);

        var status = await Svn.RunAsync(copy.Root, ["status"], None);

        await Assert.That(status.ExitCode).IsEqualTo(0);
        await Assert.That(status.StandardOutput).Contains("L     .");
    }

    /// <summary>
    /// The other wedged state, and the worse one: queued work makes <c>svn status</c> itself fail
    /// with <c>E155037</c>, so the person cannot see anything at all until cleanup runs.
    /// </summary>
    [Test]
    public async Task Queued_work_stops_svn_reading_the_copy_and_cleanup_finishes_it()
    {
        using var copy = Committed();
        QueueInterruptedWork(copy);

        var blind = await Svn.RunAsync(copy.Root, ["status"], None);
        await Assert.That(blind.ExitCode).IsNotEqualTo(0);
        await Assert.That(blind.StandardError).Contains("E155037");

        var outcome = await CleanUp(copy);

        await Assert.That(outcome.FinishedOperations).IsEqualTo(1);
        await Assert.That(outcome.FoundNothingToDo).IsFalse();

        var seeing = await Svn.RunAsync(copy.Root, ["status"], None);
        await Assert.That(seeing.ExitCode).IsEqualTo(0);
    }

    /// <summary>
    /// Cleanup is asked for at the root whatever path the caller named, because its two halves scope
    /// differently: measured on 1.8.15, <c>svn cleanup sub</c> drains the whole work queue but
    /// releases only the locks reaching into <c>sub</c> — so a scoped run exits zero having left the
    /// copy locked.
    /// </summary>
    [Test]
    public async Task A_lock_on_a_subtree_is_released_because_the_cleanup_runs_at_the_root()
    {
        using var copy = Committed();
        WriteLock(copy, "src", 0);

        var outcome = await CleanUp(copy);

        await Assert.That(outcome.ReleasedWriteLocks).IsEquivalentTo(new[] { "src" });
        await Assert.That(outcome.RemainingWriteLocks).IsEmpty();
    }

    [Test]
    public async Task Both_wedged_states_at_once_are_cleared_in_one_run()
    {
        using var copy = Committed();
        WriteLock(copy, string.Empty, EveryLevel);
        QueueInterruptedWork(copy);

        var outcome = await CleanUp(copy);

        await Assert.That(outcome.ReleasedWriteLocks).IsEquivalentTo(new[] { string.Empty });
        await Assert.That(outcome.FinishedOperations).IsEqualTo(1);
        await Assert.That(outcome.RemainingWriteLocks).IsEmpty();
    }

    /// <summary>
    /// The read that produces the report comes first, so a path in no working copy fails there
    /// rather than in the client — and fails before anything has been run.
    /// </summary>
    [Test]
    public async Task A_path_in_no_working_copy_is_refused_before_the_client_is_asked()
    {
        using var copy = Committed();
        var outside = Path.Combine(Path.GetTempPath(), $"subverted-absent-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outside);

        try
        {
            await Assert
                .That(async () => await Cleanup().CleanUpAsync(outside, None))
                .Throws<WcDbException>();
        }
        finally
        {
            Directory.Delete(outside);
        }
    }

    /// <summary>
    /// Cleanup can fail, and when it does it is the client failing outright rather than refusing a
    /// path — there is no per-path warning to read the way lock and resolve have. The trigger is a
    /// queued step naming a file with no pristine to install from, which is what a node scheduled
    /// for addition and never committed is.
    /// </summary>
    [Test]
    public async Task A_queued_step_that_cannot_run_fails_the_whole_cleanup()
    {
        using var copy = Committed();
        copy.Write("src/b.txt", "two\n");
        copy.Svn("add", "--quiet", "src/b.txt");
        WorkingCopyWedge.QueueInterruptedWork(copy, "src/b.txt");

        await Assert.That(async () => await CleanUp(copy)).Throws<SvnCommandException>();
    }

    /// <summary>
    /// And it leaves a write lock of its own behind, which is the whole reason the outcome carries
    /// what is still held as well as what went. Seen first by running the shipped binaries.
    /// </summary>
    [Test]
    public async Task A_cleanup_that_fails_part_way_leaves_the_working_copy_locked()
    {
        using var copy = Committed();
        copy.Write("src/b.txt", "two\n");
        copy.Svn("add", "--quiet", "src/b.txt");
        WorkingCopyWedge.QueueInterruptedWork(copy, "src/b.txt");

        try
        {
            await CleanUp(copy);
        }
        catch (SvnCommandException)
        {
            // The failure is the other test's assertion; this one is about what it left behind.
        }

        await Assert.That(HeldLocks(copy)).IsNotEmpty();
    }

    /// <summary>
    /// The silence the whole design turns on, asserted rather than assumed: cleanup writes nothing
    /// to either stream on the run that unwedges a working copy.
    /// </summary>
    [Test]
    public async Task Cleanup_says_nothing_at_all_even_when_it_unwedges_the_working_copy()
    {
        using var copy = Committed();
        WriteLock(copy, string.Empty, EveryLevel);

        var result = await Svn.RunAsync(copy.Root, ["cleanup", "."], None);

        await Assert.That(result.ExitCode).IsEqualTo(0);
        await Assert.That(result.StandardOutput).IsEmpty();
        await Assert.That(result.StandardError).IsEmpty();
        await Assert.That(HeldLocks(copy)).IsEmpty();
    }

    private static SvnCleanupCommand Cleanup() => new(Svn, PendingCleanup.Read);

    private static Task<Core.CleanupOutcome> CleanUp(SvnWorkingCopy copy) =>
        Cleanup().CleanUpAsync(copy.Root, None);

    private static SvnWorkingCopy Committed()
    {
        var copy = SvnWorkingCopy.Create();
        try
        {
            copy.Write("src/a.txt", "one\n");
            copy.Write("art/hero.png", "pixels\n");
            copy.Svn("add", "--quiet", "src", "art");
            copy.Svn("commit", "--quiet", "-m", "r1");
            copy.Svn("update", "--quiet");
            return copy;
        }
        catch
        {
            copy.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Parks a commit in a pre-commit hook that never returns, then kills it. The lock is taken
    /// before the hook runs, so this leaves exactly what a crash leaves and does not race the
    /// commit's own speed.
    /// </summary>
    private static void KillAClientMidCommit(SvnWorkingCopy copy)
    {
        File.WriteAllText(
            Path.Combine(copy.RepositoryPath, "hooks", "pre-commit.bat"),
            "@echo off\r\nping -n 120 127.0.0.1 > nul\r\nexit 0\r\n"
        );
        copy.Write("src/b.txt", "two\n");
        copy.Svn("add", "--quiet", "src/b.txt");

        using var parked = Process.Start(
            new ProcessStartInfo("svn")
            {
                WorkingDirectory = copy.Root,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                ArgumentList = { "commit", "-m", "parked", "--non-interactive" },
            }
        )!;

        try
        {
            // Long enough for the client to take the lock, short enough not to slow the suite; the
            // hook holds it there regardless, so this is not a race against the commit finishing.
            if (parked.WaitForExit(TimeSpan.FromSeconds(3)))
            {
                throw new InvalidOperationException(
                    "The commit finished despite the blocking hook, so nothing was interrupted."
                );
            }
        }
        finally
        {
            parked.Kill(entireProcessTree: true);
            parked.WaitForExit();
        }
    }

    private static void WriteLock(SvnWorkingCopy copy, string relPath, int lockedLevels) =>
        WorkingCopyWedge.TakeWriteLock(copy, relPath, lockedLevels);

    private static void QueueInterruptedWork(SvnWorkingCopy copy) =>
        WorkingCopyWedge.QueueInterruptedWork(copy, "src/a.txt");

    private static IReadOnlyList<WorkingCopyWriteLock> HeldLocks(SvnWorkingCopy copy)
    {
        using var reader = WcDbReader.Open(copy.Root);
        return reader.ReadWriteLocks();
    }
}
