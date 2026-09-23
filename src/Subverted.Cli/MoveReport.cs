using Subverted.Core;
using Subverted.Protocol;

namespace Subverted.Cli;

/// <summary>
/// What a rename did, as the lines to print. Pure, so both the wording and the two things SVN does
/// not mention — that a rename was recorded after the fact, and that a lock did not follow it — are
/// testable without a working copy.
/// </summary>
public static class MoveReport
{
    /// <param name="sourceHeldLock">
    /// The source held this working copy's lock token before the move. SVN leaves it on the old
    /// path, which now only exists to be deleted — so the file is not locked under its new name, and
    /// nothing says so.
    /// </param>
    public static IReadOnlyList<string> Lines(MoveResponse response, bool sourceHeldLock)
    {
        var lines = new List<string>();

        if (response.Route == MoveRoute.AlreadyRenamed)
        {
            lines.Add(
                "recorded a rename that had already been made on disk — SVN keeps the file's "
                    + "history under the new name now."
            );
        }

        lines.AddRange(NotificationLines.OrElse(response.Notifications, "nothing renamed"));

        if (sourceHeldLock)
        {
            lines.Add(string.Empty);
            lines.Add(
                "warning: your lock stayed on the old path and did not follow the file. "
                    + "Lock the new name to keep holding it."
            );
        }

        return lines;
    }
}
