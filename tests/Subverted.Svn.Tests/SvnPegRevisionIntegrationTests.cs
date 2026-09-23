using Subverted.Core;

namespace Subverted.Svn.Tests;

/// <summary>
/// Every command Subverted runs, against a file whose name contains <c>@</c>. <c>svn</c> splits a
/// target at its last <c>@</c> and reads what follows as a revision, so <c>icon@2x.png</c> — what a
/// retina asset is called, and a studio's art folder is full of them — failed the whole command
/// with <c>E200009</c>. One terminator in <see cref="SvnTarget"/> fixes all of them; these say so
/// per command, because the rule is in one place and the damage was in nine.
/// </summary>
public sealed class SvnPegRevisionIntegrationTests
{
    private const string Retina = "art/icon@2x.png";
    private static readonly CancellationToken None = CancellationToken.None;
    private static readonly SvnCommand Svn = new("svn");

    [Test]
    public async Task An_at_sign_in_a_name_does_not_stop_it_being_scheduled_for_add()
    {
        using var copy = Committed();
        copy.Write("art/new@1.png", "pixels\n");

        var scheduled = await new SvnAddCommand(Svn).AddAsync(
            copy.Root,
            [copy.Absolute("art/new@1.png")],
            None
        );

        await Assert.That(scheduled).Contains("art/new@1.png");
    }

    [Test]
    public async Task An_at_sign_in_a_name_does_not_stop_it_reaching_the_server()
    {
        using var copy = Committed();
        copy.Write(Retina, "repainted\n");

        var outcome = await new SvnCommitCommand(Svn).CommitAsync(
            copy.Root,
            [copy.Absolute(Retina)],
            "repaint",
            CommitScope.WholeSubtree,
            None
        );

        await Assert.That(outcome.Revision).IsEqualTo(2L);
        await Assert.That(outcome.Notifications).Contains("art/icon@2x.png");
    }

    /// <summary>
    /// The worst of the nine. A revert that silently refused every target would leave the artist
    /// believing their unwanted change was gone; the fix must be shown to actually restore bytes.
    /// </summary>
    [Test]
    public async Task An_at_sign_in_a_name_does_not_stop_a_change_to_it_being_reverted()
    {
        using var copy = Committed();
        copy.Write(Retina, "wrong\n");

        await new SvnRevertCommand(Svn).RevertAsync(copy.Root, [copy.Absolute(Retina)], None);

        await Assert.That(File.ReadAllText(copy.Absolute(Retina))).IsEqualTo("pixels\n");
    }

    [Test]
    public async Task An_at_sign_in_a_name_does_not_stop_it_being_locked_and_unlocked()
    {
        using var copy = Committed();
        var path = copy.Absolute(Retina);

        var locked = await new SvnLockCommand(Svn).LockAsync(
            copy.Root,
            [path],
            "mine",
            ForeignLock.Respected,
            None
        );
        var unlocked = await new SvnLockCommand(Svn).UnlockAsync(
            copy.Root,
            [path],
            ForeignLock.Respected,
            None
        );

        await Assert.That(locked.Refusals).IsEmpty();
        await Assert.That(locked.Notifications).Contains("locked");
        await Assert.That(unlocked.Refusals).IsEmpty();
        await Assert.That(unlocked.Notifications).Contains("unlocked");
    }

    [Test]
    public async Task An_at_sign_in_a_name_does_not_stop_an_update_bringing_work_down()
    {
        using var copy = Committed();
        using var teammate = copy.AnotherCheckout();
        teammate.Write(Retina, "theirs\n");
        teammate.Svn("commit", "--quiet", "-m", "their repaint");

        var outcome = await new SvnUpdateCommand(Svn).UpdateAsync(
            copy.Root,
            copy.Absolute(Retina),
            None
        );

        await Assert.That(outcome.Revision).IsEqualTo(2L);
        await Assert.That(outcome.Conflicts).IsEqualTo(0);
        await Assert.That(File.ReadAllText(copy.Absolute(Retina))).IsEqualTo("theirs\n");
    }

    [Test]
    public async Task An_at_sign_in_a_name_does_not_stop_its_history_being_read()
    {
        using var copy = Committed();

        var history = await new SvnLogCommand(Svn).ReadAsync(
            copy.Root,
            copy.Absolute(Retina),
            null,
            None
        );

        await Assert.That(history.Count).IsEqualTo(1);
        await Assert.That(history[0].Revision).IsEqualTo(1L);
    }

    [Test]
    public async Task An_at_sign_in_a_name_does_not_stop_a_conflict_on_it_being_resolved()
    {
        using var copy = Committed();
        using var teammate = copy.AnotherCheckout();
        teammate.Write(Retina, "theirs\n");
        teammate.Svn("commit", "--quiet", "-m", "their repaint");
        copy.Write(Retina, "mine\n");
        copy.Svn("update", "--quiet", "--accept", "postpone");

        var outcome = await new SvnResolveCommand(Svn).ResolveAsync(
            copy.Root,
            [copy.Absolute(Retina)],
            ConflictResolution.Mine,
            None
        );

        await Assert.That(outcome.ResolvedPaths).IsEquivalentTo(new[] { Retina });
        await Assert.That(outcome.Refusals).IsEmpty();
        await Assert.That(File.ReadAllText(copy.Absolute(Retina))).IsEqualTo("mine\n");
    }

    /// <summary>
    /// The one that must <em>not</em> be terminated. <c>svn diff</c> reads the whole argument as a
    /// path, so the escape every other subcommand needs makes it report a file that is right there
    /// as unversioned — which is why this asks for the diff's own text and not just for success.
    /// </summary>
    [Test]
    public async Task An_at_sign_in_a_name_does_not_stop_its_local_change_being_diffed()
    {
        using var copy = Committed();
        copy.Write(Retina, "repainted\n");

        var diff = await new SvnDiffCommand(Svn).ReadAsync(copy.Root, copy.Absolute(Retina), None);

        await Assert.That(diff).Contains("-pixels");
        await Assert.That(diff).Contains("+repainted");
    }

    /// <summary>A committed <c>art/icon@2x.png</c>, added through its parent so the fixture's own
    /// <c>svn</c> calls never have to name it.</summary>
    private static SvnWorkingCopy Committed()
    {
        var copy = SvnWorkingCopy.Create();
        try
        {
            copy.Write(Retina, "pixels\n");
            copy.Svn("add", "--quiet", "art");
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
