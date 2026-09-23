namespace Subverted.Svn.Tests;

/// <summary>
/// The one line of SVN's output Subverted reads rather than passes through. Getting it wrong
/// reports a commit that happened as one that did not.
/// </summary>
public sealed class SvnCommitOutputTests
{
    [Test]
    public async Task The_revision_comes_out_of_the_line_svn_ends_with()
    {
        var output =
            "Sending        src/a.txt\nTransmitting file data .done\nCommitted revision 12.\n";

        await Assert.That(SvnCommitOutput.Revision(output)).IsEqualTo(12L);
    }

    /// <summary>
    /// A commit with nothing to send prints nothing and exits zero. Reporting a revision here —
    /// any revision — would tell someone their work is on the server when it is not.
    /// </summary>
    [Test]
    public async Task Output_with_no_revision_line_reports_none_rather_than_zero()
    {
        await Assert.That(SvnCommitOutput.Revision(string.Empty)).IsNull();
        await Assert.That(SvnCommitOutput.Revision("Sending        src/a.txt\n")).IsNull();
    }

    /// <summary>
    /// A post-commit hook that fails still leaves the revision on the server, and SVN keeps
    /// printing afterwards. Taking the first match would work; taking the last one keeps working
    /// if SVN ever prints a second.
    /// </summary>
    [Test]
    public async Task The_last_revision_line_wins_over_an_earlier_one()
    {
        var output = "Committed revision 3.\nsomething else\nCommitted revision 4.\n";

        await Assert.That(SvnCommitOutput.Revision(output)).IsEqualTo(4L);
    }

    [Test]
    public async Task Windows_line_endings_do_not_hide_the_revision()
    {
        await Assert.That(SvnCommitOutput.Revision("Committed revision 9.\r\n")).IsEqualTo(9L);
    }

    [Test]
    public async Task A_revision_line_svn_did_not_finish_is_not_a_revision()
    {
        await Assert.That(SvnCommitOutput.Revision("Committed revision 12")).IsNull();
        await Assert.That(SvnCommitOutput.Revision("Committed revision .")).IsNull();
        await Assert.That(SvnCommitOutput.Revision("Committed revision twelve.")).IsNull();
    }

    /// <summary>
    /// <c>NumberStyles.None</c> on purpose: SVN never writes a signed or spaced revision, and a
    /// parser that accepted one would be reading something other than what SVN wrote.
    /// </summary>
    [Test]
    public async Task A_revision_that_is_not_plain_digits_is_refused()
    {
        await Assert.That(SvnCommitOutput.Revision("Committed revision -4.")).IsNull();
        await Assert.That(SvnCommitOutput.Revision("Committed revision 1 000.")).IsNull();
    }

    /// <summary>Six digits is an ordinary revision for a studio repository, not an edge case.</summary>
    [Test]
    public async Task A_large_revision_survives()
    {
        await Assert
            .That(SvnCommitOutput.Revision("Committed revision 284119."))
            .IsEqualTo(284119L);
    }
}
