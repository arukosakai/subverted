using Subverted.Core;

namespace Subverted.Daemon;

/// <summary>
/// Reads which BASE revisions a path and everything below it are at, or null when none has one.
/// Declared here for the same reason as <see cref="ReadRevisionLog"/>.
/// </summary>
public delegate Task<BaseRevisionRange?> ReadBaseRevisionRange(
    string workingCopyRoot,
    string path,
    CancellationToken cancellationToken
);
