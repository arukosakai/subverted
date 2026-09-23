using Subverted.Core;

namespace Subverted.Protocol;

/// <param name="Paths">
/// Absolute paths, all inside one working copy. Files — SVN will not lock a directory. The daemon
/// resolves the first to a root and rejects the request if any of the others fall outside it.
/// </param>
/// <param name="Comment">What the lock is for, shown to whoever else tries the path, or null.</param>
/// <param name="Foreign">Whether a lock somebody else holds may be taken.</param>
public sealed record LockRequest(
    IReadOnlyList<string> Paths,
    string? Comment,
    ForeignLock Foreign = ForeignLock.Respected
) : DaemonRequest;
