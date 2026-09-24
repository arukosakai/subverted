using Subverted.Core;
using Subverted.Protocol;

namespace Subverted.Cli;

/// <summary>
/// What a removal did, as the lines to print. SVN prints a <c>D</c> line per versioned node and
/// nothing whatever for an unversioned file or an external checkout, so printing its output alone
/// would leave someone who deleted five untracked files looking at an empty screen.
/// </summary>
public static class RemovalReport
{
    /// <param name="confirmed">
    /// The preview that was confirmed. The daemon cannot say what went silently: by the time the
    /// client has run, it is gone.
    /// </param>
    public static IReadOnlyList<string> Lines(DeleteResponse response, RemovalPreview confirmed)
    {
        var lines = new List<string>(NotificationLines.Of(response.Notifications));

        var untracked = confirmed.Unrecoverable.Count(node =>
            node.Entry.Status is NodeStatus.Unversioned or NodeStatus.Ignored
        );
        if (untracked > 0)
        {
            lines.Add(
                $"{untracked} file(s) SVN does not track were removed from disk. "
                    + "Nothing here can bring those back."
            );
        }

        var externals = confirmed.Unrecoverable.Count(node =>
            node.Entry.Status == NodeStatus.External
        );
        if (externals > 0)
        {
            lines.Add(
                $"{externals} external checkout(s) were removed from disk with everything in them, "
                    + "their own edits included. Nothing here can bring those edits back."
            );
        }

        return lines.Count > 0 ? lines : ["nothing removed"];
    }
}
