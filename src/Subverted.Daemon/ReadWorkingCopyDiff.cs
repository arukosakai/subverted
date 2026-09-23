namespace Subverted.Daemon;

/// <summary>
/// Reads a path's local changes as a unified diff. Declared here for the same reason as
/// <see cref="ReadRevisionLog"/>.
/// </summary>
public delegate Task<string> ReadWorkingCopyDiff(
    string workingCopyRoot,
    string path,
    CancellationToken cancellationToken
);
