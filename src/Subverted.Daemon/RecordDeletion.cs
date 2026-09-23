namespace Subverted.Daemon;

/// <summary>
/// Records the deletion of nodes already gone from disk, refusing rather than unlinking one that
/// has come back with changes. Not <see cref="ScheduleDeletion"/>, which removes whatever it is
/// given: this one acts on the daemon's belief that a node is missing, and a belief can be stale.
/// </summary>
public delegate Task<string> RecordDeletion(
    string workingCopyRoot,
    IReadOnlyList<string> paths,
    CancellationToken cancellationToken
);
