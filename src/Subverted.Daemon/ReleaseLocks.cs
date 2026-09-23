using Subverted.Core;

namespace Subverted.Daemon;

/// <summary>
/// Gives repository locks on these paths back. Declared here rather than taken as an SVN type so
/// the request handler can be tested without a repository or a server.
/// </summary>
public delegate Task<LockOutcome> ReleaseLocks(
    string workingCopyRoot,
    IReadOnlyList<string> paths,
    ForeignLock foreign,
    CancellationToken cancellationToken
);
