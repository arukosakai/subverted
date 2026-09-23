using Subverted.Protocol;

namespace Subverted.Cli;

/// <summary>
/// What a removal did, as the lines to print. SVN prints a <c>D</c> line per versioned node and
/// nothing whatever for an unversioned one, so printing its output alone would leave someone who
/// deleted five untracked files looking at an empty screen.
/// </summary>
public static class RemovalReport
{
    /// <param name="unrecoverableCount">
    /// How many of the targets had no pristine behind them, counted from the preview that was
    /// confirmed. The daemon cannot supply it: by the time the client has run, they are gone.
    /// </param>
    public static IReadOnlyList<string> Lines(DeleteResponse response, int unrecoverableCount)
    {
        var lines = new List<string>(NotificationLines.Of(response.Notifications));

        if (unrecoverableCount > 0)
        {
            lines.Add(
                $"{unrecoverableCount} file(s) SVN does not track were removed from disk. "
                    + "Nothing here can bring those back."
            );
        }

        return lines.Count > 0 ? lines : ["nothing removed"];
    }
}
