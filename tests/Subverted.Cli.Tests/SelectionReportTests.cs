using Subverted.Protocol;

namespace Subverted.Cli.Tests;

/// <summary>
/// What a commit that marks on the way prints. The marks are the part nobody typed a command for,
/// so they are listed whether the commit went through or stopped — and when it stopped, the report
/// has to say they are still there.
/// </summary>
public sealed class SelectionReportTests
{
    private static readonly SelectionSchedule Nothing = new([], [], []);

    private static readonly SelectionSchedule EveryKind = new(
        Added: ["newdir", "newdir/n.txt"],
        Deleted: ["gone"],
        Moved: [new RecordedMove("art/hero.png", "art/protagonist.png")]
    );

    [Test]
    public async Task A_commit_lists_every_mark_it_made_before_svns_own_text()
    {
        var response = new CommitSelectionResponse(
            7,
            EveryKind,
            "Adding         newdir\nCommitted revision 7.\n"
        );

        await Assert
            .That(SelectionReport.Committed(response))
            .IsEquivalentTo([
                "marked on the way: 1 renamed, 2 added, 1 deleted",
                "  moved    art/hero.png -> art/protagonist.png",
                "  added    newdir",
                "  added    newdir/n.txt",
                "  deleted  gone",
                "Adding         newdir",
                "Committed revision 7.",
            ]);
    }

    /// <summary>An edit needs no mark, so a commit of edits alone reads exactly as a plain one.</summary>
    [Test]
    public async Task A_commit_that_marked_nothing_prints_svns_text_alone()
    {
        var response = new CommitSelectionResponse(8, Nothing, "Committed revision 8.\n");

        await Assert
            .That(SelectionReport.Committed(response))
            .IsEquivalentTo(["Committed revision 8."]);
    }

    [Test]
    public async Task A_commit_with_no_revision_says_there_was_nothing_to_commit()
    {
        var response = new CommitSelectionResponse(null, Nothing, string.Empty);

        await Assert
            .That(SelectionReport.Committed(response))
            .IsEquivalentTo(["nothing to commit"]);
    }

    [Test]
    public async Task A_revision_with_nothing_printed_before_it_still_reports_the_revision()
    {
        var response = new CommitSelectionResponse(31, Nothing, string.Empty);

        await Assert.That(SelectionReport.Committed(response)).IsEquivalentTo(["committed r31"]);
    }

    [Test]
    [Arguments(SelectionStep.Move, "recording a rename failed")]
    [Arguments(SelectionStep.Addition, "adding the new nodes failed")]
    [Arguments(SelectionStep.Deletion, "recording the deletions failed")]
    [Arguments(SelectionStep.Commit, "every mark was made, then sending failed")]
    public async Task A_stopped_commit_says_which_step_failed_and_why(
        SelectionStep step,
        string where
    )
    {
        var response = new SelectionNotCommittedResponse(
            Nothing,
            step,
            string.Empty,
            "svn: E165001: hook refused\n"
        );

        await Assert
            .That(SelectionReport.Failure(response))
            .IsEqualTo($"not committed — {where}: svn: E165001: hook refused");
    }

    /// <summary>
    /// The contract that sets this response apart from an error: marks were made and nothing was
    /// rolled back. A report that dropped them would leave somebody thinking their tree is as it was.
    /// </summary>
    [Test]
    public async Task A_stopped_commit_lists_what_it_marked_and_says_the_marks_are_still_in_place()
    {
        var response = new SelectionNotCommittedResponse(
            EveryKind,
            SelectionStep.Commit,
            "A         newdir\n",
            "hook refused"
        );

        var lines = SelectionReport.LeftInPlace(response);

        await Assert
            .That(lines.Take(6))
            .IsEquivalentTo([
                "A         newdir",
                "marked before it stopped: 1 renamed, 2 added, 1 deleted",
                "  moved    art/hero.png -> art/protagonist.png",
                "  added    newdir",
                "  added    newdir/n.txt",
                "  deleted  gone",
            ]);
        await Assert.That(lines.Count).IsEqualTo(7);
        await Assert
            .That(lines[6])
            .StartsWith("Nothing was rolled back: these marks are still in place");
    }

    /// <summary>
    /// A client that failed part-way may have marked paths the schedule does not list, so even an
    /// empty list still points at <c>sv st</c> rather than implying nothing changed.
    /// </summary>
    [Test]
    public async Task A_stopped_commit_with_no_listed_marks_still_points_at_sv_st()
    {
        var response = new SelectionNotCommittedResponse(
            Nothing,
            SelectionStep.Addition,
            string.Empty,
            "E155004"
        );

        var lines = SelectionReport.LeftInPlace(response);

        await Assert.That(lines.Count).IsEqualTo(1);
        await Assert.That(lines[0]).Contains("`sv st` shows exactly what is marked now");
    }

    /// <summary>
    /// The protocol could grow a step this build has never heard of. Naming the wrong one would
    /// send somebody looking for a failure in the wrong place.
    /// </summary>
    [Test]
    public async Task A_step_this_build_does_not_know_is_refused_rather_than_misnamed()
    {
        var response = new SelectionNotCommittedResponse(
            Nothing,
            (SelectionStep)99,
            string.Empty,
            "?"
        );

        await Assert
            .That(() => SelectionReport.Failure(response))
            .Throws<ArgumentOutOfRangeException>();
    }
}
