namespace Subverted.Protocol;

/// <param name="ReleasedWriteLocks">
/// Directories that were write-locked and no longer are. Empty is the healthy case and means the
/// working copy was never stuck — a front-end that reported that as "cleaned" would be inventing
/// work that did not happen.
/// </param>
/// <param name="FinishedOperations">
/// Queued steps of an interrupted operation that were run. Any at all and <c>svn</c> itself had
/// been refusing to read this working copy until now.
/// </param>
/// <param name="RemainingWriteLocks">
/// Locks still held once cleanup had run. Non-empty means it did not work and the copy is still
/// wedged, which is the one outcome a person has to be told about.
/// </param>
public sealed record CleanupResponse(
    IReadOnlyList<string> ReleasedWriteLocks,
    int FinishedOperations,
    IReadOnlyList<string> RemainingWriteLocks
) : DaemonResponse;
