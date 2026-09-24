using Subverted.Frontend;
using Subverted.Protocol;

namespace Subverted.App.ViewModels;

/// <summary>
/// What the view says about a lock's or an unlock's answer. A refusal is shown in SVN's own words,
/// because only they name who holds the file (D22), and it is never drawn as success.
/// </summary>
public static class LockNotices
{
    private const string LookAtTheLine = "The lock icon on its line shows whether you hold it.";

    /// <param name="target">The file as the list shows it.</param>
    public static Notice Locked(string target, DaemonResponse response) =>
        response switch
        {
            LockResponse refused when LockAttention.IsNeeded(refused) => new Notice(
                NoticeKind.NeedsAttention,
                $"{target} was not locked",
                SvnText(refused.Notifications, refused.Refusals),
                "Do not start work on it — SVN's warning says why."
            ),
            LockResponse locked => Done(
                $"Locked {target}",
                $"SVN said nothing about locking {target}",
                locked.Notifications
            ),
            ErrorResponse error => Failed($"{target} could not be locked", error),
            _ => NotUnderstood("lock", "a lock", response),
        };

    /// <param name="target">The file as the list shows it.</param>
    public static Notice Unlocked(string target, DaemonResponse response) =>
        response switch
        {
            UnlockResponse refused when LockAttention.IsNeeded(refused) => new Notice(
                NoticeKind.NeedsAttention,
                $"The lock on {target} had already gone",
                SvnText(refused.Notifications, refused.Refusals),
                "Somebody broke it or took it. This copy does not hold it any more either way."
            ),
            UnlockResponse unlocked => Done(
                $"Unlocked {target}",
                $"SVN said nothing about unlocking {target}",
                unlocked.Notifications
            ),
            ErrorResponse error => Failed($"{target} could not be unlocked", error),
            _ => NotUnderstood("unlock", "an unlock", response),
        };

    public static Notice Unreachable(string message) =>
        new(NoticeKind.Uncertain, "The daemon is not answering", message, LookAtTheLine);

    /// <summary>SVN printing nothing is not a lock taken, so silence is not drawn as one.</summary>
    private static Notice Done(string headline, string whenSilent, string notifications) =>
        Trimmed(notifications) is { Length: > 0 } said
            ? new Notice(NoticeKind.Succeeded, headline, said, null)
            : new Notice(NoticeKind.Uncertain, whenSilent, null, LookAtTheLine);

    private static Notice Failed(string headline, ErrorResponse error) =>
        new(NoticeKind.Uncertain, headline, error.Message, LookAtTheLine);

    private static Notice NotUnderstood(
        string operation,
        string expected,
        DaemonResponse response
    ) =>
        new(
            NoticeKind.Uncertain,
            $"The {operation}'s answer was not understood",
            $"The daemon answered with {response.GetType().Name}, which is not {expected}.",
            LookAtTheLine
        );

    private static string SvnText(string notifications, IReadOnlyList<string> refusals)
    {
        var said = Trimmed(notifications);
        IEnumerable<string> lines = said.Length > 0 ? [said, .. refusals] : refusals;
        return string.Join('\n', lines);
    }

    private static string Trimmed(string notifications) =>
        notifications.Replace("\r\n", "\n", StringComparison.Ordinal).Trim('\n', ' ');
}
