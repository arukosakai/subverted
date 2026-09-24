using System.Text;
using Subverted.Core;

namespace Subverted.Svn.Tests;

/// <summary>
/// The reason the in-process diff exists — more context than svn prints — and the ways the disk
/// can make it decline, against real working copies.
/// </summary>
public sealed class ContextDiffIntegrationTests
{
    private static readonly CancellationToken None = CancellationToken.None;
    private static readonly SvnCommand Svn = new("svn");
    private static readonly string Twelve = string.Concat(
        Enumerable.Range(1, 12).Select(i => $"line {i}\n")
    );

    [Test]
    public async Task A_local_edit_asked_for_with_ten_lines_shows_ten_on_each_side()
    {
        using var copy = CommittedTwelveLines();
        copy.Write("a.txt", Twelve.Replace("line 6\n", "line six\n"));

        var written = await Read(copy, new DiffContext(10));

        await Assert
            .That(written)
            .IsEqualTo(
                Headers("a.txt", "revision 1", "working copy")
                    + "@@ -1,12 +1,12 @@\n"
                    + Lines(1, 5, ' ')
                    + "-line 6\n+line six\n"
                    + Lines(7, 12, ' ')
            );
    }

    [Test]
    public async Task The_whole_file_is_every_line_however_far_from_the_change()
    {
        using var copy = CommittedTwelveLines();
        copy.Write("a.txt", Twelve.Replace("line 1\n", "line one\n"));

        var written = await Read(copy, DiffContext.WholeFile);

        await Assert
            .That(written)
            .IsEqualTo(
                Headers("a.txt", "revision 1", "working copy")
                    + "@@ -1,12 +1,12 @@\n-line 1\n+line one\n"
                    + Lines(2, 12, ' ')
            );
    }

    [Test]
    public async Task A_pristine_gone_from_the_store_is_left_to_svn()
    {
        using var copy = CommittedTwelveLines();
        copy.Write("a.txt", Twelve.Replace("line 6\n", "line six\n"));
        foreach (var pristine in Pristines(copy))
        {
            File.SetAttributes(pristine, FileAttributes.Normal);
            File.Delete(pristine);
        }

        await Assert.That(await Read(copy, new DiffContext(10))).IsNull();
    }

    [Test]
    public async Task A_pristine_whose_size_is_not_the_recorded_one_is_left_to_svn()
    {
        using var copy = CommittedTwelveLines();
        copy.Write("a.txt", Twelve.Replace("line 6\n", "line six\n"));
        foreach (var pristine in Pristines(copy))
        {
            File.SetAttributes(pristine, FileAttributes.Normal);
            File.AppendAllText(pristine, "tampered\n");
        }

        await Assert.That(await Read(copy, new DiffContext(10))).IsNull();
    }

    [Test]
    public async Task A_folder_standing_where_the_file_was_is_left_to_svn()
    {
        using var copy = CommittedTwelveLines();
        copy.Delete("a.txt");
        copy.CreateDirectory("a.txt");

        await Assert.That(await Read(copy, new DiffContext(10))).IsNull();
    }

    /// <summary>svn diff itself fails on this file, E135000; declining lets it say so.</summary>
    [Test]
    public async Task A_native_file_with_mixed_endings_on_disk_is_left_to_svn()
    {
        using var copy = CommittedTwelveLines();
        copy.Svn("propset", "--quiet", "svn:eol-style", "native", "a.txt");
        copy.Svn("commit", "--quiet", "-m", "native");
        copy.WriteBytes("a.txt", Encoding.ASCII.GetBytes("line 1\r\nline 2\n"));

        await Assert.That(await Read(copy, new DiffContext(10))).IsNull();
    }

    [Test]
    public async Task A_folder_that_is_not_a_working_copy_is_left_to_svn()
    {
        var elsewhere = Directory.CreateTempSubdirectory("subverted-not-a-wc-");
        try
        {
            var written = await new WorkingCopyContextDiff(Svn.Spelling, "\n").ReadAsync(
                elsewhere.FullName,
                Path.Combine(elsewhere.FullName, "a.txt"),
                new DiffContext(10),
                None
            );

            await Assert.That(written).IsNull();
        }
        finally
        {
            elsewhere.Delete(recursive: true);
        }
    }

    [Test]
    public async Task The_pristine_base_row_names_the_base_revision_and_the_pristines_size()
    {
        using var copy = CommittedTwelveLines();

        using var reader = WcDbReader.Open(copy.Root);
        var row = reader.ReadPristineBase("a.txt");

        await Assert.That(row!.OpDepth).IsEqualTo(0);
        await Assert.That(row.Presence).IsEqualTo("normal");
        await Assert.That(row.Kind).IsEqualTo(NodeKind.File);
        await Assert.That(row.Revision).IsEqualTo(1L);
        await Assert.That(row.PristineSize).IsEqualTo((long)Twelve.Length);
        await Assert.That(row.PristineIsPlain).IsTrue();
        await Assert.That(row.HasConflict).IsFalse();
        await Assert.That(row.WorkingProperties).IsNull();
        await Assert.That(SvnChecksum.TryParseSha1(row.Checksum)).IsNotNull();
    }

    [Test]
    public async Task A_path_wc_db_has_no_node_for_has_no_pristine_base_row()
    {
        using var copy = CommittedTwelveLines();

        using var reader = WcDbReader.Open(copy.Root);

        await Assert.That(reader.ReadPristineBase("nothing-here.txt")).IsNull();
    }

    [Test]
    public async Task A_committed_edit_asked_for_with_ten_lines_shows_ten_on_each_side()
    {
        using var copy = CommittedTwelveLines();
        copy.Write("a.txt", Twelve.Replace("line 6\n", "line six\n"));
        copy.Svn("commit", "--quiet", "-m", "r2");

        var written = await new RevisionContextDiff(Svn, "\n").ReadAsync(
            copy.Root,
            RepositoryRoot(copy),
            "/a.txt",
            2,
            new DiffContext(10),
            None
        );

        await Assert
            .That(written)
            .IsEqualTo(
                Headers("a.txt", "revision 1", "revision 2")
                    + "@@ -1,12 +1,12 @@\n"
                    + Lines(1, 5, ' ')
                    + "-line 6\n+line six\n"
                    + Lines(7, 12, ' ')
            );
    }

    [Test]
    public async Task A_revision_svn_cannot_find_is_left_to_svn_to_say_so()
    {
        using var copy = CommittedTwelveLines();

        var written = await new RevisionContextDiff(Svn, "\n").ReadAsync(
            copy.Root,
            RepositoryRoot(copy),
            "/a.txt",
            7,
            new DiffContext(10),
            None
        );

        await Assert.That(written).IsNull();
    }

    private static SvnWorkingCopy CommittedTwelveLines()
    {
        var copy = SvnWorkingCopy.Create();
        copy.Write("a.txt", Twelve);
        copy.Svn("add", "--quiet", "a.txt");
        copy.Svn("commit", "--quiet", "-m", "r1");
        return copy;
    }

    private static Task<string?> Read(SvnWorkingCopy copy, DiffContext context) =>
        new WorkingCopyContextDiff(Svn.Spelling, "\n").ReadAsync(
            copy.Root,
            copy.Absolute("a.txt"),
            context,
            None
        );

    private static IEnumerable<string> Pristines(SvnWorkingCopy copy) =>
        Directory.EnumerateFiles(
            Path.Combine(copy.Root, ".svn", "pristine"),
            "*.svn-base",
            SearchOption.AllDirectories
        );

    private static string RepositoryRoot(SvnWorkingCopy copy)
    {
        using var reader = WcDbReader.Open(copy.Root);
        return reader.Info.RepositoryRoot;
    }

    private static string Headers(string name, string oldLabel, string newLabel) =>
        $"Index: {name}\n"
        + "===================================================================\n"
        + $"--- {name}\t({oldLabel})\n"
        + $"+++ {name}\t({newLabel})\n";

    private static string Lines(int first, int last, char sign) =>
        string.Concat(Enumerable.Range(first, last - first + 1).Select(i => $"{sign}line {i}\n"));
}
