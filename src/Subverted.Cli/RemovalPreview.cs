using Subverted.Core;
using Subverted.Protocol;

namespace Subverted.Cli;

/// <summary>
/// What a removal would take, split by whether anything could bring it back. Pure, and the split is
/// the point: a versioned file comes back with <c>sv revert</c> until it is committed, and an
/// unversioned one has no pristine behind it and is simply gone.
/// </summary>
/// <param name="Recoverable">Versioned nodes, restorable until the delete is committed.</param>
/// <param name="Unrecoverable">
/// Unversioned and ignored files. <c>svn delete --force</c> unlinks these and says nothing about it.
/// </param>
public sealed record RemovalPreview(
    IReadOnlyList<WorkingCopyEntry> Recoverable,
    IReadOnlyList<WorkingCopyEntry> Unrecoverable
)
{
    /// <summary>
    /// The listing this preview has to be built from.
    /// </summary>
    /// <remarks>
    /// Both switches are on, and neither is optional. A file nobody has edited is the commonest
    /// thing anyone removes and is absent from a default listing entirely — asking for one would
    /// make <c>sv rm</c> answer "nothing to remove" and do nothing, which is how it behaved until
    /// somebody ran it. An ignored file is hidden for the opposite reason and is about to be
    /// unlinked with no pristine behind it.
    /// </remarks>
    public static StatusRequest ListingFor(string path) =>
        new(path, IncludeUnmodified: true, IncludeIgnored: true);

    /// <param name="status">A listing of the whole working copy the targets are in.</param>
    /// <param name="paths">Absolute removal targets, as the daemon will be given them.</param>
    /// <param name="comparison">
    /// How this platform compares paths. Taken as an argument so both answers are covered by tests
    /// on either operating system.
    /// </param>
    /// <remarks>
    /// An external is in neither list. It is a working copy of its own, and removing it is a change
    /// to the <c>svn:externals</c> property of the one it sits in rather than a delete.
    /// </remarks>
    public static RemovalPreview Of(
        StatusResponse status,
        IReadOnlyList<string> paths,
        StringComparison comparison
    ) =>
        new(
            AffectedNodes.Under(status, paths, comparison, IsVersioned),
            AffectedNodes.Under(status, paths, comparison, HasNoPristine)
        );

    public int Count => Recoverable.Count + Unrecoverable.Count;

    /// <summary>The nodes in <c>sv st</c>'s own layout, with the unrecoverable ones called out below.</summary>
    public IReadOnlyList<string> Lines
    {
        get
        {
            var lines = new List<string>(Recoverable.Select(StatusLine.Compact));
            if (Unrecoverable.Count == 0)
            {
                return lines;
            }

            if (lines.Count > 0)
            {
                lines.Add(string.Empty);
            }

            lines.Add(
                $"{Unrecoverable.Count} of these are not in SVN, so nothing can bring them back:"
            );
            lines.AddRange(Unrecoverable.Select(StatusLine.Compact));
            return lines;
        }
    }

    private static bool IsVersioned(WorkingCopyEntry entry) =>
        entry.Status is not (NodeStatus.Unversioned or NodeStatus.Ignored or NodeStatus.External);

    private static bool HasNoPristine(WorkingCopyEntry entry) =>
        entry.Status is NodeStatus.Unversioned or NodeStatus.Ignored;
}
