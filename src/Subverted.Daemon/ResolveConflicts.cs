using Subverted.Core;

namespace Subverted.Daemon;

/// <summary>
/// Marks conflicted nodes under these paths resolved. Declared here rather than taken as an SVN
/// type so the request handler can be tested without a working copy or an `svn` on PATH.
/// </summary>
public delegate Task<ResolveOutcome> ResolveConflicts(
    string workingCopyRoot,
    IReadOnlyList<string> paths,
    ConflictResolution resolution,
    CancellationToken cancellationToken
);
