using Subverted.Core;

namespace Subverted.Svn.Tests;

/// <summary>
/// Detecting a rename made outside SVN, against a real checkout. The pairing rules are unit-tested
/// on <see cref="UnrecordedMoveResolver"/>; what only a real working copy can settle is that the
/// checksum wc.db records is the one hashing the moved file produces.
/// </summary>
public sealed class UnrecordedMoveIntegrationTests
{
    [Test]
    public async Task A_file_renamed_outside_svn_is_paired_with_where_it_went()
    {
        using var copy = Committed(("art/hero.png", "pixels"));
        Rename(copy, "art/hero.png", "art/protagonist.png");

        var moves = FindMoves(copy);

        await Assert.That(moves.Count).IsEqualTo(1);
        await Assert.That(moves[0].FromRelPath).IsEqualTo("art/hero.png");
        await Assert.That(moves[0].ToRelPath).IsEqualTo("art/protagonist.png");
    }

    /// <summary>
    /// A rename inside a copy that is not committed yet, or of the copy itself. The copy's row
    /// records the checksum it was copied with, so the pair is recognised exactly as a BASE one is,
    /// and the repair records it the same way.
    /// </summary>
    [Test]
    [Arguments("gfx/tree.png")]
    [Arguments("tree-copy.png")]
    public async Task A_rename_of_an_uncommitted_copy_is_paired_like_any_other(string copied)
    {
        using var copy = Committed(("art/tree.png", "bark"));
        copy.Svn("copy", "--quiet", "art", "gfx");
        copy.Svn("copy", "--quiet", "art/tree.png", "tree-copy.png");
        var renamed = copied.Replace("tree", "oak", StringComparison.Ordinal);
        Rename(copy, copied, renamed);

        var moves = FindMoves(copy);

        await Assert.That(moves.Count).IsEqualTo(1);
        await Assert.That(moves[0].FromRelPath).IsEqualTo(copied);
        await Assert.That(moves[0].ToRelPath).IsEqualTo(renamed);
    }

    /// <summary>A move into another directory is the same thing, and reads the same way.</summary>
    [Test]
    public async Task A_file_moved_to_another_directory_is_paired_across_it()
    {
        using var copy = Committed(("art/hero.png", "pixels"), ("sprites/keep.txt", "x"));
        Rename(copy, "art/hero.png", "sprites/hero.png");

        var moves = FindMoves(copy);

        await Assert.That(moves.Count).IsEqualTo(1);
        await Assert.That(moves[0].ToRelPath).IsEqualTo("sprites/hero.png");
    }

    /// <summary>
    /// The negative side of the branch above: a deleted file and an unrelated new one are what a
    /// working copy looks like most days, and neither is a rename.
    /// </summary>
    [Test]
    public async Task A_deletion_and_an_unrelated_new_file_are_not_a_move()
    {
        using var copy = Committed(("art/hero.png", "pixels"));
        copy.Delete("art/hero.png");
        copy.Write("art/notes.txt", "something else entirely");

        await Assert.That(FindMoves(copy)).IsEmpty();
    }

    /// <summary>
    /// Same length, different bytes — the case the size filter lets through and only the hash can
    /// reject. Without this the filter could be mistaken for the decision.
    /// </summary>
    [Test]
    public async Task A_new_file_of_the_same_size_but_different_content_is_not_a_move()
    {
        using var copy = Committed(("art/hero.png", "pixels"));
        copy.Delete("art/hero.png");
        copy.Write("art/other.png", "PIXELS");

        await Assert.That(FindMoves(copy)).IsEmpty();
    }

    [Test]
    public async Task A_working_copy_with_nothing_missing_has_no_moves()
    {
        using var copy = Committed(("art/hero.png", "pixels"));
        copy.Write("art/extra.png", "pixels");

        await Assert.That(FindMoves(copy)).IsEmpty();
    }

    /// <summary>
    /// An edit on top of the rename breaks the content match, so the pair is not claimed. The safe
    /// direction: <c>sv mv</c> still records it when told to, and guessing here would be the kind of
    /// claim that is wrong the moment two assets share a byte count.
    /// </summary>
    [Test]
    public async Task A_file_renamed_and_then_edited_is_not_claimed_as_a_move()
    {
        using var copy = Committed(("art/hero.png", "pixels"));
        Rename(copy, "art/hero.png", "art/protagonist.png");
        copy.Write("art/protagonist.png", "pixels and then some");

        await Assert.That(FindMoves(copy)).IsEmpty();
    }

    [Test]
    public async Task Two_renames_are_both_reported()
    {
        using var copy = Committed(("art/hero.png", "one"), ("art/villain.png", "two"));
        Rename(copy, "art/hero.png", "art/protagonist.png");
        Rename(copy, "art/villain.png", "art/antagonist.png");

        var moves = FindMoves(copy);

        await Assert.That(moves.Count).IsEqualTo(2);
        await Assert.That(moves[0].ToRelPath).IsEqualTo("art/protagonist.png");
        await Assert.That(moves[1].ToRelPath).IsEqualTo("art/antagonist.png");
    }

    /// <summary>
    /// wc.db records one checksum for two files with the same bytes, so nothing distinguishes which
    /// of them moved where. Both are dropped rather than guessed at.
    /// </summary>
    [Test]
    public async Task Two_missing_files_with_identical_content_are_not_guessed_between()
    {
        using var copy = Committed(("art/one.png", "same"), ("art/two.png", "same"));
        copy.Delete("art/one.png");
        copy.Delete("art/two.png");
        copy.Write("art/somewhere.png", "same");

        await Assert.That(FindMoves(copy)).IsEmpty();
    }

    /// <summary>
    /// A rename SVN was told about is not an unrecorded one: the old path is <c>D</c> rather than
    /// missing, and the new one is versioned rather than unversioned.
    /// </summary>
    [Test]
    public async Task A_rename_made_through_svn_is_not_reported_as_unrecorded()
    {
        using var copy = Committed(("art/hero.png", "pixels"));
        copy.Svn("move", "--quiet", "art/hero.png", "art/protagonist.png");

        await Assert.That(FindMoves(copy)).IsEmpty();
    }

    /// <summary>An ignored file is not a candidate — it is not something anyone renamed into.</summary>
    [Test]
    public async Task An_ignored_file_holding_the_content_is_not_paired()
    {
        using var copy = Committed(("art/hero.png", "pixels"));
        copy.Svn("propset", "svn:ignore", "*.bak", "art");
        Rename(copy, "art/hero.png", "art/hero.bak");

        await Assert.That(FindMoves(copy)).IsEmpty();
    }

    /// <summary>
    /// A translated working file never hashed to its recorded checksum in the first place, so its
    /// copy at another path cannot be recognised either. Skipped rather than paired wrongly — the
    /// same rule <see cref="PristineComparer"/> follows, for the same reason.
    /// </summary>
    [Test]
    public async Task A_file_svn_translates_is_not_paired()
    {
        using var copy = Committed(("art/notes.txt", "one line\n"));
        copy.Svn("propset", "svn:eol-style", "native", "art/notes.txt");
        copy.Svn("commit", "--quiet", "-m", "translate it");
        copy.Svn("update", "--quiet");
        Rename(copy, "art/notes.txt", "art/readme.txt");

        await Assert.That(FindMoves(copy)).IsEmpty();
    }

    /// <summary>
    /// The candidate is picked from a listing and hashed afterwards, so it can be gone by the time
    /// the hash is taken — an export or a build step clearing up while a scan runs. Dropping it is
    /// the only honest answer; claiming the move would name a path that no longer exists.
    /// </summary>
    [Test]
    public async Task A_candidate_that_disappears_before_it_is_hashed_is_dropped()
    {
        using var copy = Committed(("art/hero.png", "pixels"));
        Rename(copy, "art/hero.png", "art/protagonist.png");

        using var reader = WcDbReader.Open(copy.Root);
        var scanner = new WorkingCopyScanner(reader, GlobalIgnoreConfiguration.SubversionDefault);
        var entries = scanner.Scan();
        copy.Delete("art/protagonist.png");

        await Assert.That(scanner.FindUnrecordedMoves(entries)).IsEmpty();
    }

    private static IReadOnlyList<UnrecordedMove> FindMoves(SvnWorkingCopy copy)
    {
        using var reader = WcDbReader.Open(copy.Root);
        var scanner = new WorkingCopyScanner(reader, GlobalIgnoreConfiguration.SubversionDefault);
        return scanner.FindUnrecordedMoves(scanner.Scan());
    }

    /// <summary>What a file manager does: the bytes move and SVN is told nothing.</summary>
    private static void Rename(SvnWorkingCopy copy, string from, string to)
    {
        var destination = copy.Absolute(to);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Move(copy.Absolute(from), destination);
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
