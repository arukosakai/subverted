using Subverted.Core;
using Subverted.Protocol;

namespace Subverted.Cli;

/// <summary>
/// What <c>sv commit --mark</c> names, one node at a time, so a selection commit can stand in for
/// SVN's recursive one. Pure, because leaving a node out here is a change that silently stays local.
/// </summary>
public static class MarkingCommitTargets
{
    /// <param name="status">A listing of the working copy the paths are in, as <c>sv st</c> asks for it.</param>
    /// <param name="paths">Absolute targets, as the user gave them.</param>
    /// <param name="comparison">How this platform compares paths.</param>
    /// <returns>
    /// Every changed node under the paths, as relative paths, in the order the daemon listed them.
    /// Both halves of a rename are in it when both are under the paths; when only one is, the
    /// daemon refuses the lot rather than commit the half as a delete or an add.
    /// </returns>
    public static IReadOnlyList<string> Under(
        StatusResponse status,
        IReadOnlyList<string> paths,
        StringComparison comparison
    ) => [.. AffectedNodes.Under(status, paths, comparison, IsSent).Select(entry => entry.RelPath)];

    /// <summary>
    /// A conflicted, obstructed or incomplete node is named anyway: plain <c>svn commit</c> fails on
    /// one, and the daemon refuses it before writing anything instead of committing around it.
    /// An external is a working copy of its own, which a recursive commit passes over too.
    /// </summary>
    private static bool IsSent(WorkingCopyEntry entry) =>
        entry.IsConflicted
        || entry.Status switch
        {
            NodeStatus.Unmodified => entry.PropertyStatus == PropertyStatus.Modified,
            NodeStatus.External or NodeStatus.Ignored => false,
            _ => true,
        };
}
