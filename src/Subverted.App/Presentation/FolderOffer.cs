using Subverted.Core;
using Subverted.Frontend;

namespace Subverted.App.Presentation;

/// <summary>
/// Which of Revert… and Delete… the directory pane offers on a folder, by the same rules a line's
/// menu uses — the folder is simply a target that may have no line of its own.
/// </summary>
public static class FolderOffer
{
    /// <summary>Every path here is the daemon's own spelling within one listing.</summary>
    private const StringComparison Ordinal = StringComparison.Ordinal;

    /// <summary>By <see cref="DeletionOffer"/>, as a line would be: never the root, nor a folder holding a half-updated node.</summary>
    /// <param name="folder">Relative to the root; empty for the root itself.</param>
    /// <param name="rows">Every row of the listing, unmodified ones included when they were listed.</param>
    public static bool CanDelete(string folder, IReadOnlyList<ChangeRow> rows) =>
        DeletionOffer.RefusalFor(NodeOf(folder, rows), rows.Select(row => row.Entry), Ordinal)
        is null;

    /// <summary>
    /// Only where the revert would do something, and only on a folder the listing reaches all of:
    /// above the opened folder, the confirmation could not name everything the revert touches.
    /// </summary>
    /// <param name="folder">Relative to the root; empty for the root itself.</param>
    /// <param name="listedScope">The folder the listing was scoped to, relative to the root.</param>
    /// <param name="changes">Every change listed, whatever the filter shows.</param>
    public static bool CanRevert(
        string folder,
        string listedScope,
        IEnumerable<ChangeRow> changes
    ) =>
        TargetCoverage.Covers(listedScope, folder, Ordinal)
        && RevertConfirmation.For(folder, changes).Lines.Count > 0;

    /// <summary>
    /// The folder's own row where the listing has one. A folder the pane shows without one holds a
    /// listed path, so SVN versions it, and a listing of changes leaves it out only when it is clean.
    /// </summary>
    private static WorkingCopyEntry NodeOf(string folder, IReadOnlyList<ChangeRow> rows) =>
        rows.FirstOrDefault(row => row.RelPath == folder)?.Entry
        ?? new WorkingCopyEntry(
            folder,
            NodeKind.Directory,
            NodeStatus.Unmodified,
            PropertyStatus.Unmodified,
            Revision: null,
            Changelist: null,
            IsConflicted: false,
            HasLockToken: false,
            IsWriteLocked: false,
            IsCopied: false
        );
}
