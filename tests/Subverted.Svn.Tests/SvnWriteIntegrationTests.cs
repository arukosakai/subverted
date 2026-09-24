using Subverted.Core;

namespace Subverted.Svn.Tests;

/// <summary>
/// The three commands that change a working copy, against a real repository. Nothing else can
/// establish that the arguments we pass mean what we think they mean — and these are the commands
/// where being wrong costs somebody their work rather than a wrong number on a screen.
/// </summary>
public sealed class SvnWriteIntegrationTests
{
    private static readonly CancellationToken None = CancellationToken.None;
    private static readonly SvnCommand Svn = new("svn");

    [Test]
    public async Task A_new_file_is_scheduled_and_then_reaches_the_server()
    {
        using var copy = OneCommit();
        copy.Write("src/b.txt", "new\n");

        var scheduled = await new SvnAddCommand(Svn).AddAsync(
            copy.Root,
            [copy.Absolute("src/b.txt")],
            None
        );
        var outcome = await new SvnCommitCommand(Svn).CommitAsync(
            copy.Root,
            [copy.Root],
            "add b",
            CommitScope.WholeSubtree,
            None
        );

        await Assert.That(scheduled).Contains("b.txt");
        await Assert.That(outcome.Revision).IsEqualTo(2L);
        await Assert.That(outcome.Notifications).Contains("b.txt");
    }

    /// <summary>
    /// <c>svn add</c> repeats the target back verbatim, unlike <c>svn diff</c>. Found by running
    /// it: a backslash here puts <c>A  src\b.txt</c> next to a <c>sv st</c> saying <c>src/b.txt</c>,
    /// and the studio ends up with two spellings of one path.
    /// </summary>
    [Test]
    public async Task The_path_svn_echoes_back_is_spelled_the_way_status_spells_it()
    {
        using var copy = OneCommit();
        copy.Write("src/b.txt", "new\n");

        var scheduled = await new SvnAddCommand(Svn).AddAsync(
            copy.Root,
            [copy.Absolute("src/b.txt")],
            None
        );

        await Assert.That(scheduled).Contains("src/b.txt");
        await Assert.That(scheduled).DoesNotContain(@"src\b.txt");
    }

    /// <summary>
    /// <c>--parents</c> is why adding <c>art/hero/final.png</c> under an unversioned <c>art/hero</c>
    /// works. Without it SVN refuses the whole command and nothing is scheduled.
    /// </summary>
    [Test]
    public async Task A_file_under_an_unversioned_directory_brings_its_parents_with_it()
    {
        using var copy = OneCommit();
        copy.Write("art/hero/final.png", "pixels\n");

        var scheduled = await new SvnAddCommand(Svn).AddAsync(
            copy.Root,
            [copy.Absolute("art/hero/final.png")],
            None
        );

        await Assert.That(scheduled).Contains("art");
        await Assert.That(scheduled).Contains("final.png");
    }

    [Test]
    public async Task Reverting_a_modified_file_puts_its_committed_content_back()
    {
        using var copy = OneCommit();
        copy.Write("src/a.txt", "ruined\n");

        var reverted = await new SvnRevertCommand(Svn).RevertAsync(
            copy.Root,
            [copy.Absolute("src/a.txt")],
            None
        );

        await Assert.That(reverted).Contains("a.txt");
        await Assert.That(File.ReadAllText(copy.Absolute("src/a.txt"))).IsEqualTo("one\n");
    }

    /// <summary>
    /// SVN reverts the named node only unless told otherwise, so a caller naming a directory would
    /// get nothing back. Infinite depth is what makes <c>sv revert src</c> mean what it looks like.
    /// </summary>
    [Test]
    public async Task Reverting_a_directory_reaches_the_files_inside_it()
    {
        using var copy = OneCommit();
        copy.Write("src/a.txt", "ruined\n");

        await new SvnRevertCommand(Svn).RevertAsync(copy.Root, [copy.Absolute("src")], None);

        await Assert.That(File.ReadAllText(copy.Absolute("src/a.txt"))).IsEqualTo("one\n");
    }

    /// <summary>
    /// The premise of file-level staging: naming one path sends that path and leaves the rest
    /// local. If this were not true, partial commits would be impossible to build on top of.
    /// </summary>
    [Test]
    public async Task Committing_one_path_leaves_the_other_ones_changes_where_they_were()
    {
        using var copy = OneCommit();
        copy.Write("src/a.txt", "sent\n");
        copy.Write("src/b.txt", "kept\n");
        copy.Svn("add", "--quiet", "src/b.txt");

        var outcome = await new SvnCommitCommand(Svn).CommitAsync(
            copy.Root,
            [copy.Absolute("src/a.txt")],
            "only a",
            CommitScope.WholeSubtree,
            None
        );
        var remaining = await new SvnDiffCommand(Svn).ReadAsync(copy.Root, copy.Root, None);

        await Assert.That(outcome.Revision).IsEqualTo(2L);
        await Assert.That(outcome.Notifications).Contains("a.txt");
        await Assert.That(outcome.Notifications).DoesNotContain("b.txt");
        await Assert.That(remaining).Contains("b.txt");
    }

    /// <summary>
    /// SVN exits zero and says nothing when there is nothing to send. Treating that as a failure
    /// would make <c>sv commit</c> cry wolf on every no-op.
    /// </summary>
    [Test]
    public async Task A_commit_with_nothing_to_send_succeeds_without_a_revision()
    {
        using var copy = OneCommit();

        var outcome = await new SvnCommitCommand(Svn).CommitAsync(
            copy.Root,
            [copy.Root],
            "nothing",
            CommitScope.WholeSubtree,
            None
        );

        await Assert.That(outcome.Revision).IsNull();
    }

    [Test]
    public async Task Adding_a_path_that_is_already_versioned_fails_with_what_svn_said()
    {
        using var copy = OneCommit();

        await Assert
            .That(async () =>
                await new SvnAddCommand(Svn).AddAsync(copy.Root, [copy.Absolute("src/a.txt")], None)
            )
            .Throws<SvnCommandException>()
            .WithMessageContaining("already under version control");
    }

    [Test]
    public async Task Adding_a_path_that_is_not_there_fails_rather_than_reporting_nothing()
    {
        using var copy = OneCommit();

        await Assert
            .That(async () =>
                await new SvnAddCommand(Svn).AddAsync(copy.Root, [copy.Absolute("ghost")], None)
            )
            .Throws<SvnCommandException>();
    }

    /// <summary>
    /// Reverting a path with nothing to restore is not an error — <c>sv revert</c> on a clean tree
    /// has to be a no-op rather than something the user has to think about.
    /// </summary>
    [Test]
    public async Task Reverting_a_clean_path_says_nothing_and_does_not_fail()
    {
        using var copy = OneCommit();

        var reverted = await new SvnRevertCommand(Svn).RevertAsync(
            copy.Root,
            [copy.Absolute("src/a.txt")],
            None
        );

        await Assert.That(reverted).IsEmpty();
    }

    /// <summary>
    /// Revert restores versioned nodes and leaves the rest alone. An artist's unsaved export
    /// sitting in the tree must survive a revert of the directory around it.
    /// </summary>
    [Test]
    public async Task Reverting_a_directory_does_not_delete_the_unversioned_files_in_it()
    {
        using var copy = OneCommit();
        copy.Write("src/scratch.txt", "not committed anywhere\n");

        await new SvnRevertCommand(Svn).RevertAsync(copy.Root, [copy.Absolute("src")], None);

        await Assert.That(File.Exists(copy.Absolute("src/scratch.txt"))).IsTrue();
    }

    /// <summary>
    /// The whole point of <see cref="CommitScope.ExactlyTheseNodes"/>. A directory named in a
    /// picked set carries its own property change and nothing else — the default would sweep up the
    /// children whose owner had just declined them.
    /// </summary>
    [Test]
    public async Task A_directory_committed_exactly_sends_its_own_properties_and_not_its_children()
    {
        using var copy = OneCommit();
        copy.Write("src/b.txt", "kept\n");
        copy.Svn("add", "--quiet", "src/b.txt");
        copy.Svn("propset", "--quiet", "custom:x", "1", "src");

        var outcome = await new SvnCommitCommand(Svn).CommitAsync(
            copy.Root,
            [copy.Absolute("src")],
            "the directory alone",
            CommitScope.ExactlyTheseNodes,
            None
        );

        await Assert.That(outcome.Revision).IsEqualTo(2L);
        await Assert.That(outcome.Notifications).DoesNotContain("b.txt");
    }

    /// <summary>
    /// The same directory, committed the way <c>sv commit</c> means it, takes the child with it.
    /// Both halves are here because one without the other proves nothing about which flag did it.
    /// </summary>
    [Test]
    public async Task The_same_directory_committed_as_a_subtree_takes_its_children()
    {
        using var copy = OneCommit();
        copy.Write("src/b.txt", "kept\n");
        copy.Svn("add", "--quiet", "src/b.txt");
        copy.Svn("propset", "--quiet", "custom:x", "1", "src");

        var outcome = await new SvnCommitCommand(Svn).CommitAsync(
            copy.Root,
            [copy.Absolute("src")],
            "the directory and what is under it",
            CommitScope.WholeSubtree,
            None
        );

        await Assert.That(outcome.Revision).IsEqualTo(2L);
        await Assert.That(outcome.Notifications).Contains("b.txt");
    }

    /// <summary>
    /// SVN refuses the whole commit when a child's added parent is not in it (E200009), so the
    /// picker cannot offer such a child once its directory has been declined.
    /// </summary>
    [Test]
    public async Task A_child_of_an_added_directory_cannot_be_committed_without_that_directory()
    {
        using var copy = OneCommit();
        copy.Write("fresh/n.txt", "new\n");
        copy.Svn("add", "--quiet", "fresh");

        await Assert
            .That(async () =>
                await new SvnCommitCommand(Svn).CommitAsync(
                    copy.Root,
                    [copy.Absolute("fresh/n.txt")],
                    "the child on its own",
                    CommitScope.ExactlyTheseNodes,
                    None
                )
            )
            .Throws<SvnCommandException>()
            .WithMessageContaining("is not part of the commit");
    }

    /// <summary>
    /// Named together they go together, and the sibling nobody picked stays local. This is the
    /// shape every picked set has, so it is the one that has to hold.
    /// </summary>
    [Test]
    public async Task An_added_directory_and_one_child_commit_together_while_the_other_stays()
    {
        using var copy = OneCommit();
        copy.Write("fresh/n.txt", "sent\n");
        copy.Write("fresh/m.txt", "kept\n");
        copy.Svn("add", "--quiet", "fresh");

        var outcome = await new SvnCommitCommand(Svn).CommitAsync(
            copy.Root,
            [copy.Absolute("fresh"), copy.Absolute("fresh/n.txt")],
            "the directory and one child",
            CommitScope.ExactlyTheseNodes,
            None
        );

        await Assert.That(outcome.Revision).IsEqualTo(2L);
        await Assert.That(outcome.Notifications).Contains("n.txt");
        await Assert.That(outcome.Notifications).DoesNotContain("m.txt");
    }

    /// <summary>
    /// A commit releases every lock token its walk meets, sent or not. Scoped exactly, the walk is
    /// the named nodes, so a lock taken before starting survives a commit that names its folder.
    /// </summary>
    [Test]
    public async Task Committed_exactly_a_lock_on_an_unnamed_file_is_kept_even_when_its_folder_is_named()
    {
        using var copy = OneCommit();
        copy.Write("src/b.txt", "two\n");
        copy.Svn("add", "--quiet", "src/b.txt");
        copy.Svn("commit", "--quiet", "-m", "second");
        copy.Svn("update", "--quiet");
        copy.Svn("lock", "src/b.txt");
        copy.Write("src/a.txt", "edited\n");
        copy.Svn("propset", "--quiet", "custom:x", "1", "src");

        await new SvnCommitCommand(Svn).CommitAsync(
            copy.Root,
            [copy.Absolute("src"), copy.Absolute("src/a.txt")],
            "the edit and the folder",
            CommitScope.ExactlyTheseNodes,
            None
        );

        await Assert.That(await HoldsLockAsync(copy, "src/b.txt")).IsTrue();
    }

    /// <summary>The other half: the same commit as a subtree walks into the file and releases it.</summary>
    [Test]
    public async Task Committed_as_a_subtree_a_lock_on_an_unchanged_file_beneath_is_released()
    {
        using var copy = OneCommit();
        copy.Write("src/b.txt", "two\n");
        copy.Svn("add", "--quiet", "src/b.txt");
        copy.Svn("commit", "--quiet", "-m", "second");
        copy.Svn("update", "--quiet");
        copy.Svn("lock", "src/b.txt");
        copy.Write("src/a.txt", "edited\n");

        await new SvnCommitCommand(Svn).CommitAsync(
            copy.Root,
            [copy.Absolute("src")],
            "the whole folder",
            CommitScope.WholeSubtree,
            None
        );

        await Assert.That(await HoldsLockAsync(copy, "src/b.txt")).IsFalse();
    }

    /// <summary>A locked file that is itself committed gives its lock up, as TortoiseSVN's does.</summary>
    [Test]
    public async Task Committed_exactly_a_locked_edited_file_that_is_named_releases_its_lock()
    {
        using var copy = OneCommit();
        copy.Svn("lock", "src/a.txt");
        copy.Write("src/a.txt", "edited\n");

        await new SvnCommitCommand(Svn).CommitAsync(
            copy.Root,
            [copy.Absolute("src/a.txt")],
            "the locked edit",
            CommitScope.ExactlyTheseNodes,
            None
        );

        await Assert.That(await HoldsLockAsync(copy, "src/a.txt")).IsFalse();
    }

    /// <summary>
    /// A deletion is recorded on the directory, so naming the directory removes the subtree even
    /// scoped exactly — and naming a child alone sends nothing. Either way the children are not a
    /// choice, which is why the picker does not present them as one.
    /// </summary>
    [Test]
    public async Task A_deleted_directory_takes_its_subtree_and_a_deleted_child_alone_sends_nothing()
    {
        using var copy = OneCommit();
        copy.Svn("delete", "--quiet", "src");

        var childAlone = await new SvnCommitCommand(Svn).CommitAsync(
            copy.Root,
            [copy.Absolute("src/a.txt")],
            "the child's deletion alone",
            CommitScope.ExactlyTheseNodes,
            None
        );
        var directory = await new SvnCommitCommand(Svn).CommitAsync(
            copy.Root,
            [copy.Absolute("src")],
            "the directory's deletion",
            CommitScope.ExactlyTheseNodes,
            None
        );

        await Assert.That(childAlone.Revision).IsNull();
        await Assert.That(directory.Revision).IsEqualTo(2L);
        await Assert.That(Directory.Exists(copy.Absolute("src"))).IsFalse();
    }

    private static async Task<bool> HoldsLockAsync(SvnWorkingCopy copy, string relPath) =>
        (await Svn.RunAsync(copy.Root, ["info", copy.Absolute(relPath)], None)).StandardOutput.Contains(
            "Lock Token:",
            StringComparison.Ordinal
        );

    private static SvnWorkingCopy OneCommit()
    {
        var copy = SvnWorkingCopy.Create();
        try
        {
            copy.Write("src/a.txt", "one\n");
            copy.Svn("add", "--quiet", "src");
            copy.Svn("commit", "--quiet", "-m", "first");

            // svn commit leaves the root at the revision before it, and the next commit on a path
            // under an out-of-date root is refused.
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
