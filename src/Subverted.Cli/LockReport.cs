using Subverted.Protocol;

namespace Subverted.Cli;

/// <summary>
/// What a lock or an unlock did, as the lines to print. SVN's own text is passed through from both
/// of its streams; what is added is the count, because a warning scrolls past exactly as easily as
/// the exit code hides it.
/// </summary>
public static class LockReport
{
    /// <remarks>
    /// The last line is the whole point. <c>svn lock</c> prints nothing on stdout for a path it
    /// refused, so without it a refusal reads as "nothing to do" rather than as "somebody else is
    /// editing this".
    /// </remarks>
    public static IReadOnlyList<string> Locked(LockResponse response) =>
        Lines(
            response.Notifications,
            response.Refusals,
            "nothing locked",
            "were NOT locked — see the warning(s) above. Do not start work on them."
        );

    public static IReadOnlyList<string> Unlocked(UnlockResponse response) =>
        Lines(
            response.Notifications,
            response.Refusals,
            "nothing unlocked",
            "had no lock on the server — see the warning(s) above."
        );

    private static IReadOnlyList<string> Lines(
        string notifications,
        IReadOnlyList<string> refusals,
        string whenNothingHappened,
        string whatWentWrong
    ) =>
        [
            .. NotificationLines.OrElse(notifications, whenNothingHappened),
            .. refusals,
            .. Summary(refusals.Count, whatWentWrong),
        ];

    private static IEnumerable<string> Summary(int refused, string whatWentWrong)
    {
        if (refused > 0)
        {
            yield return $"{refused} path(s) {whatWentWrong}";
        }
    }
}
