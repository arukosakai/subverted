using Subverted.Protocol;

namespace Subverted.App.ViewModels;

/// <summary>
/// What the view says about a delete. One that failed may have deleted part of what it was given,
/// so a failure after sending is never reported as "nothing happened"; one stopped before sending is.
/// </summary>
public static class DeleteNotices
{
    private const string PartWay =
        "Some of it may already be deleted; the list shows what is left.";

    /// <param name="target">The deleted path as the list shows it.</param>
    public static Notice For(string target, DaemonResponse response) =>
        response switch
        {
            DeleteResponse => new Notice(
                NoticeKind.Succeeded,
                $"Deleted {target}",
                null,
                "Commit to record it. Until then, Revert brings back what SVN had."
            ),
            ErrorResponse error => new Notice(
                NoticeKind.Uncertain,
                $"{target} was not deleted",
                error.Message,
                PartWay
            ),
            _ => new Notice(
                NoticeKind.Uncertain,
                "The delete's answer was not understood",
                $"The daemon answered with {response.GetType().Name}, which is not a delete.",
                PartWay
            ),
        };

    /// <summary>The listing read before sending no longer has the target in it.</summary>
    public static Notice NothingLeft(string target) =>
        new(NoticeKind.NothingWritten, $"Nothing left to delete at {target}", null, null);

    /// <param name="reason">Why the target is not one to delete, as the listing read before sending shows it.</param>
    public static Notice Refused(string target, string reason) =>
        new(NoticeKind.NothingWritten, $"{target} was not deleted", reason, "Nothing was written.");

    /// <param name="detail">The daemon's text, or why there was none.</param>
    public static Notice CouldNotLook(string target, string detail) =>
        new(
            NoticeKind.NothingWritten,
            $"Could not see what deleting {target} would take",
            detail,
            "Nothing was deleted."
        );

    /// <summary>The daemon went away after the delete was sent.</summary>
    public static Notice Unreachable(string message) =>
        new(NoticeKind.Uncertain, "The daemon is not answering", message, PartWay);
}
