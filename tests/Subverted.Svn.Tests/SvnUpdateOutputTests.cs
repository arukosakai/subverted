namespace Subverted.Svn.Tests;

/// <summary>
/// The part of <c>svn update</c>'s output Subverted reads rather than passes through. Every blob
/// below is what <c>svn</c> 1.8.15 actually printed under <c>LC_ALL=C</c>, not a shape invented
/// here — the whole reason this type exists is that update exits zero on a conflict, so this text
/// is the only place the difference is stated.
/// </summary>
public sealed class SvnUpdateOutputTests
{
    private const string BroughtChanges =
        "Updating '.':\nD    art/hero.txt\nU    src/a.txt\nA    src/c.txt\nUpdated to revision 2.\n";

    private const string BroughtNothing = "Updating '.':\nAt revision 2.\n";

    private const string TextConflict =
        "Updating '.':\nC    src/a.txt\nU    src/b.txt\nUpdated to revision 3.\n"
        + "Summary of conflicts:\n  Text conflicts: 1\n";

    private const string PropertyConflict =
        "Updating '.':\nUC   src/b.txt\nUpdated to revision 6.\n"
        + "Summary of conflicts:\n  Property conflicts: 1\n";

    private const string TreeConflict =
        "Updating '.':\n   C src/b.txt\nAt revision 7.\n"
        + "Summary of conflicts:\n  Tree conflicts: 1\n";

    private const string SkippedPath =
        "Skipped 'repo'\nSummary of conflicts:\n  Skipped paths: 1\n";

    [Test]
    public async Task The_revision_comes_off_the_line_an_update_that_brought_work_ends_with()
    {
        await Assert.That(SvnUpdateOutput.Revision(BroughtChanges)).IsEqualTo(2L);
    }

    /// <summary>
    /// SVN says "At revision N" when nothing came down, and that is still the revision the working
    /// copy stands at. Reading only "Updated to" would report no revision for every up-to-date
    /// working copy in the studio — and, worse, for a tree conflict, which also prints "At".
    /// </summary>
    [Test]
    public async Task An_update_that_brought_nothing_still_reports_the_revision()
    {
        await Assert.That(SvnUpdateOutput.Revision(BroughtNothing)).IsEqualTo(2L);
        await Assert.That(SvnUpdateOutput.Revision(TreeConflict)).IsEqualTo(7L);
    }

    [Test]
    public async Task Output_with_no_revision_line_reports_none_rather_than_zero()
    {
        await Assert.That(SvnUpdateOutput.Revision(string.Empty)).IsNull();
        await Assert.That(SvnUpdateOutput.Revision(SkippedPath)).IsNull();
    }

    /// <summary>
    /// A working copy with an external gets one of these lines per checkout, in the order SVN
    /// walked them, and the target's own comes last.
    /// </summary>
    [Test]
    public async Task The_last_revision_line_wins_over_an_earlier_one()
    {
        var output =
            "Updating '.':\nFetching external item into 'ext':\nUpdated to revision 3.\n"
            + "Updated to revision 9.\n";

        await Assert.That(SvnUpdateOutput.Revision(output)).IsEqualTo(9L);
    }

    [Test]
    public async Task Windows_line_endings_do_not_hide_the_revision()
    {
        await Assert.That(SvnUpdateOutput.Revision("Updated to revision 9.\r\n")).IsEqualTo(9L);
        await Assert.That(SvnUpdateOutput.Revision("At revision 9.\r\n")).IsEqualTo(9L);
    }

    /// <summary>
    /// The full stop is checked before the digits are taken. Without that check the slice that
    /// drops it turns revision 12 into revision 1, which is a wrong answer rather than no answer.
    /// </summary>
    [Test]
    public async Task A_revision_line_svn_did_not_finish_is_not_a_revision()
    {
        await Assert.That(SvnUpdateOutput.Revision("Updated to revision 12")).IsNull();
        await Assert.That(SvnUpdateOutput.Revision("At revision 12")).IsNull();
        await Assert.That(SvnUpdateOutput.Revision("Updated to revision .")).IsNull();
        await Assert.That(SvnUpdateOutput.Revision("At revision seven.")).IsNull();
    }

    /// <summary>
    /// <c>NumberStyles.None</c> on purpose: SVN never writes a signed or spaced revision, and a
    /// parser that accepted one would be reading something other than what SVN wrote.
    /// </summary>
    [Test]
    public async Task A_revision_that_is_not_plain_digits_is_refused()
    {
        await Assert.That(SvnUpdateOutput.Revision("Updated to revision -4.")).IsNull();
        await Assert.That(SvnUpdateOutput.Revision("At revision 1 000.")).IsNull();
    }

    [Test]
    public async Task A_large_revision_survives()
    {
        await Assert
            .That(SvnUpdateOutput.Revision("Updated to revision 284119."))
            .IsEqualTo(284119L);
    }

    [Test]
    [Arguments(TextConflict)]
    [Arguments(PropertyConflict)]
    [Arguments(TreeConflict)]
    public async Task Each_kind_of_conflict_svn_counts_is_counted(string output)
    {
        await Assert.That(SvnUpdateOutput.Conflicts(output)).IsEqualTo(1);
    }

    /// <summary>
    /// The negative case for the same three, because "count everything under the heading" and
    /// "count these three labels" only differ when something else is under it.
    /// </summary>
    [Test]
    [Arguments(BroughtChanges)]
    [Arguments(BroughtNothing)]
    [Arguments(SkippedPath)]
    public async Task An_update_that_conflicted_with_nothing_counts_no_conflicts(string output)
    {
        await Assert.That(SvnUpdateOutput.Conflicts(output)).IsEqualTo(0);
    }

    /// <summary>
    /// SVN counts conflicts, not paths: one file whose content and properties both conflict is
    /// two. Reporting one would be re-deciding what SVN already decided.
    /// </summary>
    [Test]
    public async Task Conflicts_of_several_kinds_add_up()
    {
        var output =
            "Updating '.':\nUC   src/b.txt\nC    src/a.txt\n   C src/gone.txt\nUpdated to revision 4.\n"
            + "Summary of conflicts:\n  Text conflicts: 1\n  Property conflicts: 1\n  Tree conflicts: 1\n";

        await Assert.That(SvnUpdateOutput.Conflicts(output)).IsEqualTo(3);
    }

    [Test]
    public async Task A_skipped_path_is_counted_as_skipped()
    {
        await Assert.That(SvnUpdateOutput.SkippedPaths(SkippedPath)).IsEqualTo(1);
    }

    /// <summary>
    /// Both directions, because SVN prints skipped paths under the same "Summary of conflicts"
    /// heading as the three real conflicts, and lumping them together would tell somebody their
    /// files hold merge markers when nothing was touched at all.
    /// </summary>
    [Test]
    public async Task A_skipped_path_is_not_a_conflict_and_a_conflict_is_not_a_skip()
    {
        await Assert.That(SvnUpdateOutput.Conflicts(SkippedPath)).IsEqualTo(0);
        await Assert.That(SvnUpdateOutput.SkippedPaths(TextConflict)).IsEqualTo(0);
    }

    [Test]
    public async Task Windows_line_endings_do_not_hide_a_count()
    {
        var output = "Summary of conflicts:\r\n  Text conflicts: 2\r\n  Skipped paths: 3\r\n";

        await Assert.That(SvnUpdateOutput.Conflicts(output)).IsEqualTo(2);
        await Assert.That(SvnUpdateOutput.SkippedPaths(output)).IsEqualTo(3);
    }

    /// <summary>
    /// A count Subverted cannot read is reported as none rather than as one. Guessing a number here
    /// would put an invented figure in front of the user; zero at least matches the exit code SVN
    /// itself gave.
    /// </summary>
    [Test]
    public async Task A_count_that_is_not_a_number_counts_nothing()
    {
        await Assert
            .That(SvnUpdateOutput.Conflicts("Summary of conflicts:\n  Text conflicts: many\n"))
            .IsEqualTo(0);
        await Assert
            .That(SvnUpdateOutput.SkippedPaths("Summary of conflicts:\n  Skipped paths: -1\n"))
            .IsEqualTo(0);
    }

    /// <summary>A path SVN announces is a notification line, never a count — it carries a status column.</summary>
    [Test]
    public async Task A_file_named_after_a_summary_label_is_not_counted_as_one()
    {
        var output = "Updating '.':\nA    Text conflicts: 9\nUpdated to revision 5.\n";

        await Assert.That(SvnUpdateOutput.Conflicts(output)).IsEqualTo(0);
    }
}
