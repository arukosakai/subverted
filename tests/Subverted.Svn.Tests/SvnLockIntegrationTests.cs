using Subverted.Core;

namespace Subverted.Svn.Tests;

/// <summary>
/// <c>svn lock</c> and <c>svn unlock</c> against a real repository, with a second checkout standing
/// in for the rest of the studio. These pin the thing no documentation settles: <b>both exit zero
/// after refusing every path they were given</b>, so the exit code says the client ran and nothing
/// about whether anybody now holds a lock.
/// </summary>
public sealed class SvnLockIntegrationTests
{
    private static readonly CancellationToken None = CancellationToken.None;
    private static readonly SvnCommand Svn = new("svn");

    [Test]
    public async Task A_lock_is_taken_and_reported()
    {
        using var copy = OneCommit();

        var outcome = await Locks()
            .LockAsync(
                copy.Root,
                [copy.Absolute("art/hero.png")],
                null,
                ForeignLock.Respected,
                None
            );

        await Assert.That(outcome.Refusals).IsEmpty();
        await Assert.That(outcome.Notifications).Contains("locked by user");
        await Assert.That(await IsLockedAsync(copy, "art/hero.png")).IsTrue();
    }

    /// <summary>
    /// The rule the whole design turns on. SVN writes a warning to stderr and locks nothing; 1.8
    /// <b>exits zero</b>, so trusting the exit code opens a file somebody else is already editing,
    /// and 1.14 exits one, so treating that as a failure would lose which paths did lock.
    /// </summary>
    [Test]
    public async Task A_path_somebody_else_holds_is_refused_whatever_the_client_exits_with()
    {
        using var copy = OneCommit();
        using var teammate = copy.AnotherCheckout();
        teammate.Svn("lock", "art/hero.png");

        var outcome = await Locks()
            .LockAsync(
                copy.Root,
                [copy.Absolute("art/hero.png")],
                null,
                ForeignLock.Respected,
                None
            );

        await Assert.That(outcome.Refusals.Count).IsEqualTo(1);
        await Assert.That(outcome.Refusals[0]).Contains("already locked");
        await Assert.That(await IsLockedAsync(copy, "art/hero.png")).IsFalse();
    }

    /// <summary>
    /// A refused path does not stop the others, so the answer is a pair and not a verdict: one
    /// notification and one warning out of one command.
    /// </summary>
    [Test]
    public async Task The_paths_that_could_be_locked_are_locked_and_the_rest_are_refused()
    {
        using var copy = OneCommit();
        using var teammate = copy.AnotherCheckout();
        teammate.Svn("lock", "art/hero.png");

        var outcome = await Locks()
            .LockAsync(
                copy.Root,
                [copy.Absolute("art/hero.png"), copy.Absolute("src/a.txt")],
                null,
                ForeignLock.Respected,
                None
            );

        await Assert.That(outcome.Refusals.Count).IsEqualTo(1);
        await Assert.That(outcome.Refusals[0]).Contains("/art/hero.png");
        await Assert.That(await IsLockedAsync(copy, "src/a.txt")).IsTrue();
        await Assert.That(await IsLockedAsync(copy, "art/hero.png")).IsFalse();
    }

    /// <summary>
    /// A second way to be refused, and the one nobody expects: a lock on a file that has moved on
    /// since this working copy last updated. Also a warning, also exit zero.
    /// </summary>
    [Test]
    public async Task A_lock_on_a_file_this_copy_has_not_caught_up_with_is_refused()
    {
        using var copy = OneCommit();
        using var teammate = copy.AnotherCheckout();
        teammate.Write("art/hero.png", "theirs\n");
        teammate.Svn("commit", "--quiet", "-m", "their re-export");

        var outcome = await Locks()
            .LockAsync(
                copy.Root,
                [copy.Absolute("art/hero.png")],
                null,
                ForeignLock.Respected,
                None
            );

        await Assert.That(outcome.Refusals.Count).IsEqualTo(1);
        await Assert.That(outcome.Refusals[0]).Contains("newer version");
    }

    [Test]
    public async Task A_lock_comment_reaches_the_server()
    {
        using var copy = OneCommit();

        await Locks()
            .LockAsync(
                copy.Root,
                [copy.Absolute("art/hero.png")],
                "retouching the hero",
                ForeignLock.Respected,
                None
            );

        await Assert.That(await InfoAsync(copy, "art/hero.png")).Contains("retouching the hero");
    }

    /// <summary>
    /// Stealing works, and the working copy it was taken from <b>goes on reporting <c>K</c></b>
    /// until it updates. That is not a bug in Subverted and cannot be fixed from this side: the
    /// stale token lives in the other machine's wc.db and only its own update clears it.
    /// </summary>
    [Test]
    public async Task Stealing_takes_a_lock_the_other_checkout_still_believes_it_holds()
    {
        using var copy = OneCommit();
        using var teammate = copy.AnotherCheckout();
        teammate.Svn("lock", "art/hero.png");

        var outcome = await Locks()
            .LockAsync(
                copy.Root,
                [copy.Absolute("art/hero.png")],
                null,
                ForeignLock.Overridden,
                None
            );

        await Assert.That(outcome.Refusals).IsEmpty();
        await Assert.That(await IsLockedAsync(copy, "art/hero.png")).IsTrue();
        await Assert.That(await IsLockedAsync(teammate, "art/hero.png")).IsTrue();
    }

    [Test]
    public async Task Unlocking_gives_the_lock_back()
    {
        using var copy = OneCommit();
        copy.Svn("lock", "art/hero.png");

        var outcome = await Locks()
            .UnlockAsync(copy.Root, [copy.Absolute("art/hero.png")], ForeignLock.Respected, None);

        await Assert.That(outcome.Refusals).IsEmpty();
        await Assert.That(outcome.Notifications).Contains("unlocked");
        await Assert.That(await IsLockedAsync(copy, "art/hero.png")).IsFalse();
    }

    /// <summary>
    /// How somebody finds out their lock was stolen. The token is gone from the server, SVN says so
    /// as a warning — exiting zero on 1.8 and one on 1.14 — and the local token is dropped either way.
    /// </summary>
    [Test]
    public async Task Unlocking_a_lock_that_was_stolen_is_refused_whatever_the_client_exits_with()
    {
        using var copy = OneCommit();
        copy.Svn("lock", "art/hero.png");
        using var teammate = copy.AnotherCheckout();
        teammate.Svn("lock", "--force", "art/hero.png");

        var outcome = await Locks()
            .UnlockAsync(copy.Root, [copy.Absolute("art/hero.png")], ForeignLock.Respected, None);

        await Assert.That(outcome.Refusals.Count).IsEqualTo(1);
        await Assert.That(outcome.Refusals[0]).Contains("No lock on path");
        await Assert.That(await IsLockedAsync(copy, "art/hero.png")).IsFalse();
    }

    /// <summary>
    /// Breaking somebody else's lock from a working copy that never held it. Without
    /// <see cref="ForeignLock.Overridden"/> this is an outright failure, which is the pair of cases
    /// below and the reason the flag exists at all.
    /// </summary>
    [Test]
    public async Task Breaking_releases_a_lock_this_working_copy_never_held()
    {
        using var copy = OneCommit();
        using var teammate = copy.AnotherCheckout();
        teammate.Svn("lock", "art/hero.png");

        var outcome = await Locks()
            .UnlockAsync(copy.Root, [copy.Absolute("art/hero.png")], ForeignLock.Overridden, None);

        await Assert.That(outcome.Refusals).IsEmpty();
        await Assert.That(outcome.Notifications).Contains("unlocked");
    }

    [Test]
    public async Task Unlocking_a_path_this_working_copy_never_locked_fails_outright()
    {
        using var copy = OneCommit();

        await Assert
            .That(async () =>
                await Locks()
                    .UnlockAsync(
                        copy.Root,
                        [copy.Absolute("art/hero.png")],
                        ForeignLock.Respected,
                        None
                    )
            )
            .Throws<SvnCommandException>()
            .WithMessageContaining("is not locked in this working copy");
    }

    /// <summary>
    /// The local check happens before the server is asked, and it is all-or-nothing: one name this
    /// working copy does not hold leaves <em>every</em> lock in the list held. A caller that read
    /// the failure as "some of them went" would be wrong about all of them.
    /// </summary>
    [Test]
    public async Task An_unlock_naming_one_unheld_path_releases_none_of_them()
    {
        using var copy = OneCommit();
        copy.Svn("lock", "src/a.txt");

        await Assert
            .That(async () =>
                await Locks()
                    .UnlockAsync(
                        copy.Root,
                        [copy.Absolute("art/hero.png"), copy.Absolute("src/a.txt")],
                        ForeignLock.Respected,
                        None
                    )
            )
            .Throws<SvnCommandException>();

        await Assert.That(await IsLockedAsync(copy, "src/a.txt")).IsTrue();
    }

    /// <summary>
    /// SVN locks files and only files, and refuses a directory outright rather than locking what is
    /// under it. So <c>sv lock</c> has no working default target — the current directory is the one
    /// guess that cannot work.
    /// </summary>
    [Test]
    public async Task Locking_a_directory_fails_outright()
    {
        using var copy = OneCommit();

        await Assert
            .That(async () =>
                await Locks()
                    .LockAsync(copy.Root, [copy.Absolute("art")], null, ForeignLock.Respected, None)
            )
            .Throws<SvnCommandException>()
            .WithMessageContaining("is not a file");
    }

    [Test]
    public async Task A_lock_that_cannot_reach_the_repository_fails_with_what_svn_said()
    {
        using var copy = OneCommit();
        Directory.Move(copy.RepositoryPath, copy.RepositoryPath + "-gone");

        await Assert
            .That(async () =>
                await Locks()
                    .LockAsync(
                        copy.Root,
                        [copy.Absolute("art/hero.png")],
                        null,
                        ForeignLock.Respected,
                        None
                    )
            )
            .Throws<SvnCommandException>()
            .WithMessageContaining("Unable to connect to a repository");
    }

    /// <summary>
    /// SVN spells its notification paths with the platform's separator, and every RelPath Subverted
    /// reports uses a slash. Two targets in different directories are what makes it print a path
    /// rather than a bare filename — with one target it condenses to the name alone.
    /// </summary>
    [Test]
    public async Task The_paths_svn_announces_are_spelled_the_way_status_spells_them()
    {
        using var copy = OneCommit();

        var outcome = await Locks()
            .LockAsync(
                copy.Root,
                [copy.Absolute("art/hero.png"), copy.Absolute("src/a.txt")],
                null,
                ForeignLock.Respected,
                None
            );

        await Assert.That(outcome.Notifications).Contains("art/hero.png");
        await Assert.That(outcome.Notifications).DoesNotContain(@"art\hero.png");
    }

    private static SvnLockCommand Locks() => new(Svn);

    /// <summary>Whether this working copy holds a lock token — the <c>K</c> of <c>svn status</c>.</summary>
    private static async Task<bool> IsLockedAsync(SvnWorkingCopy copy, string relPath) =>
        (await InfoAsync(copy, relPath)).Contains("Lock Token:", StringComparison.Ordinal);

    private static async Task<string> InfoAsync(SvnWorkingCopy copy, string relPath) =>
        (await Svn.RunAsync(copy.Root, ["info", copy.Absolute(relPath)], None)).StandardOutput;

    private static SvnWorkingCopy OneCommit()
    {
        var copy = SvnWorkingCopy.Create();
        try
        {
            copy.Write("art/hero.png", "ours\n");
            copy.Write("src/a.txt", "ours\n");
            copy.Svn("add", "--quiet", "art", "src");
            copy.Svn("commit", "--quiet", "-m", "first");
            copy.Svn("update", "--quiet");

            return copy;
        }
        catch
        {
            copy.Dispose();
            throw;
        }
    }
}
