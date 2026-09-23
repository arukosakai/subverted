namespace Subverted.Daemon;

/// <summary>
/// Schedules these paths for deletion and removes them from disk, reporting what the client said.
/// A delegate for the same reason as the others — and with the additional one that a test which
/// accidentally reached the real implementation would delete files.
/// </summary>
public delegate Task<string> ScheduleDeletion(
    string workingCopyRoot,
    IReadOnlyList<string> paths,
    CancellationToken cancellationToken
);
