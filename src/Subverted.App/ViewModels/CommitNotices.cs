using Subverted.Protocol;

namespace Subverted.App.ViewModels;

/// <summary>
/// What the view says about a commit selection's answer. The three answers read differently on
/// purpose: committed; marked but not sent, where a retry carries on; and refused, where nothing
/// was written (D32).
/// </summary>
public static class CommitNotices
{
    private const string MessageKept = "Your message is kept.";

    public static Notice For(DaemonResponse response) =>
        response switch
        {
            CommitSelectionResponse { Revision: { } revision } committed => new Notice(
                NoticeKind.Succeeded,
                $"Committed r{revision}",
                ScheduleText.Of(committed.Scheduled),
                null
            ),
            CommitSelectionResponse => new Notice(
                NoticeKind.Succeeded,
                "Nothing needed sending",
                null,
                "The ticked changes already match the repository."
            ),
            SelectionNotCommittedResponse stopped => new Notice(
                NoticeKind.LeftMarked,
                Stopped(stopped.FailedStep),
                stopped.Failure,
                LeftMarked(stopped.Scheduled)
            ),
            ErrorResponse refused => new Notice(
                NoticeKind.NothingWritten,
                "Nothing was committed",
                refused.Message,
                "Nothing in the working copy was changed. " + MessageKept
            ),
            _ => new Notice(
                NoticeKind.Uncertain,
                "The commit's answer was not understood",
                $"The daemon answered with {response.GetType().Name}, which is not a commit.",
                Uncertain
            ),
        };

    /// <summary>No daemon answered, or it went away mid-answer — so it may have committed.</summary>
    public static Notice Unreachable(string message) =>
        new(NoticeKind.Uncertain, "The daemon is not answering", message, Uncertain);

    private const string Uncertain =
        "It may have got part of the way; the list shows what happened once the daemon is back. "
        + MessageKept;

    private static string Stopped(SelectionStep step) =>
        step switch
        {
            SelectionStep.Move => "Not committed: a rename could not be recorded",
            SelectionStep.Addition => "Not committed: new files could not be added",
            SelectionStep.Deletion => "Not committed: deletions could not be recorded",
            _ => "Not committed: the server did not take it",
        };

    private static string LeftMarked(SelectionSchedule scheduled)
    {
        var marked = ScheduleText.Of(scheduled) is { } text ? $" ({text})" : "";
        return $"Nothing reached the server. What was marked stays marked{marked}, so those rows "
            + "now read Added or Deleted, and committing again carries on from there. "
            + MessageKept;
    }
}
