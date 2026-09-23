using Subverted.Core;

namespace Subverted.Daemon;

/// <summary>
/// Reads a path's revision history. Declared here rather than taken as an SVN type so the request
/// handler can be tested without a repository, a server, or <c>svn</c> on PATH.
/// </summary>
/// <param name="limit">How many revisions, newest first, or null for all of them.</param>
public delegate Task<IReadOnlyList<RevisionEntry>> ReadRevisionLog(
    string workingCopyRoot,
    string path,
    int? limit,
    CancellationToken cancellationToken
);
