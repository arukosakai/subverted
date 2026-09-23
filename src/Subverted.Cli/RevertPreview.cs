using Subverted.Core;
using Subverted.Protocol;

namespace Subverted.Cli;

/// <summary>
/// What a revert would throw away, as the lines to print before asking. Pure, and worth being pure:
/// this is the list someone reads in the second before they lose a morning, so every rule in it is
/// a test rather than something found out afterwards.
/// </summary>
public static class RevertPreview
{
    /// <param name="status">
    /// A listing of the whole working copy the targets are in — what <c>sv st</c> already asks for.
    /// </param>
    /// <param name="paths">Absolute revert targets, as the daemon will be given them.</param>
    /// <param name="comparison">
    /// How this platform compares paths. Taken as an argument so both answers are covered by tests
    /// on either operating system.
    /// </param>
    /// <returns>The affected nodes in <c>sv st</c>'s own layout, empty when nothing would be lost.</returns>
    public static IReadOnlyList<string> Lines(
        StatusResponse status,
        IReadOnlyList<string> paths,
        StringComparison comparison
    ) =>
        [
            .. AffectedNodes
                .Under(status, paths, comparison, WouldLoseWork)
                .Select(StatusLine.Compact),
        ];

    /// <summary>
    /// Revert restores versioned nodes and leaves everything else where it is, so an unversioned
    /// file is not at risk and saying it was would teach people to stop reading the list.
    /// </summary>
    private static bool WouldLoseWork(WorkingCopyEntry entry) =>
        entry.Status is not (NodeStatus.Unversioned or NodeStatus.Ignored or NodeStatus.External);
}
