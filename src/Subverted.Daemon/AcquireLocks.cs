using Subverted.Core;

namespace Subverted.Daemon;

/// <summary>
/// Takes repository locks on these paths. Declared here rather than taken as an SVN type so the
/// request handler can be tested without a repository or a server.
/// </summary>
public delegate Task<LockOutcome> AcquireLocks(
    string workingCopyRoot,
    IReadOnlyList<string> paths,
    string? comment,
    ForeignLock foreign,
    CancellationToken cancellationToken
);
