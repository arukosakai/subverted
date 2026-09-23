using Subverted.Core;

namespace Subverted.Daemon;

/// <summary>
/// Sends local changes under these paths to the server. Declared here rather than taken as an SVN
/// type so the request handler can be tested without a repository or a server.
/// </summary>
public delegate Task<CommitOutcome> CommitChanges(
    string workingCopyRoot,
    IReadOnlyList<string> paths,
    string message,
    CommitScope scope,
    CancellationToken cancellationToken
);
