using Subverted.Core;

namespace Subverted.Daemon;

/// <summary>
/// Releases the write locks and finishes the interrupted operations left in a working copy.
/// Declared here rather than taken as an SVN type so the request handler can be tested without a
/// working copy or an `svn` on PATH.
/// </summary>
public delegate Task<CleanupOutcome> CleanUpWorkingCopy(
    string workingCopyRoot,
    CancellationToken cancellationToken
);
