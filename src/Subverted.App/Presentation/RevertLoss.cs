using Subverted.Core;

namespace Subverted.App.Presentation;

/// <summary>
/// What <c>svn revert</c> does to one listed row, as measured on 1.8.15. Mostly it restores versioned
/// nodes and leaves the rest alone, but not always: a copy and an obstruction are deleted from disk,
/// and each line says so rather than the list promising that nothing unversioned is touched.
/// </summary>
public static class RevertLoss
{
    private const string PutBack = "Put back as it was at the last update";

    /// <returns>
    /// <c>null</c> when revert does nothing to it: unversioned, ignored, external, or listed only
    /// for its lock — revert does not release a lock.
    /// </returns>
    public static RevertLine? For(ChangeRow row)
    {
        var entry = row.Entry;
        if (row.RenamedFrom is { } from)
        {
            return new RevertLine(
                from,
                $"{PutBack}; the renamed file, {row.RelPath}, is left as it is",
                LosesWork: false
            );
        }

        if (entry.IsConflicted || entry.Status == NodeStatus.Conflicted)
        {
            return Loses(row, "Edits and the conflict are thrown away");
        }

        var propertiesChanged = entry.PropertyStatus == PropertyStatus.Modified;
        return entry.Status switch
        {
            NodeStatus.Modified => Loses(
                row,
                propertiesChanged
                    ? "Edits and property changes are thrown away"
                    : "Edits are thrown away"
            ),
            NodeStatus.Unmodified when propertiesChanged => Loses(
                row,
                "Property changes are thrown away"
            ),
            // Measured on 1.8.15: a plain add stays on disk, while a copy is deleted from it —
            // edits, and unversioned files inside a copied folder, with it.
            NodeStatus.Added when entry.IsCopied => Loses(
                row,
                "The copy is deleted from disk, with any edits and anything unversioned inside it"
            ),
            NodeStatus.Added => Keeps(row, "No longer added; what is on disk stays, not versioned"),
            NodeStatus.Deleted or NodeStatus.Missing => Keeps(row, PutBack),
            NodeStatus.Replaced => Loses(
                row,
                "The replacement is thrown away, the original put back"
            ),
            // Measured on 1.8.15: the thing in its place is deleted, unversioned contents and all.
            NodeStatus.Obstructed => Loses(
                row,
                "Whatever is in its place on disk is deleted, and it is put back as last updated"
            ),
            NodeStatus.Incomplete or NodeStatus.NeedsPristineCompare => Loses(
                row,
                "Any local changes are thrown away"
            ),
            _ => null,
        };
    }

    private static RevertLine Loses(ChangeRow row, string what) =>
        new(row.RelPath, what, LosesWork: true);

    private static RevertLine Keeps(ChangeRow row, string what) =>
        new(row.RelPath, what, LosesWork: false);
}
