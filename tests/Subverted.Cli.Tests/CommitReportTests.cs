using Subverted.Protocol;

namespace Subverted.Cli.Tests;

public sealed class CommitReportTests
{
    /// <summary>
    /// SVN's own output already ends with the revision it created. Adding a line of our own after
    /// it would print the same number twice, in two spellings.
    /// </summary>
    [Test]
    public async Task Svns_own_text_is_what_gets_printed_and_nothing_is_appended_to_it()
    {
        var response = new CommitResponse(
            12,
            "Sending        src/a.txt\nTransmitting file data .\nCommitted revision 12.\n"
        );

        await Assert
            .That(CommitReport.Lines(response))
            .IsEquivalentTo([
                "Sending        src/a.txt",
                "Transmitting file data .",
                "Committed revision 12.",
            ]);
    }

    /// <summary>
    /// Nothing to send is success, and saying so is the whole message — printing a revision
    /// somebody could quote would be worse than printing nothing.
    /// </summary>
    [Test]
    public async Task A_commit_with_no_revision_says_there_was_nothing_to_commit()
    {
        await Assert
            .That(CommitReport.Lines(new CommitResponse(null, string.Empty)))
            .IsEquivalentTo(["nothing to commit"]);
    }

    /// <summary>
    /// A commit that created a revision while printing nothing would otherwise be silent, and
    /// silence is what "nothing to commit" looks like. The two must not read the same.
    /// </summary>
    [Test]
    public async Task A_revision_with_nothing_printed_before_it_still_reports_the_revision()
    {
        await Assert
            .That(CommitReport.Lines(new CommitResponse(31, string.Empty)))
            .IsEquivalentTo(["committed r31"]);
    }
}
