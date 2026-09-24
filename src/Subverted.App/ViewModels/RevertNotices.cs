using Subverted.Protocol;

namespace Subverted.App.ViewModels;

/// <summary>
/// What the view says about a revert's answer. A revert that failed may have reverted part of what
/// it was given, so a failure is never reported as "nothing happened".
/// </summary>
public static class RevertNotices
{
    private const string PartWay =
        "Some of it may already be reverted; the list shows what is left.";

    /// <param name="target">The reverted path as the list shows it.</param>
    public static Notice For(string target, DaemonResponse response) =>
        response switch
        {
            RevertResponse => new Notice(NoticeKind.Succeeded, $"Reverted {target}", null, null),
            ErrorResponse error => new Notice(
                NoticeKind.Uncertain,
                $"{target} was not reverted",
                error.Message,
                PartWay
            ),
            _ => new Notice(
                NoticeKind.Uncertain,
                "The revert's answer was not understood",
                $"The daemon answered with {response.GetType().Name}, which is not a revert.",
                PartWay
            ),
        };

    /// <summary>Confirmed after the listing stopped showing anything under the target to revert.</summary>
    public static Notice NothingLeft(string target) =>
        new(NoticeKind.NothingWritten, $"Nothing left to revert in {target}", null, null);

    public static Notice Unreachable(string message) =>
        new(NoticeKind.Uncertain, "The daemon is not answering", message, PartWay);
}
