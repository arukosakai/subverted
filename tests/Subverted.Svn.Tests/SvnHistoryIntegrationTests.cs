using Microsoft.Data.Sqlite;
using Subverted.Core;

namespace Subverted.Svn.Tests;

/// <summary>
/// What the History view reads, against a real repository: where a log starts, one revision's
/// diff of one repository path, and the BASE range its marker is drawn from.
/// </summary>
public sealed class SvnHistoryIntegrationTests
{
    private static readonly CancellationToken None = CancellationToken.None;
    private static readonly SvnCommand Svn = new("svn");
    private static readonly SvnVersionCommand SvnVersion = new(new SvnCommand("svnversion"));

    [Test]
    public async Task A_history_with_no_start_stops_at_the_base_the_copy_was_updated_to()
    {
        using var copy = ThreeEdits();
        copy.Svn("update", "--quiet", "-r", "1");

        var revisions = await Log(copy, start: null);

        await Assert.That(revisions).IsEquivalentTo([1L]);
    }

    [Test]
    public async Task A_history_from_head_lists_revisions_the_copy_has_not_been_updated_to()
    {
        using var copy = ThreeEdits();
        copy.Svn("update", "--quiet", "-r", "1");

        var revisions = await Log(copy, new HistoryFromHead());

        await Assert.That(revisions).IsEquivalentTo([3L, 2L, 1L]);
    }

    [Test]
    public async Task A_history_from_a_revision_lists_that_revision_and_older_only()
    {
        using var copy = ThreeEdits();

        var revisions = await Log(copy, new HistoryFromRevision(2));

        await Assert.That(revisions).IsEquivalentTo([2L, 1L]);
    }

    [Test]
    public async Task A_limit_still_applies_to_a_history_from_a_revision()
    {
        using var copy = ThreeEdits();

        var revisions = await new SvnLogCommand(Svn).ReadAsync(
            copy.Root,
            copy.Root,
            1,
            new HistoryFromRevision(2),
            None
        );

        await Assert.That(revisions.Select(revision => revision.Revision)).IsEquivalentTo([2L]);
    }

    [Test]
    public async Task A_revision_diff_shows_what_that_revision_did_and_nothing_later()
    {
        using var copy = ThreeEdits();

        var diff = await RevisionDiff(copy, "/a.txt", 2);

        await Assert.That(diff).Contains("Index: a.txt");
        await Assert.That(diff).Contains("-one\n");
        await Assert.That(diff).Contains("+two\n");
        await Assert.That(diff).DoesNotContain("three");
    }

    /// <summary>
    /// The reason the request names a repository path: on 1.8.15 the same diff asked for by its
    /// place on disk fails with <c>E155010</c> once the file is gone.
    /// </summary>
    [Test]
    public async Task A_file_deleted_since_still_shows_the_revision_that_deleted_it()
    {
        using var copy = ThreeEdits();
        copy.Svn("delete", "--quiet", "a.txt");
        copy.Svn("commit", "--quiet", "-m", "gone");
        copy.Svn("update", "--quiet");

        var diff = await RevisionDiff(copy, "/a.txt", 4);

        await Assert.That(diff).Contains("--- a.txt\t(revision 3)");
        await Assert.That(diff).Contains("-three\n");
    }

    [Test]
    public async Task A_file_renamed_since_still_shows_its_history_under_its_old_name()
    {
        using var copy = ThreeEdits();
        copy.Svn("move", "--quiet", "a.txt", "b.txt");
        copy.Svn("commit", "--quiet", "-m", "renamed");
        copy.Svn("update", "--quiet");

        var diff = await RevisionDiff(copy, "/a.txt", 2);

        await Assert.That(diff).Contains("+two\n");
    }

    /// <summary>SVN compares a copy with its source, so an unedited one has nothing to show.</summary>
    [Test]
    public async Task A_copy_with_no_edits_diffs_to_nothing()
    {
        using var copy = ThreeEdits();
        copy.Svn("copy", "--quiet", "a.txt", "b.txt");
        copy.Svn("commit", "--quiet", "-m", "copied");
        copy.Svn("update", "--quiet");

        var diff = await RevisionDiff(copy, "/b.txt", 4);

        await Assert.That(diff).IsEmpty();
    }

    [Test]
    [Arguments("icon@2x.png.txt")]
    [Arguments("100%41 done.txt")]
    [Arguments("zażółć.txt")]
    public async Task A_name_that_needs_escaping_in_a_url_diffs_as_itself(string name)
    {
        using var copy = SvnWorkingCopy.Create();
        copy.Write(name, "one\n");
        copy.Svn("add", "--quiet", name + "@");
        copy.Svn("commit", "--quiet", "-m", "odd name");
        copy.Svn("update", "--quiet");

        var diff = await RevisionDiff(copy, "/" + name, 1);

        await Assert.That(diff).Contains($"Index: {name}");
        await Assert.That(diff).Contains("+one");
    }

    [Test]
    public async Task A_path_the_repository_never_had_fails_with_what_svn_said()
    {
        using var copy = ThreeEdits();

        await Assert
            .That(async () => await RevisionDiff(copy, "/never.txt", 2))
            .Throws<SvnCommandException>()
            .WithMessageContaining("E160013");
    }

    [Test]
    public async Task A_copy_updated_in_parts_reads_as_the_range_svnversion_prints()
    {
        using var copy = UpdatedInParts();

        var range = await BaseRange(copy, copy.Root);

        await Assert.That(range).IsEqualTo(new BaseRevisionRange(1, 3));
        await Assert.That(await SvnVersion.ReadAsync(copy.Root, copy.Root, None)).IsEqualTo(range);
    }

    [Test]
    public async Task A_subtree_reads_as_its_own_range_and_not_the_whole_copys()
    {
        using var copy = UpdatedInParts();

        var range = await BaseRange(copy, copy.Absolute("sub"));

        await Assert.That(range).IsEqualTo(new BaseRevisionRange(3, 3));
        await Assert
            .That(await SvnVersion.ReadAsync(copy.Root, copy.Absolute("sub"), None))
            .IsEqualTo(range);
    }

    /// <summary>A prefix match would take <c>sub-other.txt</c> for part of <c>sub</c>.</summary>
    [Test]
    public async Task A_sibling_sharing_the_subtrees_prefix_is_not_part_of_it()
    {
        using var copy = UpdatedInParts();

        var range = await BaseRange(copy, copy.Absolute("sub"));
        var sibling = await BaseRange(copy, copy.Absolute("sub-other.txt"));

        await Assert.That(range).IsEqualTo(new BaseRevisionRange(3, 3));
        await Assert.That(sibling).IsEqualTo(new BaseRevisionRange(1, 1));
    }

    /// <summary>
    /// Updating a file to before it existed leaves a <c>not-present</c> BASE row at that revision.
    /// svnversion ignores it; counting it would call a copy at r2 mixed from r1.
    /// </summary>
    [Test]
    public async Task A_node_updated_to_before_it_existed_does_not_widen_the_range()
    {
        using var copy = SvnWorkingCopy.Create();
        copy.Write("a.txt", "one\n");
        copy.Svn("add", "--quiet", "a.txt");
        copy.Svn("commit", "--quiet", "-m", "first");
        copy.Write("b.txt", "later\n");
        copy.Svn("add", "--quiet", "b.txt");
        copy.Svn("commit", "--quiet", "-m", "second");
        copy.Svn("update", "--quiet");
        copy.Svn("update", "--quiet", "-r", "1", "b.txt");

        var range = await BaseRange(copy, copy.Root);

        await Assert.That(range).IsEqualTo(new BaseRevisionRange(2, 2));
        await Assert.That(await SvnVersion.ReadAsync(copy.Root, copy.Root, None)).IsEqualTo(range);
    }

    [Test]
    public async Task A_file_externals_own_revision_does_not_count()
    {
        using var copy = ThreeEdits();
        var definition = Path.Combine(copy.Root, "..", "externals.txt");
        File.WriteAllText(definition, "^/a.txt@1 ext.txt\n");
        copy.Svn("propset", "--quiet", "svn:externals", "-F", definition, ".");
        copy.Svn("update", "--quiet");

        var range = await BaseRange(copy, copy.Root);

        await Assert.That(range).IsEqualTo(new BaseRevisionRange(3, 3));
        await Assert.That(await SvnVersion.ReadAsync(copy.Root, copy.Root, None)).IsEqualTo(range);
    }

    [Test]
    public async Task A_file_that_is_only_scheduled_for_addition_has_no_base()
    {
        using var copy = ThreeEdits();
        copy.Write("new.txt", "fresh\n");
        copy.Svn("add", "--quiet", "new.txt");

        await Assert.That(await BaseRange(copy, copy.Absolute("new.txt"))).IsNull();
        await Assert
            .That(await SvnVersion.ReadAsync(copy.Root, copy.Absolute("new.txt"), None))
            .IsNull();
    }

    /// <summary>
    /// A newer client's wc.db is asked through svnversion. The 1.8.15 on this machine is too old
    /// for format 32 as well, so what proves the route is svnversion's own complaint coming back.
    /// </summary>
    [Test]
    public async Task A_wc_db_this_build_cannot_read_is_asked_through_svnversion_instead()
    {
        using var copy = ThreeEdits();
        SetFormat(copy, 32);

        await Assert
            .That(async () => await BaseRange(copy, copy.Root))
            .Throws<SvnCommandException>()
            .WithMessageContaining("E155021");
    }

    [Test]
    public async Task A_path_outside_any_working_copy_is_not_sent_to_svnversion()
    {
        var outside = Path.Combine(Path.GetTempPath(), $"subverted-none-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outside);
        try
        {
            await Assert
                .That(async () =>
                    await new BaseRevisionRangeReader(SvnVersion).ReadAsync(outside, outside, None)
                )
                .Throws<WcDbException>();
        }
        finally
        {
            Directory.Delete(outside);
        }
    }

    private static async Task<IEnumerable<long>> Log(SvnWorkingCopy copy, HistoryStart? start) =>
        (await new SvnLogCommand(Svn).ReadAsync(copy.Root, copy.Root, null, start, None)).Select(
            revision => revision.Revision
        );

    private static Task<string> RevisionDiff(
        SvnWorkingCopy copy,
        string repositoryPath,
        long revision
    )
    {
        string repositoryRoot;
        using (var reader = WcDbReader.Open(copy.Root))
        {
            repositoryRoot = reader.Info.RepositoryRoot;
        }

        return new SvnRevisionDiffCommand(Svn).ReadAsync(
            copy.Root,
            repositoryRoot,
            repositoryPath,
            revision,
            None
        );
    }

    private static Task<BaseRevisionRange?> BaseRange(SvnWorkingCopy copy, string path) =>
        new BaseRevisionRangeReader(SvnVersion).ReadAsync(copy.Root, path, None);

    /// <summary>r1 adds <c>a.txt</c>, r2 and r3 edit it; the copy is left at r3.</summary>
    private static SvnWorkingCopy ThreeEdits()
    {
        var copy = SvnWorkingCopy.Create();
        try
        {
            copy.Write("a.txt", "one\n");
            copy.Svn("add", "--quiet", "a.txt");
            copy.Svn("commit", "--quiet", "-m", "first");
            copy.Write("a.txt", "two\n");
            copy.Svn("commit", "--quiet", "-m", "second");
            copy.Write("a.txt", "three\n");
            copy.Svn("commit", "--quiet", "-m", "third");
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
    /// Root and <c>sub-other.txt</c> at r1, <c>sub</c> at r3 — the shape svnversion prints <c>1:3</c> for.
    /// </summary>
    private static SvnWorkingCopy UpdatedInParts()
    {
        var copy = SvnWorkingCopy.Create();
        try
        {
            copy.Write("a.txt", "one\n");
            copy.Write("sub/f.txt", "one\n");
            copy.Write("sub-other.txt", "one\n");
            copy.Svn("add", "--quiet", "a.txt", "sub", "sub-other.txt");
            copy.Svn("commit", "--quiet", "-m", "first");
            copy.Write("a.txt", "two\n");
            copy.Svn("commit", "--quiet", "-m", "second");
            copy.Write("sub/f.txt", "two\n");
            copy.Svn("commit", "--quiet", "-m", "third");
            copy.Svn("update", "--quiet", "-r", "1");
            copy.Svn("update", "--quiet", "-r", "3", "sub");
            return copy;
        }
        catch
        {
            copy.Dispose();
            throw;
        }
    }

    private static void SetFormat(SvnWorkingCopy copy, int format)
    {
        using var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource = Path.Combine(copy.Root, ".svn", "wc.db"),
                Pooling = false,
            }.ToString()
        );
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA user_version = {format};";
        command.ExecuteNonQuery();
    }
}
