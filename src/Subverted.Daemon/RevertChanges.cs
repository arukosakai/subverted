namespace Subverted.Daemon;

/// <summary>
/// Throws local changes under these paths away and reports what the client said. A delegate for the
/// same reason as the others — and with the additional one that a test which accidentally reached
/// the real implementation would delete files.
/// </summary>
public delegate Task<string> RevertChanges(
    string workingCopyRoot,
    IReadOnlyList<string> paths,
    CancellationToken cancellationToken
);
