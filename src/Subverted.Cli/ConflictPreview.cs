using Subverted.Core;
using Subverted.Protocol;

namespace Subverted.Cli;

/// <summary>
/// What a resolve would overwrite, as the lines to print before asking. Only the resolutions that
/// discard this working copy's version of a file need one — see
/// <see cref="ResolveCommand.OverwritesLocalWork"/>.
/// </summary>
public static class ConflictPreview
{
    /// <param name="status">
    /// A listing of the whole working copy the targets are in — what <c>sv st</c> already asks for.
    /// </param>
    /// <param name="paths">Absolute resolve targets, as the daemon will be given them.</param>
    /// <param name="comparison">
    /// How this platform compares paths. Taken as an argument so both answers are covered by tests
    /// on either operating system.
    /// </param>
    /// <returns>
    /// The conflicted nodes in <c>sv st</c>'s own layout, empty when none are. Empty is worth
    /// acting on rather than confirming: there is nothing to resolve and nothing to lose.
    /// </returns>
    public static IReadOnlyList<string> Lines(
        StatusResponse status,
        IReadOnlyList<string> paths,
        StringComparison comparison
    ) =>
        [
            .. AffectedNodes
                .Under(status, paths, comparison, WouldBeResolved)
                .Select(StatusLine.Compact),
        ];

    /// <summary>
    /// A resolve reaches conflicted nodes and nothing else, so listing a merely modified file would
    /// claim it was at risk when SVN will not touch it.
    /// </summary>
    /// <remarks>
    /// The flag rather than <see cref="NodeStatus.Conflicted"/>. Both readers keep the two in step
    /// — a property conflict, which SVN prints with a blank first column, is folded onto the status
    /// axis as well — so this asks the question directly instead of relying on that folding.
    /// </remarks>
    private static bool WouldBeResolved(WorkingCopyEntry entry) => entry.IsConflicted;
}
