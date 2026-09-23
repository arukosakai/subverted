using Subverted.Core;

namespace Subverted.Daemon;

/// <summary>
/// Reads a whole working copy's status through the <c>svn</c> client — every node, ignored ones
/// included, because the daemon filters per request rather than per scan.
/// </summary>
public delegate Task<IReadOnlyList<WorkingCopyEntry>> ReadWorkingCopyStatus(
    string workingCopyRoot,
    CancellationToken cancellationToken
);
