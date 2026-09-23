namespace Subverted.Protocol;

/// <param name="UptimeSeconds">Since the daemon started, not since it last served a request.</param>
public sealed record DaemonInfoResponse(
    string Version,
    double UptimeSeconds,
    IReadOnlyList<WatchedWorkingCopy> WorkingCopies
) : DaemonResponse;
