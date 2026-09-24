namespace Subverted.Protocol;

/// <summary>
/// The answer to a <see cref="StatusRequest"/> whose <see cref="StatusRequest.HeldScan"/> is still
/// the working copy's current reading: the listing the caller holds is the one it would be sent.
/// Only ever sent to a request that named a scan, so a front-end that never does never sees it.
/// </summary>
/// <param name="ScanId">The scan the held listing came from, and still the current one.</param>
/// <param name="ServerElapsedMilliseconds">Time inside the daemon, as on a full answer.</param>
public sealed record StatusUnchangedResponse(Guid ScanId, double ServerElapsedMilliseconds)
    : DaemonResponse;
