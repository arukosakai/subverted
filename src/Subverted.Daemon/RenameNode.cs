using Subverted.Core;

namespace Subverted.Daemon;

/// <summary>
/// Renames a node, keeping its history, and says which kind of rename it turned out to be.
/// Declared here rather than taken as an SVN type so the request handler can be tested without a
/// working copy or an <c>svn</c> on PATH.
/// </summary>
public delegate Task<MoveOutcome> RenameNode(
    string workingCopyRoot,
    string source,
    string destination,
    CancellationToken cancellationToken
);
