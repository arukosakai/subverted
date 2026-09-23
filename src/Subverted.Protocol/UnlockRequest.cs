using Subverted.Core;

namespace Subverted.Protocol;

/// <param name="Paths">Absolute paths, all inside one working copy.</param>
/// <param name="Foreign">Whether a lock somebody else holds may be broken.</param>
public sealed record UnlockRequest(
    IReadOnlyList<string> Paths,
    ForeignLock Foreign = ForeignLock.Respected
) : DaemonRequest;
