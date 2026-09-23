using Subverted.Core;

namespace Subverted.Svn.Tests;

/// <summary>
/// Renaming and deleting against a real repository. Every claim here was measured before it was
/// coded: SVN reports three quite different refusals as one unhelpful message, and half-applies a
/// move whose source is gone.
/// </summary>
public sealed class SvnMoveIntegrationTests
{
    private static readonly CancellationToken None = CancellationToken.None;
    private static readonly SvnCommand Svn = new("svn");

    [Test]
    public async Task A_rename_schedules_the_new_name_and_deletes_the_old()
    {
        using var copy = Committed(("art/hero.png", "pixels"));

        var outcome = await Move(copy, "art/hero.png", "art/protagonist.png");

        await Assert.That(outcome.Route).IsEqualTo(MoveRoute.Ordinary);
        await Assert.That(outcome.Notifications).Contains("art/protagonist.png");
        await Assert.That(outcome.Notifications).Contains("art/hero.png");
        await Assert.That(File.Exists(copy.Absolute("art/protagonist.png"))).IsTrue();
        await Assert.That(File.Exists(copy.Absolute("art/hero.png"))).IsFalse();
    }

    /// <summary>
    /// SVN carries local modifications through a move. Worth pinning: if it reverted them instead,
    /// renaming a file you had been working on all morning would quietly undo the morning.
    /// </summary>
    [Test]
    public async Task A_rename_carries_local_modifications_to_the_new_name()
    {
        using var copy = Committed(("art/hero.png", "pixels"));
        copy.Write("art/hero.png", "pixels and my edits");

        await Move(copy, "art/hero.png", "art/protagonist.png");

        await Assert
            .That(File.ReadAllText(copy.Absolute("art/protagonist.png")))
            .IsEqualTo("pixels and my edits");
    }

    [Test]
    public async Task A_rename_onto_an_existing_file_is_refused_without_touching_either()
    {
        using var copy = Committed(("art/hero.png", "one"), ("art/villain.png", "two"));

        var outcome = await Move(copy, "art/hero.png", "art/villain.png");

        await Assert.That(outcome.Route).IsEqualTo(MoveRoute.DestinationOccupied);
        await Assert.That(outcome.Notifications).IsEmpty();
        await Assert.That(File.ReadAllText(copy.Absolute("art/hero.png"))).IsEqualTo("one");
        await Assert.That(File.ReadAllText(copy.Absolute("art/villain.png"))).IsEqualTo("two");
    }

    [Test]
    public async Task A_rename_of_nothing_is_refused()
    {
        using var copy = Committed(("art/hero.png", "pixels"));

        var outcome = await Move(copy, "art/absent.png", "art/also-absent.png");

        await Assert.That(outcome.Route).IsEqualTo(MoveRoute.NothingAtSource);
        await Assert.That(outcome.Notifications).IsEmpty();
    }

    /// <summary>
    /// The state a file manager leaves: nothing at the source, the content at the destination.
    /// <c>svn move</c> exits 1 here and leaves *both* paths missing, so this route never reaches it.
    /// </summary>
    [Test]
    public async Task A_rename_already_made_on_disk_is_recorded_after_the_fact()
    {
        using var copy = Committed(("art/hero.png", "pixels"));
        File.Move(copy.Absolute("art/hero.png"), copy.Absolute("art/protagonist.png"));

        var outcome = await Move(copy, "art/hero.png", "art/protagonist.png");

        await Assert.That(outcome.Route).IsEqualTo(MoveRoute.AlreadyRenamed);
        await Assert.That(outcome.Notifications).Contains("art/protagonist.png");
        await Assert.That(StatusOf(copy, "art/protagonist.png")).IsEqualTo(NodeStatus.Added);
        await Assert.That(StatusOf(copy, "art/hero.png")).IsEqualTo(NodeStatus.Deleted);
    }

    /// <summary>
    /// The repair hands SVN the person's own file at the old path and lets <c>svn move</c> carry it,
    /// so a rename made *and* edited outside SVN keeps the edit — nothing is restored in between.
    /// </summary>
    [Test]
    public async Task Recording_a_rename_made_on_disk_keeps_the_bytes_that_were_there()
    {
        using var copy = Committed(("art/hero.png", "pixels"));
        File.Move(copy.Absolute("art/hero.png"), copy.Absolute("art/protagonist.png"));
        copy.Write("art/protagonist.png", "pixels and my edits");

        await Move(copy, "art/hero.png", "art/protagonist.png");

        await Assert
            .That(File.ReadAllText(copy.Absolute("art/protagonist.png")))
            .IsEqualTo("pixels and my edits");
    }

    [Test]
    public async Task Recording_a_rename_made_on_disk_leaves_nothing_at_the_old_path()
    {
        using var copy = Committed(("art/hero.png", "pixels"));
        File.Move(copy.Absolute("art/hero.png"), copy.Absolute("art/protagonist.png"));

        await Move(copy, "art/hero.png", "art/protagonist.png");

        await Assert.That(File.Exists(copy.Absolute("art/hero.png"))).IsFalse();
        await Assert.That(Directory.GetFiles(copy.Absolute("art"))).Count().IsEqualTo(1);
    }

    /// <summary>
    /// Inside a copy not yet committed. The first repair restored the source with <c>svn revert</c>,
    /// which refuses a node inside a copy (<c>E155038</c>) and unschedules a copy's own root; handing
    /// SVN the person's file instead works for both, measured on 1.8.15.
    /// </summary>
    [Test]
    [Arguments("gfx/tree.png", "gfx/oak.png")]
    [Arguments("tree-copy.png", "oak-copy.png")]
    public async Task A_rename_inside_an_uncommitted_copy_is_recorded_after_the_fact(
        string source,
        string destination
    )
    {
        using var copy = Committed(("art/tree.png", "bark"));
        copy.Svn("copy", "--quiet", "art", "gfx");
        copy.Svn("copy", "--quiet", "art/tree.png", "tree-copy.png");
        File.Move(copy.Absolute(source), copy.Absolute(destination));

        var outcome = await Move(copy, source, destination);

        await Assert.That(outcome.Route).IsEqualTo(MoveRoute.AlreadyRenamed);
        await Assert.That(StatusOf(copy, destination)).IsEqualTo(NodeStatus.Added);
        await Assert.That(File.ReadAllText(copy.Absolute(destination))).IsEqualTo("bark");
    }

    /// <summary>
    /// Moved into another folder and the old folder deleted. The source's parent is recreated so
    /// SVN has somewhere to find the file, and removed again afterwards: the artist deleted it, and
    /// whether that deletion is recorded is theirs to decide, not the repair's.
    /// </summary>
    [Test]
    public async Task A_rename_out_of_a_deleted_folder_is_recorded_without_bringing_the_folder_back()
    {
        using var copy = Committed(("old/deep/solo.png", "lonely"), ("sprites/keep.txt", "x"));
        File.Move(copy.Absolute("old/deep/solo.png"), copy.Absolute("sprites/solo.png"));
        Directory.Delete(copy.Absolute("old"), recursive: true);

        var outcome = await Move(copy, "old/deep/solo.png", "sprites/solo.png");

        await Assert.That(outcome.Route).IsEqualTo(MoveRoute.AlreadyRenamed);
        await Assert.That(StatusOf(copy, "sprites/solo.png")).IsEqualTo(NodeStatus.Added);
        await Assert.That(Directory.Exists(copy.Absolute("old"))).IsFalse();
    }

    /// <summary>
    /// The negative side: a parent that was there before the repair is not the repair's to remove,
    /// even though the move leaves it empty.
    /// </summary>
    [Test]
    public async Task A_folder_the_repair_did_not_create_stays_though_the_rename_empties_it()
    {
        using var copy = Committed(("old/solo.png", "lonely"), ("sprites/keep.txt", "x"));
        File.Move(copy.Absolute("old/solo.png"), copy.Absolute("sprites/solo.png"));

        await Move(copy, "old/solo.png", "sprites/solo.png");

        await Assert.That(Directory.Exists(copy.Absolute("old"))).IsTrue();
    }

    /// <summary>
    /// A directory renamed outside SVN cannot take the repair route — reverting the subtree and
    /// merging the local changes back over it is a different operation, and it is not made unasked.
    /// </summary>
    [Test]
    public async Task A_directory_renamed_on_disk_is_refused_rather_than_repaired()
    {
        using var copy = Committed(("art/hero.png", "pixels"));
        Directory.Move(copy.Absolute("art"), copy.Absolute("sprites"));

        var outcome = await Move(copy, "art", "sprites");

        await Assert.That(outcome.Route).IsEqualTo(MoveRoute.AlreadyRenamedDirectory);
        await Assert.That(File.Exists(copy.Absolute("sprites/hero.png"))).IsTrue();
    }

    [Test]
    public async Task A_directory_still_in_place_renames_with_everything_under_it()
    {
        using var copy = Committed(("art/hero.png", "pixels"));

        var outcome = await Move(copy, "art", "sprites");

        await Assert.That(outcome.Route).IsEqualTo(MoveRoute.Ordinary);
        await Assert.That(File.Exists(copy.Absolute("sprites/hero.png"))).IsTrue();
        await Assert.That(Directory.Exists(copy.Absolute("art"))).IsFalse();
    }

    [Test]
    public async Task A_deleted_file_is_scheduled_and_gone_from_disk()
    {
        using var copy = Committed(("art/hero.png", "pixels"));

        var notifications = await new SvnDeleteCommand(Svn).DeleteAsync(
            copy.Root,
            [copy.Absolute("art/hero.png")],
            None
        );

        await Assert.That(notifications).Contains("art/hero.png");
        await Assert.That(File.Exists(copy.Absolute("art/hero.png"))).IsFalse();
        await Assert.That(StatusOf(copy, "art/hero.png")).IsEqualTo(NodeStatus.Deleted);
    }

    /// <summary>
    /// <c>svn delete</c> refuses a modified file without <c>--force</c> (<c>E195006</c>). We always
    /// pass it, because the caller has already shown the person what they are about to lose.
    /// </summary>
    [Test]
    public async Task A_modified_file_is_deleted_rather_than_refused()
    {
        using var copy = Committed(("art/hero.png", "pixels"));
        copy.Write("art/hero.png", "pixels and my edits");

        var notifications = await new SvnDeleteCommand(Svn).DeleteAsync(
            copy.Root,
            [copy.Absolute("art/hero.png")],
            None
        );

        await Assert.That(notifications).Contains("art/hero.png");
        await Assert.That(File.Exists(copy.Absolute("art/hero.png"))).IsFalse();
    }

    /// <summary>
    /// The measured fact <c>sv rm</c>'s confirmation exists for: an unversioned file is unlinked
    /// with no pristine behind it, and SVN says nothing at all about having done it.
    /// </summary>
    [Test]
    public async Task An_unversioned_file_is_removed_from_disk_without_a_word()
    {
        using var copy = Committed(("art/hero.png", "pixels"));
        copy.Write("art/scratch.txt", "not in svn");

        var notifications = await new SvnDeleteCommand(Svn).DeleteAsync(
            copy.Root,
            [copy.Absolute("art/scratch.txt")],
            None
        );

        await Assert.That(notifications).IsEmpty();
        await Assert.That(File.Exists(copy.Absolute("art/scratch.txt"))).IsFalse();
    }

    /// <summary>
    /// The path that protects somebody's file: the repair moves it to the old path, and if the move
    /// that follows fails it has to go back. A write lock is what makes that reachable on purpose —
    /// every write to the copy then fails with <c>E155004</c>.
    /// </summary>
    [Test]
    public async Task A_repair_that_cannot_finish_puts_the_renamed_file_back()
    {
        using var copy = Committed(("art/hero.png", "pixels"));
        File.Move(copy.Absolute("art/hero.png"), copy.Absolute("art/protagonist.png"));
        copy.Write("art/protagonist.png", "pixels and my edits");
        WorkingCopyWedge.TakeWriteLock(copy, string.Empty, -1);

        await Assert
            .That(async () => await Move(copy, "art/hero.png", "art/protagonist.png"))
            .Throws<SvnCommandException>();

        await Assert
            .That(File.ReadAllText(copy.Absolute("art/protagonist.png")))
            .IsEqualTo("pixels and my edits");
        await Assert.That(File.Exists(copy.Absolute("art/hero.png"))).IsFalse();
    }

    [Test]
    public async Task A_repair_that_cannot_finish_removes_the_folder_it_recreated()
    {
        using var copy = Committed(("old/solo.png", "lonely"), ("sprites/keep.txt", "x"));
        File.Move(copy.Absolute("old/solo.png"), copy.Absolute("sprites/solo.png"));
        Directory.Delete(copy.Absolute("old"));
        WorkingCopyWedge.TakeWriteLock(copy, string.Empty, -1);

        await Assert
            .That(async () => await Move(copy, "old/solo.png", "sprites/solo.png"))
            .Throws<SvnCommandException>();

        await Assert.That(File.ReadAllText(copy.Absolute("sprites/solo.png"))).IsEqualTo("lonely");
        await Assert.That(Directory.Exists(copy.Absolute("old"))).IsFalse();
    }

    /// <summary>
    /// The refusing half of the command's one branch. <see cref="NodeMove"/> keeps this case away
    /// from the client, so it is driven directly — the command must still raise rather than hand
    /// back an empty success.
    /// </summary>
    [Test]
    public async Task A_move_the_client_refuses_is_raised_rather_than_reported_as_done()
    {
        using var copy = Committed(("art/hero.png", "one"), ("art/villain.png", "two"));

        await Assert
            .That(async () =>
                await new SvnMoveCommand(Svn).MoveAsync(
                    copy.Root,
                    copy.Absolute("art/hero.png"),
                    copy.Absolute("art/villain.png"),
                    None
                )
            )
            .Throws<SvnCommandException>();
    }

    [Test]
    public async Task A_delete_the_client_refuses_is_raised_rather_than_reported_as_done()
    {
        using var copy = Committed(("art/hero.png", "pixels"));

        await Assert
            .That(async () =>
                await new SvnDeleteCommand(Svn).DeleteAsync(
                    copy.Root,
                    [copy.Absolute("art/nothing-here.png")],
                    None
                )
            )
            .Throws<SvnCommandException>();
    }

    private static Task<MoveOutcome> Move(SvnWorkingCopy copy, string source, string destination)
    {
        var move = new SvnMoveCommand(Svn);
        return new NodeMove(move, new UnrecordedMoveRepair(move)).MoveAsync(
            copy.Root,
            copy.Absolute(source),
            copy.Absolute(destination),
            None
        );
    }

    private static NodeStatus StatusOf(SvnWorkingCopy copy, string relPath)
    {
        using var reader = WcDbReader.Open(copy.Root);
        return new WorkingCopyScanner(reader, GlobalIgnoreConfiguration.SubversionDefault)
            .Scan()
            .Single(entry => entry.RelPath == relPath)
            .Status;
    }

    private static SvnWorkingCopy Committed(params (string RelPath, string Content)[] files)
    {
        var copy = SvnWorkingCopy.Create();
        try
        {
            foreach (var (relPath, content) in files)
            {
                copy.Write(relPath, content);
            }

            copy.Svn("add", "--quiet", "--force", ".");
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
