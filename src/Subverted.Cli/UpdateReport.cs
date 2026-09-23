using Subverted.Protocol;

namespace Subverted.Cli;

/// <summary>
/// What an update did, as the lines to print. SVN's own text already names every path it touched
/// and ends with the revision reached, so it is passed through; what is added is the part SVN only
/// states as a count, because a conflict it merged around is easy to scroll past.
/// </summary>
public static class UpdateReport
{
    public static IReadOnlyList<string> Lines(UpdateResponse response) =>
        [
            .. NotificationLines.OrElse(response.Notifications, "already up to date"),
            .. Attention(response),
        ];

    private static IEnumerable<string> Attention(UpdateResponse response)
    {
        if (response.Conflicts > 0)
        {
            yield return $"{response.Conflicts} conflict(s) left for you to resolve — "
                + "the files are on disk with SVN's markers beside them.";
        }

        if (response.SkippedPaths > 0)
        {
            yield return $"{response.SkippedPaths} path(s) skipped — nothing was updated there.";
        }
    }
}
