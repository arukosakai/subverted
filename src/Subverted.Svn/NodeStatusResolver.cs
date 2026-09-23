using Subverted.Core;

namespace Subverted.Svn;

/// <summary>
/// Maps a wc.db row plus the node's on-disk state onto a <see cref="NodeStatus"/>.
/// Deliberately pure and I/O-free: this is the rule set most likely to be wrong, so it has to be
/// exhaustively testable without a working copy on disk.
/// </summary>
internal static class NodeStatusResolver
{
    private static readonly DateTime AprEpoch = DateTime.UnixEpoch;

    /// <param name="snapshot">Filesystem state, or <see langword="null"/> when the node is absent.</param>
    public static NodeStatus Resolve(WcDbRow row, NodeSnapshot? snapshot)
    {
        if (row.HasConflict)
        {
            return NodeStatus.Conflicted;
        }

        if (row.Presence == "incomplete")
        {
            return NodeStatus.Incomplete;
        }

        // A delete is decided by its row alone: `svn delete --keep-local` leaves the file on disk
        // and svn still prints D.
        if (row.OpDepth > 0 && row.Presence is "base-deleted" or "not-present")
        {
            return NodeStatus.Deleted;
        }

        // Below its operation root and not normal: a presence this build does not know.
        if (row.OpDepth > 0 && !row.IsOperationRoot && !row.IsWithinCopy)
        {
            return NodeStatus.NeedsPristineCompare;
        }

        // Ahead of the operation root on purpose: an add, copy or replace with its file gone or
        // the wrong kind in its place prints ! or ~, never A or R.
        if (snapshot is null)
        {
            return NodeStatus.Missing;
        }

        if (IsObstructed(row.Kind, snapshot))
        {
            return NodeStatus.Obstructed;
        }

        if (row.IsOperationRoot)
        {
            return ResolveOperationRoot(row);
        }

        // A directory has no content of its own to compare; its children carry the changes.
        return snapshot is FileNode file
            ? CompareAgainstRecordedState(row, file)
            : NodeStatus.Unmodified;
    }

    /// <summary>
    /// Only the two mismatches SVN itself calls obstructed. A <see cref="NodeKind.Symlink"/> or
    /// <see cref="NodeKind.Unknown"/> row is left alone rather than guessed at: the index reports
    /// what the filesystem says a symlink points at, so calling that a mismatch would obstruct
    /// every symlink in the tree.
    /// </summary>
    private static bool IsObstructed(NodeKind versionedKind, NodeSnapshot snapshot) =>
        (versionedKind, snapshot) switch
        {
            (NodeKind.File, DirectoryNode) => true,
            (NodeKind.Directory, FileNode) => true,
            _ => false,
        };

    /// <summary>
    /// An add, copy or move aimed at this very path. Its content is never compared — an edited add
    /// is still <c>A</c> — and it is a replace when any layer lies beneath it, not only BASE.
    /// </summary>
    private static NodeStatus ResolveOperationRoot(WcDbRow row) =>
        row.Presence switch
        {
            "normal" when row.LowerOpDepth is not null => NodeStatus.Replaced,
            "normal" => NodeStatus.Added,
            _ => NodeStatus.NeedsPristineCompare,
        };

    /// <summary>
    /// SVN's own fast path. Matching size and mtime prove a file is clean; a mismatch proves
    /// nothing, because saving a file without editing it moves the mtime. So the negative case
    /// escalates to a content compare instead of reporting a modification.
    /// </summary>
    private static NodeStatus CompareAgainstRecordedState(WcDbRow row, FileNode file)
    {
        if (
            row.RecordedSize is not { } recordedSize
            || row.RecordedModTime is not { } recordedModTime
        )
        {
            return NodeStatus.NeedsPristineCompare;
        }

        if (file.Length != recordedSize)
        {
            return NodeStatus.Modified;
        }

        return ToAprTime(file.LastWriteTimeUtc) == recordedModTime
            ? NodeStatus.Unmodified
            : NodeStatus.NeedsPristineCompare;
    }

    /// <summary>Converts to APR time — microseconds since the Unix epoch, as stored by SVN.</summary>
    private static long ToAprTime(DateTime utc) => (long)(utc - AprEpoch).TotalMicroseconds;
}
