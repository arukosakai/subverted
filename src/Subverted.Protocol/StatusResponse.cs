using Subverted.Core;

namespace Subverted.Protocol;

/// <param name="ServedFromWarmIndex">
/// False means this request paid for a full scan. The M1 speed criterion is about the true case;
/// a front-end that never sees it is looking at a daemon whose watcher is not keeping up.
/// </param>
/// <param name="ServerElapsedMilliseconds">
/// Time inside the daemon, excluding IPC. Compared against the caller's wall clock it separates a
/// slow scan from a slow socket.
/// </param>
/// <param name="UnfinishedOperations">
/// Queued steps of an operation that was interrupted. Non-zero means <c>svn</c> itself refuses to
/// read this working copy — <c>E155037</c> — while Subverted answered anyway, so the entries are
/// its own reading and not one SVN would agree it could make.
/// </param>
/// <param name="UnrecordedMoves">
/// Renames made outside SVN, paired by content. Each one appears in <paramref name="Entries"/> as
/// an unrelated missing node and unversioned file, which is all SVN can see of it — committing in
/// that state ends the file's history at its old name. Empty from a reader that cannot detect them.
/// </param>
/// <param name="ScanId">
/// Which reading of the working copy this answer was filtered from: two answers to the same request
/// with the same id carry the same entries. Unique across daemons and restarts, not only within one.
/// Null from a daemon that predates it, which cannot be asked <see cref="StatusRequest.HeldScan"/>.
/// </param>
public sealed record StatusResponse(
    WorkingCopyInfo Info,
    IReadOnlyList<WorkingCopyEntry> Entries,
    bool ServedFromWarmIndex,
    double ServerElapsedMilliseconds,
    int UnfinishedOperations,
    IReadOnlyList<UnrecordedMove> UnrecordedMoves,
    Guid? ScanId = null
) : DaemonResponse;
