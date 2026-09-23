namespace Subverted.Svn.Tests;

/// <summary>
/// <c>svn diff --summarize --xml</c> is UTF-8 on every build, which is why the true names are read
/// from it. The documents are 1.8.15's and 1.14.5's, identical in shape.
/// </summary>
public sealed class SvnDiffSummaryTests
{
    private const string WorkingCopySummary = """
        <?xml version="1.0" encoding="UTF-8"?>
        <diff>
        <paths>
        <path
           item="modified"
           props="none"
           kind="file">names\zażółć.txt</path>
        <path
           item="modified"
           props="none"
           kind="file">names\ドラゴン.txt</path>
        </paths>
        </diff>
        """;

    private const string RevisionSummary = """
        <?xml version="1.0" encoding="UTF-8"?>
        <diff>
        <paths>
        <path
           item="added"
           props="none"
           kind="file">file:///C:/tmp/repo/names/%E3%83%89%E3%83%A9%E3%82%B4%E3%83%B3.txt</path>
        <path
           item="added"
           props="none"
           kind="dir">file:///C:/tmp/repo/names</path>
        </paths>
        </diff>
        """;

    [Test]
    public async Task A_working_copy_summary_names_its_paths_with_slashes()
    {
        await Assert
            .That(SvnDiffSummary.Paths(WorkingCopySummary))
            .IsEquivalentTo(["names/zażółć.txt", "names/ドラゴン.txt"]);
    }

    [Test]
    public async Task A_revision_summary_names_its_urls_unescaped()
    {
        await Assert
            .That(SvnDiffSummary.Paths(RevisionSummary))
            .IsEquivalentTo([
                "file:///C:/tmp/repo/names/ドラゴン.txt",
                "file:///C:/tmp/repo/names",
            ]);
    }

    [Test]
    public async Task An_empty_summary_names_nothing()
    {
        await Assert
            .That(
                SvnDiffSummary.Paths("<?xml version=\"1.0\"?>\n<diff>\n<paths>\n</paths>\n</diff>")
            )
            .IsEmpty();
    }

    [Test]
    public async Task Something_that_is_not_xml_is_refused()
    {
        await Assert
            .That(() => SvnDiffSummary.Paths("svn: E155010: not found"))
            .Throws<SvnCommandException>();
    }
}
