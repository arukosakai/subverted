using Subverted.App.ViewModels;
using Subverted.Protocol;

namespace Subverted.App.Tests;

/// <summary>The three answers D32 distinguishes read differently, and each says what it left behind.</summary>
public sealed class CommitNoticesTests
{
    private static readonly SelectionSchedule NothingMarked = new([], [], []);

    [Test]
    public async Task A_commit_names_its_revision_and_what_it_marked()
    {
        var schedule = new SelectionSchedule(["a.png"], [], [new RecordedMove("x", "y")]);

        var notice = CommitNotices.For(
            new CommitSelectionResponse(1825, schedule, "Committed revision 1825.")
        );

        await Assert
            .That(notice)
            .IsEqualTo(
                new Notice(
                    NoticeKind.Succeeded,
                    "Committed r1825",
                    "1 rename recorded, 1 added",
                    null
                )
            );
    }

    [Test]
    public async Task A_commit_with_nothing_to_send_is_success_that_says_so()
    {
        var notice = CommitNotices.For(new CommitSelectionResponse(null, NothingMarked, ""));

        await Assert
            .That(notice)
            .IsEqualTo(
                new Notice(
                    NoticeKind.Succeeded,
                    "Nothing needed sending",
                    null,
                    "The ticked changes already match the repository."
                )
            );
    }

    [Test]
    [Arguments(SelectionStep.Move, "Not committed: a rename could not be recorded")]
    [Arguments(SelectionStep.Addition, "Not committed: new files could not be added")]
    [Arguments(SelectionStep.Deletion, "Not committed: deletions could not be recorded")]
    [Arguments(SelectionStep.Commit, "Not committed: the server did not take it")]
    public async Task Stopping_after_marking_names_the_step_and_shows_svn_s_text(
        SelectionStep step,
        string headline
    )
    {
        var notice = CommitNotices.For(
            new SelectionNotCommittedResponse(NothingMarked, step, "", "svn: E165001: hook refused")
        );

        await Assert.That(notice.Kind).IsEqualTo(NoticeKind.LeftMarked);
        await Assert.That(notice.Headline).IsEqualTo(headline);
        await Assert.That(notice.Detail).IsEqualTo("svn: E165001: hook refused");
    }

    /// <summary>No rollback, so the hint has to say the rows now read A and D and a retry carries on.</summary>
    [Test]
    public async Task Stopping_after_marking_says_what_stays_marked_and_that_a_retry_carries_on()
    {
        var schedule = new SelectionSchedule(["new.png", "b.png"], ["gone.png"], []);

        var notice = CommitNotices.For(
            new SelectionNotCommittedResponse(schedule, SelectionStep.Commit, "", "refused")
        );

        await Assert
            .That(notice.Hint)
            .IsEqualTo(
                "Nothing reached the server. What was marked stays marked (2 added, 1 deleted), so "
                    + "those rows now read Added or Deleted, and committing again carries on from "
                    + "there. Your message is kept."
            );
    }

    [Test]
    public async Task Stopping_before_any_mark_finished_still_says_a_retry_carries_on()
    {
        var notice = CommitNotices.For(
            new SelectionNotCommittedResponse(NothingMarked, SelectionStep.Move, "", "refused")
        );

        await Assert
            .That(notice.Hint)
            .IsEqualTo(
                "Nothing reached the server. What was marked stays marked, so those rows now read "
                    + "Added or Deleted, and committing again carries on from there. Your message is kept."
            );
    }

    [Test]
    public async Task A_refusal_shows_its_lines_and_says_nothing_was_written()
    {
        var notice = CommitNotices.For(
            new ErrorResponse(
                DaemonErrorKind.RequestRefused,
                "'a' is in conflict.\n'b' is ignored."
            )
        );

        await Assert
            .That(notice)
            .IsEqualTo(
                new Notice(
                    NoticeKind.NothingWritten,
                    "Nothing was committed",
                    "'a' is in conflict.\n'b' is ignored.",
                    "Nothing in the working copy was changed. Your message is kept."
                )
            );
    }

    [Test]
    public async Task An_answer_that_is_not_a_commit_is_uncertain()
    {
        var notice = CommitNotices.For(new RevertResponse(""));

        await Assert.That(notice.Kind).IsEqualTo(NoticeKind.Uncertain);
        await Assert
            .That(notice.Detail)
            .IsEqualTo("The daemon answered with RevertResponse, which is not a commit.");
    }

    /// <summary>It may have gone away mid-commit, so the notice must not claim nothing happened.</summary>
    [Test]
    public async Task An_unreachable_daemon_is_uncertain_rather_than_nothing_written()
    {
        var notice = CommitNotices.Unreachable("connection refused");

        await Assert
            .That(notice)
            .IsEqualTo(
                new Notice(
                    NoticeKind.Uncertain,
                    "The daemon is not answering",
                    "connection refused",
                    "It may have got part of the way; the list shows what happened once the daemon "
                        + "is back. Your message is kept."
                )
            );
    }
}
