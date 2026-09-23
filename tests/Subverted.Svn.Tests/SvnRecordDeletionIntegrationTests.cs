using Subverted.Core;

namespace Subverted.Svn.Tests;

/// <summary>
/// Recording a deletion that already happened on disk, against a real repository. The case that
/// matters is the one where the belief "it is missing" has gone stale: the file must survive it.
/// </summary>
public sealed class SvnRecordDeletionIntegrationTests
{
    private static readonly CancellationToken None = CancellationToken.None;
    private static readonly SvnCommand Svn = new("svn");

    [Test]
    public async Task A_missing_file_is_recorded_as_deleted()
    {
        using var copy = Committed(("art/hero.png", "pixels"));
        copy.Delete("art/hero.png");

        var notifications = await Record(copy, "art/hero.png");

        await Assert.That(notifications).Contains("art/hero.png");
        await Assert.That(StatusOf(copy, "art/hero.png")).IsEqualTo(NodeStatus.Deleted);
    }

    /// <summary>SVN records the directory and every node under it, which D20's third rule relies on.</summary>
    [Test]
    public async Task A_missing_directory_is_recorded_as_deleted_with_everything_under_it()
    {
        using var copy = Committed(("art/hero.png", "pixels"), ("art/villain.png", "more"));
        Directory.Delete(copy.Absolute("art"), recursive: true);

        await Record(copy, "art");

        await Assert.That(StatusOf(copy, "art")).IsEqualTo(NodeStatus.Deleted);
        await Assert.That(StatusOf(copy, "art/hero.png")).IsEqualTo(NodeStatus.Deleted);
        await Assert.That(StatusOf(copy, "art/villain.png")).IsEqualTo(NodeStatus.Deleted);
    }

    /// <summary>
    /// The reason this is not <see cref="SvnDeleteCommand"/>: a file that came back with somebody's
    /// edits in it is refused and left exactly as it was, where <c>--force</c> would unlink it.
    /// </summary>
    [Test]
    public async Task A_file_that_came_back_with_edits_is_refused_and_left_on_disk()
    {
        using var copy = Committed(("art/hero.png", "pixels"));
        copy.Write("art/hero.png", "pixels and my afternoon");

        var refusal = await Assert.ThrowsAsync<SvnCommandException>(() =>
            Record(copy, "art/hero.png")
        );

        await Assert.That(refusal!.Message).Contains("E195006");
        await Assert
            .That(File.ReadAllText(copy.Absolute("art/hero.png")))
            .IsEqualTo("pixels and my afternoon");
        await Assert.That(StatusOf(copy, "art/hero.png")).IsEqualTo(NodeStatus.Modified);
    }

    private static Task<string> Record(SvnWorkingCopy copy, string relPath) =>
        new SvnRecordDeletionCommand(Svn).RecordAsync(copy.Root, [copy.Absolute(relPath)], None);

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
