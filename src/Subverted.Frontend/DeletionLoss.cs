using Subverted.Core;

namespace Subverted.Frontend;

/// <summary>
/// What <c>svn delete --force</c> does to one node it reaches, as measured on 1.8.15. It removes
/// everything from disk, versioned or not, so each line says whether SVN keeps anything to bring
/// it back from.
/// </summary>
public static class DeletionLoss
{
    /// <returns><c>null</c> for a node already marked deleted: the delete does nothing more to it.</returns>
    public static DeletionLine? For(WorkingCopyEntry entry)
    {
        if (entry.IsConflicted || entry.Status == NodeStatus.Conflicted)
        {
            return Loses(entry, "Its conflict is dropped, and any local edits with it");
        }

        var propertiesChanged = entry.PropertyStatus == PropertyStatus.Modified;
        return entry.Status switch
        {
            NodeStatus.Unmodified when propertiesChanged => Loses(
                entry,
                "Deleted from disk, and its property changes with it"
            ),
            NodeStatus.Unmodified when entry.IsCopied => Keeps(
                entry,
                "Deleted with the copy; its source still has it"
            ),
            NodeStatus.Unmodified => Keeps(
                entry,
                "Deleted from disk; Revert brings it back until the delete is committed"
            ),
            NodeStatus.Missing => Keeps(entry, "Already gone from disk; recorded as deleted"),
            NodeStatus.Deleted => null,
            NodeStatus.Modified => Loses(
                entry,
                propertiesChanged
                    ? "Deleted from disk, and its edits and property changes with it"
                    : "Deleted from disk, and its edits with it"
            ),
            // Measured: an edited copied file still reads as a plain `A  +`, so edits cannot be ruled out.
            NodeStatus.Added when entry.IsCopied => Loses(
                entry,
                "The copy is undone and deleted from disk, with any edits made to it; its source is untouched"
            ),
            NodeStatus.Added => Loses(
                entry,
                "Deleted from disk; it was never committed, so nothing can bring it back"
            ),
            NodeStatus.Replaced => Loses(
                entry,
                "The replacement is deleted from disk and nothing can bring it back; the original is marked deleted"
            ),
            NodeStatus.Obstructed => Loses(entry, "Whatever is in its place on disk is deleted"),
            NodeStatus.Unversioned => Loses(
                entry,
                "Not in SVN: deleted from disk, and nothing can bring it back"
            ),
            NodeStatus.Ignored => Loses(
                entry,
                "Ignored by SVN: deleted from disk, and nothing can bring it back"
            ),
            // Measured: its edits and unversioned files go too, and SVN prints a line for none of them.
            NodeStatus.External => Loses(
                entry,
                "An external checkout: deleted from disk with everything in it, its own edits included, and this list cannot see inside it"
            ),
            _ => Loses(entry, "Deleted from disk, with any edits it has"),
        };
    }

    private static DeletionLine Loses(WorkingCopyEntry entry, string what) =>
        new(entry.RelPath, what, LosesWork: true);

    private static DeletionLine Keeps(WorkingCopyEntry entry, string what) =>
        new(entry.RelPath, what, LosesWork: false);
}
