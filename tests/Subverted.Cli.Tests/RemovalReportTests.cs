using Subverted.Protocol;

namespace Subverted.Cli.Tests;

public sealed class RemovalReportTests
{
    [Test]
    public async Task Svns_own_notification_is_passed_through()
    {
        var lines = RemovalReport.Lines(new DeleteResponse("D         art/hero.png\n"), 0);

        await Assert.That(lines).IsEquivalentTo(["D         art/hero.png"]);
    }

    /// <summary>
    /// SVN says nothing whatever about an unversioned file it unlinked, so without this line the
    /// person who removed five untracked files is looking at an empty screen.
    /// </summary>
    [Test]
    public async Task Files_svn_removed_silently_are_counted_out_loud()
    {
        var lines = RemovalReport.Lines(new DeleteResponse(string.Empty), 5);

        await Assert.That(lines.Count).IsEqualTo(1);
        await Assert.That(lines[0]).Contains("5 file(s)");
        await Assert.That(lines[0]).Contains("bring those back");
    }

    [Test]
    public async Task Both_kinds_are_reported_together()
    {
        var lines = RemovalReport.Lines(new DeleteResponse("D         art/hero.png\n"), 2);

        await Assert.That(lines[0]).IsEqualTo("D         art/hero.png");
        await Assert.That(lines[1]).Contains("2 file(s)");
    }

    /// <summary>
    /// The negative side of the count's branch: zero must print nothing rather than "0 file(s)".
    /// </summary>
    [Test]
    public async Task Nothing_removed_silently_adds_no_line_about_it()
    {
        var lines = RemovalReport.Lines(new DeleteResponse("D         art/hero.png\n"), 0);

        await Assert.That(lines.Count).IsEqualTo(1);
    }

    [Test]
    public async Task A_removal_that_did_nothing_says_so_rather_than_printing_nothing()
    {
        await Assert
            .That(RemovalReport.Lines(new DeleteResponse(string.Empty), 0))
            .IsEquivalentTo(["nothing removed"]);
    }
}
