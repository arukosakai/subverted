using Subverted.Core;

namespace Subverted.Daemon;

/// <summary>
/// Brings a path in line with the server. Declared here rather than taken as an SVN type so the
/// request handler can be tested without a repository or a server.
/// </summary>
public delegate Task<UpdateOutcome> BringUpToDate(
    string workingCopyRoot,
    string path,
    CancellationToken cancellationToken
);
