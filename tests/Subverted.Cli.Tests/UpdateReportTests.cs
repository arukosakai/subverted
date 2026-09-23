using Subverted.Protocol;

namespace Subverted.Cli.Tests;

/// <summary>
/// SVN already names every path an update touched, so the report passes its text through. What it
/// adds is the part SVN states only as a count at the bottom — on a studio-sized update that line
/// is a hundred rows above where the reader stops, and it is the line that means "not done yet".
/// </summary>
public sealed class UpdateReportTests
{
    [Test]
    public async Task Svns_own_text_is_what_gets_printed_when_nothing_needs_attention()
    {
        var response = new UpdateResponse(
            2,
            Conflicts: 0,
            SkippedPaths: 0,
            "Updating '.':\nU    src/a.txt\nUpdated to revision 2.\n"
        );

        await Assert
            .That(UpdateReport.Lines(response))
            .IsEquivalentTo(["Updating '.':", "U    src/a.txt", "Updated to revision 2."]);
    }

    [Test]
    public async Task A_conflict_is_called_out_after_svns_own_text()
    {
        var response = new UpdateResponse(
            3,
            Conflicts: 1,
            SkippedPaths: 0,
            "Updating '.':\nC    src/a.txt\nUpdated to revision 3.\n"
                + "Summary of conflicts:\n  Text conflicts: 1\n"
        );

        var lines = UpdateReport.Lines(response);

        await Assert.That(lines.Count).IsEqualTo(6);
        await Assert.That(lines[5]).Contains("1 conflict(s)");
    }

    /// <summary>
    /// Skipped is not conflicted — nothing was merged and no file holds markers — so it gets its
    /// own line. One message covering both would tell somebody to resolve a file SVN never opened.
    /// </summary>
    [Test]
    public async Task A_skipped_path_is_called_out_as_skipped_rather_than_as_a_conflict()
    {
        var response = new UpdateResponse(
            3,
            Conflicts: 0,
            SkippedPaths: 2,
            "Skipped 'ghost'\nSummary of conflicts:\n  Skipped paths: 2\n"
        );

        var lines = UpdateReport.Lines(response);

        await Assert.That(lines[^1]).Contains("2 path(s) skipped");
        await Assert.That(string.Join('\n', lines)).DoesNotContain("conflict(s)");
    }

    /// <summary>
    /// One update can do both, and each line has to appear on its own account. Reporting only the
    /// first would hide whichever SVN happened to count second.
    /// </summary>
    [Test]
    public async Task Conflicts_and_skips_in_one_update_are_both_reported()
    {
        var response = new UpdateResponse(3, Conflicts: 1, SkippedPaths: 1, "At revision 3.\n");

        var lines = UpdateReport.Lines(response);

        await Assert.That(lines.Count).IsEqualTo(3);
        await Assert.That(lines[1]).Contains("1 conflict(s)");
        await Assert.That(lines[2]).Contains("1 path(s) skipped");
    }

    /// <summary>
    /// An update that printed nothing at all must not come out silent — silence is what a command
    /// that never ran looks like.
    /// </summary>
    [Test]
    public async Task An_update_that_printed_nothing_still_says_something()
    {
        await Assert
            .That(
                UpdateReport.Lines(
                    new UpdateResponse(2, Conflicts: 0, SkippedPaths: 0, string.Empty)
                )
            )
            .IsEquivalentTo(["already up to date"]);
    }
}
