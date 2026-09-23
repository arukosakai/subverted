using Subverted.Core;

namespace Subverted.Daemon;

/// <param name="ServedFromWarmIndex">False when this call is the one that paid for the scan.</param>
/// <param name="UnfinishedOperations">
/// Queued steps of an interrupted operation, read with the entries and standing as long as they do.
/// Zero from a reader that cannot see them, which is not the same claim as a healthy working copy —
/// on that reader a wedged copy fails the scan instead.
/// </param>
/// <param name="UnrecordedMoves">
/// Renames made outside SVN, paired against the entries they were found in and standing as long as
/// those do. Empty from a reader that cannot pair them, which is not a claim that none were made.
/// </param>
public sealed record CurrentScan(
    IReadOnlyList<WorkingCopyEntry> Entries,
    bool ServedFromWarmIndex,
    int UnfinishedOperations,
    IReadOnlyList<UnrecordedMove> UnrecordedMoves
);
