using Subverted.Protocol;

namespace Subverted.Cli;

/// <summary>
/// What <c>sv daemon status</c> prints. Leads with the watcher, because a working copy the daemon
/// has stopped watching is the one fact a user needs out of this command.
/// </summary>
public static class DaemonInfoReport
{
    public static IReadOnlyList<string> Lines(DaemonInfoResponse response)
    {
        var lines = new List<string>
        {
            $"subverted-daemon {response.Version}, up {response.UptimeSeconds:F0}s",
        };

        if (response.WorkingCopies.Count == 0)
        {
            lines.Add("no working copies held yet");
            return lines;
        }

        lines.Add(string.Empty);
        lines.AddRange(
            response.WorkingCopies.Select(copy =>
                $"  {Describe(copy.WatcherState), -12} {copy.EntryCount, 8} nodes  {copy.RootPath}"
            )
        );
        return lines;
    }

    private static string Describe(WatcherState state) =>
        state switch
        {
            WatcherState.Healthy => "watching",
            WatcherState.Recovering => "rescanning",
            _ => "NOT WATCHED",
        };
}
